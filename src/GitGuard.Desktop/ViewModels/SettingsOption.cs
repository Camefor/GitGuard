using CommunityToolkit.Mvvm.ComponentModel;
using GitGuard.Desktop.Localization;

namespace GitGuard.Desktop.ViewModels;

/// <summary>设置页语言选项（GGD-0046）：语言名称使用原生写法，不随界面语言变化。</summary>
public sealed class SettingsLanguageOption
{
    /// <summary>初始化语言选项。</summary>
    /// <param name="value">语言取值。</param>
    /// <param name="label">显示名称（中文 / English）。</param>
    public SettingsLanguageOption(DesktopLanguage value, string label)
    {
        Value = value;
        Label = label;
    }

    /// <summary>语言取值。</summary>
    public DesktopLanguage Value { get; }

    /// <summary>显示名称。</summary>
    public string Label { get; }
}

/// <summary>设置页主题选项（GGD-0046）：跟随系统 / 浅色 / 深色，标签随界面语言刷新。</summary>
public sealed class SettingsThemeOption : ObservableObject
{
    private string _label;

    /// <summary>初始化主题选项。</summary>
    /// <param name="value">主题模式。</param>
    /// <param name="language">初始界面语言。</param>
    public SettingsThemeOption(DesktopThemeMode value, DesktopLanguage language)
    {
        Value = value;
        _label = DesktopText.ThemeOptionLabel(value, language);
    }

    /// <summary>主题模式。</summary>
    public DesktopThemeMode Value { get; }

    /// <summary>本地化显示标签。</summary>
    public string Label
    {
        get => _label;
        private set => SetProperty(ref _label, value);
    }

    /// <summary>按新界面语言刷新标签。</summary>
    /// <param name="language">目标界面语言。</param>
    public void Relocalize(DesktopLanguage language) =>
        Label = DesktopText.ThemeOptionLabel(Value, language);
}
