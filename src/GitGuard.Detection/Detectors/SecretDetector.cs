using GitGuard.Core.Model;
using GitGuard.Core.Rules;
using GitGuard.Detection.Validation;

namespace GitGuard.Detection.Detectors;

/// <summary>
/// Secret 检测器（模块设计 Secret）：扫描凭据与密钥泄漏，承载 <c>GGD/SECRET/*</c> 8 条内置规则。
/// 类别特有语义：压缩 / 锁文件整文件跳过；超长行（疑似 minified）仅保留高置信度规则；
/// AWS-SECRET 经 Shannon 熵判定。
/// </summary>
public sealed class SecretDetector : RegexRuleDetector
{
    /// <summary>检测器名称（IDetector.Name）。</summary>
    public const string DETECTOR_NAME = "secret";

    /// <summary>规则类别段（规则 ID 第 2 段）。</summary>
    public const string RULE_CATEGORY = "SECRET";

    /// <summary>行长阈值：超过 2048 字符视为疑似 minified，仅做高置信度规则（模块设计 Secret §6）。</summary>
    private const int LONG_LINE_THRESHOLD = 2048;

    /// <summary>整文件跳过的后缀：锁文件与压缩产物（模块设计 Secret §6）。</summary>
    private static readonly string[] _skippedFileSuffixes = new[] { ".lock", ".min.js", ".min.css" };

    /// <summary>长行场景保留的高置信度规则（跳过 GENERIC-PASSWORD / AWS-SECRET 等低置信度启发式）。</summary>
    private static readonly HashSet<string> _highConfidenceRuleIds = new HashSet<string>(StringComparer.Ordinal)
    {
        "GGD/SECRET/AWS-AKID",
        "GGD/SECRET/GITHUB-TOKEN",
        "GGD/SECRET/SLACK-TOKEN",
        "GGD/SECRET/PRIVATE-KEY",
    };

    /// <summary>初始化 Secret 检测器。</summary>
    /// <param name="rules">全量规则集；基类按 <c>SECRET</c> 类别过滤。</param>
    /// <param name="findingFactory">发现项工厂。</param>
    /// <param name="options">配置；<see langword="null"/> 使用默认值。</param>
    /// <exception cref="GitGuard.Core.DetectionException">规则引用了不支持的校验器。</exception>
    public SecretDetector(
        IReadOnlyList<DetectionRule> rules,
        FindingFactory findingFactory,
        SecretDetectorOptions? options = null)
        : base(DETECTOR_NAME, RULE_CATEGORY, rules, findingFactory, BuildValidators(options ?? new SecretDetectorOptions()))
    {
    }

    /// <summary>整文件跳过：<c>.lock</c> / <c>.min.js</c> / <c>.min.css</c>。</summary>
    /// <param name="change">文件变更。</param>
    /// <returns>跳过返回 <see langword="true"/>。</returns>
    protected override bool ShouldSkipFile(FileChange change)
    {
        foreach (string suffix in _skippedFileSuffixes)
        {
            if (change.Path.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>超长行仅保留高置信度规则（模块设计 Secret §6）。</summary>
    /// <param name="rule">当前规则。</param>
    /// <param name="line">当前变更行。</param>
    /// <returns>跳过返回 <see langword="true"/>。</returns>
    protected override bool ShouldSkipRuleForLine(DetectionRule rule, ChangedLine line) =>
        line.Text.Length > LONG_LINE_THRESHOLD && !_highConfidenceRuleIds.Contains(rule.RuleId);

    private static Dictionary<string, Func<string, bool>> BuildValidators(SecretDetectorOptions options)
    {
        var validators = new Dictionary<string, Func<string, bool>>(StringComparer.Ordinal)
        {
            ["shannon-entropy"] = value => ShannonEntropy.Calculate(value) > options.EntropyThreshold,
        };
        return validators;
    }
}
