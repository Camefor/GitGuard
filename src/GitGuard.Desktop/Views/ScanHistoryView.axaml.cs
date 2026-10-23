using Avalonia.Controls;

namespace GitGuard.Desktop.Views;

/// <summary>扫描历史视图（GGD-0043）：渲染时间线、脱敏发现项与详情，逻辑位于 <see cref="ViewModels.ScanHistoryViewModel"/>。</summary>
public sealed partial class ScanHistoryView : UserControl
{
    /// <summary>初始化视图并加载 XAML。</summary>
    public ScanHistoryView() => InitializeComponent();
}
