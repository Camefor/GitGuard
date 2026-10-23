using CommunityToolkit.Mvvm.ComponentModel;
using GitGuard.Desktop.Localization;

namespace GitGuard.Desktop.ViewModels;

/// <summary>侧边导航项：承载区块身份与随界面语言切换的文案。</summary>
public sealed class NavigationItemViewModel : ObservableObject
{
    private string _label;
    private string _description;

    /// <summary>初始化导航项。</summary>
    /// <param name="section">导航区块。</param>
    /// <param name="language">初始界面语言。</param>
    public NavigationItemViewModel(DesktopSection section, DesktopLanguage language)
    {
        Section = section;
        _label = DesktopText.NavLabel(section, language);
        _description = DesktopText.NavDescription(section, language);
    }

    /// <summary>导航区块身份。</summary>
    public DesktopSection Section { get; }

    /// <summary>本地化标题。</summary>
    public string Label
    {
        get => _label;
        private set => SetProperty(ref _label, value);
    }

    /// <summary>本地化说明。</summary>
    public string Description
    {
        get => _description;
        private set => SetProperty(ref _description, value);
    }

    /// <summary>按新界面语言刷新文案。</summary>
    /// <param name="language">目标界面语言。</param>
    public void Relocalize(DesktopLanguage language)
    {
        Label = DesktopText.NavLabel(Section, language);
        Description = DesktopText.NavDescription(Section, language);
    }
}
