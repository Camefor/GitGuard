using CommunityToolkit.Mvvm.ComponentModel;
using GitGuard.Core.Policy;
using GitGuard.Desktop.Localization;

namespace GitGuard.Desktop.ViewModels;

/// <summary>
/// 白名单条目展示模型（GGD-0044）：把领域条目拆成规则 / 路径 / 指纹 / 原因与登记信息，
/// 供列表与删除确认复用；不持有任何敏感原文。
/// </summary>
public sealed class AllowlistEntryItemViewModel : ObservableObject
{
    private readonly AllowlistEntry _entry;
    private DesktopLanguage _language;

    /// <summary>初始化白名单条目展示模型。</summary>
    /// <param name="entry">领域白名单条目。</param>
    /// <param name="language">初始界面语言。</param>
    /// <exception cref="ArgumentNullException"><paramref name="entry"/> 为 <see langword="null"/>。</exception>
    public AllowlistEntryItemViewModel(AllowlistEntry entry, DesktopLanguage language)
    {
        ArgumentNullException.ThrowIfNull(entry);

        _entry = entry;
        _language = language;
        RuleIdText = entry.RuleId ?? string.Empty;
        PathPatternText = entry.PathPattern ?? string.Empty;
        FingerprintText = entry.Fingerprint?.Hex ?? string.Empty;
        FingerprintPreview = FingerprintText.Length <= 16
            ? FingerprintText
            : string.Concat(FingerprintText.AsSpan(0, 16), "…");
        Reason = entry.Reason;
    }

    /// <summary>条目身份标识。</summary>
    public Guid Id => _entry.Id;

    /// <summary>规则 ID 匹配条件；未声明时为空字符串。</summary>
    public string RuleIdText { get; }

    /// <summary>路径 glob 匹配条件；未声明时为空字符串。</summary>
    public string PathPatternText { get; }

    /// <summary>完整指纹（64 字符十六进制）；未声明时为空字符串。</summary>
    public string FingerprintText { get; }

    /// <summary>指纹预览（前 16 字符加省略号），用于列表与确认提示。</summary>
    public string FingerprintPreview { get; }

    /// <summary>登记原因。</summary>
    public string Reason { get; }

    /// <summary>是否声明了规则 ID 条件。</summary>
    public bool HasRuleId => _entry.RuleId is not null;

    /// <summary>是否声明了路径 glob 条件。</summary>
    public bool HasPathPattern => _entry.PathPattern is not null;

    /// <summary>是否声明了指纹条件。</summary>
    public bool HasFingerprint => _entry.Fingerprint is not null;

    /// <summary>是否存在过期时间。</summary>
    public bool HasExpiry => _entry.ExpiresAt is not null;

    /// <summary>过期时间（本地时区）；未设置时为空字符串。</summary>
    public string ExpiryText => _entry.ExpiresAt is { } expiresAt
        ? DesktopText.AllowlistExpires(expiresAt, _language)
        : string.Empty;

    /// <summary>登记人与登记时间。</summary>
    public string MetaText => DesktopText.AllowlistEntryMeta(_entry.CreatedBy, _entry.CreatedAt, _language);

    /// <summary>匹配条件摘要（删除确认提示使用）。</summary>
    public string ConditionSummary
    {
        get
        {
            var parts = new List<string>(3);
            if (HasRuleId)
            {
                parts.Add($"{DesktopText.AllowlistRuleLabel(_language)}: {RuleIdText}");
            }

            if (HasPathPattern)
            {
                parts.Add($"{DesktopText.AllowlistPathLabel(_language)}: {PathPatternText}");
            }

            if (HasFingerprint)
            {
                parts.Add($"{DesktopText.AllowlistFingerprintLabel(_language)}: {FingerprintPreview}");
            }

            return string.Join(" · ", parts);
        }
    }

    /// <summary>按新界面语言刷新派生文案。</summary>
    /// <param name="language">目标界面语言。</param>
    public void Relocalize(DesktopLanguage language)
    {
        _language = language;
        OnPropertyChanged(nameof(ExpiryText));
        OnPropertyChanged(nameof(MetaText));
        OnPropertyChanged(nameof(ConditionSummary));
    }
}
