using Avalonia.Styling;
using FluentAssertions;
using GitGuard.Application.Audit;
using GitGuard.Application.Rules;
using GitGuard.Core.Model;
using GitGuard.Desktop.Localization;
using GitGuard.Desktop.Services;
using GitGuard.Desktop.ViewModels;

namespace GitGuard.Desktop.Tests;

/// <summary>主窗口 ViewModel 测试（11-前端UI设计 §1 / §7）：导航顺序、页面缓存与语言切换。</summary>
public sealed class MainViewModelTests
{
    [Fact]
    public void Constructor_BuildsFourSectionsInDeclaredOrderWithRepositoriesSelected()
    {
        var viewModel = CreateViewModel();

        viewModel.NavigationItems.Select(item => item.Section).Should().Equal(
            DesktopSection.Repositories,
            DesktopSection.Rules,
            DesktopSection.Logs,
            DesktopSection.Settings);
        viewModel.SelectedNavigation.Should().BeSameAs(viewModel.NavigationItems[0]);
        viewModel.CurrentPage.Section.Should().Be(DesktopSection.Repositories);
        viewModel.LanguageToggleText.Should().Be("English");
        viewModel.VersionText.Should().Be("GitGuard Desktop 0.1.0");
    }

    [Fact]
    public void SelectingNavigation_SwitchesCurrentPage()
    {
        var viewModel = CreateViewModel();

        viewModel.SelectedNavigation = viewModel.NavigationItems[2];

        viewModel.CurrentPage.Section.Should().Be(DesktopSection.Logs);
        viewModel.CurrentPage.Title.Should().Be("日志");
    }

    [Fact]
    public void SelectingSameNavigationAgain_ReusesCachedPageInstance()
    {
        var viewModel = CreateViewModel();
        SectionViewModel repositoriesPage = viewModel.CurrentPage;

        viewModel.SelectedNavigation = viewModel.NavigationItems[1];
        viewModel.SelectedNavigation = viewModel.NavigationItems[0];

        viewModel.CurrentPage.Should().BeSameAs(repositoriesPage);
    }

    [Fact]
    public void ClearingSelection_KeepsCurrentPage()
    {
        var viewModel = CreateViewModel();
        SectionViewModel page = viewModel.CurrentPage;

        viewModel.SelectedNavigation = null;

        viewModel.CurrentPage.Should().BeSameAs(page);
    }

    [Fact]
    public void ToggleLanguageCommand_RefreshesShellAndPages()
    {
        var viewModel = CreateViewModel();
        viewModel.SelectedNavigation = viewModel.NavigationItems[1];

        viewModel.ToggleLanguageCommand.Execute(null);

        viewModel.WindowTitle.Should().Be("GitGuard Desktop");
        viewModel.Tagline.Should().Be("Local secret leak prevention");
        viewModel.LanguageToggleText.Should().Be("中文");
        viewModel.VersionText.Should().Be("GitGuard Desktop 0.1.0");
        viewModel.NavigationItems.Select(item => item.Label)
            .Should().Equal("Repositories", "Rules", "Logs", "Settings");
        viewModel.CurrentPage.Title.Should().Be("Rules");
        viewModel.CurrentPage.Subtitle.Should().Be(
            "Browse built-in, custom, and Gitleaks rules with their enablement.");
    }

    [Fact]
    public void LanguageChange_RefreshAppliesToAllCachedPages()
    {
        var viewModel = CreateViewModel();
        viewModel.SelectedNavigation = viewModel.NavigationItems[3];
        viewModel.ToggleLanguageCommand.Execute(null);
        viewModel.ToggleLanguageCommand.Execute(null);

        viewModel.CurrentPage.Title.Should().Be("设置");
        viewModel.WindowTitle.Should().Be("GitGuard 桌面端");
    }

    [Fact]
    public async Task RulesNavigation_WithRuleOperations_BuildsAndLoadsRulesPage()
    {
        var fake = new FakeRuleOperations();
        var viewModel = new MainViewModel(
            new AppState("0.1.0"),
            new DesktopServices { RuleOperations = fake });

        viewModel.SelectedNavigation = viewModel.NavigationItems[1];
        var rulesPage = viewModel.CurrentPage.Should().BeOfType<RulesViewModel>().Subject;

        await rulesPage.EnsureLoadedAsync(TestContext.Current.CancellationToken);

        fake.Calls.Should().Be(1);
        rulesPage.Title.Should().Be("规则");
        rulesPage.IsLoading.Should().BeFalse();
    }

    [Fact]
    public void RulesNavigation_WithoutRuleOperations_KeepsSkeletonPage()
    {
        var viewModel = CreateViewModel();

        viewModel.SelectedNavigation = viewModel.NavigationItems[1];

        viewModel.CurrentPage.Should().BeOfType<SectionViewModel>();
        viewModel.CurrentPage.Title.Should().Be("规则");
    }

    [Fact]
    public async Task LogsNavigation_WithAuditOperations_BuildsAndLoadsLogsPage()
    {
        var fake = new FakeAuditOperations();
        var viewModel = new MainViewModel(
            new AppState("0.1.0"),
            new DesktopServices { AuditOperations = fake });

        viewModel.SelectedNavigation = viewModel.NavigationItems[2];
        var logsPage = viewModel.CurrentPage.Should().BeOfType<LogsViewModel>().Subject;
        await logsPage.EnsureLoadedAsync(TestContext.Current.CancellationToken);

        fake.Calls.Should().Be(1);
        logsPage.Title.Should().Be("日志");
        logsPage.IsLoading.Should().BeFalse();
    }

    [Fact]
    public async Task SettingsNavigation_WithSettingsOperations_BuildsAndLoadsSettingsPage()
    {
        var fake = new FakeSettingsOperations();
        var viewModel = new MainViewModel(
            new AppState("0.1.0"),
            new DesktopServices { SettingsOperations = fake });

        viewModel.SelectedNavigation = viewModel.NavigationItems[3];
        var settingsPage = viewModel.CurrentPage.Should().BeOfType<SettingsViewModel>().Subject;
        await settingsPage.EnsureLoadedAsync(TestContext.Current.CancellationToken);

        fake.LoadCalls.Should().Be(1);
        settingsPage.Title.Should().Be("设置");
        settingsPage.IsLoading.Should().BeFalse();
    }

    [Fact]
    public void ThemeModeChange_UpdatesRequestedThemeVariant()
    {
        var state = new AppState("0.1.0");
        var viewModel = CreateViewModel(state: state);

        viewModel.RequestedThemeVariant.Should().Be(ThemeVariant.Default);

        state.ThemeMode = DesktopThemeMode.Dark;

        viewModel.RequestedThemeVariant.Should().Be(ThemeVariant.Dark);
    }

    [Fact]
    public void ToggleLanguageCommand_WithSettingsOperations_PersistsLanguage()
    {
        var fake = new FakeSettingsOperations();
        var viewModel = new MainViewModel(
            new AppState("0.1.0"),
            new DesktopServices { SettingsOperations = fake });

        viewModel.ToggleLanguageCommand.Execute(null);

        fake.Current.Language.Should().Be(DesktopLanguage.English);
        fake.SaveCalls.Should().Be(1);
    }

    private static MainViewModel CreateViewModel(AppState? state = null) =>
        new MainViewModel(state ?? new AppState("0.1.0"));

    /// <summary>规则目录假对象：返回空目录，仅统计加载次数。</summary>
    private sealed class FakeRuleOperations : IRuleOperations
    {
        internal int Calls { get; private set; }

        public Task<RuleCatalogSnapshot> LoadCatalogAsync(CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(new RuleCatalogSnapshot(Array.Empty<RuleCatalogEntry>(), false, null));
        }
    }

    /// <summary>跨仓库审计假对象：返回空页，仅统计查询次数。</summary>
    private sealed class FakeAuditOperations : IAuditOperations
    {
        internal int Calls { get; private set; }

        public Task<AuditRunPage> QueryAsync(AuditQuery query, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(new AuditRunPage(Array.Empty<AuditRunSummary>(), 0));
        }

        public Task<AuditExportReport> ExportAsync(
            string outputPath,
            AuditExportFormat format,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new AuditExportReport(outputPath, format, 0, 0));
    }

    /// <summary>设置服务假对象：内存保存并统计调用次数。</summary>
    private sealed class FakeSettingsOperations : ISettingsOperations
    {
        public DesktopSettings Current { get; private set; } = new DesktopSettings();

        internal int LoadCalls { get; private set; }

        internal int SaveCalls { get; private set; }

        public Task<DesktopGitleaksSettings> LoadGitleaksAsync(CancellationToken cancellationToken = default)
        {
            LoadCalls++;
            return Task.FromResult(new DesktopGitleaksSettings(false, null));
        }

        public void Save(DesktopSettings settings)
        {
            SaveCalls++;
            Current = settings;
        }
    }
}
