using System.Collections.Immutable;
using GitGuard.Application.Configuration;
using GitGuard.Core.Model;
using GitGuard.Core.Rules;
using GitGuard.Detection.Rules;

namespace GitGuard.Application.Rules;

/// <summary>
/// 单条规则目录条目（<c>gitguard rules list</c> / <c>view</c> 的数据源，FR-080）。
/// <see cref="Enabled"/> 为「有效启用」：规则自身启用、未被任何类别 <c>disabled_rules</c> 命中且所属类别启用。
/// </summary>
public sealed record RuleCatalogEntry
{
    /// <summary>初始化规则目录条目。</summary>
    /// <param name="ruleId">规则 ID（<c>GGD/…</c> 或 <c>USR/…</c>）。</param>
    /// <param name="name">规则名称。</param>
    /// <param name="category">规则 ID 的类别段（<c>SECRET</c> / <c>PRIVACY</c> / <c>LOCALPATH</c> / <c>URL</c>）。</param>
    /// <param name="severity">命中严重度。</param>
    /// <param name="source">规则来源（Builtin / User / Gitleaks）。</param>
    /// <param name="enabled">是否有效启用。</param>
    /// <param name="pattern">规则模式（<c>--show-pattern</c> 时展示正则原文）。</param>
    public RuleCatalogEntry(
        string ruleId,
        string name,
        string category,
        Severity severity,
        RuleSource source,
        bool enabled,
        RulePattern pattern)
    {
        ArgumentNullException.ThrowIfNull(ruleId);
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(category);
        ArgumentNullException.ThrowIfNull(pattern);

        RuleId = ruleId;
        Name = name;
        Category = category;
        Severity = severity;
        Source = source;
        Enabled = enabled;
        Pattern = pattern;
    }

    /// <summary>规则 ID（<c>GGD/…</c> 或 <c>USR/…</c>）。</summary>
    public string RuleId { get; }

    /// <summary>规则名称。</summary>
    public string Name { get; }

    /// <summary>规则 ID 的类别段（<c>SECRET</c> / <c>PRIVACY</c> / <c>LOCALPATH</c> / <c>URL</c>）。</summary>
    public string Category { get; }

    /// <summary>命中严重度。</summary>
    public Severity Severity { get; }

    /// <summary>规则来源（Builtin / User / Gitleaks）。</summary>
    public RuleSource Source { get; }

    /// <summary>是否有效启用。</summary>
    public bool Enabled { get; }

    /// <summary>规则模式（<c>--show-pattern</c> 时展示正则原文）。</summary>
    public RulePattern Pattern { get; }
}

/// <summary>
/// 规则目录（FR-080）：合并内置规则与配置声明的自定义规则（<c>rules.custom</c>），
/// 按检测器装配相同的选择语义计算有效启用状态，供 <c>gitguard rules</c> 查询。
/// 不装配检测器、不打开审计库。
/// </summary>
public static class RuleCatalog
{
    /// <summary>构建当前配置下的规则目录。</summary>
    /// <param name="config">有效配置。</param>
    /// <returns>内置规则在前、自定义规则在后的条目列表（保持规则集书写顺序）。</returns>
    /// <exception cref="ArgumentNullException"><paramref name="config"/> 为 <see langword="null"/>。</exception>
    /// <exception cref="GitGuard.Core.DetectionException">内置规则集加载失败（FailClosed）。</exception>
    public static ImmutableArray<RuleCatalogEntry> Build(GitGuardConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        IReadOnlyList<DetectionRule> builtinRules = BuiltinRuleLoader.LoadEmbedded();
        var allRules = new List<DetectionRule>(builtinRules.Count + config.CustomRules.Length);
        allRules.AddRange(builtinRules);
        allRules.AddRange(config.CustomRules);

        HashSet<string> disabledIds = RuleSelection.CreateDisabledIdSet(config);
        var entries = ImmutableArray.CreateBuilder<RuleCatalogEntry>(allRules.Count);
        foreach (DetectionRule rule in allRules)
        {
            string category = RuleSelection.CategoryOf(rule.RuleId);
            bool enabled = rule.Enabled
                && !disabledIds.Contains(rule.RuleId)
                && RuleSelection.IsCategoryEnabled(category, config);
            entries.Add(new RuleCatalogEntry(
                rule.RuleId,
                rule.Name,
                category,
                rule.Severity,
                rule.Source,
                enabled,
                rule.Pattern));
        }

        return entries.ToImmutable();
    }
}
