using Avalonia;

namespace GitGuard.Desktop;

/// <summary>桌面端进程入口（11-前端UI设计 §1）：构建 Avalonia 应用并进入桌面生命周期。</summary>
internal static class Program
{
    /// <summary>进程入口。</summary>
    /// <param name="args">命令行参数。</param>
    [STAThread]
    public static void Main(string[] args) =>
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

    /// <summary>构建 Avalonia 应用（测试 / 设计器可复用）。</summary>
    /// <returns>应用构建器。</returns>
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
}
