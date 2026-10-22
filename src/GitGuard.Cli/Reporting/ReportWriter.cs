using System.Buffers;
using System.Globalization;
using System.Text;
using System.Text.Json;
using GitGuard.Application.Audit;
using GitGuard.Application.Bypass;
using GitGuard.Application.Hooks;
using GitGuard.Application.Rules;
using GitGuard.Application.Scanning;
using GitGuard.Cli.Localization;
using GitGuard.Core.Model;
using GitGuard.Core.Policy;

namespace GitGuard.Cli.Reporting;

/// <summary>
/// 命令结果渲染（<c>docs/06-API接口设计.md</c> §4）：文本报告走 stderr（人类可读、可选颜色），
/// <c>--format json</c> 时机器可读 JSON 走 stdout（文本报告不再输出）；错误报告同样遵循该分流。
/// JSON 一律由 <see cref="Utf8JsonWriter"/> 手写（AOT 安全，不依赖反射序列化）。
/// </summary>
internal sealed class ReportWriter
{
    private const string SCAN_SCHEMA = "gitguard.scan.v1";
    private const string ERROR_SCHEMA = "gitguard.error.v1";
    private const string INSTALL_SCHEMA = "gitguard.install.v1";
    private const string UNINSTALL_SCHEMA = "gitguard.uninstall.v1";
    private const string STATUS_SCHEMA = "gitguard.status.v1";
    private const string VERSION_SCHEMA = "gitguard.version.v1";
    private const string BYPASS_SCHEMA = "gitguard.bypass.v1";
    private const string AUDIT_LIST_SCHEMA = "gitguard.audit.list.v1";
    private const string AUDIT_VIEW_SCHEMA = "gitguard.audit.view.v1";
    private const string AUDIT_EXPORT_SUMMARY_SCHEMA = "gitguard.audit.export.summary.v1";
    private const string ALLOWLIST_ADD_SCHEMA = "gitguard.allowlist.add.v1";
    private const string ALLOWLIST_REMOVE_SCHEMA = "gitguard.allowlist.remove.v1";
    private const string ALLOWLIST_LIST_SCHEMA = "gitguard.allowlist.list.v1";
    private const string RULES_LIST_SCHEMA = "gitguard.rules.list.v1";
    private const string RULES_VIEW_SCHEMA = "gitguard.rules.view.v1";

    private readonly TextWriter _output;
    private readonly TextWriter _error;
    private readonly bool _json;
    private readonly bool _color;
    private readonly CliText _text;

    /// <summary>初始化渲染器。</summary>
    /// <param name="output">标准输出（JSON 目标）。</param>
    /// <param name="error">标准错误（文本报告与错误提示目标）。</param>
    /// <param name="json">是否使用 <c>--format json</c>。</param>
    /// <param name="color">是否允许彩色输出。</param>
    /// <param name="text">文本报告文案表（JSON 输出不受影响）。</param>
    /// <exception cref="ArgumentNullException">任一流参数或 <paramref name="text"/> 为 <see langword="null"/>。</exception>
    internal ReportWriter(TextWriter output, TextWriter error, bool json, bool color, CliText text)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);
        ArgumentNullException.ThrowIfNull(text);

        _output = output;
        _error = error;
        _json = json;
        _color = color;
        _text = text;
    }

    /// <summary>渲染扫描结果（退出码语义由调用方按 <see cref="ScanOutcome.ExitCode"/> 返回）。</summary>
    /// <param name="outcome">扫描结果。</param>
    /// <param name="rulesEvaluated">参与本次扫描的启用规则总数。</param>
    /// <param name="warnings">配置加载链产生的警告。</param>
    /// <exception cref="ArgumentNullException"><paramref name="outcome"/> 或 <paramref name="warnings"/> 为 <see langword="null"/>。</exception>
    internal void WriteScan(ScanOutcome outcome, int rulesEvaluated, IReadOnlyList<string> warnings)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        ArgumentNullException.ThrowIfNull(warnings);

        if (_json)
        {
            WriteScanJson(outcome, rulesEvaluated, warnings);
        }
        else
        {
            WriteScanText(outcome, warnings);
        }
    }

    /// <summary>渲染 post-commit 绕过检测结果（未检测到绕过时文本模式保持静默，避免每次提交刷屏）。</summary>
    /// <param name="result">检测结果。</param>
    /// <exception cref="ArgumentNullException"><paramref name="result"/> 为 <see langword="null"/>。</exception>
    internal void WriteBypass(BypassDetectionResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        if (_json)
        {
            var buffer = new ArrayBufferWriter<byte>(256);
            using (var writer = new Utf8JsonWriter(buffer, JsonText.WriterOptions))
            {
                writer.WriteStartObject();
                writer.WriteString("schema", BYPASS_SCHEMA);
                writer.WriteBoolean("detected", result.Detected);
                if (result.Reason is { } reason)
                {
                    writer.WriteString("reason", reason.ToString());
                }
                else
                {
                    writer.WriteNull("reason");
                }

                writer.WriteString("commit_sha", result.CommitSha);
                if (result.RunId is { } runId)
                {
                    writer.WriteString("run_id", runId.ToString());
                }
                else
                {
                    writer.WriteNull("run_id");
                }

                writer.WriteEndObject();
            }

            _output.WriteLine(Encoding.UTF8.GetString(buffer.WrittenSpan));
            return;
        }

        if (!result.Detected)
        {
            return;
        }

        WriteColored("⚠ ", ConsoleColor.Yellow);
        _error.WriteLine(_text.Get(CliTextKey.BypassDetected));
        string reasonText = result.Reason == BypassReason.GitGuardSkip
            ? _text.Get(CliTextKey.BypassReasonSkip)
            : _text.Get(CliTextKey.BypassReasonNoVerify);
        WriteIndented(_text.Get(CliTextKey.LabelCommit), result.CommitSha);
        WriteIndented(_text.Get(CliTextKey.LabelReason), reasonText);
        if (result.RunId is { } id)
        {
            WriteIndented(_text.Get(CliTextKey.LabelRun), id.ToString());
        }
    }

    /// <summary>渲染 Hook 安装报告。</summary>
    /// <param name="report">安装报告。</param>
    /// <exception cref="ArgumentNullException"><paramref name="report"/> 为 <see langword="null"/>。</exception>
    internal void WriteInstall(HookInstallReport report)
    {
        ArgumentNullException.ThrowIfNull(report);

        if (_json)
        {
            WriteHookListJson(INSTALL_SCHEMA, report.RepoPath, report.Version, null, report.Results);
        }
        else
        {
            WriteColored("✔ ", ConsoleColor.Green);
            _error.WriteLine(_text.Get(CliTextKey.InstallHeader));
            WriteIndented(_text.Get(CliTextKey.LabelRepo), report.RepoPath);
            WriteIndented(_text.Get(CliTextKey.LabelGitGuard), report.Version);
            foreach (HookInstallResult result in report.Results)
            {
                string line = _text.Format(
                    CliTextKey.HookLine,
                    result.HookType.ToHookName(),
                    DispositionText(result.Disposition, _text),
                    result.HookPath);
                if (result.BackupPath is { } backup)
                {
                    line += _text.Format(CliTextKey.InstallBackupSuffix, backup);
                }

                WriteIndented(null, line);
            }
        }
    }

    /// <summary>渲染 Hook 卸载报告。</summary>
    /// <param name="report">卸载报告。</param>
    /// <param name="notInstalled">全部目标均未发现 GitGuard 安装痕迹（CLI 映射退出码 5）。</param>
    /// <exception cref="ArgumentNullException"><paramref name="report"/> 为 <see langword="null"/>。</exception>
    internal void WriteUninstall(HookUninstallReport report, bool notInstalled)
    {
        ArgumentNullException.ThrowIfNull(report);

        if (_json)
        {
            WriteHookListJson(UNINSTALL_SCHEMA, report.RepoPath, null, notInstalled, report.Results);
            return;
        }

        if (notInstalled)
        {
            WriteColored("⚠ ", ConsoleColor.Yellow);
            _error.WriteLine(_text.Get(CliTextKey.UninstallNotInstalledHeader));
        }
        else
        {
            WriteColored("✔ ", ConsoleColor.Green);
            _error.WriteLine(_text.Get(CliTextKey.UninstallHeader));
        }

        WriteIndented(_text.Get(CliTextKey.LabelRepo), report.RepoPath);
        foreach (HookUninstallResult result in report.Results)
        {
            string line = _text.Format(
                CliTextKey.HookLine,
                result.HookType.ToHookName(),
                DispositionText(result.Disposition, _text),
                result.HookPath);
            if (result.BackupPath is { } backup)
            {
                line += _text.Format(CliTextKey.UninstallRestoreSuffix, backup);
            }

            WriteIndented(null, line);
        }
    }

    /// <summary>渲染状态报告（<see cref="StatusReport.NeedsAttention"/> 由调用方映射退出码 6）。</summary>
    /// <param name="report">状态报告。</param>
    /// <exception cref="ArgumentNullException"><paramref name="report"/> 为 <see langword="null"/>。</exception>
    internal void WriteStatus(StatusReport report)
    {
        ArgumentNullException.ThrowIfNull(report);

        if (_json)
        {
            WriteStatusJson(report);
            return;
        }

        _error.WriteLine(_text.Get(CliTextKey.StatusHeader));
        WriteIndented(_text.Get(CliTextKey.LabelRepo), report.RepoPath);
        WriteIndented(_text.Get(CliTextKey.LabelGitGuardVersion), report.GitGuardVersion);
        WriteIndented(_text.Get(CliTextKey.LabelGit), report.GitVersion);
        string hooksPath = report.ConfiguredHooksPath ?? _text.Format(
            CliTextKey.HooksPathNotConfigured,
            report.HooksDirectory);
        WriteIndented(_text.Get(CliTextKey.LabelHooksPath), hooksPath);
        WriteIndented(_text.Get(CliTextKey.LabelHooksDirectory), report.HooksDirectory);
        foreach (HookStatus hook in report.Hooks)
        {
            string version = hook.ManagedVersion is null
                ? string.Empty
                : _text.Format(CliTextKey.ManagedVersionSuffix, hook.ManagedVersion);
            WriteIndented(
                hook.HookType.ToHookName(),
                string.Create(CultureInfo.InvariantCulture, $"{HookStateText(hook.Kind, _text)}{version} → {hook.HookPath}"));
        }

        string configFilesText = report.ConfigFiles.Length == 0
            ? _text.Get(CliTextKey.ConfigFilesBuiltin)
            : string.Join(_text.Get(CliTextKey.ConfigFilesSeparator), report.ConfigFiles);
        WriteIndented(_text.Get(CliTextKey.LabelConfigFiles), configFilesText);
        foreach (string warning in report.Warnings)
        {
            WriteIndented(_text.Get(CliTextKey.LabelWarning), warning);
        }

        if (report.NeedsAttention)
        {
            WriteIndented(
                _text.Get(CliTextKey.LabelAttention),
                _text.Get(CliTextKey.StatusAttention));
        }
    }

    /// <summary>渲染版本报告。</summary>
    /// <param name="version">版本信息。</param>
    /// <exception cref="ArgumentNullException"><paramref name="version"/> 为 <see langword="null"/>。</exception>
    internal void WriteVersion(VersionInfo version)
    {
        ArgumentNullException.ThrowIfNull(version);

        if (_json)
        {
            WriteVersionJson(version);
            return;
        }

        _error.WriteLine(string.Create(CultureInfo.InvariantCulture, $"gitguard {version.GitGuardVersion}"));
        WriteIndented(_text.Get(CliTextKey.LabelRulesVersion), version.RulesVersion.ToString(CultureInfo.InvariantCulture));
        string gitLine = version.GitVersionLine ?? _text.Format(
            CliTextKey.GitUnavailable,
            MessageSanitizer.Sanitize(version.GitFailureMessage));
        WriteIndented(_text.Get(CliTextKey.LabelGitVersion), gitLine);
        WriteIndented(
            _text.Get(CliTextKey.LabelRuntime),
            _text.Format(CliTextKey.RuntimeDescription, version.FrameworkDescription, version.RuntimeIdentifier));
    }

    /// <summary>渲染审计列表（<c>06-API接口设计</c> §5.6；文本 → stderr；JSON → stdout）。</summary>
    /// <param name="query">本次查询条件（输出分页信息）。</param>
    /// <param name="page">查询结果。</param>
    /// <exception cref="ArgumentNullException">任一参数为 <see langword="null"/>。</exception>
    internal void WriteAuditList(AuditQuery query, AuditRunPage page)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(page);

        if (_json)
        {
            WriteAuditListJson(query, page);
            return;
        }

        _error.WriteLine(_text.Get(CliTextKey.AuditListHeader));
        if (page.Runs.IsEmpty)
        {
            string emptyMessage = page.TotalCount == 0
                ? _text.Get(CliTextKey.AuditListEmpty)
                : _text.Format(CliTextKey.AuditListPageEmpty, page.TotalCount);
            WriteIndented(null, emptyMessage);
            return;
        }

        int first = query.Offset + 1;
        int last = query.Offset + page.Runs.Length;
        _error.WriteLine(_text.Format(CliTextKey.AuditListSummary, page.TotalCount, first, last));
        foreach (AuditRunSummary run in page.Runs)
        {
            string duration = run.DurationMs is { } durationMs
                ? string.Create(CultureInfo.InvariantCulture, $"{durationMs}ms")
                : _text.Get(CliTextKey.UnknownDuration);
            WriteIndented(
                null,
                _text.Format(
                    CliTextKey.AuditListLine,
                    run.RunId,
                    FormatAuditTimestamp(run.StartedAt),
                    ScanTypeText(run.ScanType),
                    ScanResultText(run.Result),
                    run.FindingCount,
                    duration,
                    run.RepoPath));
        }
    }

    /// <summary>渲染单次审计运行详情（<c>06-API接口设计</c> §4.9；文本 → stderr；JSON → stdout）。</summary>
    /// <param name="run">扫描运行聚合（含 Finding 明细）。</param>
    /// <exception cref="ArgumentNullException"><paramref name="run"/> 为 <see langword="null"/>。</exception>
    internal void WriteAuditView(ScanRun run)
    {
        ArgumentNullException.ThrowIfNull(run);

        if (_json)
        {
            WriteAuditViewJson(run);
            return;
        }

        _error.WriteLine(_text.Get(CliTextKey.AuditViewHeader));
        WriteIndented(_text.Get(CliTextKey.LabelRun), run.Id.ToString());
        WriteIndented(_text.Get(CliTextKey.LabelRepo), run.RepoPath);
        WriteIndented(_text.Get(CliTextKey.LabelType), ScanTypeText(run.ScanType));
        WriteIndented(_text.Get(CliTextKey.LabelStartedAt), FormatAuditTimestamp(run.StartedAt));
        string duration = run.DurationMs is { } durationMs
            ? string.Create(CultureInfo.InvariantCulture, $"{durationMs}ms")
            : _text.Get(CliTextKey.UnknownDuration);
        WriteIndented(_text.Get(CliTextKey.LabelDuration), duration);
        WriteIndented(_text.Get(CliTextKey.LabelResult), ScanResultText(run.Result));
        WriteIndented(_text.Get(CliTextKey.LabelExitCode), run.ExitCode.ToString(CultureInfo.InvariantCulture));

        if (run.Findings.IsEmpty)
        {
            WriteIndented(_text.Get(CliTextKey.LabelFindings), _text.Get(CliTextKey.AuditViewEmptyFindings));
            return;
        }

        WriteIndented(_text.Get(CliTextKey.LabelFindings), run.Findings.Length.ToString(CultureInfo.InvariantCulture));
        foreach (Finding finding in run.Findings)
        {
            string location = string.Create(
                CultureInfo.InvariantCulture,
                $"{finding.FilePath}:{finding.LineNumber}");
            _error.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"    {finding.RuleId}  {location}  {finding.SnippetMasked.Value}"));
            string commit = finding.CommitSha is { } sha
                ? string.Create(CultureInfo.InvariantCulture, $"  commit={sha}")
                : string.Empty;
            _error.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"      severity={finding.Severity}  fingerprint={finding.Fingerprint.Hex}{commit}"));
        }
    }

    /// <summary>渲染审计导出摘要（文本 → stderr；JSON → stdout；文件内容由用例写入）。</summary>
    /// <param name="report">导出结果。</param>
    /// <exception cref="ArgumentNullException"><paramref name="report"/> 为 <see langword="null"/>。</exception>
    internal void WriteAuditExport(AuditExportReport report)
    {
        ArgumentNullException.ThrowIfNull(report);

        if (_json)
        {
            WriteAuditExportJson(report);
            return;
        }

        WriteColored("✔ ", ConsoleColor.Green);
        _error.WriteLine(_text.Get(CliTextKey.AuditExportHeader));
        WriteIndented(_text.Get(CliTextKey.LabelOutputPath), report.OutputPath);
        WriteIndented(_text.Get(CliTextKey.LabelFormat), report.Format == AuditExportFormat.Json ? "json" : "csv");
        WriteIndented(_text.Get(CliTextKey.LabelRunCount), report.RunCount.ToString(CultureInfo.InvariantCulture));
        WriteIndented(_text.Get(CliTextKey.LabelFindingCount), report.FindingCount.ToString(CultureInfo.InvariantCulture));
    }

    /// <summary>渲染白名单新增结果（文本 → stderr；JSON → stdout）。</summary>
    /// <param name="entry">已持久化的白名单条目。</param>
    /// <exception cref="ArgumentNullException"><paramref name="entry"/> 为 <see langword="null"/>。</exception>
    internal void WriteAllowlistAdd(AllowlistEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        if (_json)
        {
            WriteAllowlistAddJson(entry);
            return;
        }

        WriteColored("✔ ", ConsoleColor.Green);
        _error.WriteLine(_text.Get(CliTextKey.AllowlistAddHeader));
        WriteIndented(_text.Get(CliTextKey.LabelId), entry.Id.ToString());
        if (entry.RuleId is { } ruleId)
        {
            WriteIndented(_text.Get(CliTextKey.LabelRule), ruleId);
        }

        if (entry.PathPattern is { } pathPattern)
        {
            WriteIndented(_text.Get(CliTextKey.LabelPath), pathPattern);
        }

        if (entry.Fingerprint is { } fingerprint)
        {
            WriteIndented(_text.Get(CliTextKey.LabelFingerprint), fingerprint.Hex);
        }

        WriteIndented(_text.Get(CliTextKey.LabelReason), entry.Reason);
        WriteIndented(_text.Get(CliTextKey.LabelCreatedAt), FormatAuditTimestamp(entry.CreatedAt));
        WriteIndented(_text.Get(CliTextKey.LabelCreatedBy), entry.CreatedBy);
    }

    /// <summary>渲染白名单删除结果（文本 → stderr；JSON → stdout）。</summary>
    /// <param name="id">已删除条目的身份标识。</param>
    internal void WriteAllowlistRemove(Guid id)
    {
        if (_json)
        {
            WriteAllowlistRemoveJson(id);
            return;
        }

        WriteColored("✔ ", ConsoleColor.Green);
        _error.WriteLine(_text.Get(CliTextKey.AllowlistRemoveHeader));
        WriteIndented(_text.Get(CliTextKey.LabelId), id.ToString());
    }

    /// <summary>渲染白名单列表（<c>06-API接口设计</c> §5.5；文本 → stderr；JSON → stdout）。</summary>
    /// <param name="query">本次查询条件（输出分页信息）。</param>
    /// <param name="page">查询结果。</param>
    /// <exception cref="ArgumentNullException">任一参数为 <see langword="null"/>。</exception>
    internal void WriteAllowlistList(AllowlistQuery query, AllowlistPage page)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(page);

        if (_json)
        {
            WriteAllowlistListJson(query, page);
            return;
        }

        _error.WriteLine(_text.Get(CliTextKey.AllowlistListHeader));
        if (page.Entries.IsEmpty)
        {
            string emptyMessage = page.TotalCount == 0
                ? _text.Get(CliTextKey.AllowlistListEmpty)
                : _text.Format(CliTextKey.AllowlistListPageEmpty, page.TotalCount);
            WriteIndented(null, emptyMessage);
            return;
        }

        int first = query.Offset + 1;
        int last = query.Offset + page.Entries.Length;
        _error.WriteLine(_text.Format(CliTextKey.AllowlistListSummary, page.TotalCount, first, last));
        foreach (AllowlistEntry entry in page.Entries)
        {
            WriteIndented(
                null,
                _text.Format(
                    CliTextKey.AllowlistListLine,
                    entry.Id,
                    BuildAllowlistConditions(entry),
                    entry.Reason,
                    FormatAuditTimestamp(entry.CreatedAt),
                    entry.CreatedBy));
        }
    }

    /// <summary>渲染规则列表（<c>06-API接口设计</c> §5.7；文本 → stderr；JSON → stdout）。</summary>
    /// <param name="rules">过滤后的规则条目。</param>
    /// <param name="category">类别筛选（<c>Secret</c> / <c>Privacy</c> / <c>LocalPath</c> / <c>Url</c>）；未筛选为 <see langword="null"/>。</param>
    /// <param name="enabledOnly">是否仅显示有效启用的规则。</param>
    /// <param name="showPattern">是否输出正则原文。</param>
    /// <exception cref="ArgumentNullException"><paramref name="rules"/> 为 <see langword="null"/>。</exception>
    internal void WriteRulesList(
        IReadOnlyList<RuleCatalogEntry> rules,
        string? category,
        bool enabledOnly,
        bool showPattern)
    {
        ArgumentNullException.ThrowIfNull(rules);

        if (_json)
        {
            WriteRulesListJson(rules, category, enabledOnly, showPattern);
            return;
        }

        _error.WriteLine(_text.Get(CliTextKey.RulesListHeader));
        int enabledCount = 0;
        foreach (RuleCatalogEntry rule in rules)
        {
            if (rule.Enabled)
            {
                enabledCount++;
            }
        }

        WriteIndented(null, _text.Format(CliTextKey.RulesListSummary, rules.Count, enabledCount));
        if (rules.Count == 0)
        {
            WriteIndented(null, _text.Get(CliTextKey.RulesListEmpty));
            return;
        }

        foreach (RuleCatalogEntry rule in rules)
        {
            string line = _text.Format(
                CliTextKey.RulesListLine,
                rule.RuleId,
                rule.Name,
                rule.Category,
                rule.Severity,
                rule.Source,
                rule.Enabled ? _text.Get(CliTextKey.RuleEnabled) : _text.Get(CliTextKey.RuleDisabled));
            if (showPattern)
            {
                line += _text.Format(CliTextKey.RulesPatternSuffix, rule.Pattern.Regex.ToString());
            }

            WriteIndented(null, line);
        }
    }

    /// <summary>渲染单条规则详情（<c>06-API接口设计</c> §5.7；文本 → stderr；JSON → stdout）。</summary>
    /// <param name="rule">规则条目。</param>
    /// <param name="showPattern">是否输出正则原文。</param>
    /// <exception cref="ArgumentNullException"><paramref name="rule"/> 为 <see langword="null"/>。</exception>
    internal void WriteRule(RuleCatalogEntry rule, bool showPattern)
    {
        ArgumentNullException.ThrowIfNull(rule);

        if (_json)
        {
            WriteRuleJson(rule, showPattern);
            return;
        }

        WriteColored("✔ ", ConsoleColor.Green);
        _error.WriteLine(_text.Get(CliTextKey.RulesViewHeader));
        WriteIndented(_text.Get(CliTextKey.LabelRule), rule.RuleId);
        WriteIndented(_text.Get(CliTextKey.LabelName), rule.Name);
        WriteIndented(_text.Get(CliTextKey.LabelCategory), rule.Category);
        WriteIndented(_text.Get(CliTextKey.LabelSeverity), rule.Severity.ToString());
        WriteIndented(_text.Get(CliTextKey.LabelSource), rule.Source.ToString());
        WriteIndented(
            _text.Get(CliTextKey.LabelStatus),
            rule.Enabled ? _text.Get(CliTextKey.RuleEnabled) : _text.Get(CliTextKey.RuleDisabled));
        if (showPattern)
        {
            WriteIndented(_text.Get(CliTextKey.LabelPattern), rule.Pattern.Regex.ToString());
        }
    }

    /// <summary>渲染错误报告（文本 → stderr；JSON → stdout）。</summary>
    /// <param name="exitCode">退出码。</param>
    /// <param name="errorCode">错误码（<c>10-异常处理设计</c> §4 登记值）。</param>
    /// <param name="message">已脱敏的原因描述。</param>
    /// <param name="traceId">短追踪 ID（用户反馈时引用）。</param>
    /// <exception cref="ArgumentException"><paramref name="errorCode"/> 或 <paramref name="message"/> 为空或全空白。</exception>
    internal void WriteError(int exitCode, string errorCode, string message, string traceId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(errorCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);

        if (_json)
        {
            WriteErrorJson(exitCode, errorCode, message, traceId);
            return;
        }

        WriteColored("✖ ", ConsoleColor.Red);
        _error.WriteLine(_text.Format(CliTextKey.ErrorHeader, exitCode));
        WriteIndented(_text.Get(CliTextKey.LabelErrorCode), errorCode);
        WriteIndented(_text.Get(CliTextKey.LabelReason), message);
        WriteIndented(_text.Get(CliTextKey.LabelTraceId), traceId);
    }

    /// <summary>渲染用法错误（退出码 7：未知命令 / 选项冲突 / 受限取值非法 / 参数组合非法）。</summary>
    /// <param name="errors">解析错误或校验错误描述（逐条输出）。</param>
    /// <param name="traceId">短追踪 ID。</param>
    /// <exception cref="ArgumentNullException"><paramref name="errors"/> 为 <see langword="null"/>。</exception>
    /// <exception cref="ArgumentException"><paramref name="traceId"/> 为空或全空白。</exception>
    internal void WriteUsageError(IReadOnlyList<string> errors, string traceId)
    {
        ArgumentNullException.ThrowIfNull(errors);
        ArgumentException.ThrowIfNullOrWhiteSpace(traceId);

        // 解析错误可能自带换行与缩进（框架的多行取值列表）：折成单行再输出
        string message = string.Join(
            _text.Get(CliTextKey.ErrorJoinSeparator),
            errors.Select(static error => MessageSanitizer.Sanitize(error).ReplaceLineEndings(" ")));
        if (_json)
        {
            WriteErrorJson(ExitCodes.USAGE_ERROR, ExitCodeMapper.CLI_USAGE_ERROR, message, traceId);
            return;
        }

        WriteColored("✖ ", ConsoleColor.Red);
        _error.WriteLine(_text.Get(CliTextKey.UsageErrorTitle));
        WriteIndented(_text.Get(CliTextKey.LabelReason), message);
        WriteIndented(_text.Get(CliTextKey.LabelHint), _text.Get(CliTextKey.UsageErrorHint));
        WriteIndented(_text.Get(CliTextKey.LabelTraceId), traceId);
    }

    /// <summary>渲染提示行（文本模式；JSON 模式静默，避免污染 stdout 的机器可读输出）。</summary>
    /// <param name="message">提示文本。</param>
    /// <exception cref="ArgumentNullException"><paramref name="message"/> 为 <see langword="null"/>。</exception>
    internal void WriteNotice(string message)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (!_json)
        {
            _error.WriteLine(MessageSanitizer.Sanitize(message));
        }
    }

    private static string DispositionText(HookInstallDisposition disposition, CliText text) => disposition switch
    {
        HookInstallDisposition.Installed => text.Get(CliTextKey.DispositionInstalled),
        HookInstallDisposition.Refreshed => text.Get(CliTextKey.DispositionRefreshed),
        HookInstallDisposition.WrappedLegacy => text.Get(CliTextKey.DispositionWrappedLegacy),
        _ => throw new ArgumentOutOfRangeException(nameof(disposition), disposition, "未知的安装处置"),
    };

    private static string DispositionText(HookUninstallDisposition disposition, CliText text) => disposition switch
    {
        HookUninstallDisposition.Removed => text.Get(CliTextKey.DispositionRemoved),
        HookUninstallDisposition.RestoredLegacy => text.Get(CliTextKey.DispositionRestoredLegacy),
        HookUninstallDisposition.NotInstalled => text.Get(CliTextKey.DispositionNotInstalled),
        _ => throw new ArgumentOutOfRangeException(nameof(disposition), disposition, "未知的卸载处置"),
    };

    private static string HookStateText(HookStatusKind kind, CliText text) => kind switch
    {
        HookStatusKind.Missing => text.Get(CliTextKey.HookStateMissing),
        HookStatusKind.Managed => text.Get(CliTextKey.HookStateManaged),
        HookStatusKind.Legacy => text.Get(CliTextKey.HookStateLegacy),
        HookStatusKind.Binary => text.Get(CliTextKey.HookStateBinary),
        HookStatusKind.SymbolicLink => text.Get(CliTextKey.HookStateSymbolicLink),
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "未知的 Hook 状态"),
    };

    private static string HookStateName(HookStatusKind kind) => kind switch
    {
        HookStatusKind.Missing => "Missing",
        HookStatusKind.Managed => "Managed",
        HookStatusKind.Legacy => "Legacy",
        HookStatusKind.Binary => "Binary",
        HookStatusKind.SymbolicLink => "SymbolicLink",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "未知的 Hook 状态"),
    };

    private static string ScanTypeText(ScanType scanType) => scanType switch
    {
        ScanType.PreCommit => "pre-commit",
        ScanType.PrePush => "pre-push",
        ScanType.Manual => "manual",
        ScanType.Bypass => "bypass",
        _ => throw new ArgumentOutOfRangeException(nameof(scanType), scanType, "未知的扫描类型"),
    };

    private static string ScanResultText(ScanResult result) => result switch
    {
        ScanResult.Clean => "Clean",
        ScanResult.Warned => "Warned",
        ScanResult.Blocked => "Blocked",
        ScanResult.Failed => "Failed",
        _ => throw new ArgumentOutOfRangeException(nameof(result), result, "未知的扫描结果"),
    };

    private static string FailureHint(int exitCode, CliText text) => exitCode switch
    {
        ExitCodes.GIT_COMMAND_FAILED => text.Get(CliTextKey.HintGitCommandFailed),
        ExitCodes.DETECTOR_FAILED => text.Get(CliTextKey.HintDetectorFailed),
        ExitCodes.SCAN_TIMEOUT => text.Get(CliTextKey.HintScanTimeout),
        ExitCodes.CONFIG_INVALID => text.Get(CliTextKey.HintConfigInvalid),
        _ => text.Get(CliTextKey.HintScanIncomplete),
    };

    private static string FormatAuditTimestamp(DateTimeOffset value) =>
        value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);

    private static void WriteAllowlistEntryJson(Utf8JsonWriter writer, AllowlistEntry entry)
    {
        writer.WriteStartObject();
        writer.WriteString("id", entry.Id.ToString());
        writer.WriteString("rule_id", entry.RuleId);
        writer.WriteString("path_pattern", entry.PathPattern);
        writer.WriteString("fingerprint", entry.Fingerprint?.Hex);
        writer.WriteString("reason", entry.Reason);
        writer.WriteString("created_at", FormatAuditTimestamp(entry.CreatedAt));
        writer.WriteString("created_by", entry.CreatedBy);
        if (entry.ExpiresAt is { } expiresAt)
        {
            writer.WriteString("expires_at", FormatAuditTimestamp(expiresAt));
        }
        else
        {
            writer.WriteNull("expires_at");
        }

        writer.WriteEndObject();
    }

    private static void WriteFindingJson(
        Utf8JsonWriter writer,
        Finding finding,
        IReadOnlyDictionary<Finding, PolicyDecision> decisions)
    {
        string action = "Allow";
        if (decisions.TryGetValue(finding, out PolicyDecision? decision))
        {
            action = decision.IgnoredByAllowlist
                ? "AllowedByAllowlist"
                : decision.IgnoredByBaseline ? "AllowedByBaseline" : decision.Action.ToString();
        }

        writer.WriteStartObject();
        writer.WriteString("rule_id", finding.RuleId);
        writer.WriteString("severity", finding.Severity.ToString());
        writer.WriteString("file_path", finding.FilePath);
        writer.WriteNumber("line_number", finding.LineNumber);
        writer.WriteString("snippet_masked", finding.SnippetMasked.Value);
        writer.WriteString("fingerprint", finding.Fingerprint.Hex);
        writer.WriteString("action", action);
        if (finding.CommitSha is { } sha)
        {
            writer.WriteString("commit_sha", sha);
        }
        else
        {
            writer.WriteNull("commit_sha");
        }

        writer.WriteEndObject();
    }

    private static void WriteRuleEntryJson(Utf8JsonWriter writer, RuleCatalogEntry rule, bool showPattern)
    {
        writer.WriteStartObject();
        writer.WriteString("rule_id", rule.RuleId);
        writer.WriteString("name", rule.Name);
        writer.WriteString("category", rule.Category);
        writer.WriteString("severity", rule.Severity.ToString());
        writer.WriteString("source", rule.Source.ToString());
        writer.WriteBoolean("enabled", rule.Enabled);
        if (showPattern)
        {
            writer.WriteString("pattern", rule.Pattern.Regex.ToString());
        }

        writer.WriteEndObject();
    }

    private void WriteScanText(ScanOutcome outcome, IReadOnlyList<string> warnings)
    {
        int blocked = 0;
        int warned = 0;
        int ignored = 0;
        foreach (PolicyDecision decision in outcome.Decisions.Values)
        {
            if (decision.IgnoredByAllowlist)
            {
                ignored++;
            }
            else if (decision.Action == PolicyAction.Block)
            {
                blocked++;
            }
            else if (decision.Action == PolicyAction.Warn)
            {
                warned++;
            }
        }

        string result = ScanResultText(outcome.Result);
        if (outcome.Result == ScanResult.Failed)
        {
            WriteColored("✖ ", ConsoleColor.Red);
            _error.WriteLine(_text.Format(CliTextKey.ScanFailedHeader, outcome.ExitCode));
        }
        else if (outcome.Result == ScanResult.Blocked)
        {
            WriteColored("✖ ", ConsoleColor.Red);
            _error.WriteLine(_text.Format(CliTextKey.ScanBlockedHeader, outcome.ExitCode));
        }
        else if (outcome.Result == ScanResult.Warned)
        {
            WriteColored("⚠ ", ConsoleColor.Yellow);
            _error.WriteLine(_text.Get(CliTextKey.ScanWarnedHeader));
        }
        else
        {
            WriteColored("✔ ", ConsoleColor.Green);
            _error.WriteLine(_text.Get(CliTextKey.ScanCleanHeader));
        }

        string summary = _text.Format(
            CliTextKey.ScanSummary,
            result,
            outcome.FilesScanned,
            outcome.Findings.Length,
            blocked,
            warned,
            ignored);
        string durationText = outcome.Run.DurationMs is { } duration
            ? string.Create(CultureInfo.InvariantCulture, $"{duration} ms")
            : _text.Get(CliTextKey.UnknownDuration);

        WriteIndented(_text.Get(CliTextKey.LabelRepo), outcome.Run.RepoPath);
        WriteIndented(_text.Get(CliTextKey.LabelType), ScanTypeText(outcome.Run.ScanType));
        WriteIndented(_text.Get(CliTextKey.LabelResult), summary);
        WriteIndented(_text.Get(CliTextKey.LabelDuration), durationText);

        // FR-060：全量扫描展示历史扫描进度；截断时给出可直接复用的 --since 续扫命令
        if (outcome.FullScanSummary is { } fullScan)
        {
            if (fullScan.IncludeHistory)
            {
                WriteIndented(
                    _text.Get(CliTextKey.LabelHistory),
                    _text.Format(CliTextKey.ScanHistorySummary, fullScan.CommitsScanned, fullScan.RemainingCommits));
                if (fullScan.Truncated && fullScan.NextSinceSha is { } nextSinceSha)
                {
                    WriteIndented(_text.Get(CliTextKey.LabelHint), _text.Format(CliTextKey.ScanHistoryTruncated, nextSinceSha));
                }
            }
            else
            {
                WriteIndented(_text.Get(CliTextKey.LabelHistory), _text.Get(CliTextKey.ScanHistoryDisabled));
            }
        }

        // FR-061：基线生成 / 消费摘要；消费模式下历史噪声数量来自策略判定
        if (outcome.BaselineSummary is { } baseline)
        {
            string baselineText = baseline.Generated
                ? _text.Format(CliTextKey.ScanBaselineGenerated, baseline.Path, baseline.Entries)
                : _text.Format(
                    CliTextKey.ScanBaselineConsumed,
                    baseline.Path,
                    baseline.Entries,
                    baseline.SuppressedFindings);
            WriteIndented(_text.Get(CliTextKey.LabelBaseline), baselineText);
        }

        if (outcome.Result == ScanResult.Failed)
        {
            WriteIndented(_text.Get(CliTextKey.LabelHint), FailureHint(outcome.ExitCode, _text));
        }

        if (!outcome.Findings.IsEmpty)
        {
            WriteIndented(_text.Get(CliTextKey.LabelFindings), string.Empty);
            foreach (Finding finding in outcome.Findings)
            {
                string action = outcome.Decisions.TryGetValue(finding, out PolicyDecision? decision)
                    ? decision.IgnoredByAllowlist
                        ? _text.Get(CliTextKey.FindingAllowlisted)
                        : decision.IgnoredByBaseline ? _text.Get(CliTextKey.FindingBaseline) : decision.Action.ToString()
                    : "Allow";
                string location = string.Create(
                    CultureInfo.InvariantCulture,
                    $"{finding.FilePath}:{finding.LineNumber}");
                _error.WriteLine(string.Create(
                    CultureInfo.InvariantCulture,
                    $"    [{action}] {finding.RuleId}  {location}  {finding.SnippetMasked.Value}"));
                string commit = finding.CommitSha is { } sha
                    ? string.Create(CultureInfo.InvariantCulture, $"  commit={sha}")
                    : string.Empty;
                _error.WriteLine(string.Create(
                    CultureInfo.InvariantCulture,
                    $"      severity={finding.Severity}  fingerprint={finding.Fingerprint.Hex}{commit}"));
            }
        }

        if (!outcome.SkippedFiles.IsEmpty)
        {
            int binary = 0;
            int tooLarge = 0;
            foreach (SkippedFile skipped in outcome.SkippedFiles)
            {
                if (skipped.Reason == SkipReason.Binary)
                {
                    binary++;
                }
                else
                {
                    tooLarge++;
                }
            }

            WriteIndented(
                _text.Get(CliTextKey.LabelSkipped),
                _text.Format(CliTextKey.SkipSummary, outcome.SkippedFiles.Length, binary, tooLarge));
        }

        if (outcome.AuditWriteFailed)
        {
            WriteIndented(_text.Get(CliTextKey.LabelWarning), _text.Get(CliTextKey.AuditWriteFailed));
        }

        foreach (string warning in warnings)
        {
            WriteIndented(_text.Get(CliTextKey.LabelWarning), warning);
        }
    }

    private void WriteScanJson(ScanOutcome outcome, int rulesEvaluated, IReadOnlyList<string> warnings)
    {
        int blocked = 0;
        int warned = 0;
        int ignored = 0;
        int ignoredByBaseline = 0;
        foreach (PolicyDecision decision in outcome.Decisions.Values)
        {
            if (decision.IgnoredByAllowlist)
            {
                ignored++;
            }
            else if (decision.IgnoredByBaseline)
            {
                ignoredByBaseline++;
            }
            else if (decision.Action == PolicyAction.Block)
            {
                blocked++;
            }
            else if (decision.Action == PolicyAction.Warn)
            {
                warned++;
            }
        }

        var buffer = new ArrayBufferWriter<byte>(1024);
        using (var writer = new Utf8JsonWriter(buffer, JsonText.WriterOptions))
        {
            writer.WriteStartObject();
            writer.WriteString("schema", SCAN_SCHEMA);
            writer.WriteString("run_id", outcome.Run.Id.ToString());
            writer.WriteString("repo_path", outcome.Run.RepoPath);
            writer.WriteString("scan_type", outcome.Run.ScanType.ToString());
            writer.WriteString("result", ScanResultText(outcome.Result));
            writer.WriteNumber("exit_code", outcome.ExitCode);
            if (outcome.Run.DurationMs is { } duration)
            {
                writer.WriteNumber("duration_ms", duration);
            }
            else
            {
                writer.WriteNull("duration_ms");
            }

            writer.WriteStartArray("findings");
            foreach (Finding finding in outcome.Findings)
            {
                WriteFindingJson(writer, finding, outcome.Decisions);
            }

            writer.WriteEndArray();

            writer.WriteStartObject("summary");
            writer.WriteNumber("files_scanned", outcome.FilesScanned);
            writer.WriteNumber("rules_evaluated", rulesEvaluated);
            writer.WriteNumber("blocked", blocked);
            writer.WriteNumber("warned", warned);
            writer.WriteNumber("ignored_by_allowlist", ignored);
            writer.WriteNumber("ignored_by_baseline", ignoredByBaseline);
            writer.WriteEndObject();

            // FR-060：仅 --all 携带 full_scan；暂存区 / 推送扫描保持既有契约不变
            if (outcome.FullScanSummary is { } fullScan)
            {
                writer.WriteStartObject("full_scan");
                writer.WriteBoolean("include_history", fullScan.IncludeHistory);
                writer.WriteNumber("commits_scanned", fullScan.CommitsScanned);
                writer.WriteNumber("remaining_commits", fullScan.RemainingCommits);
                writer.WriteBoolean("truncated", fullScan.Truncated);
                writer.WriteString("next_since_sha", fullScan.NextSinceSha);
                writer.WriteEndObject();
            }

            // FR-061：仅涉及基线时输出 baseline 对象（暂存区 / 推送扫描不受影响）
            if (outcome.BaselineSummary is { } baseline)
            {
                writer.WriteStartObject("baseline");
                writer.WriteString("path", baseline.Path);
                writer.WriteBoolean("generated", baseline.Generated);
                writer.WriteNumber("entries", baseline.Entries);
                writer.WriteNumber("suppressed_findings", baseline.SuppressedFindings);
                writer.WriteEndObject();
            }

            writer.WriteStartArray("skipped_files");
            foreach (SkippedFile skipped in outcome.SkippedFiles)
            {
                writer.WriteStartObject();
                writer.WriteString("path", skipped.Path);
                writer.WriteString("reason", skipped.Reason == SkipReason.Binary ? "Binary" : "TooLarge");
                if (skipped.CommitSha is { } sha)
                {
                    writer.WriteString("commit_sha", sha);
                }
                else
                {
                    writer.WriteNull("commit_sha");
                }

                writer.WriteEndObject();
            }

            writer.WriteEndArray();

            writer.WriteBoolean("audit_write_failed", outcome.AuditWriteFailed);
            writer.WriteStartArray("warnings");
            foreach (string warning in warnings)
            {
                writer.WriteStringValue(MessageSanitizer.Sanitize(warning));
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        _output.WriteLine(Encoding.UTF8.GetString(buffer.WrittenSpan));
    }

    private void WriteHookListJson(
        string schema,
        string repoPath,
        string? version,
        bool? notInstalled,
        IReadOnlyList<HookInstallResult> results)
    {
        var buffer = new ArrayBufferWriter<byte>(512);
        using (var writer = new Utf8JsonWriter(buffer, JsonText.WriterOptions))
        {
            writer.WriteStartObject();
            writer.WriteString("schema", schema);
            writer.WriteString("repo_path", repoPath);
            if (version is not null)
            {
                writer.WriteString("version", version);
            }

            if (notInstalled is { } flag)
            {
                writer.WriteBoolean("not_installed", flag);
            }

            writer.WriteStartArray("hooks");
            foreach (HookInstallResult result in results)
            {
                writer.WriteStartObject();
                writer.WriteString("hook", result.HookType.ToHookName());
                writer.WriteString("disposition", result.Disposition.ToString());
                writer.WriteString("hook_path", result.HookPath);
                writer.WriteString("backup_path", result.BackupPath);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        _output.WriteLine(Encoding.UTF8.GetString(buffer.WrittenSpan));
    }

    private void WriteHookListJson(
        string schema,
        string repoPath,
        string? version,
        bool? notInstalled,
        IReadOnlyList<HookUninstallResult> results)
    {
        var buffer = new ArrayBufferWriter<byte>(512);
        using (var writer = new Utf8JsonWriter(buffer, JsonText.WriterOptions))
        {
            writer.WriteStartObject();
            writer.WriteString("schema", schema);
            writer.WriteString("repo_path", repoPath);
            if (version is not null)
            {
                writer.WriteString("version", version);
            }

            if (notInstalled is { } flag)
            {
                writer.WriteBoolean("not_installed", flag);
            }

            writer.WriteStartArray("hooks");
            foreach (HookUninstallResult result in results)
            {
                writer.WriteStartObject();
                writer.WriteString("hook", result.HookType.ToHookName());
                writer.WriteString("disposition", result.Disposition.ToString());
                writer.WriteString("hook_path", result.HookPath);
                writer.WriteString("backup_path", result.BackupPath);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        _output.WriteLine(Encoding.UTF8.GetString(buffer.WrittenSpan));
    }

    private void WriteStatusJson(StatusReport report)
    {
        var buffer = new ArrayBufferWriter<byte>(512);
        using (var writer = new Utf8JsonWriter(buffer, JsonText.WriterOptions))
        {
            writer.WriteStartObject();
            writer.WriteString("schema", STATUS_SCHEMA);
            writer.WriteString("repo_path", report.RepoPath);
            writer.WriteString("gitguard_version", report.GitGuardVersion);
            writer.WriteString("git_version", report.GitVersion);
            writer.WriteString("configured_hooks_path", report.ConfiguredHooksPath);
            writer.WriteString("hooks_directory", report.HooksDirectory);
            writer.WriteStartArray("hooks");
            foreach (HookStatus hook in report.Hooks)
            {
                writer.WriteStartObject();
                writer.WriteString("hook", hook.HookType.ToHookName());
                writer.WriteString("state", HookStateName(hook.Kind));
                writer.WriteString("hook_path", hook.HookPath);
                writer.WriteString("managed_version", hook.ManagedVersion);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();

            writer.WriteStartArray("config_files");
            foreach (string file in report.ConfigFiles)
            {
                writer.WriteStringValue(file);
            }

            writer.WriteEndArray();

            writer.WriteStartArray("warnings");
            foreach (string warning in report.Warnings)
            {
                writer.WriteStringValue(MessageSanitizer.Sanitize(warning));
            }

            writer.WriteEndArray();

            writer.WriteBoolean("needs_attention", report.NeedsAttention);
            writer.WriteEndObject();
        }

        _output.WriteLine(Encoding.UTF8.GetString(buffer.WrittenSpan));
    }

    private void WriteAuditListJson(AuditQuery query, AuditRunPage page)
    {
        var buffer = new ArrayBufferWriter<byte>(1024);
        using (var writer = new Utf8JsonWriter(buffer, JsonText.WriterOptions))
        {
            writer.WriteStartObject();
            writer.WriteString("schema", AUDIT_LIST_SCHEMA);
            writer.WriteNumber("total", page.TotalCount);
            writer.WriteNumber("limit", query.Limit);
            writer.WriteNumber("offset", query.Offset);
            writer.WriteNumber("count", page.Runs.Length);
            writer.WriteStartArray("runs");
            foreach (AuditRunSummary run in page.Runs)
            {
                writer.WriteStartObject();
                writer.WriteString("run_id", run.RunId.ToString());
                writer.WriteString("repo_path", run.RepoPath);
                writer.WriteString("scan_type", run.ScanType.ToString());
                writer.WriteString("started_at", FormatAuditTimestamp(run.StartedAt));
                if (run.DurationMs is { } duration)
                {
                    writer.WriteNumber("duration_ms", duration);
                }
                else
                {
                    writer.WriteNull("duration_ms");
                }

                writer.WriteString("result", ScanResultText(run.Result));
                writer.WriteNumber("exit_code", run.ExitCode);
                writer.WriteNumber("findings_count", run.FindingCount);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        _output.WriteLine(Encoding.UTF8.GetString(buffer.WrittenSpan));
    }

    private void WriteAuditViewJson(ScanRun run)
    {
        var buffer = new ArrayBufferWriter<byte>(1024);
        using (var writer = new Utf8JsonWriter(buffer, JsonText.WriterOptions))
        {
            writer.WriteStartObject();
            writer.WriteString("schema", AUDIT_VIEW_SCHEMA);
            writer.WriteStartObject("run");
            writer.WriteString("run_id", run.Id.ToString());
            writer.WriteString("repo_path", run.RepoPath);
            writer.WriteString("scan_type", run.ScanType.ToString());
            writer.WriteString("started_at", FormatAuditTimestamp(run.StartedAt));
            if (run.DurationMs is { } duration)
            {
                writer.WriteNumber("duration_ms", duration);
            }
            else
            {
                writer.WriteNull("duration_ms");
            }

            writer.WriteString("result", ScanResultText(run.Result));
            writer.WriteNumber("exit_code", run.ExitCode);
            writer.WriteNumber("findings_count", run.Findings.Length);
            writer.WriteEndObject();

            writer.WriteStartArray("findings");
            foreach (Finding finding in run.Findings)
            {
                writer.WriteStartObject();
                writer.WriteString("finding_id", finding.Id.ToString());
                writer.WriteString("rule_id", finding.RuleId);
                writer.WriteString("file_path", finding.FilePath);
                writer.WriteNumber("line_number", finding.LineNumber);
                writer.WriteString("severity", finding.Severity.ToString());
                writer.WriteString("fingerprint", finding.Fingerprint.Hex);
                writer.WriteString("snippet_masked", finding.SnippetMasked.Value);
                if (finding.CommitSha is { } sha)
                {
                    writer.WriteString("commit_sha", sha);
                }
                else
                {
                    writer.WriteNull("commit_sha");
                }

                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        _output.WriteLine(Encoding.UTF8.GetString(buffer.WrittenSpan));
    }

    private string BuildAllowlistConditions(AllowlistEntry entry)
    {
        var conditions = new List<string>(3);
        if (entry.RuleId is { } ruleId)
        {
            conditions.Add(_text.Format(CliTextKey.AllowlistConditionItem, _text.Get(CliTextKey.LabelRule), ruleId));
        }

        if (entry.PathPattern is { } pathPattern)
        {
            conditions.Add(_text.Format(CliTextKey.AllowlistConditionItem, _text.Get(CliTextKey.LabelPath), pathPattern));
        }

        if (entry.Fingerprint is { } fingerprint)
        {
            conditions.Add(_text.Format(CliTextKey.AllowlistConditionItem, _text.Get(CliTextKey.LabelFingerprint), fingerprint.Hex));
        }

        return string.Join(_text.Get(CliTextKey.AllowlistConditionSeparator), conditions);
    }

    private void WriteAllowlistAddJson(AllowlistEntry entry)
    {
        var buffer = new ArrayBufferWriter<byte>(512);
        using (var writer = new Utf8JsonWriter(buffer, JsonText.WriterOptions))
        {
            writer.WriteStartObject();
            writer.WriteString("schema", ALLOWLIST_ADD_SCHEMA);
            writer.WritePropertyName("entry");
            WriteAllowlistEntryJson(writer, entry);
            writer.WriteEndObject();
        }

        _output.WriteLine(Encoding.UTF8.GetString(buffer.WrittenSpan));
    }

    private void WriteAllowlistRemoveJson(Guid id)
    {
        var buffer = new ArrayBufferWriter<byte>(256);
        using (var writer = new Utf8JsonWriter(buffer, JsonText.WriterOptions))
        {
            writer.WriteStartObject();
            writer.WriteString("schema", ALLOWLIST_REMOVE_SCHEMA);
            writer.WriteString("id", id.ToString());
            writer.WriteBoolean("removed", true);
            writer.WriteEndObject();
        }

        _output.WriteLine(Encoding.UTF8.GetString(buffer.WrittenSpan));
    }

    private void WriteAllowlistListJson(AllowlistQuery query, AllowlistPage page)
    {
        var buffer = new ArrayBufferWriter<byte>(1024);
        using (var writer = new Utf8JsonWriter(buffer, JsonText.WriterOptions))
        {
            writer.WriteStartObject();
            writer.WriteString("schema", ALLOWLIST_LIST_SCHEMA);
            writer.WriteNumber("total", page.TotalCount);
            writer.WriteNumber("limit", query.Limit);
            writer.WriteNumber("offset", query.Offset);
            writer.WriteNumber("count", page.Entries.Length);
            writer.WriteStartArray("entries");
            foreach (AllowlistEntry entry in page.Entries)
            {
                WriteAllowlistEntryJson(writer, entry);
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        _output.WriteLine(Encoding.UTF8.GetString(buffer.WrittenSpan));
    }

    private void WriteRulesListJson(
        IReadOnlyList<RuleCatalogEntry> rules,
        string? category,
        bool enabledOnly,
        bool showPattern)
    {
        var buffer = new ArrayBufferWriter<byte>(1024);
        using (var writer = new Utf8JsonWriter(buffer, JsonText.WriterOptions))
        {
            writer.WriteStartObject();
            writer.WriteString("schema", RULES_LIST_SCHEMA);
            writer.WriteNumber("total", rules.Count);
            writer.WriteNumber("count", rules.Count);
            writer.WriteString("category", category);
            writer.WriteBoolean("enabled_only", enabledOnly);
            writer.WriteStartArray("rules");
            foreach (RuleCatalogEntry rule in rules)
            {
                WriteRuleEntryJson(writer, rule, showPattern);
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        _output.WriteLine(Encoding.UTF8.GetString(buffer.WrittenSpan));
    }

    private void WriteRuleJson(RuleCatalogEntry rule, bool showPattern)
    {
        var buffer = new ArrayBufferWriter<byte>(512);
        using (var writer = new Utf8JsonWriter(buffer, JsonText.WriterOptions))
        {
            writer.WriteStartObject();
            writer.WriteString("schema", RULES_VIEW_SCHEMA);
            writer.WritePropertyName("rule");
            WriteRuleEntryJson(writer, rule, showPattern);
            writer.WriteEndObject();
        }

        _output.WriteLine(Encoding.UTF8.GetString(buffer.WrittenSpan));
    }

    private void WriteAuditExportJson(AuditExportReport report)
    {
        var buffer = new ArrayBufferWriter<byte>(256);
        using (var writer = new Utf8JsonWriter(buffer, JsonText.WriterOptions))
        {
            writer.WriteStartObject();
            writer.WriteString("schema", AUDIT_EXPORT_SUMMARY_SCHEMA);
            writer.WriteString("output_path", report.OutputPath);
            writer.WriteString("format", report.Format == AuditExportFormat.Json ? "json" : "csv");
            writer.WriteNumber("run_count", report.RunCount);
            writer.WriteNumber("finding_count", report.FindingCount);
            writer.WriteEndObject();
        }

        _output.WriteLine(Encoding.UTF8.GetString(buffer.WrittenSpan));
    }

    private void WriteVersionJson(VersionInfo version)
    {
        var buffer = new ArrayBufferWriter<byte>(256);
        using (var writer = new Utf8JsonWriter(buffer, JsonText.WriterOptions))
        {
            writer.WriteStartObject();
            writer.WriteString("schema", VERSION_SCHEMA);
            writer.WriteString("gitguard_version", version.GitGuardVersion);
            writer.WriteNumber("rules_version", version.RulesVersion);
            writer.WriteString("git_version", version.GitVersionLine);
            writer.WriteString("git_failure", version.GitFailureMessage);
            writer.WriteStartObject("runtime");
            writer.WriteString("framework", version.FrameworkDescription);
            writer.WriteString("runtime_identifier", version.RuntimeIdentifier);
            writer.WriteEndObject();
            writer.WriteEndObject();
        }

        _output.WriteLine(Encoding.UTF8.GetString(buffer.WrittenSpan));
    }

    private void WriteErrorJson(int exitCode, string errorCode, string message, string traceId)
    {
        var buffer = new ArrayBufferWriter<byte>(256);
        using (var writer = new Utf8JsonWriter(buffer, JsonText.WriterOptions))
        {
            writer.WriteStartObject();
            writer.WriteString("schema", ERROR_SCHEMA);
            writer.WriteString("error_code", errorCode);
            writer.WriteNumber("exit_code", exitCode);
            writer.WriteString("message", message);
            writer.WriteString("trace_id", traceId);
            writer.WriteEndObject();
        }

        _output.WriteLine(Encoding.UTF8.GetString(buffer.WrittenSpan));
    }

    private void WriteIndented(string? label, string value)
    {
        string text = label is null
            ? string.Create(CultureInfo.InvariantCulture, $"  {value}")
            : string.Concat("  ", label, _text.Get(CliTextKey.ValueSeparator), value);
        _error.WriteLine(text);
    }

    private void WriteColored(string prefix, ConsoleColor color)
    {
        if (_color)
        {
            Console.ForegroundColor = color;
            _error.Write(prefix);
            Console.ResetColor();
        }
        else
        {
            _error.Write(prefix);
        }
    }
}
