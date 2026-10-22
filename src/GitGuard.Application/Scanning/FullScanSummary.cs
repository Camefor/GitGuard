namespace GitGuard.Application.Scanning;

/// <summary>
/// 全量扫描（FR-060）附加摘要：记录历史扫描进度与分段续扫信息。
/// HEAD 跟踪文件由全量树差异一次性扫描；历史模式额外扫描 HEAD 之前的提交差异，
/// <see cref="NextSinceSha"/> 用于下一次通过 <c>--since</c> 继续。
/// </summary>
public sealed class FullScanSummary
{
    /// <summary>初始化全量扫描摘要。</summary>
    /// <param name="includeHistory">是否包含历史提交。</param>
    /// <param name="commitsScanned">本次实际扫描的历史提交数（不含 HEAD，HEAD 由全量树扫描覆盖）。</param>
    /// <param name="remainingCommits">因 <c>--max-files</c> 截断而剩余未扫描的历史提交数。</param>
    /// <param name="truncated">是否因 <c>--max-files</c> 提前结束。</param>
    /// <param name="nextSinceSha">截断时最后完成的提交 SHA；下一次传给 <c>--since</c> 继续扫描。</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="commitsScanned"/> 或 <paramref name="remainingCommits"/> 为负。</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="truncated"/> 为 <see langword="true"/> 但 <paramref name="nextSinceSha"/> 为空；
    /// 或未截断却提供了 <paramref name="nextSinceSha"/>。
    /// </exception>
    public FullScanSummary(
        bool includeHistory,
        int commitsScanned,
        int remainingCommits,
        bool truncated,
        string? nextSinceSha)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(commitsScanned);
        ArgumentOutOfRangeException.ThrowIfNegative(remainingCommits);
        if (truncated)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(nextSinceSha);
        }
        else if (nextSinceSha is not null)
        {
            throw new ArgumentException("未截断的全量扫描不得携带 next_since_sha。", nameof(nextSinceSha));
        }

        IncludeHistory = includeHistory;
        CommitsScanned = commitsScanned;
        RemainingCommits = remainingCommits;
        Truncated = truncated;
        NextSinceSha = nextSinceSha;
    }

    /// <summary>是否包含历史提交（<c>--include-history</c> / <c>--since</c> 触发）。</summary>
    public bool IncludeHistory { get; }

    /// <summary>本次实际扫描的历史提交数（HEAD 不计入）。</summary>
    public int CommitsScanned { get; }

    /// <summary>剩余未扫描的历史提交数；未截断时为 0。</summary>
    public int RemainingCommits { get; }

    /// <summary>是否因 <c>--max-files</c> 提前结束。</summary>
    public bool Truncated { get; }

    /// <summary>截断时最后完成的提交 SHA；下一次通过 <c>--since</c> 继续。</summary>
    public string? NextSinceSha { get; }
}
