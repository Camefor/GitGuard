namespace GitGuard.Desktop.Localization;

/// <summary>
/// 桌面端文案表（中 / 英）：与 CLI 的枚举键式文案一致，集中维护且无 .resx 反射依赖，
/// 语言切换仅触发属性通知，不重新加载资源字典（12-前端组件设计 §4）。
/// </summary>
internal static class DesktopText
{
    /// <summary>窗口标题。</summary>
    /// <param name="language">界面语言。</param>
    /// <returns>本地化标题。</returns>
    internal static string WindowTitle(DesktopLanguage language) =>
        language == DesktopLanguage.English ? "GitGuard Desktop" : "GitGuard 桌面端";

    /// <summary>侧边栏产品副标题。</summary>
    /// <param name="language">界面语言。</param>
    /// <returns>本地化副标题。</returns>
    internal static string Tagline(DesktopLanguage language) =>
        language == DesktopLanguage.English ? "Local secret leak prevention" : "本地敏感信息防泄漏";

    /// <summary>导航项标题。</summary>
    /// <param name="section">导航区块。</param>
    /// <param name="language">界面语言。</param>
    /// <returns>本地化标题。</returns>
    internal static string NavLabel(DesktopSection section, DesktopLanguage language) => section switch
    {
        DesktopSection.Repositories => language == DesktopLanguage.English ? "Repositories" : "仓库",
        DesktopSection.Rules => language == DesktopLanguage.English ? "Rules" : "规则",
        DesktopSection.Logs => language == DesktopLanguage.English ? "Logs" : "日志",
        DesktopSection.Settings => language == DesktopLanguage.English ? "Settings" : "设置",
        _ => section.ToString(),
    };

    /// <summary>导航项说明。</summary>
    /// <param name="section">导航区块。</param>
    /// <param name="language">界面语言。</param>
    /// <returns>本地化说明。</returns>
    internal static string NavDescription(DesktopSection section, DesktopLanguage language) => section switch
    {
        DesktopSection.Repositories => language == DesktopLanguage.English
            ? "Hook status and scans per repository"
            : "仓库 Hook 状态与扫描",
        DesktopSection.Rules => language == DesktopLanguage.English
            ? "Built-in and custom rule catalog"
            : "内置与自定义规则库",
        DesktopSection.Logs => language == DesktopLanguage.English
            ? "Cross-repository audit and export"
            : "跨仓库审计与导出",
        DesktopSection.Settings => language == DesktopLanguage.English
            ? "Theme, language, and external tools"
            : "主题、语言与外部工具",
        _ => string.Empty,
    };

    /// <summary>页面标题。</summary>
    /// <param name="section">导航区块。</param>
    /// <param name="language">界面语言。</param>
    /// <returns>本地化标题。</returns>
    internal static string SectionTitle(DesktopSection section, DesktopLanguage language) =>
        NavLabel(section, language);

    /// <summary>页面副标题。</summary>
    /// <param name="section">导航区块。</param>
    /// <param name="language">界面语言。</param>
    /// <returns>本地化副标题。</returns>
    internal static string SectionSubtitle(DesktopSection section, DesktopLanguage language) => section switch
    {
        DesktopSection.Repositories => language == DesktopLanguage.English
            ? "Register local repositories and review hook status and scan results."
            : "登记本机仓库，查看 Hook 状态与扫描结果。",
        DesktopSection.Rules => language == DesktopLanguage.English
            ? "Browse built-in, custom, and Gitleaks rules with their enablement."
            : "浏览内置、自定义与 Gitleaks 规则及其启用状态。",
        DesktopSection.Logs => language == DesktopLanguage.English
            ? "Browse audit runs across repositories and export them."
            : "浏览跨仓库审计记录并导出。",
        DesktopSection.Settings => language == DesktopLanguage.English
            ? "Configure theme, language, and external tool paths."
            : "配置主题、语言与外部工具路径。",
        _ => string.Empty,
    };

    /// <summary>页面空状态说明。</summary>
    /// <param name="section">导航区块。</param>
    /// <param name="language">界面语言。</param>
    /// <returns>本地化说明。</returns>
    internal static string SectionBody(DesktopSection section, DesktopLanguage language) => section switch
    {
        DesktopSection.Repositories => language == DesktopLanguage.English
            ? "Registered repositories, hook installation state, and recent scans will appear here."
            : "已登记仓库、Hook 安装状态与最近扫描会在这里集中展示。",
        DesktopSection.Rules => language == DesktopLanguage.English
            ? "The rule catalog, enablement, and match samples will appear here."
            : "规则清单、启用状态与匹配样例会在这里展示。",
        DesktopSection.Logs => language == DesktopLanguage.English
            ? "Audit runs recorded by the CLI will appear here for browsing and export."
            : "CLI 已记录的审计运行会在这里供浏览与导出。",
        DesktopSection.Settings => language == DesktopLanguage.English
            ? "Theme, language, and Gitleaks path settings will appear here."
            : "主题、语言与 Gitleaks 路径设置会在这里展示。",
        _ => string.Empty,
    };

    /// <summary>语言切换按钮文案（显示目标语言）。</summary>
    /// <param name="language">当前界面语言。</param>
    /// <returns>目标语言名称。</returns>
    internal static string LanguageToggle(DesktopLanguage language) =>
        language == DesktopLanguage.English ? "中文" : "English";

    /// <summary>侧边栏版本行。</summary>
    /// <param name="version">应用版本。</param>
    /// <returns>版本文本。</returns>
    internal static string VersionText(string version) => $"GitGuard Desktop {version}";
}
