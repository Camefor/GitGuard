using GitGuard.Desktop.Localization;

namespace GitGuard.Desktop.Services;

/// <summary>
/// 桌面端持久化设置（GGD-0046）：语言、主题模式与可选的 Gitleaks 覆盖。
/// 保存在用户数据目录的 <c>desktop-settings.json</c>，与 CLI 的 <c>config.yml</c> 相互独立；
/// <see cref="Gitleaks"/> 为 <see langword="null"/> 时完全采用配置文件取值。
/// </summary>
public sealed record DesktopSettings
{
    /// <summary>界面语言；默认简体中文。</summary>
    public DesktopLanguage Language { get; init; } = DesktopLanguage.Chinese;

    /// <summary>主题模式；默认跟随操作系统。</summary>
    public DesktopThemeMode ThemeMode { get; init; } = DesktopThemeMode.System;

    /// <summary>桌面端扫描的 Gitleaks 覆盖；<see langword="null"/> 表示采用配置文件设置。</summary>
    public DesktopGitleaksSettings? Gitleaks { get; init; }
}

/// <summary>
/// Gitleaks 适配器设置快照：<see cref="Enabled"/> 对应 <c>rules.secrets.use_gitleaks</c>，
/// <see cref="Path"/> 对应 <c>rules.secrets.gitleaks_path</c>（<see langword="null"/> 表示在 PATH 中查找）。
/// </summary>
public sealed record DesktopGitleaksSettings
{
    /// <summary>初始化 Gitleaks 设置快照。</summary>
    /// <param name="enabled">是否启用 Gitleaks 适配器。</param>
    /// <param name="path">可执行文件路径；<see langword="null"/> 表示在 PATH 中查找。</param>
    public DesktopGitleaksSettings(bool enabled, string? path)
    {
        Enabled = enabled;
        Path = path;
    }

    /// <summary>是否启用 Gitleaks 适配器。</summary>
    public bool Enabled { get; }

    /// <summary>可执行文件路径；<see langword="null"/> 表示在 PATH 中查找。</summary>
    public string? Path { get; }
}
