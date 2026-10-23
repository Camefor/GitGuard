using Avalonia.Controls;

namespace GitGuard.Desktop;

/// <summary>主窗口：侧边导航与内容区组成的桌面端外壳（11-前端UI设计 §1）。</summary>
public sealed partial class MainWindow : Window
{
    /// <summary>初始化主窗口并加载 XAML。</summary>
    public MainWindow() => InitializeComponent();
}
