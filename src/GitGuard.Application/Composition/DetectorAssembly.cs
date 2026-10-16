using System.Collections.Immutable;
using GitGuard.Application.Configuration;
using GitGuard.Core;
using GitGuard.Core.Abstractions;
using GitGuard.Core.Rules;
using GitGuard.Detection.Detectors;
using GitGuard.Detection.Rules;

namespace GitGuard.Application.Composition;

/// <summary>
/// 检测器装配（FR-021 固定顺序 Secret → Privacy → LocalPath → Url；<c>模块设计/Policy.md</c> §7 <c>rules</c> 段落接线）：
/// 内置规则集经 <c>disabled_rules</c> 重标为禁用后再交给四类检测器（基类按类别段过滤并剔除禁用规则）；
/// 类别 <c>enabled: false</c> 则完全不装配该检测器；全部类别禁用视为配置无效（FailClosed，退出码 13）。
/// </summary>
internal static class DetectorAssembly
{
    /// <summary>按配置装配检测器集合。</summary>
    /// <param name="config">有效配置（<c>rules</c> 段落已合并默认值）。</param>
    /// <param name="factory">发现项工厂（注入本次运行的指纹器）。</param>
    /// <param name="warnings">宽松解析警告收集器（追加 <c>disabled_rules</c> 中的未知规则 ID）。</param>
    /// <returns>检测器集合与启用规则总数（<c>rules_evaluated</c> 报告字段的来源）。</returns>
    /// <exception cref="ArgumentNullException">任一引用参数为 <see langword="null"/>。</exception>
    /// <exception cref="DetectionException">内置规则集加载失败（FailClosed）。</exception>
    /// <exception cref="ConfigurationException">四个类别全部被禁用。</exception>
    internal static DetectorSet Assemble(
        GitGuardConfig config,
        FindingFactory factory,
        ICollection<string> warnings)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(warnings);

        IReadOnlyList<DetectionRule> rules = ApplyDisabledRules(BuiltinRuleLoader.LoadEmbedded(), config, warnings);

        var detectors = ImmutableArray.CreateBuilder<IDetector>(4);
        if (config.Secrets.Enabled)
        {
            detectors.Add(new SecretDetector(rules, factory, new SecretDetectorOptions
            {
                EntropyThreshold = config.Secrets.MinEntropy,
            }));
        }

        if (config.Privacy.Enabled)
        {
            detectors.Add(new PrivacyDetector(rules, factory, new PrivacyDetectorOptions
            {
                RequireContext = config.Privacy.RequireContext,
                TestFilePatterns = config.Privacy.TestFilePatterns,
            }));
        }

        if (config.LocalPath.Enabled)
        {
            detectors.Add(new LocalPathDetector(rules, factory));
        }

        if (config.Url.Enabled)
        {
            detectors.Add(new UrlDetector(rules, factory));
        }

        if (detectors.Count == 0)
        {
            throw new ConfigurationException(
                "rules 的四个类别（secrets / privacy / local_path / url）全部被禁用：扫描将无任何检测能力，配置无效。");
        }

        return new DetectorSet(detectors.ToImmutable(), CountEnabledRules(rules, config));
    }

    /// <summary>统计实际交付检测器的规则数（类别启用且规则未禁用）。</summary>
    private static int CountEnabledRules(IReadOnlyList<DetectionRule> rules, GitGuardConfig config)
    {
        int count = 0;
        foreach (DetectionRule rule in rules)
        {
            if (rule.Enabled && IsCategoryEnabled(CategoryOf(rule.RuleId), config))
            {
                count++;
            }
        }

        return count;
    }

    private static bool IsCategoryEnabled(string category, GitGuardConfig config) => category switch
    {
        "SECRET" => config.Secrets.Enabled,
        "PRIVACY" => config.Privacy.Enabled,
        "LOCAL_PATH" => config.LocalPath.Enabled,
        "URL" => config.Url.Enabled,
        _ => false,
    };

    /// <summary>取规则 ID 的类别段（第 2 段；命名契约保证恰好 3 段）。</summary>
    private static string CategoryOf(string ruleId)
    {
        int firstSlash = ruleId.IndexOf('/');
        int secondSlash = ruleId.IndexOf('/', firstSlash + 1);
        return ruleId.Substring(firstSlash + 1, secondSlash - firstSlash - 1);
    }

    /// <summary>把 <c>disabled_rules</c> 声明的规则重建为禁用规则（<see cref="DetectionRule"/> 不可变，无原位开关）。</summary>
    private static IReadOnlyList<DetectionRule> ApplyDisabledRules(
        IReadOnlyList<DetectionRule> rules,
        GitGuardConfig config,
        ICollection<string> warnings)
    {
        var disabledIds = new HashSet<string>(StringComparer.Ordinal);
        CollectDisabledIds(disabledIds, config.Secrets.DisabledRules);
        CollectDisabledIds(disabledIds, config.Privacy.DisabledRules);
        CollectDisabledIds(disabledIds, config.LocalPath.DisabledRules);
        CollectDisabledIds(disabledIds, config.Url.DisabledRules);

        if (disabledIds.Count == 0)
        {
            return rules;
        }

        foreach (string ruleId in disabledIds)
        {
            if (!ContainsRule(rules, ruleId))
            {
                warnings.Add($"disabled_rules 含未知规则 ID：{ruleId}（已忽略；可用 gitguard rules list 核对有效 ID）");
            }
        }

        var rebuilt = new List<DetectionRule>(rules.Count);
        foreach (DetectionRule rule in rules)
        {
            rebuilt.Add(disabledIds.Contains(rule.RuleId)
                ? DetectionRule.Create(rule.RuleId, rule.Name, rule.Severity, rule.Pattern, rule.Source, enabled: false)
                : rule);
        }

        return rebuilt;
    }

    private static void CollectDisabledIds(HashSet<string> target, IReadOnlyList<string> disabledRules)
    {
        foreach (string ruleId in disabledRules)
        {
            target.Add(ruleId);
        }
    }

    private static bool ContainsRule(IReadOnlyList<DetectionRule> rules, string ruleId)
    {
        foreach (DetectionRule rule in rules)
        {
            if (string.Equals(rule.RuleId, ruleId, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}

/// <summary>
/// 检测器装配结果：检测器列表（FR-021 顺序）+ 启用规则总数。
/// 规则总数为扫描报告 <c>summary.rules_evaluated</c> 的来源（<c>06-API接口设计</c> §4.2）。
/// </summary>
internal sealed record DetectorSet
{
    /// <summary>初始化装配结果。</summary>
    /// <param name="detectors">检测器列表。</param>
    /// <param name="rulesEvaluated">启用规则总数（类别启用且规则未禁用）。</param>
    internal DetectorSet(ImmutableArray<IDetector> detectors, int rulesEvaluated)
    {
        Detectors = detectors;
        RulesEvaluated = rulesEvaluated;
    }

    /// <summary>检测器列表（FR-021 顺序：Secret → Privacy → LocalPath → Url）。</summary>
    internal ImmutableArray<IDetector> Detectors { get; }

    /// <summary>本次扫描参与评估的启用规则总数。</summary>
    internal int RulesEvaluated { get; }
}
