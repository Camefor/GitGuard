using System.CommandLine;
using GitGuard.Application.Hooks;
using GitGuard.Cli.Logging;

namespace GitGuard.Cli.Commands;

/// <summary>
/// <c>gitguard status</c>（<c>06-API接口设计</c> §5.3 / FR-012）：只读查询 Git 版本、<c>core.hooksPath</c>、
/// 两个 Hook 的文件签名、GitGuard 版本与配置加载链；存在未被 GitGuard 托管的 Hook 时退出码 6。
/// </summary>
internal static class StatusCommand
{
    /// <summary>创建 <c>status</c> 子命令。</summary>
    /// <param name="runtime">运行时上下文。</param>
    /// <returns>已接线动作的子命令（动作闭包捕获 <paramref name="runtime"/>）。</returns>
    /// <exception cref="ArgumentNullException"><paramref name="runtime"/> 为 <see langword="null"/>。</exception>
    internal static Command Create(CliRuntime runtime)
    {
        ArgumentNullException.ThrowIfNull(runtime);

        var command = new Command("status", "查询当前仓库的 Hook 状态、Git 与配置信息");

        Func<ParseResult, int> action = _ => Execute(runtime);
        command.SetAction(action);
        return command;
    }

    private static int Execute(CliRuntime runtime)
    {
        StatusReport report = runtime.Session.CreateStatus().Execute();
        int exitCode = report.NeedsAttention ? ExitCodes.HOOK_NOT_MANAGED : ExitCodes.SUCCESS;

        runtime.Writer.WriteStatus(report);
        runtime.Logger.Log(
            CliLogLevel.Debug,
            "Hook.Status",
            "状态查询完成",
            new LogFields { RepoPath = report.RepoPath, ExitCode = exitCode, TraceId = runtime.TraceId });
        return exitCode;
    }
}
