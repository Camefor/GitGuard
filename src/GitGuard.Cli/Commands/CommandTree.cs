using System.CommandLine;
using GitGuard.Cli.Localization;

namespace GitGuard.Cli.Commands;

/// <summary>
/// 命令树装配（<c>模块设计/Cli.md</c> §10）：命令集为
/// <c>scan</c> / <c>install</c> / <c>uninstall</c> / <c>status</c> / <c>version</c> / <c>audit</c> /
/// <c>allowlist</c> / <c>rules</c> / <c>path</c>，另有隐藏的内部命令 <c>hook post-commit</c>（FR-050，不展示于帮助）；
/// 全局选项挂根命令并递归下发。
/// 每次运行构建全新命令树，命令实例不跨调用共享（并行测试安全）。
/// </summary>
internal static class CommandTree
{
    /// <summary>构建根命令与全部子命令。</summary>
    /// <param name="runtime">运行时上下文（被各命令动作闭包捕获）。</param>
    /// <param name="globalOptions">全局选项集合（挂到根命令）。</param>
    /// <returns>可执行的根命令。</returns>
    /// <exception cref="ArgumentNullException">任一参数为 <see langword="null"/>。</exception>
    internal static RootCommand Create(CliRuntime runtime, GlobalOptionSet globalOptions)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentNullException.ThrowIfNull(globalOptions);

        var root = new RootCommand(runtime.Text.Get(CliTextKey.RootDescription));
        root.Subcommands.Add(HookCommand.Create(runtime));
        root.Subcommands.Add(ScanCommand.Create(runtime));
        root.Subcommands.Add(InstallCommand.Create(runtime));
        root.Subcommands.Add(UninstallCommand.Create(runtime));
        root.Subcommands.Add(StatusCommand.Create(runtime));
        root.Subcommands.Add(VersionCommand.Create(runtime));
        root.Subcommands.Add(AuditCommand.Create(runtime));
        root.Subcommands.Add(AllowlistCommand.Create(runtime));
        root.Subcommands.Add(RulesCommand.Create(runtime));
        root.Subcommands.Add(PathCommand.Create(runtime));
        globalOptions.AddTo(root);
        return root;
    }
}
