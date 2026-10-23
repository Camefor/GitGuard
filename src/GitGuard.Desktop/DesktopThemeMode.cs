using Avalonia.Styling;

namespace GitGuard.Desktop;

/// <summary>
/// 桌面端主题模式（GGD-0046 / 11-前端UI设计 §5）：默认跟随操作系统，
/// 也可在设置页强制浅色或深色并持久化到用户数据目录。
/// </summary>
public enum DesktopThemeMode
{
    /// <summary>跟随操作系统主题（Avalonia <see cref="ThemeVariant.Default"/>）。</summary>
    System,

    /// <summary>强制浅色主题。</summary>
    Light,

    /// <summary>强制深色主题。</summary>
    Dark,
}

/// <summary>主题模式到 Avalonia 主题变体的映射。</summary>
public static class DesktopThemeModeExtensions
{
    /// <summary>转换为 Avalonia 主题变体。</summary>
    /// <param name="mode">主题模式。</param>
    /// <returns>对应的 <see cref="ThemeVariant"/>。</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="mode"/> 未定义。</exception>
    public static ThemeVariant ToThemeVariant(this DesktopThemeMode mode) => mode switch
    {
        DesktopThemeMode.System => ThemeVariant.Default,
        DesktopThemeMode.Light => ThemeVariant.Light,
        DesktopThemeMode.Dark => ThemeVariant.Dark,
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "未知的主题模式"),
    };
}
