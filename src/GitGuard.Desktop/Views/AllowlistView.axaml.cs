using Avalonia.Controls;

namespace GitGuard.Desktop.Views;

/// <summary>白名单管理视图（GGD-0044）：渲染全局白名单列表与新增 / 删除表单，逻辑位于 <see cref="ViewModels.AllowlistViewModel"/>。</summary>
public sealed partial class AllowlistView : UserControl
{
    /// <summary>初始化视图并加载 XAML。</summary>
    public AllowlistView() => InitializeComponent();
}
