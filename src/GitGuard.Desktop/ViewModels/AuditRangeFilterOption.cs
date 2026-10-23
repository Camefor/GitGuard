using CommunityToolkit.Mvvm.ComponentModel;
using GitGuard.Desktop.Localization;

namespace GitGuard.Desktop.ViewModels;

/// <summary>审计日志的时间范围筛选选项（GGD-0046）：<see cref="Days"/> 为 <see langword="null"/> 表示全部时间。</summary>
public sealed class AuditRangeFilterOption : ObservableObject
{
    private string _label;

    /// <summary>初始化时间范围筛选选项。</summary>
    /// <param name="days">最近天数；<see langword="null"/> 表示全部时间。</param>
    /// <param name="language">初始界面语言。</param>
    public AuditRangeFilterOption(int? days, DesktopLanguage language)
    {
        Days = days;
        _label = DesktopText.AuditLogRangeFilterLabel(days, language);
    }

    /// <summary>最近天数；<see langword="null"/> 表示全部时间。</summary>
    public int? Days { get; }

    /// <summary>本地化显示标签。</summary>
    public string Label
    {
        get => _label;
        private set => SetProperty(ref _label, value);
    }

    /// <summary>按新界面语言刷新标签。</summary>
    /// <param name="language">目标界面语言。</param>
    public void Relocalize(DesktopLanguage language) =>
        Label = DesktopText.AuditLogRangeFilterLabel(Days, language);
}
