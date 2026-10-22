namespace GitGuard.Application.Scanning;

/// <summary>
/// 全量扫描（FR-060）选项：默认只扫描 HEAD 跟踪文件；历史模式扫描 HEAD 之前的提交差异。
/// <c>--max-files</c> 仅约束历史提交的文件数（按提交边界分段，单提交可软超限），
/// 截断后通过报告中的 <c>next_since_sha</c> 传给 <c>--since</c> 继续。
/// </summary>
public sealed record ScanAllOptions
{
    /// <summary>全默认选项：仅扫描 HEAD 跟踪文件。</summary>
    public static ScanAllOptions Default { get; } = new ScanAllOptions();

    /// <summary>是否扫描 HEAD 之前的历史提交差异。</summary>
    public bool IncludeHistory { get; init; }

    /// <summary>历史扫描起点（不含该提交）；<see langword="null"/> 表示从 HEAD 的父提交开始。</summary>
    public string? Since { get; init; }

    /// <summary>历史扫描的文件数软上限（按提交边界生效）；<see langword="null"/> 表示不限制。</summary>
    public int? MaxFiles { get; init; }
}
