namespace GitGuard.Core.Model;

/// <summary>一次 <c>gitguard path status</c> 的只读状态快照。</summary>
public sealed record PathStatusReport
{
    /// <summary>初始化 PATH 状态报告。</summary>
    /// <param name="platform">平台标识：<c>windows</c> / <c>linux</c> / <c>macos</c>。</param>
    /// <param name="targetDirectory">GitGuard 期望写入用户 PATH 的目录（Unix 为符号链接所在目录）。</param>
    /// <param name="executablePath">GitGuard 可执行文件绝对路径。</param>
    /// <param name="installed">是否存在 GitGuard PATH 托管状态（状态文件 / 托管条目 / 符号链接）。</param>
    /// <param name="pathEntryPresent">持久化用户 PATH 中是否存在目标目录条目。</param>
    /// <param name="effective">目标目录是否已出现在当前进程的 PATH 中。</param>
    /// <param name="profilePath">GitGuard 托管标记块所在 Shell profile；未配置时为 <see langword="null"/>。</param>
    /// <param name="profileConfigured">Shell profile 中是否存在 GitGuard 标记块。</param>
    /// <exception cref="ArgumentException">任一路径或平台参数为空白。</exception>
    public PathStatusReport(
        string platform,
        string targetDirectory,
        string executablePath,
        bool installed,
        bool pathEntryPresent,
        bool effective,
        string? profilePath,
        bool profileConfigured)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(platform);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);

        Platform = platform;
        TargetDirectory = targetDirectory;
        ExecutablePath = executablePath;
        Installed = installed;
        PathEntryPresent = pathEntryPresent;
        Effective = effective;
        ProfilePath = profilePath;
        ProfileConfigured = profileConfigured;
    }

    /// <summary>平台标识：<c>windows</c> / <c>linux</c> / <c>macos</c>。</summary>
    public string Platform { get; }

    /// <summary>GitGuard 期望写入用户 PATH 的目录（Unix 为符号链接所在目录）。</summary>
    public string TargetDirectory { get; }

    /// <summary>GitGuard 可执行文件绝对路径。</summary>
    public string ExecutablePath { get; }

    /// <summary>是否存在 GitGuard PATH 托管状态（状态文件 / 托管条目 / 符号链接）。</summary>
    public bool Installed { get; }

    /// <summary>持久化用户 PATH 中是否存在目标目录条目。</summary>
    public bool PathEntryPresent { get; }

    /// <summary>目标目录是否已出现在当前进程的 PATH 中。</summary>
    public bool Effective { get; }

    /// <summary>GitGuard 托管标记块所在 Shell profile；未配置时为 <see langword="null"/>。</summary>
    public string? ProfilePath { get; }

    /// <summary>Shell profile 中是否存在 GitGuard 标记块。</summary>
    public bool ProfileConfigured { get; }
}
