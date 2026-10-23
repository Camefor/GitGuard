using CommunityToolkit.Mvvm.ComponentModel;
using GitGuard.Desktop.Localization;

namespace GitGuard.Desktop;

/// <summary>
/// 应用级状态（12-前端组件设计 §4）：语言、版本等跨页面共享数据，由组合根创建并在进程内保持单例。
/// 主题与外部工具路径在后续设置任务中扩展。
/// </summary>
public sealed class AppState : ObservableObject
{
    private DesktopLanguage _language = DesktopLanguage.Chinese;

    /// <summary>初始化应用状态。</summary>
    /// <param name="version">桌面端版本号（写入侧边栏与后续诊断信息）。</param>
    /// <exception cref="ArgumentException"><paramref name="version"/> 为空或全空白。</exception>
    public AppState(string version)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(version);
        Version = version;
    }

    /// <summary>桌面端版本号。</summary>
    public string Version { get; }

    /// <summary>当前界面语言；修改时触发属性通知，由 ViewModel 刷新全部文案。</summary>
    public DesktopLanguage Language
    {
        get => _language;
        set => SetProperty(ref _language, value);
    }

    /// <summary>在中 / 英之间切换界面语言。</summary>
    public void ToggleLanguage() =>
        Language = Language == DesktopLanguage.Chinese ? DesktopLanguage.English : DesktopLanguage.Chinese;
}
