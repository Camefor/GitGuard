using CommunityToolkit.Mvvm.ComponentModel;
using GitGuard.Desktop.Localization;

namespace GitGuard.Desktop.ViewModels;

/// <summary>
/// 一级页面骨架（12-前端组件设计 §1 业务层）：当前承载标题与空状态文案，
/// 后续任务以真实页面 ViewModel 替换内容，导航壳保持不变。
/// </summary>
public sealed class SectionViewModel : ObservableObject
{
    private string _title;
    private string _subtitle;
    private string _body;

    /// <summary>初始化页面骨架。</summary>
    /// <param name="section">导航区块。</param>
    /// <param name="language">初始界面语言。</param>
    public SectionViewModel(DesktopSection section, DesktopLanguage language)
    {
        Section = section;
        _title = DesktopText.SectionTitle(section, language);
        _subtitle = DesktopText.SectionSubtitle(section, language);
        _body = DesktopText.SectionBody(section, language);
    }

    /// <summary>导航区块身份。</summary>
    public DesktopSection Section { get; }

    /// <summary>页面标题。</summary>
    public string Title
    {
        get => _title;
        private set => SetProperty(ref _title, value);
    }

    /// <summary>页面副标题。</summary>
    public string Subtitle
    {
        get => _subtitle;
        private set => SetProperty(ref _subtitle, value);
    }

    /// <summary>空状态说明。</summary>
    public string Body
    {
        get => _body;
        private set => SetProperty(ref _body, value);
    }

    /// <summary>按新界面语言刷新文案。</summary>
    /// <param name="language">目标界面语言。</param>
    public void Relocalize(DesktopLanguage language)
    {
        Title = DesktopText.SectionTitle(Section, language);
        Subtitle = DesktopText.SectionSubtitle(Section, language);
        Body = DesktopText.SectionBody(Section, language);
    }
}
