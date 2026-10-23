using Avalonia;
using GitGuard.Desktop.Smoke;

namespace GitGuard.Desktop;

/// <summary>桌面端进程入口（11-前端UI设计 §1）：构建 Avalonia 应用并进入桌面生命周期。</summary>
internal static class Program
{
    /// <summary>进程入口。</summary>
    /// <param name="args">命令行参数。</param>
    /// <returns>冒烟模式返回检查结果；桌面模式随生命周期结束返回 0。</returns>
    [STAThread]
    public static int Main(string[] args)
    {
        // GGD-0047：发行流水线以 --smoke-test 在无显示环境下验证安装包可读取 CLI 审计数据，
        // 必须早于 Avalonia 初始化，避免 Linux / macOS 无图形会话时启动失败
        if (DesktopSmokeTest.IsRequested(args))
        {
            return DesktopSmokeTest.Run(args);
        }

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        return 0;
    }

    /// <summary>构建 Avalonia 应用（测试 / 设计器可复用）。</summary>
    /// <returns>应用构建器。</returns>
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
}
