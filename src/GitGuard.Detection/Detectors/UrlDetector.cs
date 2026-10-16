using GitGuard.Core.Model;
using GitGuard.Core.Rules;

namespace GitGuard.Detection.Detectors;

/// <summary>
/// Url 检测器（模块设计 LocalPathUrl §3.2）：扫描 URL 中的凭据 / Token 与内网地址泄漏，
/// 承载 <c>GGD/URL/*</c> 4 条规则。类别特有语义：示例文件（example / sample / demo 开头）中严重度降一级。
/// </summary>
public sealed class UrlDetector : RegexRuleDetector
{
    /// <summary>检测器名称（IDetector.Name）。</summary>
    public const string DETECTOR_NAME = "url";

    /// <summary>规则类别段（规则 ID 第 2 段）。</summary>
    public const string RULE_CATEGORY = "URL";

    private readonly UrlDetectorOptions _options;

    /// <summary>初始化 Url 检测器。</summary>
    /// <param name="rules">全量规则集；基类按 <c>URL</c> 类别过滤。</param>
    /// <param name="findingFactory">发现项工厂。</param>
    /// <param name="options">配置；<see langword="null"/> 使用默认值。</param>
    public UrlDetector(
        IReadOnlyList<DetectionRule> rules,
        FindingFactory findingFactory,
        UrlDetectorOptions? options = null)
        : base(DETECTOR_NAME, RULE_CATEGORY, rules, findingFactory) =>
        _options = options ?? new UrlDetectorOptions();

    /// <summary>示例文件名中降一级（Low 保持 Low，命中记录保留，模块设计 LocalPathUrl §3.2）。</summary>
    /// <param name="rule">当前规则。</param>
    /// <param name="change">文件变更。</param>
    /// <returns>最终严重度。</returns>
    protected override Severity? AdjustSeverity(DetectionRule rule, FileChange change) =>
        IsSampleFile(change.Path) ? SeverityDowngrade.StepOrFloor(rule.Severity) : rule.Severity;

    private bool IsSampleFile(string path)
    {
        int lastSlash = path.LastIndexOf('/');
        string fileName = lastSlash >= 0 ? path.Substring(lastSlash + 1) : path;
        foreach (string prefix in _options.SampleFilePrefixes)
        {
            if (fileName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
