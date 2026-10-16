namespace GitGuard.Detection.Detectors;

/// <summary>
/// Privacy 检测器配置（模块设计 Privacy §7 的 <c>rules.privacy.*</c> 子集，由 Application 装配时注入）。
/// </summary>
public sealed record PrivacyDetectorOptions
{
    /// <summary>
    /// 测试文件路径片段（<c>rules.privacy.test_file_patterns</c>）：路径包含任一片段（大小写不敏感）
    /// 即视为测试文件，严重度降一级（模块设计 Privacy §6）。
    /// </summary>
    public IReadOnlyList<string> TestFilePatterns { get; init; } =
        new[] { "test", "spec", "fixture", "mock", "sample", "example" };

    /// <summary>
    /// 是否要求上下文关键字（<c>rules.privacy.require_context</c>，默认 true）；
    /// 关闭后裸格式（如无上下文的 11 位数字）也会命中，提升召回但增加误报（模块设计 Privacy §7）。
    /// </summary>
    public bool RequireContext { get; init; } = true;
}
