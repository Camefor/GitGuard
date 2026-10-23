using System.Collections.Immutable;
using GitGuard.Application.Configuration;
using GitGuard.Application.Rules;
using GitGuard.Core;
using GitGuard.Core.Abstractions;
using GitGuard.Core.Model;
using GitGuard.Core.Policy;
using GitGuard.Core.Rules;
using GitGuard.Detection.Adapters;
using GitGuard.Detection.Detectors;
using GitGuard.Detection.Rules;

namespace GitGuard.Application.Composition;

/// <summary>
/// 检测器装配（FR-021 固定顺序 Secret → Gitleaks → Privacy → LocalPath → Url；<c>模块设计/Policy.md</c> §7 <c>rules</c> 段落接线）：
/// 内置规则集与 <c>rules.custom</c> 自定义规则合并后，经 <c>disabled_rules</c> 重标为禁用再交给四类检测器
/// （基类按类别段过滤并剔除禁用规则）；类别 <c>enabled: false</c> 则完全不装配该检测器；
/// 全部类别禁用视为配置无效（FailClosed，退出码 13）。
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

        IReadOnlyList<DetectionRule> builtinRules = BuiltinRuleLoader.LoadEmbedded();
        var allRules = new List<DetectionRule>(builtinRules.Count + config.CustomRules.Length);
        allRules.AddRange(builtinRules);
        allRules.AddRange(config.CustomRules);

        HashSet<string> disabledIds = RuleSelection.CreateDisabledIdSet(config);
        ImmutableArray<PolicyOverride> policyOverrides = CollectPolicyOverrides(allRules, config, disabledIds);
        IReadOnlyList<DetectionRule> rules = RuleSelection.ApplyDisabledRules(allRules, disabledIds, warnings);

        var detectors = ImmutableArray.CreateBuilder<IDetector>(4);
        if (config.Secrets.Enabled)
        {
            detectors.Add(new SecretDetector(rules, factory, new SecretDetectorOptions
            {
                EntropyThreshold = config.Secrets.MinEntropy,
            }));

            // FR-090 / ADR-0003：GitleaksAdapter 可独立开关；启动期探测缺失时仅告警降级，
            // 不阻断扫描（与 use_gitleaks 未实现时代的 FailClosed 行为相反）
            if (config.Secrets.UseGitleaks)
            {
                GitleaksProbeResult probe = GitleaksProbe.Probe(
                    config.Secrets.GitleaksPath,
                    new GitleaksProcessRunner());
                if (probe.Warning is { } probeWarning)
                {
                    warnings.Add(probeWarning);
                }

                if (probe.IsAvailable && probe.ExecutablePath is not null)
                {
                    detectors.Add(new GitleaksAdapter(
                        new GitleaksAdapterOptions
                        {
                            ExecutablePath = probe.ExecutablePath,
                            DisabledRuleIds = config.Secrets.DisabledRules,
                        },
                        factory));
                }
            }
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
            detectors.Add(new UrlDetector(rules, factory, new UrlDetectorOptions
            {
                PublicHostnameWhitelist = config.Url.PublicHostnameWhitelist,
            }));
        }

        if (detectors.Count == 0)
        {
            throw new ConfigurationException(
                "rules 的四个类别（secrets / privacy / local_path / url）全部被禁用：扫描将无任何检测能力，配置无效。");
        }

        return new DetectorSet(detectors.ToImmutable(), RuleSelection.CountEnabledRules(rules, config), policyOverrides);
    }

    /// <summary>
    /// 计算相对默认策略的降级项（08-权限设计 §7 <c>PolicyOverride</c>）：
    /// 严重度映射降级使用合成 ID <c>SEVERITY/&lt;Severity&gt;</c>；被禁用规则（含类别禁用）按规则 ID 记 <c>from → Allow</c>。
    /// 仅记录实际收紧程度下降的项；未知 <c>disabled_rules</c> 由装配警告处理，不计入覆盖。
    /// </summary>
    private static ImmutableArray<PolicyOverride> CollectPolicyOverrides(
        IReadOnlyList<DetectionRule> rules,
        GitGuardConfig config,
        HashSet<string> disabledIds)
    {
        var overrides = ImmutableArray.CreateBuilder<PolicyOverride>();

        foreach (Severity severity in Enum.GetValues<Severity>())
        {
            PolicyAction defaultAction = SeverityPolicy.Default.ActionFor(severity);
            PolicyAction effectiveAction = config.Policy.ActionFor(severity);
            if (effectiveAction < defaultAction)
            {
                overrides.Add(new PolicyOverride($"SEVERITY/{severity}", defaultAction, effectiveAction));
            }
        }

        foreach (DetectionRule rule in rules)
        {
            bool disabled = disabledIds.Contains(rule.RuleId)
                || !RuleSelection.IsCategoryEnabled(RuleSelection.CategoryOf(rule.RuleId), config);
            if (!disabled)
            {
                continue;
            }

            PolicyAction effectiveAction = config.Policy.ActionFor(rule.Severity);
            if (effectiveAction > PolicyAction.Allow)
            {
                overrides.Add(new PolicyOverride(rule.RuleId, effectiveAction, PolicyAction.Allow));
            }
        }

        return overrides.ToImmutable();
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
    /// <param name="policyOverrides">相对默认策略的降级项（供 PolicyOverride 审计事件）。</param>
    internal DetectorSet(
        ImmutableArray<IDetector> detectors,
        int rulesEvaluated,
        ImmutableArray<PolicyOverride> policyOverrides)
    {
        Detectors = detectors;
        RulesEvaluated = rulesEvaluated;
        PolicyOverrides = policyOverrides;
    }

    /// <summary>检测器列表（FR-021 顺序：Secret → Gitleaks → Privacy → LocalPath → Url；Gitleaks 仅在启用且探测可用时存在）。</summary>
    internal ImmutableArray<IDetector> Detectors { get; }

    /// <summary>本次扫描参与评估的启用规则总数。</summary>
    internal int RulesEvaluated { get; }

    /// <summary>相对默认策略的降级项（配置级；进程生命周期内不变）。</summary>
    internal ImmutableArray<PolicyOverride> PolicyOverrides { get; }
}
