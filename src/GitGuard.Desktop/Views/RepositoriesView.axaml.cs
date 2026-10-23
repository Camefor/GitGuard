using Avalonia.Controls;

namespace GitGuard.Desktop.Views;

/// <summary>仓库页视图（GGD-0042）：仅负责渲染，全部逻辑位于 <see cref="ViewModels.RepositoriesViewModel"/>。</summary>
public sealed partial class RepositoriesView : UserControl
{
    /// <summary>初始化视图并加载 XAML。</summary>
    public RepositoriesView() => InitializeComponent();
}
