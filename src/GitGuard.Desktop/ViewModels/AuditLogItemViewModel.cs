using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using GitGuard.Core.Model;
using GitGuard.Desktop.Localization;

namespace GitGuard.Desktop.ViewModels;

/// <summary>
/// 跨仓库日志列表项（GGD-0046）：包装 <see cref="AuditRunSummary"/>，展示仓库、时间、结果与计数；
/// 不加载 Finding 明细，保持分页查询轻量。
/// </summary>
public sealed class AuditLogItemViewModel : ObservableObject
{
    private DesktopLanguage _language;

    /// <summary>初始化日志列表项。</summary>
    /// <param name="summary">审计运行摘要。</param>
    /// <param name="language">初始界面语言。</param>
    /// <exception cref="ArgumentNullException"><paramref name="summary"/> 为 <see langword="null"/>。</exception>
    public AuditLogItemViewModel(AuditRunSummary summary, DesktopLanguage language)
    {
        ArgumentNullException.ThrowIfNull(summary);

        Summary = summary;
        _language = language;
        RepoName = ResolveRepositoryName(summary.RepoPath);
    }

    /// <summary>审计运行摘要数据。</summary>
    public AuditRunSummary Summary { get; }

    /// <summary>运行身份标识。</summary>
    public Guid RunId => Summary.RunId;

    /// <summary>扫描结果（用于视图样式标记）。</summary>
    public ScanResult Result => Summary.Result;

    /// <summary>仓库显示名（取路径末段）。</summary>
    public string RepoName { get; }

    /// <summary>仓库绝对路径。</summary>
    public string RepoPath => Summary.RepoPath;

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

    /// <summary>列表行摘要（类型 / 发现数 / 耗时 / 退出码）。</summary>
    public string MetaText =>
        $"{ScanTypeText} · {FindingCountText} · {DurationText} · {ExitCodeText}";

    /// <summary>是否为阻断结果（视图高亮用）。</summary>
    public bool IsBlocked => Summary.Result == ScanResult.Blocked;

    /// <summary>是否为失败结果（视图高亮用）。</summary>
    public bool IsFailed => Summary.Result == ScanResult.Failed;

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

    private static string ResolveRepositoryName(string repoPath)
    {
        string trimmed = Path.TrimEndingDirectorySeparator(repoPath);
        string name = Path.GetFileName(trimmed);
        return string.IsNullOrWhiteSpace(name) ? trimmed : name;
    }
}
