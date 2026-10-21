using System.Collections.Immutable;
using System.CommandLine;
using GitGuard.Application.Scanning;
using GitGuard.Cli.Localization;
using GitGuard.Cli.Logging;
using GitGuard.Core;

namespace GitGuard.Cli.Commands;

/// <summary>
/// <c>gitguard scan</c>（<c>06-API接口设计</c> §5.4）：交付 <c>--staged</c>（FR-020）与 <c>--push</c>（FR-030 推送范围）
/// 两个入口，二者必居其一；<c>--all</c> / <c>--since</c> / <c>--max-files</c>（历史扫描）属 v0.2。
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
        var recordPendingOption = new Option<bool>("--record-pending")
        {
            Description = runtime.Text.Get(CliTextKey.ScanRecordPendingDescription),
            Hidden = true,
        };

        var command = new Command("scan", runtime.Text.Get(CliTextKey.ScanDescription));
        command.Options.Add(stagedOption);
        command.Options.Add(pushOption);
        command.Options.Add(recordPendingOption);

        Func<ParseResult, CancellationToken, Task<int>> action = (parseResult, cancellationToken) =>
            ExecuteAsync(runtime, parseResult, stagedOption, pushOption, recordPendingOption, cancellationToken);
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

    private static async Task<int> ExecuteAsync(
        CliRuntime runtime,
        ParseResult parseResult,
        Option<bool> stagedOption,
        Option<bool> pushOption,
        Option<bool> recordPendingOption,
        CancellationToken cancellationToken)
    {
        bool staged = parseResult.GetValue(stagedOption);
        bool push = parseResult.GetValue(pushOption);
        bool recordPending = parseResult.GetValue(recordPendingOption);
        if (staged == push)
        {
            return UsageError.Report(runtime, runtime.Text.Get(CliTextKey.ScanScopeRequired));
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

        ScanOutcome outcome;
        if (staged)
        {
            outcome = await runtime.Session.CreateStagedScan().ExecuteAsync(cancellationToken).ConfigureAwait(false);
        }
        else
        {
            string pushStdin = await runtime.Host.Input.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
            outcome = await runtime.Session.CreatePushScan().ExecuteAsync(pushStdin, cancellationToken).ConfigureAwait(false);
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
        runtime.Logger.Log(
            CliLogLevel.Information,
            staged ? "Scan.Staged" : "Scan.Push",
            runtime.Text.Get(CliTextKey.LogScanCompleted),
            new LogFields
            {
                RunId = outcome.Run.Id.ToString(),
                RepoPath = outcome.Run.RepoPath,
                ExitCode = outcome.ExitCode,
                DurationMs = outcome.Run.DurationMs,
                TraceId = runtime.TraceId,
            });
        return outcome.ExitCode;
    }
}
