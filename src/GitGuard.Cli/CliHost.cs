using GitGuard.Core.Abstractions;

namespace GitGuard.Cli;

/// <summary>
/// CLI 进程宿主的外部注入点（测试缝隙 + 进程环境快照）：工作目录、标准流、数据目录、git 可执行文件与
/// 可执行文件自身路径。真实进程使用 <see cref="CreateDefault"/>；测试注入内存流与临时目录以驱动完整命令链。
/// </summary>
public sealed class CliHost
{
    /// <summary>工作目录（仓库内任意目录）；默认当前工作目录。</summary>
    public string WorkingDirectory { get; init; } = Environment.CurrentDirectory;

    /// <summary>标准输出：机器可读 JSON（<c>--format json</c>）；默认 <c>Console.Out</c>。</summary>
    public TextWriter Output { get; init; } = Console.Out;

    /// <summary>标准错误：人类可读报告、日志与错误提示；默认 <c>Console.Error</c>。</summary>
    public TextWriter Error { get; init; } = Console.Error;

    /// <summary>标准输入（<c>scan --push</c> 读取推送信息行）；默认 <c>Console.In</c>。</summary>
    public TextReader Input { get; init; } = Console.In;

    /// <summary>git 可执行文件；默认从 PATH 解析 <c>git</c>。</summary>
    public string GitExecutable { get; init; } = "git";

    /// <summary>用户数据目录根（<c>audit.db</c> / <c>cache</c> / <c>logs</c> / <c>crash</c>）；<see langword="null"/> 按平台默认解析。</summary>
    public string? DataDirectory { get; init; }

    /// <summary>用户级配置目录（<c>config.yml</c> 的父目录）；<see langword="null"/> 取 <c>~/.config/gitguard</c>。</summary>
    public string? UserConfigDirectory { get; init; }

    /// <summary>GitGuard 版本覆盖（测试用）；<see langword="null"/> 取本程序集版本。</summary>
    public string? GitGuardVersion { get; init; }

    /// <summary>GitGuard 可执行文件路径（写入 Hook 分发脚本 / PATH）；<see langword="null"/> 取 <see cref="ResolveExecutablePath"/>。</summary>
    public string? ExecutablePath { get; init; }

    /// <summary>PATH 管理器覆盖（测试注入假实现，避免修改真实用户 PATH）；<see langword="null"/> 按当前平台创建。</summary>
    public IPathManager? PathManager { get; init; }

    /// <summary><c>GITGUARD_SKIP</c> 环境变量取值（<c>1</c> 表示 Hook 模式下显式跳过扫描，FR-050）；未设置为 <see langword="null"/>。</summary>
    public string? SkipEnvironment { get; init; }

    /// <summary><c>GITGUARD_LANG</c> 环境变量取值（输出语言；命令行 <c>--language</c> 优先）；未设置为 <see langword="null"/>。</summary>
    public string? LanguageEnvironment { get; init; }

    /// <summary>stderr 是否连接到终端（彩色输出的必要条件之一）；测试注入内存流时应保持 <see langword="false"/>。</summary>
    public bool IsErrorTerminal { get; init; }

    /// <summary><c>NO_COLOR</c> 环境变量是否已设置（非空即视为禁用彩色，no-color.org 约定）。</summary>
    public bool NoColorEnvironment { get; init; }

    /// <summary>解析本进程可执行文件路径（Hook 分发脚本固化用；AOT 单文件形态即为自身路径）。</summary>
    /// <returns>绝对路径；<see cref="Environment.ProcessPath"/> 不可得时回退程序集所在目录。</returns>
    public static string ResolveExecutablePath()
    {
        string? processPath = Environment.ProcessPath;
        return string.IsNullOrWhiteSpace(processPath)
            ? Path.Combine(AppContext.BaseDirectory, "gitguard")
            : processPath;
    }

    /// <summary>按当前进程环境创建默认宿主（真实命令行运行路径）。</summary>
    /// <returns>接线 <c>Console</c> 标准流与进程信息的宿主。</returns>
    public static CliHost CreateDefault() => new CliHost
    {
        WorkingDirectory = Environment.CurrentDirectory,
        Output = Console.Out,
        Error = Console.Error,
        Input = Console.In,
        GitExecutable = "git",
        ExecutablePath = ResolveExecutablePath(),
        SkipEnvironment = Environment.GetEnvironmentVariable("GITGUARD_SKIP"),
        LanguageEnvironment = Environment.GetEnvironmentVariable("GITGUARD_LANG"),
        IsErrorTerminal = !Console.IsErrorRedirected,
        NoColorEnvironment = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NO_COLOR")),
    };
}
