using System.Collections.Immutable;
using GitGuard.Application.Configuration;
using GitGuard.Core.Rules;

namespace GitGuard.Application.Rules;

/// <summary>
/// 规则选择语义的单一实现（检测器装配与规则目录共用）：类别启用判定、<c>disabled_rules</c> 汇总、
/// 「有效启用」判定与规则计数。规则 ID 的类别段与检测器 <c>RULE_CATEGORY</c> 常量一致：
/// <c>LOCALPATH</c> 而非配置键 <c>local_path</c>。
/// </summary>
internal static class RuleSelection
{
    /// <summary>支持的规则类别段（与四类检测器一一对应）。</summary>
    internal static readonly ImmutableHashSet<string> SupportedCategories =
        ImmutableHashSet.Create(StringComparer.Ordinal, "SECRET", "PRIVACY", "LOCALPATH", "URL");

    /// <summary>判断规则类别段对应的检测器是否启用。</summary>
    /// <param name="category">规则 ID 的类别段（如 <c>SECRET</c>）。</param>
    /// <param name="config">有效配置。</param>
    /// <returns>类别启用返回 <see langword="true"/>。</returns>
    internal static bool IsCategoryEnabled(string category, GitGuardConfig config) => category switch
    {
        "SECRET" => config.Secrets.Enabled,
        "PRIVACY" => config.Privacy.Enabled,
        "LOCALPATH" => config.LocalPath.Enabled,
        "URL" => config.Url.Enabled,
        _ => false,
    };

    /// <summary>取规则 ID 的类别段（第 2 段；命名契约保证恰好 3 段）。</summary>
    /// <param name="ruleId">规则 ID。</param>
    /// <returns>类别段（如 <c>SECRET</c>）。</returns>
    internal static string CategoryOf(string ruleId)
    {
        int firstSlash = ruleId.IndexOf('/');
        int secondSlash = ruleId.IndexOf('/', firstSlash + 1);
        return ruleId.Substring(firstSlash + 1, secondSlash - firstSlash - 1);
    }

    /// <summary>汇总四个类别 <c>disabled_rules</c> 声明的规则 ID。</summary>
    /// <param name="config">有效配置。</param>
    /// <returns>禁用规则 ID 集合（序数比较）。</returns>
    internal static HashSet<string> CreateDisabledIdSet(GitGuardConfig config)
    {
        var disabledIds = new HashSet<string>(StringComparer.Ordinal);
        CollectDisabledIds(disabledIds, config.Secrets.DisabledRules);
        CollectDisabledIds(disabledIds, config.Privacy.DisabledRules);
        CollectDisabledIds(disabledIds, config.LocalPath.DisabledRules);
        CollectDisabledIds(disabledIds, config.Url.DisabledRules);
        return disabledIds;
    }

    /// <summary>把 <c>disabled_rules</c> 声明的规则重建为禁用规则（<see cref="DetectionRule"/> 不可变，无原位开关）。</summary>
    /// <param name="rules">待处理规则（内置 + 自定义）。</param>
    /// <param name="disabledIds">禁用规则 ID 集合。</param>
    /// <param name="warnings">宽松解析警告收集器（追加未知规则 ID）。</param>
    /// <returns>重标后的规则列表；无禁用项时返回原列表。</returns>
    internal static IReadOnlyList<DetectionRule> ApplyDisabledRules(
        IReadOnlyList<DetectionRule> rules,
        HashSet<string> disabledIds,
        ICollection<string> warnings)
    {
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

    /// <summary>统计实际交付检测器的规则数（类别启用且规则未禁用）。</summary>
    /// <param name="rules">已重标禁用状态的规则列表。</param>
    /// <param name="config">有效配置。</param>
    /// <returns>启用规则总数。</returns>
    internal static int CountEnabledRules(IReadOnlyList<DetectionRule> rules, GitGuardConfig config)
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

    /// <summary>判断规则列表是否包含指定 ID。</summary>
    /// <param name="rules">规则列表。</param>
    /// <param name="ruleId">规则 ID。</param>
    /// <returns>包含返回 <see langword="true"/>。</returns>
    internal static bool ContainsRule(IReadOnlyList<DetectionRule> rules, string ruleId)
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

    private static void CollectDisabledIds(HashSet<string> target, IReadOnlyList<string> disabledRules)
    {
        foreach (string ruleId in disabledRules)
        {
            target.Add(ruleId);
        }
    }
}
