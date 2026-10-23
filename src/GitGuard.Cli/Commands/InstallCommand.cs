using System.CommandLine;
using GitGuard.Application.Composition;
using GitGuard.Application.Configuration;
using GitGuard.Application.Hooks;
using GitGuard.Cli.Localization;
using GitGuard.Cli.Logging;
using GitGuard.Core;
using GitGuard.Core.Model;

namespace GitGuard.Cli.Commands;

/// <summary>
/// <c>gitguard install</c>（<c>06-API接口设计</c> §5.1 / FR-010）：在当前仓库写入 pre-commit / pre-push / post-commit 分发脚本。
/// 未显式指定 <c>--hook</c> 时，注册集合由配置 <c>hooks.*</c> 决定（模块设计/Hook.md §8）；
/// <c>hooks.gitguard_executable_path</c> 可覆盖固化到脚本的可执行文件路径。
/// 失败语义全部由用例异常经 <see cref="ExitCodeMapper"/> 映射（3 写入权限 / 4 需 <c>--force</c> / 13 配置错误 / 50 安装锁冲突）。
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

        GitGuardConfig config = runtime.Session.Config;
        hooks ??= ResolveConfiguredHooks(config.Hooks);
        if (hooks.Count == 0)
        {
            throw new ConfigurationException(
                "hooks 段已禁用全部 Hook（pre_commit / pre_push / post_commit_bypass_detection 均为 false），无可安装项；"
                + "请调整配置或使用 --hook 显式指定");
        }

        string executablePath = ResolveExecutablePath(runtime, config.Hooks.ExecutablePath);
        HookInstallReport report = runtime.Session.CreateInstall().Execute(executablePath, force, hooks);

        runtime.Writer.WriteInstall(report);
        runtime.Logger.Log(
            CliLogLevel.Information,
            "Hook.Install",
            runtime.Text.Get(CliTextKey.LogHookInstalled),
            new LogFields { RepoPath = report.RepoPath, ExitCode = ExitCodes.SUCCESS, TraceId = runtime.TraceId });
        return ExitCodes.SUCCESS;
    }

    /// <summary>按 <c>hooks.*</c> 注册开关生成默认安装集合（顺序固定：pre-commit → pre-push → post-commit）。</summary>
    private static List<HookType> ResolveConfiguredHooks(HooksSettings settings)
    {
        var hooks = new List<HookType>(3);
        if (settings.PreCommit)
        {
            hooks.Add(HookType.PreCommit);
        }

        if (settings.PrePush)
        {
            hooks.Add(HookType.PrePush);
        }

        if (settings.PostCommitBypassDetection)
        {
            hooks.Add(HookType.PostCommit);
        }

        return hooks;
    }

    /// <summary>
    /// 解析固化到分发脚本的可执行文件路径：显式配置优先（支持 <c>~</c>，相对路径按当前目录绝对化），
    /// 否则使用本进程路径；显式配置指向的文件不存在时 FailClosed（退出码 13）。
    /// </summary>
    private static string ResolveExecutablePath(CliRuntime runtime, string? configuredPath)
    {
        if (string.IsNullOrWhiteSpace(configuredPath))
        {
            return runtime.Host.ExecutablePath ?? CliHost.ResolveExecutablePath();
        }

        string fullPath = Path.GetFullPath(GitGuardSession.ExpandUserPath(configuredPath));
        if (!File.Exists(fullPath))
        {
            throw new ConfigurationException($"hooks.gitguard_executable_path 指向的文件不存在：{fullPath}");
        }

        return fullPath;
    }
}
