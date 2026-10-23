using System.Globalization;
using GitGuard.Application.Hooks;
using GitGuard.Core.Model;

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

    /// <summary>仓库页左侧列表标题。</summary>
    /// <param name="language">界面语言。</param>
    /// <returns>本地化标题。</returns>
    internal static string RepositoriesListTitle(DesktopLanguage language) =>
        language == DesktopLanguage.English ? "Registered repositories" : "已登记仓库";

    /// <summary>仓库路径输入框标题。</summary>
    /// <param name="language">界面语言。</param>
    /// <returns>本地化标题。</returns>
    internal static string RepositoriesPathLabel(DesktopLanguage language) =>
        language == DesktopLanguage.English ? "Repository path" : "仓库路径";

    /// <summary>仓库路径输入框水印。</summary>
    /// <param name="language">界面语言。</param>
    /// <returns>本地化水印。</returns>
    internal static string RepositoriesPathWatermark(DesktopLanguage language) =>
        language == DesktopLanguage.English
            ? @"e.g. D:\code\my-repo or ~/code/my-repo"
            : @"例如 D:\code\my-repo 或 ~/code/my-repo";

    /// <summary>登记仓库按钮。</summary>
    /// <param name="language">界面语言。</param>
    /// <returns>本地化按钮文案。</returns>
    internal static string RepositoriesAddButton(DesktopLanguage language) =>
        language == DesktopLanguage.English ? "Register" : "登记仓库";

    /// <summary>刷新仓库列表按钮。</summary>
    /// <param name="language">界面语言。</param>
    /// <returns>本地化按钮文案。</returns>
    internal static string RepositoriesRefreshButton(DesktopLanguage language) =>
        language == DesktopLanguage.English ? "Refresh" : "刷新";

    /// <summary>仓库列表空状态。</summary>
    /// <param name="language">界面语言。</param>
    /// <returns>本地化提示。</returns>
    internal static string RepositoriesEmptyState(DesktopLanguage language) =>
        language == DesktopLanguage.English
            ? "No repositories registered yet. Enter a local Git repository path above to begin."
            : "尚未登记仓库。在上方输入本地 Git 仓库路径即可开始。";

    /// <summary>未选择仓库时的详情占位提示。</summary>
    /// <param name="language">界面语言。</param>
    /// <returns>本地化提示。</returns>
    internal static string RepositoriesSelectPrompt(DesktopLanguage language) =>
        language == DesktopLanguage.English
            ? "Select a repository on the left to review hook status and the latest scan."
            : "从左侧选择仓库，查看 Hook 状态与最近扫描。";

    /// <summary>仓库目录缺失徽标。</summary>
    /// <param name="language">界面语言。</param>
    /// <returns>本地化徽标。</returns>
    internal static string RepositoryMissingBadge(DesktopLanguage language) =>
        language == DesktopLanguage.English ? "Missing" : "目录不存在";

    /// <summary>Hook 状态区块标题。</summary>
    /// <param name="language">界面语言。</param>
    /// <returns>本地化标题。</returns>
    internal static string RepositoryHooksTitle(DesktopLanguage language) =>
        language == DesktopLanguage.English ? "Hook status" : "Hook 状态";

    /// <summary>GitGuard 版本标签。</summary>
    /// <param name="language">界面语言。</param>
    /// <returns>本地化标签。</returns>
    internal static string RepositoryGitGuardVersionLabel(DesktopLanguage language) =>
        language == DesktopLanguage.English ? "GitGuard version" : "GitGuard 版本";

    /// <summary>Git 版本标签。</summary>
    /// <param name="language">界面语言。</param>
    /// <returns>本地化标签。</returns>
    internal static string RepositoryGitVersionLabel(DesktopLanguage language) =>
        language == DesktopLanguage.English ? "Git version" : "Git 版本";

    /// <summary>Hooks 目录标签。</summary>
    /// <param name="language">界面语言。</param>
    /// <returns>本地化标签。</returns>
    internal static string RepositoryHooksDirectoryLabel(DesktopLanguage language) =>
        language == DesktopLanguage.English ? "Hooks directory" : "Hooks 目录";

    /// <summary>最近扫描区块标题。</summary>
    /// <param name="language">界面语言。</param>
    /// <returns>本地化标题。</returns>
    internal static string RepositoryLatestScanLabel(DesktopLanguage language) =>
        language == DesktopLanguage.English ? "Latest scan" : "最近扫描";

    /// <summary>尚无扫描记录。</summary>
    /// <param name="language">界面语言。</param>
    /// <returns>本地化提示。</returns>
    internal static string RepositoryNoScan(DesktopLanguage language) =>
        language == DesktopLanguage.English ? "No scans recorded yet." : "尚无扫描记录。";

    /// <summary>立即扫描按钮。</summary>
    /// <param name="language">界面语言。</param>
    /// <returns>本地化按钮文案。</returns>
    internal static string RepositoryScanButton(DesktopLanguage language) =>
        language == DesktopLanguage.English ? "Scan now" : "立即扫描";

    /// <summary>扫描中按钮文案。</summary>
    /// <param name="language">界面语言。</param>
    /// <returns>本地化按钮文案。</returns>
    internal static string RepositoryScanningButton(DesktopLanguage language) =>
        language == DesktopLanguage.English ? "Scanning…" : "扫描中…";

    /// <summary>详情加载提示。</summary>
    /// <param name="language">界面语言。</param>
    /// <returns>本地化提示。</returns>
    internal static string RepositoryLoading(DesktopLanguage language) =>
        language == DesktopLanguage.English ? "Loading repository status…" : "正在读取仓库状态…";

    /// <summary>移除登记按钮。</summary>
    /// <param name="language">界面语言。</param>
    /// <returns>本地化按钮文案。</returns>
    internal static string RepositoryRemoveButton(DesktopLanguage language) =>
        language == DesktopLanguage.English ? "Remove registration" : "移除登记";

    /// <summary>移除确认提示。</summary>
    /// <param name="language">界面语言。</param>
    /// <returns>本地化提示。</returns>
    internal static string RepositoryRemoveConfirmPrompt(DesktopLanguage language) =>
        language == DesktopLanguage.English
            ? "This only removes the registration. Repository files, audit history, and hooks stay untouched."
            : "仅移除登记，不会删除仓库文件、审计历史或 Hook。";

    /// <summary>确认移除按钮。</summary>
    /// <param name="language">界面语言。</param>
    /// <returns>本地化按钮文案。</returns>
    internal static string RepositoryRemoveConfirmButton(DesktopLanguage language) =>
        language == DesktopLanguage.English ? "Confirm removal" : "确认移除";

    /// <summary>取消按钮。</summary>
    /// <param name="language">界面语言。</param>
    /// <returns>本地化按钮文案。</returns>
    internal static string RepositoryCancelButton(DesktopLanguage language) =>
        language == DesktopLanguage.English ? "Cancel" : "取消";

    /// <summary>未托管 Hook 的接管提示。</summary>
    /// <param name="language">界面语言。</param>
    /// <returns>本地化提示。</returns>
    internal static string HookAttentionHint(DesktopLanguage language) =>
        language == DesktopLanguage.English
            ? "Unmanaged hook detected. Run `gitguard install` to take it over."
            : "检测到未托管 Hook，请执行 `gitguard install` 接管。";

    /// <summary>Hook 探测结果标签。</summary>
    /// <param name="kind">探测结果分类。</param>
    /// <param name="language">界面语言。</param>
    /// <returns>本地化标签。</returns>
    internal static string HookKindLabel(HookStatusKind kind, DesktopLanguage language) => kind switch
    {
        HookStatusKind.Missing => language == DesktopLanguage.English ? "Not installed" : "未安装",
        HookStatusKind.Managed => language == DesktopLanguage.English ? "Managed" : "已托管",
        HookStatusKind.Legacy => language == DesktopLanguage.English ? "Unmanaged script" : "未托管脚本",
        HookStatusKind.Binary => language == DesktopLanguage.English ? "Binary file" : "二进制文件",
        HookStatusKind.SymbolicLink => language == DesktopLanguage.English ? "Symbolic link" : "符号链接",
        _ => kind.ToString(),
    };

    /// <summary>扫描结果标签。</summary>
    /// <param name="result">扫描结果。</param>
    /// <param name="language">界面语言。</param>
    /// <returns>本地化标签。</returns>
    internal static string ScanResultLabel(ScanResult result, DesktopLanguage language) => result switch
    {
        ScanResult.Clean => language == DesktopLanguage.English ? "Clean" : "干净",
        ScanResult.Warned => language == DesktopLanguage.English ? "Warned" : "警告",
        ScanResult.Blocked => language == DesktopLanguage.English ? "Blocked" : "已阻断",
        ScanResult.Failed => language == DesktopLanguage.English ? "Failed" : "失败",
        _ => result.ToString(),
    };

    /// <summary>扫描类型标签。</summary>
    /// <param name="scanType">扫描类型。</param>
    /// <param name="language">界面语言。</param>
    /// <returns>本地化标签。</returns>
    internal static string ScanTypeLabel(ScanType scanType, DesktopLanguage language) => scanType switch
    {
        ScanType.PreCommit => language == DesktopLanguage.English ? "Pre-commit" : "提交前",
        ScanType.PrePush => language == DesktopLanguage.English ? "Pre-push" : "推送前",
        ScanType.Manual => language == DesktopLanguage.English ? "Manual" : "手动",
        ScanType.Bypass => language == DesktopLanguage.English ? "Bypass" : "绕过",
        _ => scanType.ToString(),
    };

    /// <summary>最近扫描摘要（时间按用户区域设置显示，审计内部仍为 UTC epoch）。</summary>
    /// <param name="summary">扫描运行摘要。</param>
    /// <param name="language">界面语言。</param>
    /// <returns>本地化摘要文本。</returns>
    internal static string LatestScanSummary(AuditRunSummary summary, DesktopLanguage language)
    {
        ArgumentNullException.ThrowIfNull(summary);

        string timestamp = summary.StartedAt.ToLocalTime().ToString("g", CultureInfo.CurrentCulture);
        string findings = language == DesktopLanguage.English
            ? $"{summary.FindingCount} findings"
            : $"{summary.FindingCount} 个发现";
        string duration = summary.DurationMs is { } durationMs ? $" · {durationMs} ms" : string.Empty;
        string result = ScanResultLabel(summary.Result, language);
        return $"{result} · {timestamp} · {ScanTypeLabel(summary.ScanType, language)} · {findings}{duration}";
    }

    /// <summary>扫描完成提示。</summary>
    /// <param name="result">扫描结果。</param>
    /// <param name="findingCount">发现项数量。</param>
    /// <param name="language">界面语言。</param>
    /// <returns>本地化提示。</returns>
    internal static string ScanCompletedMessage(ScanResult result, int findingCount, DesktopLanguage language) =>
        language == DesktopLanguage.English
            ? $"Scan completed: {ScanResultLabel(result, language)}, {findingCount} findings."
            : $"扫描完成：{ScanResultLabel(result, language)}，{findingCount} 个发现。";

    /// <summary>审计写入失败降级提示。</summary>
    /// <param name="language">界面语言。</param>
    /// <returns>本地化提示。</returns>
    internal static string AuditWriteFailedWarning(DesktopLanguage language) =>
        language == DesktopLanguage.English
            ? "The scan finished, but the audit record could not be written."
            : "扫描已完成，但审计记录写入失败。";

    /// <summary>仓库操作错误提示（按稳定错误码映射；未知错误回退原始消息）。</summary>
    /// <param name="errorCode">错误码。</param>
    /// <param name="fallbackMessage">原始消息。</param>
    /// <param name="language">界面语言。</param>
    /// <returns>本地化提示。</returns>
    internal static string RepositoryError(string errorCode, string fallbackMessage, DesktopLanguage language) =>
        (errorCode, language) switch
        {
            (GitGuard.Core.RepositoryException.REPOSITORY_VALIDATION_FAILED, DesktopLanguage.English) =>
                "The repository path is invalid or the directory does not exist.",
            (GitGuard.Core.RepositoryException.REPOSITORY_NOT_A_REPOSITORY, DesktopLanguage.English) =>
                "The selected directory is not a Git working tree.",
            (GitGuard.Core.RepositoryException.REPOSITORY_ALREADY_REGISTERED, DesktopLanguage.English) =>
                "This repository is already registered.",
            (GitGuard.Core.RepositoryException.REPOSITORY_NOT_FOUND, DesktopLanguage.English) =>
                "The repository registration no longer exists.",
            (GitGuard.Core.RepositoryException.REPOSITORY_VALIDATION_FAILED, _) =>
                "仓库路径无效或目录不存在。",
            (GitGuard.Core.RepositoryException.REPOSITORY_NOT_A_REPOSITORY, _) =>
                "所选目录不是 Git 工作区。",
            (GitGuard.Core.RepositoryException.REPOSITORY_ALREADY_REGISTERED, _) =>
                "该仓库已经登记。",
            (GitGuard.Core.RepositoryException.REPOSITORY_NOT_FOUND, _) =>
                "仓库登记已不存在。",
            _ => fallbackMessage,
        };
}
