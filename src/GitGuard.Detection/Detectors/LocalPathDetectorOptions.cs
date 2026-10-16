namespace GitGuard.Detection.Detectors;

/// <summary>
/// LocalPath 检测器配置（模块设计 LocalPathUrl §7，由 Application 装配时注入）。
/// </summary>
public sealed record LocalPathDetectorOptions
{
    /// <summary>
    /// 文档类文件 glob（<c>GlobMatcher</c> 语义）：命中其中任一模式时严重度降一级，
    /// Low 规则直接不命中（模块设计 LocalPathUrl §3.1，默认 <c>.gitignore</c> / <c>README.md</c> / <c>docs/**</c>）。
    /// </summary>
    public IReadOnlyList<string> DocFileGlobs { get; init; } = new[] { "**/.gitignore", "**/README.md", "docs/**" };
}
