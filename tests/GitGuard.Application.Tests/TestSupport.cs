using GitGuard.Core.Abstractions;
using GitGuard.Core.Rules;
using GitGuard.Core.Services;
using GitGuard.Detection.Detectors;
using GitGuard.Detection.Rules;

namespace GitGuard.Application.Tests;

/// <summary>扫描用例测试基础设施：金丝雀样本与真实检测器装配（16-测试方案 §7：禁止真实敏感数据）。</summary>
internal static class TestSupport
{
    /// <summary>生成 AWS Access Key ID 形态的金丝雀样本；每次调用唯一，避免测试间指纹串扰。</summary>
    internal static string CreateCanaryToken() =>
        "AKIA" + Guid.NewGuid().ToString("N").Substring(0, 16).ToUpperInvariant();

    /// <summary>按 FR-021 固定顺序（Secret → Privacy → LocalPath → Url）装配四类真实检测器；确定性指纹盐。</summary>
    internal static IReadOnlyList<IDetector> CreateDetectors()
    {
        IReadOnlyList<DetectionRule> rules = BuiltinRuleLoader.LoadEmbedded();
        var factory = new FindingFactory(new Fingerprinter(new byte[Fingerprinter.SALT_BYTES]));
        return new List<IDetector>
        {
            new SecretDetector(rules, factory),
            new PrivacyDetector(rules, factory),
            new LocalPathDetector(rules, factory),
            new UrlDetector(rules, factory),
        };
    }
}
