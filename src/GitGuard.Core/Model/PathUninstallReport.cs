namespace GitGuard.Core.Model;

/// <summary>一次 <c>gitguard path uninstall</c> 的结果（供 CLI 渲染摘要与映射退出码）。</summary>
public sealed record PathUninstallReport
{
    /// <summary>初始化 PATH 卸载报告。</summary>
    /// <param name="platform">平台标识：<c>windows</c> / <c>linux</c> / <c>macos</c>。</param>
    /// <param name="targetDirectory">被移除的用户 PATH 目录（Unix 为符号链接所在目录）。</param>
    /// <param name="pathUpdated">本次是否实际从持久化 PATH 中移除条目。</param>
    /// <param name="linkRemoved">本次是否删除了 GitGuard 托管的 Unix 符号链接。</param>
    /// <param name="profileBlockRemoved">本次是否从 Shell profile 移除了 GitGuard 标记块。</param>
    /// <param name="notInstalled">未发现 GitGuard PATH 管理痕迹（非故障；CLI 映射退出码 5）。</param>
    /// <exception cref="ArgumentException"><paramref name="platform"/> 或 <paramref name="targetDirectory"/> 为空白。</exception>
    public PathUninstallReport(
        string platform,
        string targetDirectory,
        bool pathUpdated,
        bool linkRemoved,
        bool profileBlockRemoved,
        bool notInstalled)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(platform);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetDirectory);

        Platform = platform;
        TargetDirectory = targetDirectory;
        PathUpdated = pathUpdated;
        LinkRemoved = linkRemoved;
        ProfileBlockRemoved = profileBlockRemoved;
        NotInstalled = notInstalled;
    }

    /// <summary>平台标识：<c>windows</c> / <c>linux</c> / <c>macos</c>。</summary>
    public string Platform { get; }

    /// <summary>被移除的用户 PATH 目录（Unix 为符号链接所在目录）。</summary>
    public string TargetDirectory { get; }

    /// <summary>本次是否实际从持久化 PATH 中移除条目。</summary>
    public bool PathUpdated { get; }

    /// <summary>本次是否删除了 GitGuard 托管的 Unix 符号链接。</summary>
    public bool LinkRemoved { get; }

    /// <summary>本次是否从 Shell profile 移除了 GitGuard 标记块。</summary>
    public bool ProfileBlockRemoved { get; }

    /// <summary>未发现 GitGuard PATH 管理痕迹（非故障；CLI 映射退出码 5）。</summary>
    public bool NotInstalled { get; }
}
