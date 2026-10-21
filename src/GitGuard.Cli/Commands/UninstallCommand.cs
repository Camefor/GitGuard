using System.CommandLine;
using GitGuard.Application.Hooks;
using GitGuard.Cli.Localization;
using GitGuard.Cli.Logging;
using GitGuard.Core.Model;

namespace GitGuard.Cli.Commands;

/// <summary>
/// <c>gitguard uninstall</c>（<c>06-API接口设计</c> §5.2 / FR-011）：删除 GitGuard 分发脚本并还原
/// <c>&lt;hook&gt;.local.bak</c>；全部目标均无 GitGuard 安装痕迹时不改动任何文件，退出码 5（非故障提示）。
/// </summary>
internal static class UninstallCommand
{
    /// <summary>创建 <c>uninstall</c> 子命令。</summary>
    /// <param name="runtime">运行时上下文。</param>
    /// <returns>已接线动作的子命令（动作闭包捕获 <paramref name="runtime"/>）。</returns>
    /// <exception cref="ArgumentNullException"><paramref name="runtime"/> 为 <see langword="null"/>。</exception>
    internal static Command Create(CliRuntime runtime)
    {
        ArgumentNullException.ThrowIfNull(runtime);

        var forceOption = new Option<bool>("--force")
        {
            Description = runtime.Text.Get(CliTextKey.UninstallForceDescription),
        };
        var hookOption = new Option<string>("--hook")
        {
            Description = runtime.Text.Get(CliTextKey.UninstallHookDescription),
        };

        var command = new Command("uninstall", runtime.Text.Get(CliTextKey.UninstallDescription));
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

        HookUninstallReport report = runtime.Session.CreateUninstall().Execute(force, hooks);
        bool notInstalled = report.Results.All(static result => result.Disposition == HookUninstallDisposition.NotInstalled);

        runtime.Writer.WriteUninstall(report, notInstalled);
        int exitCode = notInstalled ? ExitCodes.HOOK_NOT_INSTALLED : ExitCodes.SUCCESS;
        runtime.Logger.Log(
            notInstalled ? CliLogLevel.Warning : CliLogLevel.Information,
            "Hook.Uninstall",
            runtime.Text.Get(notInstalled ? CliTextKey.LogHookNotInstalled : CliTextKey.LogHookUninstalled),
            new LogFields { RepoPath = report.RepoPath, ExitCode = exitCode, TraceId = runtime.TraceId });
        return exitCode;
    }
}
