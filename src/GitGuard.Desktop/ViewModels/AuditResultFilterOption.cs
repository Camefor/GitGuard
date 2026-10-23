using CommunityToolkit.Mvvm.ComponentModel;
using GitGuard.Core.Model;
using GitGuard.Desktop.Localization;

namespace GitGuard.Desktop.ViewModels;

/// <summary>审计日志的结果筛选选项（GGD-0046）：<see cref="Result"/> 为 <see langword="null"/> 表示全部结果。</summary>
public sealed class AuditResultFilterOption : ObservableObject
{
    private string _label;

    /// <summary>初始化结果筛选选项。</summary>
    /// <param name="result">扫描结果；<see langword="null"/> 表示不过滤。</param>
    /// <param name="language">初始界面语言。</param>
    public AuditResultFilterOption(ScanResult? result, DesktopLanguage language)
    {
        Result = result;
        _label = DesktopText.AuditLogResultFilterLabel(result, language);
    }

    /// <summary>筛选结果；<see langword="null"/> 表示全部结果。</summary>
    public ScanResult? Result { get; }

    /// <summary>本地化显示标签。</summary>
    public string Label
    {
        get => _label;
        private set => SetProperty(ref _label, value);
    }

    /// <summary>按新界面语言刷新标签。</summary>
    /// <param name="language">目标界面语言。</param>
    public void Relocalize(DesktopLanguage language) =>
        Label = DesktopText.AuditLogResultFilterLabel(Result, language);
}
