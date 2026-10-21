using System.CommandLine;
using GitGuard.Application.Composition;
using GitGuard.Cli.Localization;
using GitGuard.Cli.Logging;
using GitGuard.Cli.Reporting;

namespace GitGuard.Cli.Commands;

/// <summary>
/// <c>gitguard version</c>（<c>06-API接口设计</c> §5.8）：展示 GitGuard 版本、规则库版本、本机 git 版本与运行时信息。
/// 只读取程序集版本与独立 git 探测，不加载配置、不要求仓库（任意目录可用）。
/// </summary>
internal static class VersionCommand
{
    /// <summary>创建 <c>version</c> 子命令。</summary>
    /// <param name="runtime">运行时上下文。</param>
    /// <returns>已接线动作的子命令（动作闭包捕获 <paramref name="runtime"/>）。</returns>
    /// <exception cref="ArgumentNullException"><paramref name="runtime"/> 为 <see langword="null"/>。</exception>
    internal static Command Create(CliRuntime runtime)
    {
        ArgumentNullException.ThrowIfNull(runtime);

        var command = new Command("version", runtime.Text.Get(CliTextKey.VersionDescription));

        Func<ParseResult, int> action = _ => Execute(runtime);
        command.SetAction(action);
        return command;
    }

    private static int Execute(CliRuntime runtime)
    {
        var version = new VersionInfo(
            runtime.Session.Version,
            GitGuardSession.RulesVersion,
            runtime.Session.GitVersionLine,
            runtime.Session.GitFailureMessage);

        runtime.Writer.WriteVersion(version);
        runtime.Logger.Log(
            CliLogLevel.Debug,
            "Cli.Version",
            runtime.Text.Get(CliTextKey.LogVersionReported),
            new LogFields { ExitCode = ExitCodes.SUCCESS, TraceId = runtime.TraceId });
        return ExitCodes.SUCCESS;
    }
}
