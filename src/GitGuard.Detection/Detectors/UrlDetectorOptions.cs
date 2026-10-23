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

    /// <summary>
    /// 公共文档主机名白名单（<c>rules.url.public_hostname_whitelist</c>）：精确主机名或
    /// <c>*.example.com</c> 通配，大小写不敏感；命中主机的 URL 规则发现项不产出。默认空（不抑制）。
    /// </summary>
    public IReadOnlyList<string> PublicHostnameWhitelist { get; init; } = Array.Empty<string>();
}
