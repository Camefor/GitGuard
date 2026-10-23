using FluentAssertions;
using GitGuard.Desktop.Localization;

namespace GitGuard.Desktop.Tests;

/// <summary>应用级状态测试（12-前端组件设计 §4）：版本校验、默认语言与语言切换通知。</summary>
public sealed class AppStateTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_InvalidVersion_Throws(string? version)
    {
        Action act = () => _ = new AppState(version!);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Constructor_ValidVersion_ExposesVersionAndDefaultsToChinese()
    {
        var state = new AppState("0.1.0");

        state.Version.Should().Be("0.1.0");
        state.Language.Should().Be(DesktopLanguage.Chinese);
        state.ThemeMode.Should().Be(DesktopThemeMode.System);
    }

    [Fact]
    public void Constructor_InvalidThemeMode_Throws()
    {
        Action act = () => _ = new AppState("0.1.0", themeMode: (DesktopThemeMode)99);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void ThemeMode_ChangeNotifiesAndKeepsLanguage()
    {
        var state = new AppState("0.1.0");
        var changed = new List<string?>();
        state.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        state.ThemeMode = DesktopThemeMode.Dark;

        state.ThemeMode.Should().Be(DesktopThemeMode.Dark);
        state.Language.Should().Be(DesktopLanguage.Chinese);
        changed.Should().Equal(nameof(AppState.ThemeMode));
    }

    [Fact]
    public void SettingSameThemeMode_DoesNotNotify()
    {
        var state = new AppState("0.1.0");
        var changed = new List<string?>();
        state.PropertyChanged += (sender, e) => changed.Add(e.PropertyName);

        state.ThemeMode = DesktopThemeMode.System;

        changed.Should().BeEmpty();
    }

    [Fact]
    public void ToggleLanguage_AlternatesAndNotifies()
    {
        var state = new AppState("0.1.0");
        var changed = new List<string?>();
        state.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        state.ToggleLanguage();
        state.Language.Should().Be(DesktopLanguage.English);

        state.ToggleLanguage();
        state.Language.Should().Be(DesktopLanguage.Chinese);

        changed.Should().Equal(
            nameof(AppState.Language),
            nameof(AppState.Language));
    }

    [Fact]
    public void SettingSameLanguage_DoesNotNotify()
    {
        var state = new AppState("0.1.0");
        var changed = new List<string?>();
        state.PropertyChanged += (sender, e) => changed.Add(e.PropertyName);

        state.Language = DesktopLanguage.Chinese;

        changed.Should().BeEmpty();
    }
}
