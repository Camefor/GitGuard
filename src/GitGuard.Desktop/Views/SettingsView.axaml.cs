using Avalonia.Controls;

namespace GitGuard.Desktop.Views;

/// <summary>全局设置视图（GGD-0046）：渲染语言、主题与 Gitleaks 桌面覆盖，逻辑位于 <see cref="ViewModels.SettingsViewModel"/>。</summary>
public sealed partial class SettingsView : UserControl
{
    /// <summary>初始化视图并加载 XAML。</summary>
    public SettingsView() => InitializeComponent();
}
