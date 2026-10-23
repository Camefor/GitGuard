namespace GitGuard.Desktop.Localization;

/// <summary>一级导航区块（11-前端UI设计 §1：仓库 / 规则 / 日志 / 设置）。</summary>
public enum DesktopSection
{
    /// <summary>仓库：登记仓库、Hook 状态与扫描结果。</summary>
    Repositories,

    /// <summary>规则：内置、自定义与 Gitleaks 规则库。</summary>
    Rules,

    /// <summary>日志：跨仓库审计浏览与导出。</summary>
    Logs,

    /// <summary>设置：主题、语言与外部工具。</summary>
    Settings,
}
