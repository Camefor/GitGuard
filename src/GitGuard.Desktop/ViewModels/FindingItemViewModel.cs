using CommunityToolkit.Mvvm.ComponentModel;
using GitGuard.Core.Model;
using GitGuard.Desktop.Localization;

namespace GitGuard.Desktop.ViewModels;

/// <summary>
/// 发现项列表 / 详情展示模型（GGD-0043，GGD-0063 起含策略动作）：只读取
/// <see cref="Finding.SnippetMasked"/>、<see cref="Finding.Fingerprint"/> 与 <see cref="Finding.Action"/>，
/// 不持有、不读取任何明文敏感内容（DR-02）。
/// </summary>
public sealed class FindingItemViewModel : ObservableObject
{
    private DesktopLanguage _language;

    /// <summary>初始化发现项展示模型。</summary>
    /// <param name="finding">已脱敏的发现项。</param>
    /// <param name="language">初始界面语言。</param>
    /// <exception cref="ArgumentNullException"><paramref name="finding"/> 为 <see langword="null"/>。</exception>
    public FindingItemViewModel(Finding finding, DesktopLanguage language)
    {
        ArgumentNullException.ThrowIfNull(finding);

        Finding = finding;
        _language = language;
    }

    /// <summary>发现项领域模型（仅含脱敏字段）。</summary>
    public Finding Finding { get; }

    /// <summary>规则 ID。</summary>
    public string RuleId => Finding.RuleId;

    /// <summary>仓库相对文件路径。</summary>
    public string FilePath => Finding.FilePath;

    /// <summary>1-based 行号。</summary>
    public int LineNumber => Finding.LineNumber;

    /// <summary>本地化严重度标签。</summary>
    public string SeverityText => DesktopText.SeverityLabel(Finding.Severity, _language);

    /// <summary>文件位置（<c>path:line</c>）。</summary>
    public string LocationText => $"{Finding.FilePath}:{Finding.LineNumber}";

    /// <summary>唯一允许展示的脱敏片段。</summary>
    public string MaskedSnippetText => Finding.SnippetMasked.Value;

    /// <summary>HMAC 指纹（64 位小写十六进制）。</summary>
    public string FingerprintText => Finding.Fingerprint.Hex;

    /// <summary>完整提交 SHA；非提交扫描为 <see langword="null"/>。</summary>
    public string? CommitSha => Finding.CommitSha;

    /// <summary>提交字段展示文本（无提交时为短横线）。</summary>
    public string CommitText => Finding.CommitSha ?? "—";

    /// <summary>提交 SHA 的短形式，用于紧凑列表。</summary>
    public string CommitShortText =>
        Finding.CommitSha is { Length: > 12 } sha ? sha.Substring(0, 12) : CommitText;

    /// <summary>是否存在提交上下文。</summary>
    public bool HasCommit => Finding.CommitSha is not null;

    /// <summary>策略动作（<c>findings.action</c>）；扫描期不置位，持久化读回时填充。</summary>
    public string? Action => Finding.Action;

    /// <summary>策略动作的本地化文案（放行 / 警告 / 阻断 / 白名单忽略 / 基线忽略）。</summary>
    public string ActionText => DesktopText.FindingActionText(Finding.Action, _language);

    /// <summary>是否存在策略动作。</summary>
    public bool HasAction => !string.IsNullOrWhiteSpace(Finding.Action);

    /// <summary>按新界面语言刷新派生文案。</summary>
    /// <param name="language">目标界面语言。</param>
    public void Relocalize(DesktopLanguage language)
    {
        _language = language;
        OnPropertyChanged(nameof(SeverityText));
        OnPropertyChanged(nameof(ActionText));
    }
}
