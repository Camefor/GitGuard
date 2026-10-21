using System.CommandLine;
using GitGuard.Application.Hooks;
using GitGuard.Cli.Localization;
using GitGuard.Cli.Logging;
using GitGuard.Core.Model;

namespace GitGuard.Cli.Commands;

/// <summary>
/// <c>gitguard install</c>（<c>06-API接口设计</c> §5.1 / FR-010）：在当前仓库写入 pre-commit / pre-push 分发脚本。
/// 分发脚本固化本进程可执行文件路径（08-权限设计 §6）；失败语义全部由用例异常经
/// <see cref="ExitCodeMapper"/> 映射（3 写入权限 / 4 需 <c>--force</c> / 50 安装锁冲突）。
/// </summary>
internal static class InstallCommand
{
    /// <summary>创建 <c>install</c> 子命令。</summary>
    /// <param name="runtime">运行时上下文。</param>
    /// <returns>已接线动作的子命令（动作闭包捕获 <paramref name="runtime"/>）。</returns>
    /// <exception cref="ArgumentNullException"><paramref name="runtime"/> 为 <see langword="null"/>。</exception>
    internal static Command Create(CliRuntime runtime)
    {
        ArgumentNullException.ThrowIfNull(runtime);

        var forceOption = new Option<bool>("--force")
        {
            Description = runtime.Text.Get(CliTextKey.InstallForceDescription),
        };
        var hookOption = new Option<string>("--hook")
        {
            Description = runtime.Text.Get(CliTextKey.InstallHookDescription),
        };

        var command = new Command("install", runtime.Text.Get(CliTextKey.InstallDescription));
        command.Options.Add(forceOption);
        command.Options.Add(hookOption);

        Func<ParseResult, int> action = parseResult => Execute(runtime, parseResult, forceOption, hookOption);
        command.SetAction(action);
        return command;
    }

    private static int Execute(
        CliRuntime runtime,
        ParseResult parseResult,
        Option<bool> forceOption,
        Option<string> hookOption)
    {
        bool force = parseResult.GetValue(forceOption);
        IReadOnlyList<HookType>? hooks = HookNames.Resolve(parseResult.GetValue(hookOption), runtime.Text, out string? hookError);
        if (hookError is not null)
        {
            return UsageError.Report(runtime, hookError);
        }

        string executablePath = runtime.Host.ExecutablePath ?? CliHost.ResolveExecutablePath();
        HookInstallReport report = runtime.Session.CreateInstall().Execute(executablePath, force, hooks);

        runtime.Writer.WriteInstall(report);
        runtime.Logger.Log(
            CliLogLevel.Information,
            "Hook.Install",
            runtime.Text.Get(CliTextKey.LogHookInstalled),
            new LogFields { RepoPath = report.RepoPath, ExitCode = ExitCodes.SUCCESS, TraceId = runtime.TraceId });
        return ExitCodes.SUCCESS;
    }
}
