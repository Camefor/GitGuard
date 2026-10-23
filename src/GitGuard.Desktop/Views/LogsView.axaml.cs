using Avalonia.Controls;

namespace GitGuard.Desktop.Views;

/// <summary>跨仓库日志视图（GGD-0046）：渲染筛选、分页列表与 JSON / CSV 导出操作，逻辑位于 <see cref="ViewModels.LogsViewModel"/>。</summary>
public sealed partial class LogsView : UserControl
{
    /// <summary>初始化视图并加载 XAML。</summary>
    public LogsView() => InitializeComponent();
}
