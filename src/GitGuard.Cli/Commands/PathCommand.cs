using System.CommandLine;
using GitGuard.Application.Paths;
using GitGuard.Cli.Localization;
using GitGuard.Cli.Logging;
using GitGuard.Core;
using GitGuard.Core.Model;

namespace GitGuard.Cli.Commands;

/// <summary>
/// <c>gitguard path install / uninstall / status</c>（GGD-0061 / <c>06-API接口设计</c> §5.8）：
/// 用户级 PATH 管理。安装把 GitGuard 可执行文件所在目录写入当前用户 PATH（Windows 用户环境变量；
/// Unix 为 <c>~/.local/bin/gitguard</c> 符号链接，可选用 <c>--update-profile</c> 写入 Shell profile 标记块）；
/// 卸载只移除 GitGuard 托管痕迹；状态查询区分已登记 / 持久化条目 / 当前进程生效。
/// </summary>
internal static class PathCommand
{
    /// <summary>创建 <c>path</c> 子命令及其 <c>install</c> / <c>uninstall</c> / <c>status</c> 子命令。</summary>
    /// <param name="runtime">运行时上下文。</param>
    /// <returns>已接线动作的子命令（动作闭包捕获 <paramref name="runtime"/>）。</returns>
    /// <exception cref="ArgumentNullException"><paramref name="runtime"/> 为 <see langword="null"/>。</exception>
    internal static Command Create(CliRuntime runtime)
    {
        ArgumentNullException.ThrowIfNull(runtime);

        var command = new Command("path", runtime.Text.Get(CliTextKey.PathDescription));
        command.Subcommands.Add(CreateInstall(runtime));
        command.Subcommands.Add(CreateUninstall(runtime));
        command.Subcommands.Add(CreateStatus(runtime));
        return command;
    }

    private static Command CreateInstall(CliRuntime runtime)
    {
        var forceOption = new Option<bool>("--force")
        {
            Description = runtime.Text.Get(CliTextKey.PathInstallForceDescription),
        };
        var updateProfileOption = new Option<bool>("--update-profile")
        {
            Description = runtime.Text.Get(CliTextKey.PathInstallUpdateProfileDescription),
        };

        var command = new Command("install", runtime.Text.Get(CliTextKey.PathInstallDescription));
        command.Options.Add(forceOption);
        command.Options.Add(updateProfileOption);

        Func<ParseResult, int> action = parseResult => ExecuteInstall(
            runtime,
            parseResult.GetValue(forceOption),
            parseResult.GetValue(updateProfileOption));
        command.SetAction(action);
        return command;
    }

    private static Command CreateUninstall(CliRuntime runtime)
    {
        var command = new Command("uninstall", runtime.Text.Get(CliTextKey.PathUninstallDescription));

        Func<ParseResult, int> action = _ => ExecuteUninstall(runtime);
        command.SetAction(action);
        return command;
    }

    private static Command CreateStatus(CliRuntime runtime)
    {
        var command = new Command("status", runtime.Text.Get(CliTextKey.PathStatusDescription));

        Func<ParseResult, int> action = _ => ExecuteStatus(runtime);
        command.SetAction(action);
        return command;
    }

    private static int ExecuteInstall(CliRuntime runtime, bool force, bool updateProfile)
    {
        PathManagementService service = runtime.Session.CreatePathManagement();
        PathInstallReport report = service.Install(ResolveExecutablePath(runtime), force, updateProfile);

        runtime.Writer.WritePathInstall(report);
        runtime.Logger.Log(
            CliLogLevel.Information,
            "Path.Install",
            runtime.Text.Get(CliTextKey.LogPathInstalled),
            new LogFields { ExitCode = ExitCodes.SUCCESS, TraceId = runtime.TraceId });
        return ExitCodes.SUCCESS;
    }

    private static int ExecuteUninstall(CliRuntime runtime)
    {
        PathManagementService service = runtime.Session.CreatePathManagement();
        PathUninstallReport report = service.Uninstall(ResolveExecutablePath(runtime));
        int exitCode = report.NotInstalled ? ExitCodes.HOOK_NOT_INSTALLED : ExitCodes.SUCCESS;

        runtime.Writer.WritePathUninstall(report);
        runtime.Logger.Log(
            report.NotInstalled ? CliLogLevel.Warning : CliLogLevel.Information,
            "Path.Uninstall",
            runtime.Text.Get(report.NotInstalled ? CliTextKey.LogPathNotInstalled : CliTextKey.LogPathUninstalled),
            new LogFields { ExitCode = exitCode, TraceId = runtime.TraceId });
        return exitCode;
    }

    private static int ExecuteStatus(CliRuntime runtime)
    {
        PathManagementService service = runtime.Session.CreatePathManagement();
        PathStatusReport report = service.GetStatus(ResolveExecutablePath(runtime));

        runtime.Writer.WritePathStatus(report);
        runtime.Logger.Log(
            CliLogLevel.Debug,
            "Path.Status",
            runtime.Text.Get(CliTextKey.LogPathStatusChecked),
            new LogFields { ExitCode = ExitCodes.SUCCESS, TraceId = runtime.TraceId });
        return ExitCodes.SUCCESS;
    }

    /// <summary>
    /// 解析本次 PATH 管理的 GitGuard 可执行文件：拒绝 <c>dotnet run</c> / 测试宿主等非 gitguard 进程，
    /// 避免把宿主目录（如 dotnet 安装目录）误写入用户 PATH 或在卸载时误删宿主目录条目。
    /// </summary>
    private static string ResolveExecutablePath(CliRuntime runtime)
    {
        string executablePath = runtime.Host.ExecutablePath ?? CliHost.ResolveExecutablePath();
        string fileName = Path.GetFileName(executablePath);
        if (!string.Equals(fileName, "gitguard", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(fileName, "gitguard.exe", StringComparison.OrdinalIgnoreCase))
        {
            throw new PathException(
                PathException.UNSUPPORTED,
                runtime.Text.Format(CliTextKey.PathExecutableInvalid, fileName));
        }

        return Path.GetFullPath(executablePath);
    }
}
