using System.Text;
using System.Text.Json;
using GitGuard.Core.Abstractions;
using GitGuard.Core.Domain;
using GitGuard.Core.Model;
using GitGuard.Core.Policy;
using GitGuard.Infrastructure.FileSystem;
using Microsoft.Data.Sqlite;
using GitGuardSqliteException = GitGuard.Core.SqliteException;
using MsSqliteException = Microsoft.Data.Sqlite.SqliteException;

namespace GitGuard.Infrastructure.Sqlite;

/// <summary>
/// SQLite 审计存储（SAD §5 / 模块设计/Audit.md）：本地单文件审计库，WAL 模式、ScanRun 与 Findings 单事务写入、
/// BUSY 指数退避重试（10-异常处理设计 §6：50ms / 100ms / 200ms，重试耗尽后 FailClosed）。
/// 构造期执行一次 schema 迁移（<see cref="SchemaMigrator"/>）与文件加固：Unix 0700/0600（08-权限设计 §5.1）、
/// Windows ACL 收紧（08 §5.2）、符号链接路径拒绝（08-权限设计 §9）。
/// </summary>
public sealed class SqliteAuditStore : IAuditStore, IAllowlistStore
{
    private const int SQLITE_BUSY_ERROR_CODE = 5;
    private const int SQLITE_LOCKED_ERROR_CODE = 6;

    /// <summary>
    /// 每次命令的 BUSY 等待窗口（秒）。Microsoft.Data.Sqlite 在 CommandTimeout 内对 SQLITE_BUSY 持续重试
    /// （连接串 <c>Default Timeout</c> 控制；默认 30s），置 1s 收紧窗口，把「等待与退避」策略交还给本类的重试循环
    /// —— 探测结论：<c>PRAGMA busy_timeout</c> 对 Microsoft.Data.Sqlite 的重试行为无效。
    /// </summary>
    private const int COMMAND_TIMEOUT_SECONDS = 1;

    private static readonly TimeSpan[] _defaultBusyRetryDelays = new[]
    {
        TimeSpan.FromMilliseconds(50),
        TimeSpan.FromMilliseconds(100),
        TimeSpan.FromMilliseconds(200),
    };

    private readonly string _databasePath;
    private readonly string _connectionString;
    private readonly string _gitGuardVersion;
    private readonly TimeSpan[] _busyRetryDelays;

    /// <summary>
    /// 打开（或创建）指定路径的审计数据库：校验路径、执行迁移、加固文件权限。
    /// </summary>
    /// <param name="databasePath">数据库文件路径（自动创建父目录）。</param>
    /// <param name="gitGuardVersion">写入 <c>scan_runs.gitguard_version</c> 的版本号；缺省时取本程序集版本（CLI 接线时注入 SemVer）。</param>
    /// <param name="busyRetryDelays">BUSY 重试间隔；缺省 50ms / 100ms / 200ms（10-异常处理设计 §6）。</param>
    /// <exception cref="ArgumentException"><paramref name="databasePath"/> 为空或全空白。</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="busyRetryDelays"/> 含非正间隔。</exception>
    /// <exception cref="GitGuardSqliteException">路径为符号链接、迁移失败或数据库无法打开时抛出（FailClosed）。</exception>
    public SqliteAuditStore(string databasePath, string? gitGuardVersion = null, IReadOnlyList<TimeSpan>? busyRetryDelays = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        _databasePath = Path.GetFullPath(databasePath);
        _gitGuardVersion = string.IsNullOrWhiteSpace(gitGuardVersion) ? ResolveDefaultGitGuardVersion() : gitGuardVersion;
        _busyRetryDelays = ResolveBusyRetryDelays(busyRetryDelays);
        _connectionString = BuildConnectionString(_databasePath);

        EnsureDatabaseDirectory();
        RejectSymbolicLink();
        Initialize();
    }

    /// <inheritdoc />
    public void SaveScanRun(ScanRun run, IReadOnlyDictionary<Finding, PolicyDecision> findingDecisions)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(findingDecisions);
        EnsureAllDecisionsPresent(run, findingDecisions);

        ExecuteWithBusyRetry("写入扫描运行", connection =>
        {
            using SqliteTransaction transaction = connection.BeginTransaction();
            InsertScanRun(connection, transaction, run);
            foreach (Finding finding in run.Findings)
            {
                InsertFinding(connection, transaction, run.Id, finding, findingDecisions[finding]);
            }

            InsertScanCompletedEvent(connection, transaction, run, findingDecisions);
            transaction.Commit();
        });
    }

    /// <inheritdoc />
    public void SaveBypassEvent(ScanRun run, BypassReason reason, string commitSha)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentException.ThrowIfNullOrWhiteSpace(commitSha);
        if (!Enum.IsDefined(reason))
        {
            throw new ArgumentOutOfRangeException(nameof(reason), reason, "未知的 BypassReason 取值");
        }

        if (run.ScanType != ScanType.Bypass || !run.Findings.IsEmpty)
        {
            throw new ArgumentException("绕过事件必须使用不带 Finding 的 ScanType.Bypass 运行记录。", nameof(run));
        }

        if (!IsHexSha(commitSha))
        {
            throw new ArgumentException($"提交 SHA 必须是 40 或 64 位十六进制：{commitSha}", nameof(commitSha));
        }

        ExecuteWithBusyRetry("写入绕过事件", connection =>
        {
            using SqliteTransaction transaction = connection.BeginTransaction();
            InsertScanRun(connection, transaction, run);
            InsertAuditEvent(
                connection,
                transaction,
                "Bypass",
                "BypassDetected",
                BuildBypassDetectedPayload(run, reason, commitSha),
                run.StartedAt);
            transaction.Commit();
        });
    }

    /// <inheritdoc />
    public ScanRun? FindScanRun(Guid runId)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(runId, Guid.Empty, nameof(runId));

        return ExecuteWithBusyRetry("读取扫描运行", connection => SelectScanRun(connection, runId));
    }

    /// <inheritdoc />
    public IReadOnlyList<ScanRun> ListRecentScanRuns(string repoPath, int limit)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repoPath);
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);

        string normalizedRepoPath = Path.TrimEndingDirectorySeparator(repoPath);
        return ExecuteWithBusyRetry("查询最近扫描", connection => SelectRecentScanRuns(connection, normalizedRepoPath, limit));
    }

    /// <inheritdoc />
    public AuditRunPage QueryScanRuns(AuditQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);

        return ExecuteWithBusyRetry("查询审计运行", connection => SelectScanRunPage(connection, query));
    }

    /// <inheritdoc />
    public IReadOnlyList<ScanRun> ListScanRunsForExport() =>
        ExecuteWithBusyRetry("读取审计导出数据", SelectAllScanRuns);

    /// <inheritdoc />
    public void SaveHookInstallation(HookInstallation installation)
    {
        ArgumentNullException.ThrowIfNull(installation);

        ExecuteWithBusyRetry("写入 Hook 安装记录", connection =>
        {
            using SqliteTransaction transaction = connection.BeginTransaction();
            UpsertHookInstallation(connection, transaction, installation, preserveExisting: false);
            InsertAuditEvent(
                connection,
                transaction,
                "Hook",
                "HookInstalled",
                BuildHookInstalledPayload(installation),
                installation.InstalledAt);
            transaction.Commit();
        });
    }

    /// <inheritdoc />
    public void SaveHookUninstallation(HookInstallation installation, bool restoredLegacyHook)
    {
        ArgumentNullException.ThrowIfNull(installation);

        ExecuteWithBusyRetry("写入 Hook 卸载记录", connection =>
        {
            using SqliteTransaction transaction = connection.BeginTransaction();
            UpsertHookInstallation(connection, transaction, installation, preserveExisting: true);
            InsertAuditEvent(
                connection,
                transaction,
                "Hook",
                "HookUninstalled",
                BuildHookUninstalledPayload(installation, restoredLegacyHook),
                DateTimeOffset.UtcNow);
            transaction.Commit();
        });
    }

    /// <inheritdoc />
    public void SavePolicyOverrideEvent(string ruleId, PolicyAction fromAction, PolicyAction toAction)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ruleId);
        if (!Enum.IsDefined(fromAction))
        {
            throw new ArgumentOutOfRangeException(nameof(fromAction), fromAction, "未知的 PolicyAction 取值");
        }

        if (!Enum.IsDefined(toAction))
        {
            throw new ArgumentOutOfRangeException(nameof(toAction), toAction, "未知的 PolicyAction 取值");
        }

        if (toAction >= fromAction)
        {
            throw new ArgumentException("策略覆盖事件必须记录降级（toAction 必须比 fromAction 更宽松）。", nameof(toAction));
        }

        InsertStandaloneAuditEvent(
            "写入策略覆盖事件",
            "Policy",
            "PolicyOverride",
            BuildPolicyOverridePayload(ruleId, fromAction, toAction));
    }

    /// <inheritdoc />
    public void SaveFilePermissionFixedEvent(string filePath, string previousMode, string newMode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(previousMode);
        ArgumentException.ThrowIfNullOrWhiteSpace(newMode);

        InsertStandaloneAuditEvent(
            "写入文件权限修复事件",
            "Security",
            "FilePermissionFixed",
            BuildFilePermissionFixedPayload(filePath, previousMode, newMode));
    }

    /// <inheritdoc />
    public void SaveFailClosedEvent(int exitCode, string errorCode, string messageHash)
    {
        const int MIN_FAIL_CLOSED_EXIT_CODE = 10;
        ArgumentOutOfRangeException.ThrowIfLessThan(exitCode, MIN_FAIL_CLOSED_EXIT_CODE, nameof(exitCode));
        ArgumentException.ThrowIfNullOrWhiteSpace(errorCode);
        if (!IsSha256Hex(messageHash))
        {
            throw new ArgumentException($"消息摘要必须是 64 位十六进制 SHA-256：{messageHash}", nameof(messageHash));
        }

        InsertStandaloneAuditEvent(
            "写入 FailClosed 事件",
            "Error",
            "FailClosed",
            BuildFailClosedPayload(exitCode, errorCode, messageHash));
    }

    /// <inheritdoc />
    public void AddAllowlistEntry(AllowlistEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        ExecuteWithBusyRetry("写入白名单条目", connection =>
        {
            using SqliteTransaction transaction = connection.BeginTransaction();
            InsertAllowlistEntry(connection, transaction, entry);
            InsertAuditEvent(
                connection,
                transaction,
                "Allowlist",
                "AllowlistChanged",
                BuildAllowlistChangedPayload(entry, "Add"),
                entry.CreatedAt);
            transaction.Commit();
        });
    }

    /// <inheritdoc />
    public bool RemoveAllowlistEntry(Guid id)
    {
        return ExecuteWithBusyRetry("删除白名单条目", connection =>
        {
            using SqliteTransaction transaction = connection.BeginTransaction();
            AllowlistEntry? entry = SelectAllowlistEntry(connection, transaction, id);
            if (entry is null)
            {
                transaction.Rollback();
                return false;
            }

            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = "DELETE FROM allowlist WHERE id = $id;";
                command.Parameters.AddWithValue("$id", id.ToString());
                command.ExecuteNonQuery();
            }

            InsertAuditEvent(
                connection,
                transaction,
                "Allowlist",
                "AllowlistChanged",
                BuildAllowlistChangedPayload(entry, "Remove"),
                DateTimeOffset.UtcNow);
            transaction.Commit();
            return true;
        });
    }

    /// <inheritdoc />
    public AllowlistPage QueryAllowlistEntries(AllowlistQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        return ExecuteWithBusyRetry("查询白名单条目", connection => SelectAllowlistPage(connection, query));
    }

    /// <inheritdoc />
    public IReadOnlyList<AllowlistEntry> ListAllAllowlistEntries() =>
        ExecuteWithBusyRetry("读取白名单条目", SelectAllAllowlistEntries);

    private static TimeSpan[] ResolveBusyRetryDelays(IReadOnlyList<TimeSpan>? delays)
    {
        if (delays is null)
        {
            return _defaultBusyRetryDelays;
        }

        var copy = new TimeSpan[delays.Count];
        for (int i = 0; i < delays.Count; i++)
        {
            ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(delays[i], TimeSpan.Zero, nameof(delays));
            copy[i] = delays[i];
        }

        return copy;
    }

    private static string ResolveDefaultGitGuardVersion()
    {
        Version? version = typeof(SqliteAuditStore).Assembly.GetName().Version;
        return version is null ? "0.0.0" : version.ToString();
    }

    private static string BuildConnectionString(string databasePath)
    {
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            DefaultTimeout = COMMAND_TIMEOUT_SECONDS,

            // CLI 为短生命周期进程：不保留池化连接，文件句柄随操作释放（避免审计库文件被长期占用）
            Pooling = false,
        };
        return builder.ToString();
    }

    private static void EnsureAllDecisionsPresent(ScanRun run, IReadOnlyDictionary<Finding, PolicyDecision> findingDecisions)
    {
        foreach (Finding finding in run.Findings)
        {
            if (!findingDecisions.ContainsKey(finding))
            {
                throw new ArgumentException(
                    $"缺少 Finding {finding.Id} 的策略判定；每条 Finding 必须提供 PolicyDecision（PolicyDecision §2.2）。",
                    nameof(findingDecisions));
            }
        }
    }

    private static bool IsBusy(MsSqliteException exception) =>
        exception.SqliteErrorCode is SQLITE_BUSY_ERROR_CODE or SQLITE_LOCKED_ERROR_CODE;

    private static bool IsHexSha(string value) =>
        value.Length is 40 or 64
        && value.All(static character => character is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F');

    private static bool IsSha256Hex(string value) =>
        value.Length == 64
        && value.All(static character => character is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F');

    private static GitGuardSqliteException CreateSqliteException(string operation, MsSqliteException exception) =>
        new GitGuardSqliteException($"{operation}失败（SQLite 错误码 {exception.SqliteErrorCode}）：{exception.Message}", exception);

    private static void EnableWriteAheadLogging(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA journal_mode=WAL;";
        command.ExecuteScalar();
    }

    private static void InsertFinding(SqliteConnection connection, SqliteTransaction transaction, Guid runId, Finding finding, PolicyDecision decision)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            "INSERT INTO findings (id, run_id, rule_id, file_path, line_number, severity, fingerprint, snippet_masked, commit_sha, action) " +
            "VALUES ($id, $run_id, $rule_id, $file_path, $line_number, $severity, $fingerprint, $snippet_masked, $commit_sha, $action);";
        command.Parameters.AddWithValue("$id", finding.Id.ToString());
        command.Parameters.AddWithValue("$run_id", runId.ToString());
        command.Parameters.AddWithValue("$rule_id", finding.RuleId);
        command.Parameters.AddWithValue("$file_path", finding.FilePath);
        command.Parameters.AddWithValue("$line_number", finding.LineNumber);
        command.Parameters.AddWithValue("$severity", finding.Severity.ToString());
        command.Parameters.AddWithValue("$fingerprint", finding.Fingerprint.Hex);
        command.Parameters.AddWithValue("$snippet_masked", finding.SnippetMasked.Value);
        command.Parameters.AddWithValue("$commit_sha", (object?)finding.CommitSha ?? DBNull.Value);
        command.Parameters.AddWithValue("$action", ResolveFindingAction(decision));
        command.ExecuteNonQuery();
    }

    private static string ResolveFindingAction(PolicyDecision decision) =>
        decision.IgnoredByAllowlist
            ? "AllowedByAllowlist"
            : decision.IgnoredByBaseline ? "AllowedByBaseline" : decision.Action.ToString();

    private static void InsertScanCompletedEvent(
        SqliteConnection connection,
        SqliteTransaction transaction,
        ScanRun run,
        IReadOnlyDictionary<Finding, PolicyDecision> findingDecisions) =>
        InsertAuditEvent(
            connection,
            transaction,
            "Scan",
            "ScanCompleted",
            BuildScanCompletedPayload(run, findingDecisions),
            DateTimeOffset.UtcNow);

    private static void InsertAuditEvent(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string category,
        string eventType,
        string payloadJson,
        DateTimeOffset occurredAt)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            "INSERT INTO audit_logs (occurred_at, category, event_type, payload_json) " +
            "VALUES ($occurred_at, $category, $event_type, $payload_json);";
        command.Parameters.AddWithValue("$occurred_at", occurredAt.ToUnixTimeMilliseconds());
        command.Parameters.AddWithValue("$category", category);
        command.Parameters.AddWithValue("$event_type", eventType);
        command.Parameters.AddWithValue("$payload_json", payloadJson);
        command.ExecuteNonQuery();
    }

    private static void UpsertHookInstallation(
        SqliteConnection connection,
        SqliteTransaction transaction,
        HookInstallation installation,
        bool preserveExisting)
    {
        // 卸载（FR-011 / 05-数据库设计 §7）：行已存在时仅翻转状态，保留首次安装的 installed_at / version / 备份路径；
        // 安装（FR-010）：刷新为本次安装的字段并置 Active
        string sql = preserveExisting
            ? "INSERT INTO hook_installations (repo_path, hook_type, installed_at, version, original_hook_backup_path, status) " +
              "VALUES ($repo_path, $hook_type, $installed_at, $version, $backup_path, 'Uninstalled') " +
              "ON CONFLICT (repo_path, hook_type) DO UPDATE SET status = 'Uninstalled';"
            : "INSERT INTO hook_installations (repo_path, hook_type, installed_at, version, original_hook_backup_path, status) " +
              "VALUES ($repo_path, $hook_type, $installed_at, $version, $backup_path, 'Active') " +
              "ON CONFLICT (repo_path, hook_type) DO UPDATE SET " +
              "installed_at = excluded.installed_at, version = excluded.version, " +
              "original_hook_backup_path = excluded.original_hook_backup_path, status = 'Active';";

        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        command.Parameters.AddWithValue("$repo_path", installation.RepoPath);
        command.Parameters.AddWithValue("$hook_type", installation.HookType.ToHookName());
        command.Parameters.AddWithValue("$installed_at", installation.InstalledAt.ToUnixTimeMilliseconds());
        command.Parameters.AddWithValue("$version", installation.Version);
        command.Parameters.AddWithValue("$backup_path", (object?)installation.OriginalHookBackupPath ?? DBNull.Value);
        command.ExecuteNonQuery();
    }

    private static void InsertAllowlistEntry(SqliteConnection connection, SqliteTransaction transaction, AllowlistEntry entry)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            "INSERT INTO allowlist (id, rule_id, path_pattern, fingerprint, reason, created_at, created_by, expires_at) " +
            "VALUES ($id, $rule_id, $path_pattern, $fingerprint, $reason, $created_at, $created_by, $expires_at);";
        command.Parameters.AddWithValue("$id", entry.Id.ToString());
        command.Parameters.AddWithValue("$rule_id", (object?)entry.RuleId ?? DBNull.Value);
        command.Parameters.AddWithValue("$path_pattern", (object?)entry.PathPattern ?? DBNull.Value);
        command.Parameters.AddWithValue("$fingerprint", (object?)entry.Fingerprint?.Hex ?? DBNull.Value);
        command.Parameters.AddWithValue("$reason", entry.Reason);
        command.Parameters.AddWithValue("$created_at", entry.CreatedAt.ToUnixTimeMilliseconds());
        command.Parameters.AddWithValue("$created_by", entry.CreatedBy);
        command.Parameters.AddWithValue("$expires_at", (object?)entry.ExpiresAt?.ToUnixTimeMilliseconds() ?? DBNull.Value);
        command.ExecuteNonQuery();
    }

    private static string BuildAllowlistChangedPayload(AllowlistEntry entry, string action)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("EntryId", entry.Id.ToString());
            writer.WriteString("Action", action);
            WriteOptionalString(writer, "RuleId", entry.RuleId);
            WriteOptionalString(writer, "PathPattern", entry.PathPattern);
            WriteOptionalString(writer, "Fingerprint", entry.Fingerprint?.Hex);
            writer.WriteString("Reason", entry.Reason);
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static void WriteOptionalString(Utf8JsonWriter writer, string propertyName, string? value)
    {
        if (value is null)
        {
            writer.WriteNull(propertyName);
        }
        else
        {
            writer.WriteString(propertyName, value);
        }
    }

    private static string BuildHookInstalledPayload(HookInstallation installation)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("RepoPath", installation.RepoPath);
            writer.WriteString("HookType", installation.HookType.ToHookName());
            writer.WriteString("Version", installation.Version);
            writer.WriteBoolean("HadLegacyHook", installation.OriginalHookBackupPath is not null);
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static string BuildHookUninstalledPayload(HookInstallation installation, bool restoredLegacyHook)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("RepoPath", installation.RepoPath);
            writer.WriteString("HookType", installation.HookType.ToHookName());
            writer.WriteBoolean("RestoredLegacyHook", restoredLegacyHook);
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static string BuildBypassDetectedPayload(ScanRun run, BypassReason reason, string commitSha)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("RunId", run.Id.ToString());
            writer.WriteString("CommitSha", commitSha);
            writer.WriteNumber("DetectedAt", run.StartedAt.ToUnixTimeMilliseconds());
            writer.WriteString("Reason", reason.ToString());
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static string BuildPolicyOverridePayload(string ruleId, PolicyAction from, PolicyAction to)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("RuleId", ruleId);
            writer.WriteString("From", from.ToString());
            writer.WriteString("To", to.ToString());
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static string BuildFilePermissionFixedPayload(string filePath, string previousMode, string newMode)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("FilePath", filePath);
            writer.WriteString("PrevMode", previousMode);
            writer.WriteString("NewMode", newMode);
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static string BuildFailClosedPayload(int exitCode, string errorCode, string messageHash)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteNumber("ExitCode", exitCode);
            writer.WriteString("ErrorCode", errorCode);
            writer.WriteString("MessageHash", messageHash);
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static string BuildScanCompletedPayload(ScanRun run, IReadOnlyDictionary<Finding, PolicyDecision> findingDecisions)
    {
        int blockedCount = run.Findings.Count(finding => findingDecisions[finding].Action == PolicyAction.Block);

        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("RunId", run.Id.ToString());
            writer.WriteString("Type", run.ScanType.ToString());
            writer.WriteString("Result", run.Result.ToString());
            writer.WriteNumber("ExitCode", run.ExitCode);
            writer.WriteNumber("FindingsCount", run.Findings.Length);
            writer.WriteNumber("BlockedCount", blockedCount);
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static ScanRun? SelectScanRun(SqliteConnection connection, Guid runId)
    {
        Guid id;
        string repoPath;
        ScanType scanType;
        DateTimeOffset startedAt;
        long? durationMs;
        ScanResult result;
        int exitCode;

        using (var command = connection.CreateCommand())
        {
            command.CommandText =
                "SELECT id, repo_path, scan_type, started_at, duration_ms, result, exit_code FROM scan_runs WHERE id = $id;";
            command.Parameters.AddWithValue("$id", runId.ToString());

            using SqliteDataReader reader = command.ExecuteReader();
            if (!reader.Read())
            {
                return null;
            }

            id = Guid.Parse(reader.GetString(0));
            repoPath = reader.GetString(1);
            scanType = Enum.Parse<ScanType>(reader.GetString(2));
            startedAt = DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(3));
            durationMs = reader.IsDBNull(4) ? null : reader.GetInt64(4);
            result = Enum.Parse<ScanResult>(reader.GetString(5));
            exitCode = reader.GetInt32(6);
        }

        IReadOnlyList<Finding> findings = SelectFindings(connection, runId);
        return ScanRun.Restore(id, repoPath, scanType, startedAt, durationMs, result, exitCode, findings);
    }

    private static List<Finding> SelectFindings(SqliteConnection connection, Guid runId)
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT id, rule_id, file_path, line_number, severity, fingerprint, snippet_masked, commit_sha " +
            "FROM findings WHERE run_id = $run_id ORDER BY rowid;";
        command.Parameters.AddWithValue("$run_id", runId.ToString());

        var findings = new List<Finding>();
        using SqliteDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            var id = Guid.Parse(reader.GetString(0));
            string ruleId = reader.GetString(1);
            string filePath = reader.GetString(2);
            int lineNumber = reader.GetInt32(3);
            Severity severity = Enum.Parse<Severity>(reader.GetString(4));
            var fingerprint = Fingerprint.FromHex(reader.GetString(5));
            var snippetMasked = SnippetMasked.Create(reader.GetString(6));
            string? commitSha = reader.IsDBNull(7) ? null : reader.GetString(7);

            findings.Add(Finding.Restore(id, ruleId, filePath, lineNumber, severity, fingerprint, snippetMasked, commitSha));
        }

        return findings;
    }

    private static List<ScanRun> SelectRecentScanRuns(SqliteConnection connection, string repoPath, int limit)
    {
        var runIds = new List<Guid>();
        using (var command = connection.CreateCommand())
        {
            command.CommandText =
                "SELECT id FROM scan_runs WHERE repo_path = $repo_path ORDER BY started_at DESC, rowid DESC LIMIT $limit;";
            command.Parameters.AddWithValue("$repo_path", repoPath);
            command.Parameters.AddWithValue("$limit", limit);

            using SqliteDataReader reader = command.ExecuteReader();
            while (reader.Read())
            {
                runIds.Add(Guid.Parse(reader.GetString(0)));
            }
        }

        var runs = new List<ScanRun>(runIds.Count);
        foreach (Guid runId in runIds)
        {
            ScanRun? run = SelectScanRun(connection, runId);
            if (run is not null)
            {
                runs.Add(run);
            }
        }

        return runs;
    }

    private static AuditRunPage SelectScanRunPage(SqliteConnection connection, AuditQuery query)
    {
        object since = (object?)query.Since?.ToUnixTimeMilliseconds() ?? DBNull.Value;
        object result = (object?)query.Result?.ToString() ?? DBNull.Value;

        int totalCount;
        using (var command = connection.CreateCommand())
        {
            command.CommandText =
                "SELECT COUNT(*) FROM scan_runs " +
                "WHERE ($since IS NULL OR started_at >= $since) AND ($result IS NULL OR result = $result);";
            command.Parameters.AddWithValue("$since", since);
            command.Parameters.AddWithValue("$result", result);
            totalCount = checked((int)(long)(command.ExecuteScalar() ?? 0L));
        }

        var runs = new List<AuditRunSummary>(query.Limit);
        using (var command = connection.CreateCommand())
        {
            // Finding 计数用相关子查询：摘要页只需数量，不加载明细
            command.CommandText =
                "SELECT r.id, r.repo_path, r.scan_type, r.started_at, r.duration_ms, r.result, r.exit_code, " +
                "(SELECT COUNT(*) FROM findings f WHERE f.run_id = r.id) " +
                "FROM scan_runs r " +
                "WHERE ($since IS NULL OR r.started_at >= $since) AND ($result IS NULL OR r.result = $result) " +
                "ORDER BY r.started_at DESC, r.rowid DESC LIMIT $limit OFFSET $offset;";
            command.Parameters.AddWithValue("$since", since);
            command.Parameters.AddWithValue("$result", result);
            command.Parameters.AddWithValue("$limit", query.Limit);
            command.Parameters.AddWithValue("$offset", query.Offset);

            using SqliteDataReader reader = command.ExecuteReader();
            while (reader.Read())
            {
                runs.Add(new AuditRunSummary(
                    Guid.Parse(reader.GetString(0)),
                    reader.GetString(1),
                    Enum.Parse<ScanType>(reader.GetString(2)),
                    DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(3)),
                    reader.IsDBNull(4) ? null : reader.GetInt64(4),
                    Enum.Parse<ScanResult>(reader.GetString(5)),
                    reader.GetInt32(6),
                    checked((int)reader.GetInt64(7))));
            }
        }

        return new AuditRunPage(runs, totalCount);
    }

    private static List<ScanRun> SelectAllScanRuns(SqliteConnection connection)
    {
        var runIds = new List<Guid>();
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT id FROM scan_runs ORDER BY started_at DESC, rowid DESC;";

            using SqliteDataReader reader = command.ExecuteReader();
            while (reader.Read())
            {
                runIds.Add(Guid.Parse(reader.GetString(0)));
            }
        }

        var runs = new List<ScanRun>(runIds.Count);
        foreach (Guid runId in runIds)
        {
            ScanRun? run = SelectScanRun(connection, runId);
            if (run is not null)
            {
                runs.Add(run);
            }
        }

        return runs;
    }

    private static AllowlistEntry? SelectAllowlistEntry(SqliteConnection connection, SqliteTransaction transaction, Guid id)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            "SELECT id, rule_id, path_pattern, fingerprint, reason, created_at, created_by, expires_at " +
            "FROM allowlist WHERE id = $id;";
        command.Parameters.AddWithValue("$id", id.ToString());

        using SqliteDataReader reader = command.ExecuteReader();
        return reader.Read() ? ReadAllowlistEntry(reader) : null;
    }

    private static AllowlistPage SelectAllowlistPage(SqliteConnection connection, AllowlistQuery query)
    {
        int totalCount;
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT COUNT(*) FROM allowlist;";
            totalCount = checked((int)(long)(command.ExecuteScalar() ?? 0L));
        }

        var entries = new List<AllowlistEntry>(query.Limit);
        using (var command = connection.CreateCommand())
        {
            command.CommandText =
                "SELECT id, rule_id, path_pattern, fingerprint, reason, created_at, created_by, expires_at " +
                "FROM allowlist ORDER BY created_at DESC, rowid DESC LIMIT $limit OFFSET $offset;";
            command.Parameters.AddWithValue("$limit", query.Limit);
            command.Parameters.AddWithValue("$offset", query.Offset);

            using SqliteDataReader reader = command.ExecuteReader();
            while (reader.Read())
            {
                entries.Add(ReadAllowlistEntry(reader));
            }
        }

        return new AllowlistPage(entries, totalCount);
    }

    private static List<AllowlistEntry> SelectAllAllowlistEntries(SqliteConnection connection)
    {
        var entries = new List<AllowlistEntry>();
        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT id, rule_id, path_pattern, fingerprint, reason, created_at, created_by, expires_at " +
            "FROM allowlist ORDER BY created_at DESC, rowid DESC;";

        using SqliteDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            entries.Add(ReadAllowlistEntry(reader));
        }

        return entries;
    }

    private static AllowlistEntry ReadAllowlistEntry(SqliteDataReader reader)
    {
        // 行数据可能因人工修改或旧版本迁移而损坏：统一转为审计存储异常，由调用方按 FailClosed 处理
        try
        {
            var id = Guid.Parse(reader.GetString(0));
            string? ruleId = reader.IsDBNull(1) ? null : reader.GetString(1);
            string? pathPattern = reader.IsDBNull(2) ? null : reader.GetString(2);
            Fingerprint? fingerprint = reader.IsDBNull(3) ? null : Fingerprint.FromHex(reader.GetString(3));
            string reason = reader.GetString(4);
            var createdAt = DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(5));
            string createdBy = reader.GetString(6);
            DateTimeOffset? expiresAt = reader.IsDBNull(7) ? null : DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(7));

            return AllowlistEntry.Restore(id, ruleId, pathPattern, fingerprint, reason, createdAt, createdBy, expiresAt);
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException or InvariantViolationException)
        {
            throw new GitGuardSqliteException("白名单条目数据损坏。", ex);
        }
    }

    private void EnsureDatabaseDirectory()
    {
        string? directory = Path.GetDirectoryName(_databasePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }

    private void RejectSymbolicLink()
    {
        if (!File.Exists(_databasePath))
        {
            return;
        }

        FileSystemInfo? linkTarget = File.ResolveLinkTarget(_databasePath, returnFinalTarget: false);
        if (linkTarget is not null)
        {
            throw new GitGuardSqliteException($"审计数据库路径是符号链接，拒绝打开（08-权限设计 §9）：{_databasePath}");
        }
    }

    private void Initialize()
    {
        var permissionFixes = new List<FilePermissionFix>();
        HardenDataDirectory(permissionFixes);

        ExecuteWithBusyRetry("初始化审计数据库", connection =>
        {
            EnableWriteAheadLogging(connection);
            SchemaMigrator.Apply(connection);
        });

        HardenDatabaseFiles(permissionFixes);
        if (permissionFixes.Count > 0)
        {
            // 权限修复与初始化同源：单事务写入全部 FilePermissionFixed 事件，失败按审计写入失败处理
            ExecuteWithBusyRetry("写入文件权限修复事件", connection =>
            {
                using SqliteTransaction transaction = connection.BeginTransaction();
                DateTimeOffset occurredAt = DateTimeOffset.UtcNow;
                foreach (FilePermissionFix fix in permissionFixes)
                {
                    InsertAuditEvent(
                        connection,
                        transaction,
                        "Security",
                        "FilePermissionFixed",
                        BuildFilePermissionFixedPayload(fix.FilePath, fix.PreviousMode, fix.NewMode),
                        occurredAt);
                }

                transaction.Commit();
            });
        }
    }

    /// <summary>先收紧数据目录（08 §5.1/§5.2），使随后创建的 audit.db / WAL / SHM 继承受限 ACL。</summary>
    private void HardenDataDirectory(List<FilePermissionFix> fixes)
    {
        string? directory = Path.GetDirectoryName(_databasePath);
        if (string.IsNullOrEmpty(directory))
        {
            return;
        }

        if (FilePermissionHardener.HardenDirectory(directory) is { } fix)
        {
            fixes.Add(fix);
        }
    }

    private void HardenDatabaseFiles(List<FilePermissionFix> fixes)
    {
        foreach (string path in new[] { _databasePath, _databasePath + "-wal", _databasePath + "-shm" })
        {
            if (FilePermissionHardener.HardenFile(path) is { } fix)
            {
                fixes.Add(fix);
            }
        }
    }

    private void InsertScanRun(SqliteConnection connection, SqliteTransaction transaction, ScanRun run)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            "INSERT INTO scan_runs (id, repo_path, scan_type, started_at, duration_ms, result, exit_code, gitguard_version, git_version, summary_json) " +
            "VALUES ($id, $repo_path, $scan_type, $started_at, $duration_ms, $result, $exit_code, $gitguard_version, NULL, NULL);";
        command.Parameters.AddWithValue("$id", run.Id.ToString());
        command.Parameters.AddWithValue("$repo_path", run.RepoPath);
        command.Parameters.AddWithValue("$scan_type", run.ScanType.ToString());
        command.Parameters.AddWithValue("$started_at", run.StartedAt.ToUnixTimeMilliseconds());
        command.Parameters.AddWithValue("$duration_ms", (object?)run.DurationMs ?? DBNull.Value);
        command.Parameters.AddWithValue("$result", run.Result.ToString());
        command.Parameters.AddWithValue("$exit_code", run.ExitCode);
        command.Parameters.AddWithValue("$gitguard_version", _gitGuardVersion);
        command.ExecuteNonQuery();
    }

    private void InsertStandaloneAuditEvent(string operation, string category, string eventType, string payloadJson) =>
        ExecuteWithBusyRetry(operation, connection =>
        {
            using SqliteTransaction transaction = connection.BeginTransaction();
            InsertAuditEvent(connection, transaction, category, eventType, payloadJson, DateTimeOffset.UtcNow);
            transaction.Commit();
        });

    private SqliteConnection OpenConnection()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        return connection;
    }

    private void ExecuteWithBusyRetry(string operation, Action<SqliteConnection> action) =>
        ExecuteWithBusyRetry(
            operation,
            connection =>
            {
                action(connection);
                return true;
            });

    // BUSY 重试循环：由 return / throw 退出（10-异常处理设计 §6——不无限重试，耗尽即 FailClosed）
    private T ExecuteWithBusyRetry<T>(string operation, Func<SqliteConnection, T> action)
    {
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                using SqliteConnection connection = OpenConnection();
                return action(connection);
            }
            catch (MsSqliteException ex) when (attempt <= _busyRetryDelays.Length && IsBusy(ex))
            {
                Thread.Sleep(_busyRetryDelays[attempt - 1]);
            }
            catch (MsSqliteException ex)
            {
                throw CreateSqliteException(operation, ex);
            }
        }
    }
}
