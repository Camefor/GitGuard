using System.Collections.Immutable;
using FluentAssertions;
using GitGuard.Application.Configuration;
using GitGuard.Application.Rules;
using GitGuard.Core.Model;
using GitGuard.Core.Rules;

namespace GitGuard.Application.Tests;

/// <summary>
/// 规则目录测试（FR-080）：内置规则全集、自定义规则合并顺序、<c>disabled_rules</c> 与类别开关
/// 对「有效启用」状态的影响。规则目录与检测器装配共用 <c>RuleSelection</c> 语义。
/// </summary>
public sealed class RuleCatalogTests
{
    [Fact]
    public void Build_DefaultConfig_ContainsAllBuiltinRulesEnabled()
    {
        ImmutableArray<RuleCatalogEntry> catalog = RuleCatalog.Build(GitGuardConfig.Default);

        catalog.Should().HaveCount(50, "GGD-0049 后内置规则集为 50 条");
        catalog.Should().OnlyContain(entry => entry.Enabled);
        catalog.Should().OnlyContain(entry => entry.Source == RuleSource.Builtin);
        catalog.Select(entry => entry.RuleId).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void Build_CustomRule_AppearsAfterBuiltinsWithUserSource()
    {
        DetectionRule custom = CreateCustomRule("USR/SECRET/INTERNAL-TOKEN", enabled: true);
        GitGuardConfig config = CreateConfig(customRules: ImmutableArray.Create(custom));

        ImmutableArray<RuleCatalogEntry> catalog = RuleCatalog.Build(config);

        catalog.Should().HaveCount(51);
        catalog[^1].RuleId.Should().Be("USR/SECRET/INTERNAL-TOKEN", "自定义规则保持列表顺序追加在内置规则之后");
        catalog[^1].Source.Should().Be(RuleSource.User);
        catalog[^1].Enabled.Should().BeTrue();
        catalog[^1].Pattern.Regex.ToString().Should().Be("INT-[0-9]{4}");
    }

    [Fact]
    public void Build_RuleInDisabledRules_IsMarkedDisabled()
    {
        const string ruleId = "USR/SECRET/INTERNAL-TOKEN";
        SecretRulesSettings secrets = GitGuardConfig.Default.Secrets with
        {
            DisabledRules = ImmutableArray.Create(ruleId),
        };
        GitGuardConfig config = CreateConfig(
            customRules: ImmutableArray.Create(CreateCustomRule(ruleId, enabled: true)),
            secrets: secrets);

        RuleCatalogEntry entry = RuleCatalog.Build(config).Single(item => item.RuleId == ruleId);

        entry.Enabled.Should().BeFalse();
    }

    [Fact]
    public void Build_RuleWithEnabledFalse_IsMarkedDisabled()
    {
        const string ruleId = "USR/SECRET/INTERNAL-TOKEN";
        GitGuardConfig config = GitGuardConfig.Default with
        {
            CustomRules = ImmutableArray.Create(CreateCustomRule(ruleId, enabled: false)),
        };

        RuleCatalogEntry entry = RuleCatalog.Build(config).Single(item => item.RuleId == ruleId);

        entry.Enabled.Should().BeFalse();
    }

    [Fact]
    public void Build_CategoryDisabled_MarksAllRulesOfCategoryDisabled()
    {
        GitGuardConfig config = CreateConfig(
            url: GitGuardConfig.Default.Url with { Enabled = false });

        ImmutableArray<RuleCatalogEntry> catalog = RuleCatalog.Build(config);

        catalog.Should().HaveCount(50);
        catalog.Where(entry => entry.Category == "URL").Should().OnlyContain(entry => !entry.Enabled);
        catalog.Where(entry => entry.Category != "URL").Should().OnlyContain(entry => entry.Enabled);
    }

    private static GitGuardConfig CreateConfig(
        ImmutableArray<DetectionRule>? customRules = null,
        SecretRulesSettings? secrets = null,
        UrlRulesSettings? url = null)
    {
        GitGuardConfig defaults = GitGuardConfig.Default;
        return new GitGuardConfig(
            defaults.Version,
            defaults.Scan,
            defaults.Policy,
            secrets ?? defaults.Secrets,
            defaults.Privacy,
            defaults.LocalPath,
            url ?? defaults.Url,
            defaults.LogFile,
            defaults.LogVerbose,
            defaults.Allowlist,
            defaults.LoadedConfigFiles,
            defaults.Warnings)
        {
            CustomRules = customRules ?? ImmutableArray<DetectionRule>.Empty,
        };
    }

    private static DetectionRule CreateCustomRule(string ruleId, bool enabled) => DetectionRule.Create(
        ruleId,
        "内部令牌",
        Severity.High,
        RulePattern.Create("INT-[0-9]{4}"),
        RuleSource.User,
        enabled);
}
