using FluentAssertions;
using GitGuard.Application.Rules;
using GitGuard.Core.Model;
using GitGuard.Core.Rules;
using GitGuard.Desktop.Localization;
using GitGuard.Desktop.Services;
using GitGuard.Desktop.ViewModels;

namespace GitGuard.Desktop.Tests;

/// <summary>
/// 规则库 ViewModel 测试（GGD-0045 / 11-前端UI设计 §2、12-前端组件设计 §3.7）：目录加载、搜索节流、
/// 来源 / 类别 / 启用状态筛选、正则默认隐藏与 Gitleaks 分类说明；数据访问由假对象驱动，不访问真实规则集。
/// </summary>
public sealed class RulesViewModelTests
{
    [Fact]
    public async Task EnsureLoadedAsync_PopulatesCatalogAndSelectsFirstRule()
    {
        var fake = new FakeRuleOperations();
        var viewModel = CreateViewModel(fake);

        await viewModel.EnsureLoadedAsync(TestContext.Current.CancellationToken);

        viewModel.TotalRuleCount.Should().Be(5);
        viewModel.EnabledRuleCount.Should().Be(3);
        viewModel.VisibleRuleCount.Should().Be(5);
        viewModel.VisibleRules.Select(rule => rule.RuleId).Should().Equal(
            "GGD/SECRET/AWS_ACCESS_KEY",
            "USR/SECRET/INTERNAL_TOKEN",
            "GGD/PRIVACY/CN_ID_CARD",
            "GGD/LOCALPATH/WINDOWS_USER_PATH",
            "GGD/URL/PRIVATE_IP_URL");
        viewModel.SelectedRule.Should().BeSameAs(viewModel.VisibleRules[0]);
        viewModel.HasSelectedRule.Should().BeTrue();
        viewModel.SummaryText.Should().Be("共 5 条 · 启用 3 条 · 当前显示 5 条");
        viewModel.IsLoading.Should().BeFalse();
        viewModel.HasLoadMessage.Should().BeFalse();
        viewModel.IsEmpty.Should().BeFalse();
        fake.Calls.Should().Be(1);
    }

    [Fact]
    public async Task EnsureLoadedAsync_SecondCall_DoesNotReload()
    {
        var fake = new FakeRuleOperations();
        var viewModel = CreateViewModel(fake);

        await viewModel.EnsureLoadedAsync(TestContext.Current.CancellationToken);
        await viewModel.EnsureLoadedAsync(TestContext.Current.CancellationToken);

        fake.Calls.Should().Be(1);
    }

    [Fact]
    public async Task RefreshCommand_ReloadsCatalog()
    {
        var fake = new FakeRuleOperations();
        var viewModel = CreateViewModel(fake);
        await viewModel.EnsureLoadedAsync(TestContext.Current.CancellationToken);

        await viewModel.RefreshCommand.ExecuteAsync(null);

        fake.Calls.Should().Be(2);
        viewModel.VisibleRuleCount.Should().Be(5);
    }

    [Fact]
    public async Task SearchText_FiltersByRuleIdAndNameCaseInsensitively()
    {
        var fake = new FakeRuleOperations();
        var viewModel = CreateViewModel(fake);
        await viewModel.EnsureLoadedAsync(TestContext.Current.CancellationToken);

        viewModel.SearchText = "aws";

        viewModel.VisibleRules.Should().ContainSingle()
            .Which.RuleId.Should().Be("GGD/SECRET/AWS_ACCESS_KEY");

        viewModel.SearchText = "id CARD";

        viewModel.VisibleRules.Should().ContainSingle()
            .Which.RuleId.Should().Be("GGD/PRIVACY/CN_ID_CARD");
        viewModel.VisibleRuleCount.Should().Be(1);
        viewModel.SummaryText.Should().Be("共 5 条 · 启用 3 条 · 当前显示 1 条");
    }

    [Fact]
    public async Task SearchText_IsDebouncedBeforeFiltering()
    {
        var fake = new FakeRuleOperations();
        var viewModel = new RulesViewModel(
            fake,
            DesktopLanguage.Chinese,
            TimeSpan.FromMilliseconds(150));
        await viewModel.EnsureLoadedAsync(TestContext.Current.CancellationToken);

        viewModel.SearchText = "aws";
        viewModel.VisibleRuleCount.Should().Be(5);

        await WaitUntilAsync(
            () => viewModel.VisibleRuleCount == 1,
            TestContext.Current.CancellationToken);

        viewModel.VisibleRules.Should().ContainSingle()
            .Which.RuleId.Should().Be("GGD/SECRET/AWS_ACCESS_KEY");
    }

    [Fact]
    public async Task SearchText_RapidInput_AppliesOnlyLatestTerm()
    {
        var fake = new FakeRuleOperations();
        var viewModel = new RulesViewModel(
            fake,
            DesktopLanguage.Chinese,
            TimeSpan.FromMilliseconds(100));
        await viewModel.EnsureLoadedAsync(TestContext.Current.CancellationToken);

        viewModel.SearchText = "aws";
        viewModel.SearchText = "id card";

        await WaitUntilAsync(
            () => viewModel.VisibleRuleCount == 1,
            TestContext.Current.CancellationToken);

        viewModel.VisibleRules.Should().ContainSingle()
            .Which.RuleId.Should().Be("GGD/PRIVACY/CN_ID_CARD");
    }

    [Fact]
    public async Task SourceFilter_SeparatesBuiltinAndCustomRules()
    {
        var fake = new FakeRuleOperations();
        var viewModel = CreateViewModel(fake);
        await viewModel.EnsureLoadedAsync(TestContext.Current.CancellationToken);

        viewModel.SelectedSourceFilter = FindFilter(viewModel.SourceFilters, RulesViewModel.SOURCE_BUILTIN);

        viewModel.VisibleRules.Should().HaveCount(4)
            .And.OnlyContain(rule => rule.Source == RuleSource.Builtin);

        viewModel.SelectedSourceFilter = FindFilter(viewModel.SourceFilters, RulesViewModel.SOURCE_USER);

        viewModel.VisibleRules.Should().ContainSingle()
            .Which.RuleId.Should().Be("USR/SECRET/INTERNAL_TOKEN");
    }

    [Fact]
    public async Task GitleaksSourceFilter_ShowsExplanationAndNoStaticRows()
    {
        var fake = new FakeRuleOperations(
            CreateSnapshot(gitleaksEnabled: true, gitleaksPath: @"C:\tools\gitleaks.exe"));
        var viewModel = CreateViewModel(fake);
        await viewModel.EnsureLoadedAsync(TestContext.Current.CancellationToken);

        viewModel.SelectedSourceFilter = FindFilter(viewModel.SourceFilters, RulesViewModel.SOURCE_GITLEAKS);

        viewModel.VisibleRules.Should().BeEmpty();
        viewModel.IsGitleaksInfoVisible.Should().BeTrue();
        viewModel.GitleaksInfoText.Should().Contain(@"C:\tools\gitleaks.exe");
        viewModel.IsEmpty.Should().BeTrue();
        viewModel.EmptyStateText.Should().Contain("Gitleaks");

        viewModel.SelectedSourceFilter = FindFilter(viewModel.SourceFilters, RulesViewModel.SOURCE_ALL);

        viewModel.IsGitleaksInfoVisible.Should().BeFalse();
        viewModel.VisibleRuleCount.Should().Be(5);
    }

    [Fact]
    public async Task GitleaksDisabled_ExplainsAdapterState()
    {
        var fake = new FakeRuleOperations();
        var viewModel = CreateViewModel(fake);
        await viewModel.EnsureLoadedAsync(TestContext.Current.CancellationToken);

        viewModel.SelectedSourceFilter = FindFilter(viewModel.SourceFilters, RulesViewModel.SOURCE_GITLEAKS);

        viewModel.GitleaksInfoText.Should().Contain("rules.secrets.use_gitleaks = false");
    }

    [Fact]
    public async Task CategoryFilter_LimitsVisibleRules()
    {
        var fake = new FakeRuleOperations();
        var viewModel = CreateViewModel(fake);
        await viewModel.EnsureLoadedAsync(TestContext.Current.CancellationToken);

        viewModel.SelectedCategoryFilter = FindFilter(viewModel.CategoryFilters, "PRIVACY");

        viewModel.VisibleRules.Should().ContainSingle()
            .Which.Category.Should().Be("PRIVACY");
        viewModel.SelectedRule.Should().BeSameAs(viewModel.VisibleRules[0]);
    }

    [Fact]
    public async Task EnabledOnly_HidesEffectivelyDisabledRules()
    {
        var fake = new FakeRuleOperations();
        var viewModel = CreateViewModel(fake);
        await viewModel.EnsureLoadedAsync(TestContext.Current.CancellationToken);

        viewModel.EnabledOnly = true;

        viewModel.VisibleRules.Should().HaveCount(3)
            .And.OnlyContain(rule => rule.Enabled);
        viewModel.SummaryText.Should().Be("共 5 条 · 启用 3 条 · 当前显示 3 条");
    }

    [Fact]
    public async Task CombinedFiltersWithoutMatch_ShowsEmptyState()
    {
        var fake = new FakeRuleOperations();
        var viewModel = CreateViewModel(fake);
        await viewModel.EnsureLoadedAsync(TestContext.Current.CancellationToken);

        viewModel.SelectedCategoryFilter = FindFilter(viewModel.CategoryFilters, "URL");
        viewModel.EnabledOnly = true;
        viewModel.SearchText = "no-such-rule";

        viewModel.VisibleRules.Should().BeEmpty();
        viewModel.IsEmpty.Should().BeTrue();
        viewModel.SelectedRule.Should().BeNull();
        viewModel.HasSelectedRule.Should().BeFalse();
        viewModel.EmptyStateText.Should().Be("没有符合当前搜索与筛选条件的规则。");
    }

    [Fact]
    public async Task Filters_KeepSelectionWhenStillVisibleOtherwiseSelectFirst()
    {
        var fake = new FakeRuleOperations();
        var viewModel = CreateViewModel(fake);
        await viewModel.EnsureLoadedAsync(TestContext.Current.CancellationToken);
        RuleItemViewModel firstRule = viewModel.VisibleRules[0];

        viewModel.SelectedCategoryFilter = FindFilter(viewModel.CategoryFilters, "SECRET");

        viewModel.SelectedRule.Should().BeSameAs(firstRule);

        viewModel.SelectedCategoryFilter = FindFilter(viewModel.CategoryFilters, "PRIVACY");

        viewModel.SelectedRule.Should().NotBeSameAs(firstRule);
        viewModel.SelectedRule!.RuleId.Should().Be("GGD/PRIVACY/CN_ID_CARD");
    }

    [Fact]
    public async Task Pattern_IsHiddenByDefaultAndTogglesOnDemand()
    {
        var fake = new FakeRuleOperations();
        var viewModel = CreateViewModel(fake);
        await viewModel.EnsureLoadedAsync(TestContext.Current.CancellationToken);

        viewModel.IsPatternVisible.Should().BeFalse();
        viewModel.PatternToggleText.Should().Be("显示正则");
        viewModel.SelectedPatternText.Should().Be("默认隐藏，点击「显示正则」查看。");
        viewModel.SelectedPatternText.Should().NotContain("AKIA");

        viewModel.TogglePatternCommand.Execute(null);

        viewModel.IsPatternVisible.Should().BeTrue();
        viewModel.PatternToggleText.Should().Be("隐藏正则");
        viewModel.SelectedPatternText.Should().Be("AKIA[0-9A-Z]{16}");

        viewModel.TogglePatternCommand.Execute(null);

        viewModel.IsPatternVisible.Should().BeFalse();
        viewModel.SelectedPatternText.Should().Be("默认隐藏，点击「显示正则」查看。");
    }

    [Fact]
    public async Task ChangingSelection_ResetsPatternVisibility()
    {
        var fake = new FakeRuleOperations();
        var viewModel = CreateViewModel(fake);
        await viewModel.EnsureLoadedAsync(TestContext.Current.CancellationToken);
        viewModel.TogglePatternCommand.Execute(null);
        viewModel.IsPatternVisible.Should().BeTrue();

        viewModel.SelectedRule = viewModel.VisibleRules[1];

        viewModel.IsPatternVisible.Should().BeFalse();
        viewModel.SelectedPatternText.Should().Be("默认隐藏，点击「显示正则」查看。");
    }

    [Fact]
    public async Task LoadFailure_ShowsLocalizedMessageAndKeepsEmptyStateHidden()
    {
        var fake = new FakeRuleOperations
        {
            Handler = _ => Task.FromException<RuleCatalogSnapshot>(new InvalidOperationException("读取失败")),
        };
        var viewModel = CreateViewModel(fake);

        await viewModel.EnsureLoadedAsync(TestContext.Current.CancellationToken);

        viewModel.LoadMessage.Should().Be("规则目录加载失败：读取失败");
        viewModel.HasLoadMessage.Should().BeTrue();
        viewModel.IsLoading.Should().BeFalse();
        viewModel.IsEmpty.Should().BeFalse();
        viewModel.TotalRuleCount.Should().Be(0);
    }

    [Fact]
    public async Task Relocalize_RefreshesLabelsFilterOptionsAndRuleText()
    {
        var fake = new FakeRuleOperations();
        var viewModel = CreateViewModel(fake);
        await viewModel.EnsureLoadedAsync(TestContext.Current.CancellationToken);
        viewModel.Title.Should().Be("规则");
        viewModel.SourceFilters[0].Label.Should().Be("全部来源");
        viewModel.VisibleRules[0].CategoryText.Should().Be("秘密");

        viewModel.Relocalize(DesktopLanguage.English);

        viewModel.Title.Should().Be("Rules");
        viewModel.SourceFilters[0].Label.Should().Be("All sources");
        viewModel.CategoryFilters[0].Label.Should().Be("All categories");
        viewModel.VisibleRules[0].CategoryText.Should().Be("Secret");
        viewModel.VisibleRules[0].MetaText.Should().Be("Secret · Built-in · High");
        viewModel.SummaryText.Should().Be("5 rules · 3 enabled · 5 shown");
        viewModel.PatternToggleText.Should().Be("Show regex");
        viewModel.SelectedPatternText.Should().Be("Hidden by default. Use \"Show regex\" to reveal it.");
        viewModel.GitleaksInfoText.Should().Contain("Adapter disabled");
        viewModel.EnabledOnlyText.Should().Be("Enabled only");
    }

    private static RulesViewModel CreateViewModel(FakeRuleOperations fake) =>
        new RulesViewModel(fake, DesktopLanguage.Chinese, TimeSpan.Zero);

    private static RuleFilterOption FindFilter(IReadOnlyList<RuleFilterOption> filters, string key) =>
        filters.Single(option => string.Equals(option.Key, key, StringComparison.Ordinal));

    private static async Task WaitUntilAsync(Func<bool> condition, CancellationToken cancellationToken)
    {
        var timeout = TimeSpan.FromSeconds(5);
        DateTimeOffset deadline = DateTimeOffset.UtcNow + timeout;
        while (!condition())
        {
            if (DateTimeOffset.UtcNow > deadline)
            {
                throw new TimeoutException("等待搜索结果超时。");
            }

            await Task.Delay(25, cancellationToken);
        }
    }

    private static RuleCatalogSnapshot CreateSnapshot(
        bool gitleaksEnabled = false,
        string? gitleaksPath = null) =>
        new RuleCatalogSnapshot(
            new[]
            {
                CreateEntry(
                    "GGD/SECRET/AWS_ACCESS_KEY",
                    "AWS access key",
                    "SECRET",
                    Severity.High,
                    RuleSource.Builtin,
                    enabled: true,
                    "AKIA[0-9A-Z]{16}"),
                CreateEntry(
                    "USR/SECRET/INTERNAL_TOKEN",
                    "Internal API token",
                    "SECRET",
                    Severity.Medium,
                    RuleSource.User,
                    enabled: false,
                    "tok_[a-z]{24}"),
                CreateEntry(
                    "GGD/PRIVACY/CN_ID_CARD",
                    "Chinese ID card",
                    "PRIVACY",
                    Severity.Critical,
                    RuleSource.Builtin,
                    enabled: true,
                    "[0-9]{17}[0-9X]"),
                CreateEntry(
                    "GGD/LOCALPATH/WINDOWS_USER_PATH",
                    "Windows user path",
                    "LOCALPATH",
                    Severity.Low,
                    RuleSource.Builtin,
                    enabled: false,
                    @"C:\\Users"),
                CreateEntry(
                    "GGD/URL/PRIVATE_IP_URL",
                    "Private IP URL",
                    "URL",
                    Severity.Medium,
                    RuleSource.Builtin,
                    enabled: true,
                    "https?://10\\."),
            },
            gitleaksEnabled,
            gitleaksPath);

    private static RuleCatalogEntry CreateEntry(
        string ruleId,
        string name,
        string category,
        Severity severity,
        RuleSource source,
        bool enabled,
        string pattern) =>
        new RuleCatalogEntry(
            ruleId,
            name,
            category,
            severity,
            source,
            enabled,
            RulePattern.Create(pattern));

    /// <summary>可编程规则目录假对象；默认返回固定目录（5 条、3 条有效启用）。</summary>
    private sealed class FakeRuleOperations : IRuleOperations
    {
        private readonly RuleCatalogSnapshot _snapshot;

        internal FakeRuleOperations(RuleCatalogSnapshot? snapshot = null)
        {
            _snapshot = snapshot ?? CreateSnapshot();
            Handler = _ => Task.FromResult(_snapshot);
        }

        internal Func<CancellationToken, Task<RuleCatalogSnapshot>> Handler { get; set; }

        internal int Calls { get; private set; }

        public Task<RuleCatalogSnapshot> LoadCatalogAsync(CancellationToken cancellationToken = default)
        {
            Calls++;
            return Handler(cancellationToken);
        }
    }
}
