namespace GitGuard.Core.Model;

/// <summary>一次 <c>gitguard path install</c> 的结果（供 CLI 渲染摘要与映射退出码）。</summary>
public sealed record PathInstallReport
{
    /// <summary>初始化 PATH 安装报告。</summary>
    /// <param name="platform">平台标识：<c>windows</c> / <c>linux</c> / <c>macos</c>。</param>
    /// <param name="targetDirectory">写入用户 PATH 的目录（Unix 为符号链接所在目录）。</param>
    /// <param name="executablePath">GitGuard 可执行文件绝对路径。</param>
    /// <param name="alreadyPresent">安装前目标目录已存在于用户 PATH（本次未重复追加）。</param>
    /// <param name="pathUpdated">本次是否实际修改了持久化 PATH / 创建了符号链接。</param>
    /// <param name="profilePath">被更新的 Shell profile 路径；未要求或未适用时为 <see langword="null"/>。</param>
    /// <param name="profileUpdated">本次是否实际写入 Shell profile 标记块。</param>
    /// <param name="backupPath">接管既有非托管符号链接 / 文件时生成的备份路径；无接管时为 <see langword="null"/>。</param>
    /// <param name="effective">目标目录是否已出现在当前进程的 PATH 中（新终端通常需要重新启动）。</param>
    /// <exception cref="ArgumentException">任一路径参数为空白，或 <paramref name="platform"/> 为空白。</exception>
    public PathInstallReport(
        string platform,
        string targetDirectory,
        string executablePath,
        bool alreadyPresent,
        bool pathUpdated,
        string? profilePath,
        bool profileUpdated,
        string? backupPath,
        bool effective)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(platform);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);

        Platform = platform;
        TargetDirectory = targetDirectory;
        ExecutablePath = executablePath;
        AlreadyPresent = alreadyPresent;
        PathUpdated = pathUpdated;
        ProfilePath = profilePath;
        ProfileUpdated = profileUpdated;
        BackupPath = backupPath;
        Effective = effective;
    }

    /// <summary>平台标识：<c>windows</c> / <c>linux</c> / <c>macos</c>。</summary>
    public string Platform { get; }

    /// <summary>写入用户 PATH 的目录（Unix 为符号链接所在目录）。</summary>
    public string TargetDirectory { get; }

    /// <summary>GitGuard 可执行文件绝对路径。</summary>
    public string ExecutablePath { get; }

    /// <summary>安装前目标目录已存在于用户 PATH（本次未重复追加）。</summary>
    public bool AlreadyPresent { get; }

    /// <summary>本次是否实际修改了持久化 PATH / 创建了符号链接。</summary>
    public bool PathUpdated { get; }

    /// <summary>被更新的 Shell profile 路径；未要求或未适用时为 <see langword="null"/>。</summary>
    public string? ProfilePath { get; }

    /// <summary>本次是否实际写入 Shell profile 标记块。</summary>
    public bool ProfileUpdated { get; }

    /// <summary>接管既有非托管符号链接 / 文件时生成的备份路径；无接管时为 <see langword="null"/>。</summary>
    public string? BackupPath { get; }

    /// <summary>目标目录是否已出现在当前进程的 PATH 中（新终端通常需要重新启动）。</summary>
    public bool Effective { get; }
}
