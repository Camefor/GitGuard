using CommunityToolkit.Mvvm.ComponentModel;

namespace GitGuard.Desktop.ViewModels;

/// <summary>
/// 规则页筛选下拉项（GGD-0045）：<see cref="Key"/> 为稳定筛选标识（ALL / BUILTIN / USER / GITLEAKS 等），
/// <see cref="Label"/> 由 <see cref="RulesViewModel"/> 在语言切换时刷新。
/// </summary>
public sealed class RuleFilterOption : ObservableObject
{
    private string _label;

    /// <summary>初始化筛选下拉项。</summary>
    /// <param name="key">稳定筛选标识。</param>
    /// <param name="label">初始展示文案。</param>
    /// <exception cref="ArgumentException"><paramref name="key"/> 为空或全空白。</exception>
    /// <exception cref="ArgumentNullException"><paramref name="label"/> 为 <see langword="null"/>。</exception>
    public RuleFilterOption(string key, string label)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(label);

        Key = key;
        _label = label;
    }

    /// <summary>稳定筛选标识（不随语言变化）。</summary>
    public string Key { get; }

    /// <summary>本地化展示文案。</summary>
    public string Label
    {
        get => _label;
        internal set => SetProperty(ref _label, value);
    }
}
