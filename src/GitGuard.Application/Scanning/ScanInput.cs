namespace GitGuard.Application.Scanning;

/// <summary>
/// 扫描管线的差异获取结果：解析后的逐文件差异 + 可选的用例级摘要
/// （全量扫描 FR-060 需要把历史扫描进度带回报告；暂存区 / 推送扫描为 <see langword="null"/>）。
/// </summary>
internal sealed class ScanInput
{
    /// <summary>初始化扫描输入。</summary>
    /// <param name="files">解析后的逐文件差异。</param>
    /// <param name="summary">用例级摘要；无附加摘要时为 <see langword="null"/>。</param>
    /// <exception cref="ArgumentNullException"><paramref name="files"/> 为 <see langword="null"/>。</exception>
    internal ScanInput(IReadOnlyList<ParsedDiffFile> files, FullScanSummary? summary = null)
    {
        ArgumentNullException.ThrowIfNull(files);
        Files = files;
        Summary = summary;
    }

    /// <summary>解析后的逐文件差异。</summary>
    internal IReadOnlyList<ParsedDiffFile> Files { get; }

    /// <summary>用例级摘要（当前仅全量扫描提供）。</summary>
    internal FullScanSummary? Summary { get; }
}
