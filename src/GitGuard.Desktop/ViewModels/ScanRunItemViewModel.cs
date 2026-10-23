using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using GitGuard.Core.Model;
using GitGuard.Desktop.Localization;

namespace GitGuard.Desktop.ViewModels;

/// <summary>
/// 扫描时间线列表项（GGD-0043）：包装 <see cref="AuditRunSummary"/>，只承载展示文案；
/// 完整 Finding 明细由 <see cref="ScanHistoryViewModel"/> 在选中后按运行标识异步加载。
/// </summary>
public sealed class ScanRunItemViewModel : ObservableObject
{
    private DesktopLanguage _language;

    /// <summary>初始化扫描时间线列表项。</summary>
    /// <param name="summary">扫描运行摘要。</param>
    /// <param name="language">初始界面语言。</param>
    /// <exception cref="ArgumentNullException"><paramref name="summary"/> 为 <see langword="null"/>。</exception>
    public ScanRunItemViewModel(AuditRunSummary summary, DesktopLanguage language)
    {
        ArgumentNullException.ThrowIfNull(summary);

        Summary = summary;
        _language = language;
    }

    /// <summary>扫描运行摘要数据。</summary>
    public AuditRunSummary Summary { get; }

    /// <summary>运行身份标识。</summary>
    public Guid RunId => Summary.RunId;

    /// <summary>扫描结果。</summary>
    public ScanResult Result => Summary.Result;

    /// <summary>按用户区域设置显示的本地开始时间。</summary>
    public string TimestampText => Summary.StartedAt.ToLocalTime().ToString("g", CultureInfo.CurrentCulture);

    /// <summary>本地化扫描结果标签。</summary>
    public string ResultText => DesktopText.ScanResultLabel(Summary.Result, _language);

    /// <summary>本地化扫描类型标签。</summary>
    public string ScanTypeText => DesktopText.ScanTypeLabel(Summary.ScanType, _language);

    /// <summary>本地化发现项数量。</summary>
    public string FindingCountText => DesktopText.ScanHistoryFindingCount(Summary.FindingCount, _language);

    /// <summary>扫描耗时。</summary>
    public string DurationText => DesktopText.ScanHistoryDuration(Summary.DurationMs);

    /// <summary>本地化退出码文本。</summary>
    public string ExitCodeText => DesktopText.ScanHistoryExitCode(Summary.ExitCode, _language);

    /// <summary>时间线卡片摘要行（发现数 / 耗时 / 退出码）。</summary>
    public string MetaText => $"{FindingCountText} · {DurationText} · {ExitCodeText}";

    /// <summary>按新界面语言刷新派生文案。</summary>
    /// <param name="language">目标界面语言。</param>
    public void Relocalize(DesktopLanguage language)
    {
        _language = language;
        OnPropertyChanged(nameof(ResultText));
        OnPropertyChanged(nameof(ScanTypeText));
        OnPropertyChanged(nameof(FindingCountText));
        OnPropertyChanged(nameof(DurationText));
        OnPropertyChanged(nameof(ExitCodeText));
        OnPropertyChanged(nameof(MetaText));
    }
}
