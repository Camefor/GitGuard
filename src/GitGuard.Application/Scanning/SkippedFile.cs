namespace GitGuard.Application.Scanning;

/// <summary>跳过原因（SRS FR-020「跳过并记为 skipped」）。</summary>
public enum SkipReason
{
    /// <summary>文件大小超过 <see cref="ScanSettings.MaxFileBytes"/>。</summary>
    TooLarge = 0,

    /// <summary>二进制文件：git 差异不含文本内容，无法检测。</summary>
    Binary = 1,
}

/// <summary>
/// 被跳过的文件记录：报告层据此输出「skipped」清单（PrePush 扫描按提交标注 <see cref="CommitSha"/>）。
/// </summary>
public sealed record SkippedFile
{
    /// <summary>初始化跳过记录。</summary>
    /// <param name="path">仓库相对路径（<c>/</c> 分隔）。</param>
    /// <param name="reason">跳过原因。</param>
    /// <param name="commitSha">PrePush 扫描时所属提交的 SHA；PreCommit 为 <see langword="null"/>。</param>
    /// <exception cref="ArgumentException"><paramref name="path"/> 为空或全空白。</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="reason"/> 取值未知。</exception>
    public SkippedFile(string path, SkipReason reason, string? commitSha = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!Enum.IsDefined(reason))
        {
            throw new ArgumentOutOfRangeException(nameof(reason), reason, "未知的 SkipReason 取值");
        }

        Path = path;
        Reason = reason;
        CommitSha = commitSha;
    }

    /// <summary>仓库相对路径（<c>/</c> 分隔）。</summary>
    public string Path { get; }

    /// <summary>跳过原因。</summary>
    public SkipReason Reason { get; }

    /// <summary>PrePush 扫描时所属提交的 SHA；PreCommit 为 <see langword="null"/>。</summary>
    public string? CommitSha { get; }
}
