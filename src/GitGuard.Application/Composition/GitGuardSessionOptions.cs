namespace GitGuard.Application.Composition;

/// <summary>
/// 组合根（<see cref="GitGuardSession"/>）的外部注入点：CLI 全局选项 / 进程环境 / 测试缝隙。
/// 只承载纯数据（路径与版本）；时钟、随机源等组件由 <see cref="GitGuardSession"/> 的构造参数注入。
/// </summary>
public sealed record GitGuardSessionOptions
{
    /// <summary>工作目录（仓库内任意目录）：项目级配置的定位起点与 Git 命令的启动目录。默认当前工作目录。</summary>
    public string WorkingDirectory { get; init; } = Environment.CurrentDirectory;

    /// <summary>git 可执行文件；默认从 PATH 解析 <c>git</c>。</summary>
    public string GitExecutable { get; init; } = "git";

    /// <summary>用户数据目录根（<c>audit.db</c> / <c>cache</c> / <c>logs</c> / <c>crash</c> 的父目录）；<see langword="null"/> 按平台默认解析。</summary>
    public string? DataDirectory { get; init; }

    /// <summary>用户级配置目录（<c>config.yml</c> 的父目录）；<see langword="null"/> 取 <c>~/.config/gitguard</c>。</summary>
    public string? UserConfigDirectory { get; init; }

    /// <summary>GitGuard 版本（写入 Hook 标记行与审计 <c>gitguard_version</c>）；<see langword="null"/> 取本程序集版本。</summary>
    public string? GitGuardVersion { get; init; }
}
