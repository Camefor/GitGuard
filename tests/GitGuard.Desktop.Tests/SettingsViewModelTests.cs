using FluentAssertions;
using GitGuard.Desktop.Localization;
using GitGuard.Desktop.Services;
using GitGuard.Desktop.ViewModels;

namespace GitGuard.Desktop.Tests;

/// <summary>
/// 设置页 ViewModel 测试（GGD-0046 / 11-前端UI设计 §2、§5、§7）：语言 / 主题即时生效与持久化、
/// Gitleaks 覆盖的加载、保存与清除，以及错误与语言切换处理。持久化由假对象驱动，不访问真实用户目录。
/// </summary>
public sealed class SettingsViewModelTests
{
    [Fact]
    public async Task EnsureLoadedAsync_WithoutOverride_UsesEffectiveConfigValues()
    {
        var fake = new FakeSettingsOperations
        {
            Effective = new DesktopGitleaksSettings(true, @"C:\tools\gitleaks.exe"),
        };
        var viewModel = CreateViewModel(fake: fake);

        await viewModel.EnsureLoadedAsync(TestContext.Current.CancellationToken);

        viewModel.GitleaksEnabled.Should().BeTrue();
        viewModel.GitleaksPath.Should().Be(@"C:\tools\gitleaks.exe");
        viewModel.HasOverride.Should().BeFalse();
        viewModel.OverrideStatusText.Should().Be("当前使用 config.yml 中的 Gitleaks 设置。");
        viewModel.IsLoading.Should().BeFalse();
        fake.LoadCalls.Should().Be(1);
    }

    [Fact]
    public async Task EnsureLoadedAsync_WithOverride_PrefersPersistedValues()
    {
        var fake = new FakeSettingsOperations
        {
            Current = new DesktopSettings
            {
                Gitleaks = new DesktopGitleaksSettings(false, null),
            },
            Effective = new DesktopGitleaksSettings(true, "/usr/local/bin/gitleaks"),
        };
        var viewModel = CreateViewModel(fake: fake);

        await viewModel.EnsureLoadedAsync(TestContext.Current.CancellationToken);

        viewModel.GitleaksEnabled.Should().BeFalse();
        viewModel.GitleaksPath.Should().BeEmpty();
        viewModel.HasOverride.Should().BeTrue();
        viewModel.OverrideStatusText.Should().Contain("桌面覆盖已生效");
        viewModel.CanResetGitleaks.Should().BeTrue();
    }

    [Fact]
    public async Task EnsureLoadedAsync_CalledTwice_LoadsOnlyOnce()
    {
        var fake = new FakeSettingsOperations();
        var viewModel = CreateViewModel(fake: fake);

        await viewModel.EnsureLoadedAsync(TestContext.Current.CancellationToken);
        await viewModel.EnsureLoadedAsync(TestContext.Current.CancellationToken);

        fake.LoadCalls.Should().Be(1);
    }

    [Fact]
    public void SelectedLanguage_English_UpdatesAppStateAndPersists()
    {
        var state = new AppState("0.3.0");
        var fake = new FakeSettingsOperations();
        var viewModel = CreateViewModel(state: state, fake: fake);

        viewModel.SelectedLanguage = viewModel.LanguageOptions[1];

        viewModel.SelectedLanguage.Value.Should().Be(DesktopLanguage.English);
        state.Language.Should().Be(DesktopLanguage.English);
        fake.SaveCalls.Should().Be(1);
        fake.Current.Language.Should().Be(DesktopLanguage.English);
    }

    [Fact]
    public void SelectedTheme_Dark_UpdatesAppStateAndPersists()
    {
        var state = new AppState("0.3.0");
        var fake = new FakeSettingsOperations();
        var viewModel = CreateViewModel(state: state, fake: fake);

        viewModel.SelectedTheme = viewModel.ThemeOptions.Single(option => option.Value == DesktopThemeMode.Dark);

        state.ThemeMode.Should().Be(DesktopThemeMode.Dark);
        fake.SaveCalls.Should().Be(1);
        fake.Current.ThemeMode.Should().Be(DesktopThemeMode.Dark);
    }

    [Fact]
    public void SaveGitleaksCommand_PersistsTrimmedOverride()
    {
        var fake = new FakeSettingsOperations();
        var viewModel = CreateViewModel(fake: fake);
        viewModel.GitleaksEnabled = true;
        viewModel.GitleaksPath = @"  C:\tools\gitleaks.exe  ";

        viewModel.SaveGitleaksCommand.Execute(null);

        fake.Current.Gitleaks.Should().NotBeNull();
        fake.Current.Gitleaks!.Enabled.Should().BeTrue();
        fake.Current.Gitleaks.Path.Should().Be(@"C:\tools\gitleaks.exe");
        viewModel.HasOverride.Should().BeTrue();
        viewModel.Message.Should().Be("Gitleaks 设置已保存。");
        viewModel.IsMessageError.Should().BeFalse();
    }

    [Fact]
    public async Task ResetGitleaksCommand_ClearsOverrideAndRestoresEffectiveValues()
    {
        var fake = new FakeSettingsOperations
        {
            Current = new DesktopSettings
            {
                Gitleaks = new DesktopGitleaksSettings(true, "custom-gitleaks"),
            },
            Effective = new DesktopGitleaksSettings(false, null),
        };
        var viewModel = CreateViewModel(fake: fake);
        await viewModel.EnsureLoadedAsync(TestContext.Current.CancellationToken);

        await viewModel.ResetGitleaksCommand.ExecuteAsync(null);

        fake.Current.Gitleaks.Should().BeNull();
        viewModel.HasOverride.Should().BeFalse();
        viewModel.GitleaksEnabled.Should().BeFalse();
        viewModel.GitleaksPath.Should().BeEmpty();
        viewModel.Message.Should().Be("已清除桌面覆盖，恢复 config.yml 中的值。");
    }

    [Fact]
    public void SaveGitleaksCommand_WhenSaveFails_ShowsLocalizedError()
    {
        var fake = new FakeSettingsOperations { SaveException = new IOException("disk full") };
        var viewModel = CreateViewModel(fake: fake);

        viewModel.SaveGitleaksCommand.Execute(null);

        viewModel.IsMessageError.Should().BeTrue();
        viewModel.Message.Should().Contain("设置保存失败").And.Contain("disk full");
        viewModel.IsSaving.Should().BeFalse();
    }

    [Fact]
    public async Task EnsureLoadedAsync_WhenConfigLoadFails_ShowsLocalizedError()
    {
        var fake = new FakeSettingsOperations { LoadException = new InvalidOperationException("bad config") };
        var viewModel = CreateViewModel(fake: fake);

        await viewModel.EnsureLoadedAsync(TestContext.Current.CancellationToken);

        viewModel.IsMessageError.Should().BeTrue();
        viewModel.Message.Should().Be("设置加载失败：bad config");
        viewModel.IsLoading.Should().BeFalse();
    }

    [Fact]
    public void Relocalize_UpdatesThemeLabelsAndSyncsLanguageSelection()
    {
        var state = new AppState("0.3.0");
        var viewModel = CreateViewModel(state: state);
        viewModel.ThemeOptions.Select(option => option.Label)
            .Should().Equal("跟随系统", "浅色", "深色");

        state.Language = DesktopLanguage.English;
        viewModel.Relocalize(DesktopLanguage.English);

        viewModel.SelectedLanguage.Value.Should().Be(DesktopLanguage.English);
        viewModel.LanguageLabel.Should().Be("Interface language");
        viewModel.ThemeOptions.Select(option => option.Label)
            .Should().Equal("Follow system", "Light", "Dark");
        viewModel.OverrideStatusText.Should().Be("Using Gitleaks settings from config.yml.");
    }

    private static SettingsViewModel CreateViewModel(
        AppState? state = null,
        FakeSettingsOperations? fake = null) =>
        new SettingsViewModel(
            state ?? new AppState("0.3.0"),
            fake ?? new FakeSettingsOperations(),
            DesktopLanguage.Chinese);

    /// <summary>内存设置服务：记录调用次数并可按测试需要抛出异常。</summary>
    private sealed class FakeSettingsOperations : ISettingsOperations
    {
        public DesktopSettings Current { get; set; } = new DesktopSettings();

        internal DesktopGitleaksSettings Effective { get; set; } = new DesktopGitleaksSettings(false, null);

        internal Exception? LoadException { get; set; }

        internal Exception? SaveException { get; set; }

        internal int LoadCalls { get; private set; }

        internal int SaveCalls { get; private set; }

        public Task<DesktopGitleaksSettings> LoadGitleaksAsync(CancellationToken cancellationToken = default)
        {
            LoadCalls++;
            return LoadException is null
                ? Task.FromResult(Effective)
                : Task.FromException<DesktopGitleaksSettings>(LoadException);
        }

        public void Save(DesktopSettings settings)
        {
            SaveCalls++;
            if (SaveException is not null)
            {
                throw SaveException;
            }

            Current = settings;
        }
    }
}
