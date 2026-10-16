using System.CommandLine;
using GitGuard.Application.Scanning;
using GitGuard.Cli.Logging;

namespace GitGuard.Cli.Commands;

/// <summary>
/// <c>gitguard scan</c>（<c>06-API接口设计</c> §5.4）：MVP 交付 <c>--staged</c>（FR-020）与 <c>--push</c>（FR-030 推送范围）
/// 两个入口，二者必居其一；<c>--all</c> / <c>--since</c> / <c>--max-files</c>（历史扫描）随 FR-030 全量交付实现，
/// 当前按未识别选项由框架拒绝（用法错误 7）。
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
            Description = "扫描暂存区（pre-commit 语义；Hook 与手动调用共用）",
        };
        var pushOption = new Option<bool>("--push")
        {
            Description = "扫描推送范围；从 stdin 读取 Git 推送信息行",
        };

        var command = new Command("scan", "扫描 Git 变更内容（合并后的 .gitguard.yml 策略与白名单生效）");
        command.Options.Add(stagedOption);
        command.Options.Add(pushOption);

        Func<ParseResult, CancellationToken, Task<int>> action = (parseResult, cancellationToken) =>
            ExecuteAsync(runtime, parseResult, stagedOption, pushOption, cancellationToken);
        command.SetAction(action);
        return command;
    }

    private static async Task<int> ExecuteAsync(
        CliRuntime runtime,
        ParseResult parseResult,
        Option<bool> stagedOption,
        Option<bool> pushOption,
        CancellationToken cancellationToken)
    {
        bool staged = parseResult.GetValue(stagedOption);
        bool push = parseResult.GetValue(pushOption);
        if (staged == push)
        {
            return UsageError.Report(runtime, "scan 必须且只能指定 --staged 或 --push 之一。");
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

        runtime.Writer.WriteScan(outcome, runtime.Session.RuleCount, runtime.Session.Warnings);
        runtime.Logger.Log(
            CliLogLevel.Information,
            staged ? "Scan.Staged" : "Scan.Push",
            "扫描完成",
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
