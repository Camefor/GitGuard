using Avalonia.Controls;

namespace GitGuard.Desktop.Views;

/// <summary>规则库视图（GGD-0045）：渲染规则目录、筛选工具栏与默认隐藏的正则详情，逻辑位于 <see cref="ViewModels.RulesViewModel"/>。</summary>
public sealed partial class RulesView : UserControl
{
    /// <summary>初始化视图并加载 XAML。</summary>
    public RulesView() => InitializeComponent();
}
