using System.CommandLine;
using GitGuard.Application.Bypass;
using GitGuard.Cli.Localization;
using GitGuard.Cli.Logging;

namespace GitGuard.Cli.Commands;

/// <summary>
/// <c>gitguard hook</c>（内部命令，不在帮助中展示）：供安装的 Git 分发脚本调用。
/// MVP 仅交付 <c>hook post-commit</c>（FR-050）：对比 pre-commit 记录的待提交预测与实际提交，
/// 未匹配或标记为跳过时登记 <c>BypassDetected</c> 审计事件。命令始终以 0 返回（Git 忽略 post-commit 退出码），
/// 审计写入失败等 FailClosed 情形由异常映射为对应退出码。
/// </summary>
internal static class HookCommand
{
    /// <summary>创建隐藏的 <c>hook</c> 命令组。</summary>
    /// <param name="runtime">运行时上下文。</param>
    /// <returns>已接线动作的命令组。</returns>
    /// <exception cref="ArgumentNullException"><paramref name="runtime"/> 为 <see langword="null"/>。</exception>
    internal static Command Create(CliRuntime runtime)
    {
        ArgumentNullException.ThrowIfNull(runtime);

        var command = new Command("hook", runtime.Text.Get(CliTextKey.HookDescription))
        {
            Hidden = true,
        };
        command.Subcommands.Add(CreatePostCommit(runtime));
        return command;
    }

    private static Command CreatePostCommit(CliRuntime runtime)
    {
        var command = new Command("post-commit", runtime.Text.Get(CliTextKey.HookPostCommitDescription))
        {
            Hidden = true,
        };
        command.SetAction((_, cancellationToken) => ExecutePostCommitAsync(runtime, cancellationToken));
        return command;
    }

    private static Task<int> ExecutePostCommitAsync(CliRuntime runtime, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        BypassDetectionResult result = runtime.Session.CreateBypassDetection().Detect();
        runtime.Writer.WriteBypass(result);
        runtime.Logger.Log(
            CliLogLevel.Information,
            result.Detected ? "Bypass.Detected" : "Bypass.Checked",
            runtime.Text.Get(result.Detected ? CliTextKey.LogBypassDetected : CliTextKey.LogBypassChecked),
            new LogFields
            {
                RunId = result.RunId?.ToString(),
                ExitCode = ExitCodes.SUCCESS,
                TraceId = runtime.TraceId,
            });
        return Task.FromResult(ExitCodes.SUCCESS);
    }
}
