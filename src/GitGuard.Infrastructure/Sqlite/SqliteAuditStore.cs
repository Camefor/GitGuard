using System.Text;
using System.Text.Json;
using GitGuard.Core.Abstractions;
using GitGuard.Core.Model;
using GitGuard.Core.Policy;
using Microsoft.Data.Sqlite;
using GitGuardSqliteException = GitGuard.Core.SqliteException;
using MsSqliteException = Microsoft.Data.Sqlite.SqliteException;

namespace GitGuard.Infrastructure.Sqlite;

/// <summary>
/// SQLite 审计存储（SAD §5 / 模块设计/Audit.md）：本地单文件审计库，WAL 模式、ScanRun 与 Findings 单事务写入、
/// BUSY 指数退避重试（10-异常处理设计 §6：50ms / 100ms / 200ms，重试耗尽后 FailClosed）。
/// 构造期执行一次 schema 迁移（<see cref="SchemaMigrator"/>）与文件加固：Unix 0600（08-权限设计 §5.1）、
/// 符号链接路径拒绝（08-权限设计 §9）。
/// </summary>
public sealed class SqliteAuditStore : IAuditStore
{
    private const int SQLITE_BUSY_ERROR_CODE = 5;
    private const int SQLITE_LOCKED_ERROR_CODE = 6;

    /// <summary>
    /// 每次命令的 BUSY 等待窗口（秒）。Microsoft.Data.Sqlite 在 CommandTimeout 内对 SQLITE_BUSY 持续重试
    /// （连接串 <c>Default Timeout</c> 控制；默认 30s），置 1s 收紧窗口，把「等待与退避」策略交还给本类的重试循环
    /// —— 探测结论：<c>PRAGMA busy_timeout</c> 对 Microsoft.Data.Sqlite 的重试行为无效。
    /// </summary>
    private const int COMMAND_TIMEOUT_SECONDS = 1;

    private static readonly TimeSpan[] DEFAULT_BUSY_RETRY_DELAYS = new[]
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

    private static TimeSpan[] ResolveBusyRetryDelays(IReadOnlyList<TimeSpan>? delays)
    {
        if (delays is null)
        {
            return DEFAULT_BUSY_RETRY_DELAYS;
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

    private static GitGuardSqliteException CreateSqliteException(string operation, MsSqliteException exception) =>
        new GitGuardSqliteException($"{operation}失败（SQLite 错误码 {exception.SqliteErrorCode}）：{exception.Message}", exception);

    private static void EnableWriteAheadLogging(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA journal_mode=WAL;";
        command.ExecuteScalar();
    }

    private static void SetOwnerOnlyMode(string path)
    {
        // 平台守卫置于方法内：CA1416 不做跨方法分析，调用方（Windows 早退）不足以使其识别
        if (OperatingSystem.IsWindows() || !File.Exists(path))
        {
            return;
        }

        File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
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
        decision.IgnoredByAllowlist ? "AllowedByAllowlist" : decision.Action.ToString();

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
        ExecuteWithBusyRetry("初始化审计数据库", connection =>
        {
            EnableWriteAheadLogging(connection);
            SchemaMigrator.Apply(connection);
        });

        ApplyFilePermissions();
    }

    private void ApplyFilePermissions()
    {
        if (OperatingSystem.IsWindows())
        {
            // Windows ACL 加固（08-权限设计 §5.2）延后至 CLI 路径解析任务，见 AGENT-GUIDE GGD-0005 会话记录
            return;
        }

        SetOwnerOnlyMode(_databasePath);
        SetOwnerOnlyMode(_databasePath + "-wal");
        SetOwnerOnlyMode(_databasePath + "-shm");
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
