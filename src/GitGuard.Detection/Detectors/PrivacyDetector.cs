using GitGuard.Core.Model;
using GitGuard.Core.Rules;
using GitGuard.Detection.Validation;

namespace GitGuard.Detection.Detectors;

/// <summary>
/// Privacy 检测器（模块设计 Privacy）：扫描个人隐私数据泄漏，承载 <c>GGD/PRIVACY/*</c> 5 条内置规则。
/// 类别特有语义：身份证 / 银行卡经声明式校验器（校验位 / Luhn）拦截误报；
/// 上下文关键字可经配置关闭；测试文件路径命中时严重度降一级但保留记录（模块设计 Privacy §6）。
/// </summary>
public sealed class PrivacyDetector : RegexRuleDetector
{
    /// <summary>检测器名称（IDetector.Name）。</summary>
    public const string DETECTOR_NAME = "privacy";

    /// <summary>规则类别段（规则 ID 第 2 段）。</summary>
    public const string RULE_CATEGORY = "PRIVACY";

    private readonly PrivacyDetectorOptions _options;

    /// <summary>初始化 Privacy 检测器。</summary>
    /// <param name="rules">全量规则集；基类按 <c>PRIVACY</c> 类别过滤。</param>
    /// <param name="findingFactory">发现项工厂。</param>
    /// <param name="options">配置；<see langword="null"/> 使用默认值。</param>
    /// <exception cref="GitGuard.Core.DetectionException">规则引用了不支持的校验器。</exception>
    public PrivacyDetector(
        IReadOnlyList<DetectionRule> rules,
        FindingFactory findingFactory,
        PrivacyDetectorOptions? options = null)
        : base(DETECTOR_NAME, RULE_CATEGORY, rules, findingFactory, BuildValidators()) =>
        _options = options ?? new PrivacyDetectorOptions();

    /// <summary>上下文门禁；<see cref="PrivacyDetectorOptions.RequireContext"/> 为 <see langword="false"/> 时恒通过。</summary>
    /// <param name="rule">当前规则。</param>
    /// <param name="lineText">命中行文本。</param>
    /// <returns>满足要求返回 <see langword="true"/>。</returns>
    protected override bool IsContextSatisfied(DetectionRule rule, string lineText) =>
        !_options.RequireContext || base.IsContextSatisfied(rule, lineText);

    /// <summary>测试文件路径命中时严重度降一级（Low 保持 Low，命中记录保留）。</summary>
    /// <param name="rule">当前规则。</param>
    /// <param name="change">文件变更。</param>
    /// <returns>最终严重度。</returns>
    protected override Severity? AdjustSeverity(DetectionRule rule, FileChange change) =>
        IsTestFile(change.Path) ? SeverityDowngrade.StepOrFloor(rule.Severity) : rule.Severity;

    private static Dictionary<string, Func<string, bool>> BuildValidators()
    {
        var validators = new Dictionary<string, Func<string, bool>>(StringComparer.Ordinal)
        {
            ["luhn"] = LuhnChecksum.IsValid,
            ["cn-id-checksum"] = CnIdCardValidator.IsValid,
        };
        return validators;
    }

    private bool IsTestFile(string path)
    {
        foreach (string pattern in _options.TestFilePatterns)
        {
            if (path.Contains(pattern, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
