using System.Collections.Immutable;
using System.CommandLine;
using GitGuard.Application.Scanning;
using GitGuard.Cli.Localization;
using GitGuard.Cli.Logging;
using GitGuard.Core;

namespace GitGuard.Cli.Commands;

/// <summary>
/// <c>gitguard scan</c>（<c>06-API接口设计</c> §5.4）：交付 <c>--staged</c>（FR-020）、<c>--push</c>（FR-030 推送范围）
/// 与 <c>--all</c>（FR-060 全量扫描）三个入口，三选一；<c>--include-history</c> / <c>--since</c> / <c>--max-files</c>
/// 仅用于 <c>--all</c> 的历史扫描（后两者同时隐含历史模式）。
/// 隐藏选项 <c>--record-pending</c> 仅供 Hook 分发脚本使用：扫描通过（或按 <c>GITGUARD_SKIP=1</c> 跳过）后
/// 记录待提交预测，供 post-commit 绕过检测比对（FR-050）。
/// </summary>
internal static class ScanCommand
{
    /// <summary>创建 <c>scan</c> 子命令。</summary>
    /// <param name="runtime">运行时上下文。</param>
    /// <returns>已接线动作的子命令（动作闭包捕获 <paramref name="runtime"/>）。</returns>
    /// <exception cref="ArgumentNullException"><paramref name="runtime"/> 为 <see langword="null"/>。</exception>
    internal static Command Create(CliRuntime runtime)
    {
        ArgumentNullException.ThrowIfNull(runtime);

        var stagedOption = new Option<bool>("--staged")
        {
            Description = runtime.Text.Get(CliTextKey.ScanStagedDescription),
        };
        var pushOption = new Option<bool>("--push")
        {
            Description = runtime.Text.Get(CliTextKey.ScanPushDescription),
        };
        var allOption = new Option<bool>("--all")
        {
            Description = runtime.Text.Get(CliTextKey.ScanAllDescription),
        };
        var includeHistoryOption = new Option<bool>("--include-history")
        {
            Description = runtime.Text.Get(CliTextKey.ScanIncludeHistoryDescription),
        };
        var sinceOption = new Option<string?>("--since")
        {
            Description = runtime.Text.Get(CliTextKey.ScanSinceDescription),
        };
        var maxFilesOption = new Option<int?>("--max-files")
        {
            Description = runtime.Text.Get(CliTextKey.ScanMaxFilesDescription),
        };
        var baselineOption = new Option<string?>("--baseline")
        {
            Description = runtime.Text.Get(CliTextKey.ScanBaselineDescription),
        };
        var recordPendingOption = new Option<bool>("--record-pending")
        {
            Description = runtime.Text.Get(CliTextKey.ScanRecordPendingDescription),
            Hidden = true,
        };

        var command = new Command("scan", runtime.Text.Get(CliTextKey.ScanDescription));
        command.Options.Add(stagedOption);
        command.Options.Add(pushOption);
        command.Options.Add(allOption);
        command.Options.Add(includeHistoryOption);
        command.Options.Add(sinceOption);
        command.Options.Add(maxFilesOption);
        command.Options.Add(baselineOption);
        command.Options.Add(recordPendingOption);

        Func<ParseResult, CancellationToken, Task<int>> action = (parseResult, cancellationToken) =>
            ExecuteAsync(
                runtime,
                parseResult,
                stagedOption,
                pushOption,
                allOption,
                includeHistoryOption,
                sinceOption,
                maxFilesOption,
                baselineOption,
                recordPendingOption,
                cancellationToken);
        command.SetAction(action);
        return command;
    }

    /// <summary>记录或清理待提交预测；失败降级为警告（不阻断提交）。</summary>
    /// <param name="runtime">运行时上下文。</param>
    /// <param name="skipped"><see langword="true"/> / <see langword="false"/> 记录标记；<see langword="null"/> 清理标记。</param>
    /// <returns>失败原因（已脱敏的警告文案）；成功时为 <see langword="null"/>。</returns>
    private static string? TryUpdatePendingScan(CliRuntime runtime, bool? skipped)
    {
        try
        {
            var recorder = runtime.Session.CreatePendingScanRecorder();
            if (skipped is { } skip)
            {
                recorder.Record(skip);
            }
            else
            {
                recorder.Clear();
            }

            return null;
        }
        catch (Exception ex) when (ex is GitException or IOException or UnauthorizedAccessException)
        {
            return runtime.Text.Format(
                skipped is null ? CliTextKey.PendingClearFailed : CliTextKey.PendingWriteFailed,
                ex.GetType().Name);
        }
    }

    /// <summary>输出规则级耗时摘要（NFR-M-002；仅 Debug 级别启用）。</summary>
    /// <param name="runtime">运行时上下文。</param>
    /// <param name="outcome">扫描结果（携带规则耗时）。</param>
    private static void WriteRuleTimings(CliRuntime runtime, ScanOutcome outcome)
    {
        foreach (RuleTiming timing in outcome.RuleTimings)
        {
            long durationUs = (long)Math.Round(timing.ElapsedMicroseconds, MidpointRounding.AwayFromZero);
            runtime.Logger.Log(
                CliLogLevel.Debug,
                "Scan.RuleTiming",
                runtime.Text.Format(
                    CliTextKey.LogRuleTiming,
                    timing.RuleId,
                    timing.EvaluationCount,
                    timing.HitCount,
                    durationUs),
                new LogFields
                {
                    RuleId = timing.RuleId,
                    DurationUs = durationUs,
                    TraceId = runtime.TraceId,
                });
        }
    }

    private static async Task<int> ExecuteAsync(
        CliRuntime runtime,
        ParseResult parseResult,
        Option<bool> stagedOption,
        Option<bool> pushOption,
        Option<bool> allOption,
        Option<bool> includeHistoryOption,
        Option<string?> sinceOption,
        Option<int?> maxFilesOption,
        Option<string?> baselineOption,
        Option<bool> recordPendingOption,
        CancellationToken cancellationToken)
    {
        bool staged = parseResult.GetValue(stagedOption);
        bool push = parseResult.GetValue(pushOption);
        bool all = parseResult.GetValue(allOption);
        bool includeHistory = parseResult.GetValue(includeHistoryOption);
        string? since = parseResult.GetValue(sinceOption);
        int? maxFiles = parseResult.GetValue(maxFilesOption);
        string? baseline = parseResult.GetValue(baselineOption);
        bool recordPending = parseResult.GetValue(recordPendingOption);

        int scopeCount = (staged ? 1 : 0) + (push ? 1 : 0) + (all ? 1 : 0);
        if (scopeCount != 1)
        {
            return UsageError.Report(runtime, runtime.Text.Get(CliTextKey.ScanScopeRequired));
        }

        if (!all && (includeHistory || since is not null || maxFiles is not null))
        {
            return UsageError.Report(runtime, runtime.Text.Get(CliTextKey.ScanHistoryRequiresAll));
        }

        if (!all && baseline is not null)
        {
            return UsageError.Report(runtime, runtime.Text.Get(CliTextKey.ScanBaselineRequiresAll));
        }

        if (maxFiles is <= 0)
        {
            return UsageError.Report(runtime, runtime.Text.Get(CliTextKey.ScanMaxFilesInvalid));
        }

        if (since is not null && (string.IsNullOrWhiteSpace(since) || since.StartsWith('-')))
        {
            return UsageError.Report(runtime, runtime.Text.Get(CliTextKey.ScanSinceInvalid));
        }

        if (baseline is not null && (string.IsNullOrWhiteSpace(baseline) || baseline.StartsWith('-')))
        {
            return UsageError.Report(runtime, runtime.Text.Get(CliTextKey.ScanBaselineInvalid));
        }

        if (recordPending && !staged)
        {
            return UsageError.Report(runtime, runtime.Text.Get(CliTextKey.RecordPendingRequiresStaged));
        }

        // FR-050：仅在 Hook 模式（--record-pending）识别 GITGUARD_SKIP=1，手动扫描不受环境变量影响
        if (recordPending && string.Equals(runtime.Host.SkipEnvironment?.Trim(), "1", StringComparison.Ordinal))
        {
            string? skipWarning = TryUpdatePendingScan(runtime, skipped: true);
            if (skipWarning is not null)
            {
                runtime.Writer.WriteNotice(skipWarning);
            }

            runtime.Writer.WriteNotice(runtime.Text.Get(CliTextKey.NoticeSkip));
            runtime.Logger.Log(
                CliLogLevel.Information,
                "Scan.Skipped",
                runtime.Text.Get(CliTextKey.LogScanSkipped),
                new LogFields
                {
                    ExitCode = ExitCodes.SUCCESS,
                    TraceId = runtime.TraceId,
                });
            return ExitCodes.SUCCESS;
        }

        // NFR-M-002：--verbose（Debug）才采集规则耗时，普通扫描不付出计时开销
        bool collectRuleTimings = runtime.Logger.IsEnabled(CliLogLevel.Debug);

        ScanOutcome outcome;
        if (staged)
        {
            outcome = await runtime.Session.CreateStagedScan(collectRuleTimings).ExecuteAsync(cancellationToken).ConfigureAwait(false);
        }
        else if (push)
        {
            string pushStdin = await runtime.Host.Input.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
            outcome = await runtime.Session.CreatePushScan(collectRuleTimings).ExecuteAsync(pushStdin, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            var allOptions = new ScanAllOptions
            {
                IncludeHistory = includeHistory,
                Since = since,
                MaxFiles = maxFiles,
                BaselinePath = baseline,
            };
            outcome = await runtime.Session.CreateFullScan(collectRuleTimings).ExecuteAsync(allOptions, cancellationToken).ConfigureAwait(false);
        }

        // FR-050：扫描通过才记录预测；被阻断 / 失败时清理旧标记，避免残留标记掩盖后续真实绕过
        string? pendingWarning = recordPending
            ? TryUpdatePendingScan(runtime, skipped: outcome.ExitCode == ExitCodes.SUCCESS ? false : null)
            : null;

        ImmutableArray<string> warnings = runtime.Session.Warnings;
        if (pendingWarning is not null)
        {
            warnings = warnings.Add(pendingWarning);
        }

        runtime.Writer.WriteScan(outcome, runtime.Session.RuleCount, warnings);
        string scanEvent = staged ? "Scan.Staged" : push ? "Scan.Push" : "Scan.All";
        runtime.Logger.Log(
            CliLogLevel.Information,
            scanEvent,
            runtime.Text.Get(CliTextKey.LogScanCompleted),
            new LogFields
            {
                RunId = outcome.Run.Id.ToString(),
                RepoPath = outcome.Run.RepoPath,
                ExitCode = outcome.ExitCode,
                DurationMs = outcome.Run.DurationMs,
                TraceId = runtime.TraceId,
            });

        if (collectRuleTimings)
        {
            WriteRuleTimings(runtime, outcome);
        }

        return outcome.ExitCode;
    }
}
