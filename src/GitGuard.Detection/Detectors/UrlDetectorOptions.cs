namespace GitGuard.Detection.Detectors;

/// <summary>
/// Url 检测器配置（模块设计 LocalPathUrl §7，由 Application 装配时注入）。
/// </summary>
public sealed record UrlDetectorOptions
{
    /// <summary>
    /// 示例文件名前缀（大小写不敏感）：文件基名以任一项开头时严重度降一级
    /// （模块设计 LocalPathUrl §3.2：URL 出现在 example / sample / demo 开头的文件中）。
    /// </summary>
    public IReadOnlyList<string> SampleFilePrefixes { get; init; } = new[] { "example", "sample", "demo" };
}
