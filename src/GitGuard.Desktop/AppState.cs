using CommunityToolkit.Mvvm.ComponentModel;
using GitGuard.Desktop.Localization;

namespace GitGuard.Desktop;

/// <summary>
/// 应用级状态（12-前端组件设计 §4）：语言、主题与版本等跨页面共享数据，由组合根创建并在进程内保持单例。
/// GGD-0046 起主题模式与外部工具路径由设置页维护。
/// </summary>
public sealed class AppState : ObservableObject
{
    private DesktopLanguage _language;
    private DesktopThemeMode _themeMode;

    /// <summary>初始化应用状态。</summary>
    /// <param name="version">桌面端版本号（写入侧边栏与后续诊断信息）。</param>
    /// <param name="language">初始界面语言；省略时为简体中文。</param>
    /// <param name="themeMode">初始主题模式；省略时跟随操作系统。</param>
    /// <exception cref="ArgumentException"><paramref name="version"/> 为空或全空白。</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="language"/> 或 <paramref name="themeMode"/> 未定义。</exception>
    public AppState(
        string version,
        DesktopLanguage language = DesktopLanguage.Chinese,
        DesktopThemeMode themeMode = DesktopThemeMode.System)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(version);
        if (!Enum.IsDefined(language))
        {
            throw new ArgumentOutOfRangeException(nameof(language), language, "未知的界面语言");
        }

        if (!Enum.IsDefined(themeMode))
        {
            throw new ArgumentOutOfRangeException(nameof(themeMode), themeMode, "未知的主题模式");
        }

        Version = version;
        _language = language;
        _themeMode = themeMode;
    }

    /// <summary>桌面端版本号。</summary>
    public string Version { get; }

    /// <summary>当前界面语言；修改时触发属性通知，由 ViewModel 刷新全部文案。</summary>
    public DesktopLanguage Language
    {
        get => _language;
        set => SetProperty(ref _language, value);
    }

    /// <summary>当前主题模式；修改时触发属性通知，由窗口与组合根应用主题变体。</summary>
    public DesktopThemeMode ThemeMode
    {
        get => _themeMode;
        set => SetProperty(ref _themeMode, value);
    }

    /// <summary>在中 / 英之间切换界面语言。</summary>
    public void ToggleLanguage() =>
        Language = Language == DesktopLanguage.Chinese ? DesktopLanguage.English : DesktopLanguage.Chinese;
}
