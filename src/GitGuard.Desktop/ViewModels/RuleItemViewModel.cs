using CommunityToolkit.Mvvm.ComponentModel;
using GitGuard.Application.Rules;
using GitGuard.Core.Model;
using GitGuard.Core.Rules;
using GitGuard.Desktop.Localization;

namespace GitGuard.Desktop.ViewModels;

/// <summary>
/// 规则列表项 / 详情展示模型（GGD-0045）：只保留目录条目的可展示字段，
/// 不持有正则原文；正则由 <see cref="RulesViewModel"/> 单独保管并仅在用户显式切换后展示。
/// </summary>
public sealed class RuleItemViewModel : ObservableObject
{
    private DesktopLanguage _language;

    /// <summary>初始化规则展示模型。</summary>
    /// <param name="entry">规则目录条目。</param>
    /// <param name="language">初始界面语言。</param>
    /// <exception cref="ArgumentNullException"><paramref name="entry"/> 为 <see langword="null"/>。</exception>
    public RuleItemViewModel(RuleCatalogEntry entry, DesktopLanguage language)
    {
        ArgumentNullException.ThrowIfNull(entry);

        RuleId = entry.RuleId;
        Name = entry.Name;
        Category = entry.Category;
        Severity = entry.Severity;
        Source = entry.Source;
        Enabled = entry.Enabled;
        _language = language;
    }

    /// <summary>规则 ID。</summary>
    public string RuleId { get; }

    /// <summary>规则名称。</summary>
    public string Name { get; }

    /// <summary>规则类别段（<c>SECRET</c> / <c>PRIVACY</c> / <c>LOCALPATH</c> / <c>URL</c>）。</summary>
    public string Category { get; }

    /// <summary>命中严重度。</summary>
    public Severity Severity { get; }

    /// <summary>规则来源。</summary>
    public RuleSource Source { get; }

    /// <summary>是否有效启用（规则开关 ∧ 未禁用 ∧ 类别启用）。</summary>
    public bool Enabled { get; }

    /// <summary>本地化类别标签。</summary>
    public string CategoryText => DesktopText.RuleCategoryLabel(Category, _language);

    /// <summary>本地化来源标签。</summary>
    public string SourceText => DesktopText.RuleSourceLabel(Source, _language);

    /// <summary>本地化严重度标签。</summary>
    public string SeverityText => DesktopText.SeverityLabel(Severity, _language);

    /// <summary>本地化启用状态标签。</summary>
    public string EnabledText => DesktopText.RuleEnabledLabel(Enabled, _language);

    /// <summary>列表项元信息（类别 · 来源 · 严重度）。</summary>
    public string MetaText => $"{CategoryText} · {SourceText} · {SeverityText}";

    /// <summary>按新界面语言刷新派生文案。</summary>
    /// <param name="language">目标界面语言。</param>
    public void Relocalize(DesktopLanguage language)
    {
        _language = language;
        OnPropertyChanged(nameof(CategoryText));
        OnPropertyChanged(nameof(SourceText));
        OnPropertyChanged(nameof(SeverityText));
        OnPropertyChanged(nameof(EnabledText));
        OnPropertyChanged(nameof(MetaText));
    }
}
