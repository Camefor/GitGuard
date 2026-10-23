using System.Globalization;
using GitGuard.Application.Audit;
using GitGuard.Application.Hooks;
using GitGuard.Core;
using GitGuard.Core.Model;
using GitGuard.Core.Rules;
using GitGuard.Desktop.Services;

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

    /// <summary>严重度标签。</summary>
    /// <param name="severity">严重度。</param>
    /// <param name="language">界面语言。</param>
    /// <returns>本地化标签。</returns>
    internal static string SeverityLabel(Severity severity, DesktopLanguage language) => severity switch
    {
        Severity.Critical => language == DesktopLanguage.English ? "Critical" : "严重",
        Severity.High => language == DesktopLanguage.English ? "High" : "高",
        Severity.Medium => language == DesktopLanguage.English ? "Medium" : "中",
        Severity.Low => language == DesktopLanguage.English ? "Low" : "低",
        _ => severity.ToString(),
    };

    /// <summary>仓库详情页的扫描历史入口。</summary>
    /// <param name="language">界面语言。</param>
    /// <returns>本地化按钮文案。</returns>
    internal static string ScanHistoryButton(DesktopLanguage language) =>
        language == DesktopLanguage.English ? "Scan history" : "扫描历史";

    /// <summary>扫描历史页标题。</summary>
    /// <param name="language">界面语言。</param>
    /// <returns>本地化标题。</returns>
    internal static string ScanHistoryTitle(DesktopLanguage language) =>
        language == DesktopLanguage.English ? "Scan history" : "扫描历史";

    /// <summary>返回仓库详情按钮。</summary>
    /// <param name="language">界面语言。</param>
    /// <returns>本地化按钮文案。</returns>
    internal static string ScanHistoryBackButton(DesktopLanguage language) =>
        language == DesktopLanguage.English ? "Back to repository" : "返回仓库详情";

    /// <summary>刷新扫描历史按钮。</summary>
    /// <param name="language">界面语言。</param>
    /// <returns>本地化按钮文案。</returns>
    internal static string ScanHistoryRefreshButton(DesktopLanguage language) =>
        language == DesktopLanguage.English ? "Refresh" : "刷新";

    /// <summary>扫描时间线标题。</summary>
    /// <param name="language">界面语言。</param>
    /// <returns>本地化标题。</returns>
    internal static string ScanHistoryTimelineTitle(DesktopLanguage language) =>
        language == DesktopLanguage.English ? "Timeline" : "扫描时间线";

    /// <summary>发现项列表标题。</summary>
    /// <param name="language">界面语言。</param>
    /// <returns>本地化标题。</returns>
    internal static string ScanHistoryFindingsTitle(DesktopLanguage language) =>
        language == DesktopLanguage.English ? "Findings" : "发现项";

    /// <summary>发现项详情标题。</summary>
    /// <param name="language">界面语言。</param>
    /// <returns>本地化标题。</returns>
    internal static string ScanHistoryFindingDetailTitle(DesktopLanguage language) =>
        language == DesktopLanguage.English ? "Finding details" : "发现项详情";

    /// <summary>无扫描历史空状态。</summary>
    /// <param name="language">界面语言。</param>
    /// <returns>本地化提示。</returns>
    internal static string ScanHistoryEmpty(DesktopLanguage language) =>
        language == DesktopLanguage.English
            ? "No scans recorded for this repository yet. Hook or manual scans will appear here."
            : "该仓库尚无扫描记录。完成一次 Hook 或手动扫描后，这里会显示时间线。";

    /// <summary>未选择扫描运行时的提示。</summary>
    /// <param name="language">界面语言。</param>
    /// <returns>本地化提示。</returns>
    internal static string ScanHistorySelectRunPrompt(DesktopLanguage language) =>
        language == DesktopLanguage.English
            ? "Select a scan run in the timeline to inspect findings."
            : "从左侧时间线选择一次扫描，查看发现项。";

    /// <summary>扫描运行没有发现项时的提示。</summary>
    /// <param name="language">界面语言。</param>
    /// <returns>本地化提示。</returns>
    internal static string ScanHistoryNoFindings(DesktopLanguage language) =>
        language == DesktopLanguage.English
            ? "This scan recorded no findings."
            : "本次扫描未记录发现项。";

    /// <summary>未选择发现项时的提示。</summary>
    /// <param name="language">界面语言。</param>
    /// <returns>本地化提示。</returns>
    internal static string ScanHistorySelectFindingPrompt(DesktopLanguage language) =>
        language == DesktopLanguage.English
            ? "Select a finding to inspect its masked details."
            : "选择一个发现项查看脱敏详情。";

    /// <summary>扫描历史加载提示。</summary>
    /// <param name="language">界面语言。</param>
    /// <returns>本地化提示。</returns>
    internal static string ScanHistoryLoading(DesktopLanguage language) =>
        language == DesktopLanguage.English ? "Loading scan history…" : "正在加载扫描历史…";

    /// <summary>发现项加载提示。</summary>
    /// <param name="language">界面语言。</param>
    /// <returns>本地化提示。</returns>
    internal static string ScanHistoryRunLoading(DesktopLanguage language) =>
        language == DesktopLanguage.English ? "Loading findings…" : "正在加载发现项…";

    /// <summary>上一页按钮。</summary>
    /// <param name="language">界面语言。</param>
    /// <returns>本地化按钮文案。</returns>
    internal static string ScanHistoryPreviousPage(DesktopLanguage language) =>
        language == DesktopLanguage.English ? "Previous" : "上一页";

    /// <summary>下一页按钮。</summary>
    /// <param name="language">界面语言。</param>
    /// <returns>本地化按钮文案。</returns>
    internal static string ScanHistoryNextPage(DesktopLanguage language) =>
        language == DesktopLanguage.English ? "Next" : "下一页";

    /// <summary>扫描历史分页状态。</summary>
    /// <param name="currentPage">当前页（1-based）。</param>
    /// <param name="totalPages">总页数（至少 1）。</param>
    /// <param name="totalCount">扫描运行总数。</param>
    /// <param name="language">界面语言。</param>
    /// <returns>本地化分页文本。</returns>
    internal static string ScanHistoryPage(int currentPage, int totalPages, int totalCount, DesktopLanguage language) =>
        language == DesktopLanguage.English
            ? $"Page {currentPage} of {totalPages} · {totalCount} scans"
            : $"第 {currentPage} / {totalPages} 页 · 共 {totalCount} 次扫描";

    /// <summary>发现项数量标签。</summary>
    /// <param name="count">数量。</param>
    /// <param name="language">界面语言。</param>
    /// <returns>本地化数量文本。</returns>
    internal static string ScanHistoryFindingCount(int count, DesktopLanguage language) =>
        language == DesktopLanguage.English ? $"{count} findings" : $"{count} 个发现";

    /// <summary>扫描耗时文本。</summary>
    /// <param name="durationMs">耗时毫秒；未知时为 <see langword="null"/>。</param>
    /// <returns>耗时文本。</returns>
    internal static string ScanHistoryDuration(long? durationMs) =>
        durationMs is { } value ? $"{value} ms" : "—";

    /// <summary>退出码标签。</summary>
    /// <param name="exitCode">退出码。</param>
    /// <param name="language">界面语言。</param>
    /// <returns>本地化退出码文本。</returns>
    internal static string ScanHistoryExitCode(int exitCode, DesktopLanguage language) =>
        language == DesktopLanguage.English ? $"exit {exitCode}" : $"退出码 {exitCode}";

    /// <summary>在编辑器中打开按钮。</summary>
    /// <param name="language">界面语言。</param>
    /// <returns>本地化按钮文案。</returns>
    internal static string ScanHistoryOpenInEditor(DesktopLanguage language) =>
        language == DesktopLanguage.English ? "Open in editor" : "在编辑器中打开";

    /// <summary>规则字段标签。</summary>
    /// <param name="language">界面语言。</param>
    /// <returns>本地化标签。</returns>
    internal static string ScanHistoryRuleLabel(DesktopLanguage language) =>
        language == DesktopLanguage.English ? "Rule" : "规则";

    /// <summary>严重度字段标签。</summary>
    /// <param name="language">界面语言。</param>
    /// <returns>本地化标签。</returns>
    internal static string ScanHistorySeverityLabel(DesktopLanguage language) =>
        language == DesktopLanguage.English ? "Severity" : "严重度";

    /// <summary>位置字段标签。</summary>
    /// <param name="language">界面语言。</param>
    /// <returns>本地化标签。</returns>
    internal static string ScanHistoryLocationLabel(DesktopLanguage language) =>
        language == DesktopLanguage.English ? "Location" : "位置";

    /// <summary>脱敏片段字段标签。</summary>
    /// <param name="language">界面语言。</param>
    /// <returns>本地化标签。</returns>
    internal static string ScanHistorySnippetLabel(DesktopLanguage language) =>
        language == DesktopLanguage.English ? "Masked snippet" : "脱敏片段";

    /// <summary>指纹字段标签。</summary>
    /// <param name="language">界面语言。</param>
    /// <returns>本地化标签。</returns>
    internal static string ScanHistoryFingerprintLabel(DesktopLanguage language) =>
        language == DesktopLanguage.English ? "Fingerprint" : "指纹";

    /// <summary>提交字段标签。</summary>
    /// <param name="language">界面语言。</param>
    /// <returns>本地化标签。</returns>
    internal static string ScanHistoryCommitLabel(DesktopLanguage language) =>
        language == DesktopLanguage.English ? "Commit" : "提交";

    /// <summary>扫描运行已不存在提示。</summary>
    /// <param name="language">界面语言。</param>
    /// <returns>本地化提示。</returns>
    internal static string ScanHistoryRunNotFound(DesktopLanguage language) =>
        language == DesktopLanguage.English
            ? "The selected scan run no longer exists."
            : "所选扫描记录已不存在。";

    /// <summary>编辑器启动失败提示。</summary>
    /// <param name="status">启动结果分类。</param>
    /// <param name="language">界面语言。</param>
    /// <returns>本地化提示。</returns>
    internal static string ScanHistoryEditorError(EditorLaunchStatus status, DesktopLanguage language) =>
        (status, language) switch
        {
            (EditorLaunchStatus.RepositoryMissing, DesktopLanguage.English) =>
                "The repository directory no longer exists.",
            (EditorLaunchStatus.FileMissing, DesktopLanguage.English) =>
                "The file no longer exists; it may have been moved or deleted.",
            (EditorLaunchStatus.PathOutsideRepository, DesktopLanguage.English) =>
                "The finding path is outside the repository and was refused.",
            (EditorLaunchStatus.LaunchFailed, DesktopLanguage.English) =>
                "Could not start an editor. Set GITGUARD_EDITOR, VISUAL, or EDITOR.",
            (EditorLaunchStatus.RepositoryMissing, _) =>
                "仓库目录已不存在。",
            (EditorLaunchStatus.FileMissing, _) =>
                "文件已不存在，可能已被移动或删除。",
            (EditorLaunchStatus.PathOutsideRepository, _) =>
                "发现项路径不在仓库内，已拒绝打开。",
            (EditorLaunchStatus.LaunchFailed, _) =>
                "无法启动编辑器，请设置 GITGUARD_EDITOR、VISUAL 或 EDITOR。",
            _ => string.Empty,
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

    /// <summary>仓库详情页的白名单入口按钮。</summary>
    /// <param name="language">界面语言。</param>
    /// <returns>本地化按钮文案。</returns>
    internal static string AllowlistButton(DesktopLanguage language) =>
        language == DesktopLanguage.English ? "Allowlist" : "白名单";

    /// <summary>白名单页标题。</summary>
    /// <param name="language">界面语言。</param>
    /// <returns>本地化标题。</returns>
    internal static string AllowlistTitle(DesktopLanguage language) =>
        language == DesktopLanguage.English ? "Allowlist" : "白名单管理";

    /// <summary>返回仓库详情按钮。</summary>
    /// <param name="language">界面语言。</param>
    /// <returns>本地化按钮文案。</returns>
    internal static string AllowlistBackButton(DesktopLanguage language) =>
        language == DesktopLanguage.English ? "Back to repository" : "返回仓库详情";

    /// <summary>刷新白名单按钮。</summary>
    /// <param name="language">界面语言。</param>
    /// <returns>本地化按钮文案。</returns>
    internal static string AllowlistRefreshButton(DesktopLanguage language) =>
        language == DesktopLanguage.English ? "Refresh" : "刷新";

    /// <summary>白名单条目列表标题。</summary>
    /// <param name="language">界面语言。</param>
    /// <returns>本地化标题。</returns>
    internal static string AllowlistEntriesTitle(DesktopLanguage language) =>
        language == DesktopLanguage.English ? "Allowlist entries" : "白名单条目";

    /// <summary>新增白名单表单标题。</summary>
    /// <param name="language">界面语言。</param>
    /// <returns>本地化标题。</returns>
    internal static string AllowlistFormTitle(DesktopLanguage language) =>
        language == DesktopLanguage.English ? "Add allowlist entry" : "新增白名单条目";

    /// <summary>白名单全局作用域与匹配条件提示。</summary>
    /// <param name="language">界面语言。</param>
    /// <returns>本地化提示。</returns>
    internal static string AllowlistGlobalHint(DesktopLanguage language) =>
        language == DesktopLanguage.English
            ? "Entries are stored in the local audit database and apply to every registered repository. Provide at least one of rule ID, path glob, or fingerprint."
            : "白名单保存在本机审计库，对所有已登记仓库生效；规则 ID、路径 glob、指纹三者至少填写一项。";

    /// <summary>规则 ID 字段标签。</summary>
    /// <param name="language">界面语言。</param>
    /// <returns>本地化标签。</returns>
    internal static string AllowlistRuleLabel(DesktopLanguage language) =>
        language == DesktopLanguage.English ? "Rule ID" : "规则 ID";

    /// <summary>路径 glob 字段标签。</summary>
    /// <param name="language">界面语言。</param>
    /// <returns>本地化标签。</returns>
    internal static string AllowlistPathLabel(DesktopLanguage language) =>
        language == DesktopLanguage.English ? "Path glob" : "路径 glob";

    /// <summary>指纹字段标签。</summary>
    /// <param name="language">界面语言。</param>
    /// <returns>本地化标签。</returns>
    internal static string AllowlistFingerprintLabel(DesktopLanguage language) =>
        language == DesktopLanguage.English ? "Fingerprint" : "指纹";

    /// <summary>原因字段标签。</summary>
    /// <param name="language">界面语言。</param>
    /// <returns>本地化标签。</returns>
    internal static string AllowlistReasonLabel(DesktopLanguage language) =>
        language == DesktopLanguage.English ? "Reason (at least 5 characters)" : "原因（至少 5 字符）";

    /// <summary>规则 ID 输入水印。</summary>
    /// <param name="language">界面语言。</param>
    /// <returns>本地化水印。</returns>
    internal static string AllowlistRuleWatermark(DesktopLanguage language) =>
        language == DesktopLanguage.English
            ? "e.g. GGD/SECRET/AWS_ACCESS_KEY"
            : "例如 GGD/SECRET/AWS_ACCESS_KEY";

    /// <summary>路径 glob 输入水印。</summary>
    /// <param name="language">界面语言。</param>
    /// <returns>本地化水印。</returns>
    internal static string AllowlistPathWatermark(DesktopLanguage language) =>
        language == DesktopLanguage.English ? "e.g. src/**/*.cs" : "例如 src/**/*.cs";

    /// <summary>指纹输入水印。</summary>
    /// <param name="language">界面语言。</param>
    /// <returns>本地化水印。</returns>
    internal static string AllowlistFingerprintWatermark(DesktopLanguage language) =>
        language == DesktopLanguage.English ? "64-character SHA-256 hex" : "64 位 SHA-256 十六进制";

    /// <summary>原因输入水印。</summary>
    /// <param name="language">界面语言。</param>
    /// <returns>本地化水印。</returns>
    internal static string AllowlistReasonWatermark(DesktopLanguage language) =>
        language == DesktopLanguage.English
            ? "Why this match is allowed (audited)"
            : "说明放行原因（写入审计）";

    /// <summary>添加白名单按钮。</summary>
    /// <param name="language">界面语言。</param>
    /// <returns>本地化按钮文案。</returns>
    internal static string AllowlistAddButton(DesktopLanguage language) =>
        language == DesktopLanguage.English ? "Add" : "添加";

    /// <summary>白名单空状态。</summary>
    /// <param name="language">界面语言。</param>
    /// <returns>本地化提示。</returns>
    internal static string AllowlistEmpty(DesktopLanguage language) =>
        language == DesktopLanguage.English
            ? "No allowlist entries yet. Use the form on the right to add one."
            : "尚未登记白名单条目。在右侧填写条件即可新增。";

    /// <summary>白名单加载提示。</summary>
    /// <param name="language">界面语言。</param>
    /// <returns>本地化提示。</returns>
    internal static string AllowlistLoading(DesktopLanguage language) =>
        language == DesktopLanguage.English ? "Loading allowlist…" : "正在加载白名单…";

    /// <summary>白名单分页状态。</summary>
    /// <param name="currentPage">当前页（1-based）。</param>
    /// <param name="totalPages">总页数（至少 1）。</param>
    /// <param name="totalCount">白名单条目总数。</param>
    /// <param name="language">界面语言。</param>
    /// <returns>本地化分页文本。</returns>
    internal static string AllowlistPage(int currentPage, int totalPages, int totalCount, DesktopLanguage language) =>
        language == DesktopLanguage.English
            ? $"Page {currentPage} of {totalPages} · {totalCount} entries"
            : $"第 {currentPage} / {totalPages} 页 · 共 {totalCount} 条";

    /// <summary>上一页按钮。</summary>
    /// <param name="language">界面语言。</param>
    /// <returns>本地化按钮文案。</returns>
    internal static string AllowlistPreviousPage(DesktopLanguage language) =>
        language == DesktopLanguage.English ? "Previous" : "上一页";

    /// <summary>下一页按钮。</summary>
    /// <param name="language">界面语言。</param>
    /// <returns>本地化按钮文案。</returns>
    internal static string AllowlistNextPage(DesktopLanguage language) =>
        language == DesktopLanguage.English ? "Next" : "下一页";

    /// <summary>删除白名单按钮。</summary>
    /// <param name="language">界面语言。</param>
    /// <returns>本地化按钮文案。</returns>
    internal static string AllowlistDeleteButton(DesktopLanguage language) =>
        language == DesktopLanguage.English ? "Delete" : "删除";

    /// <summary>删除二次确认提示。</summary>
    /// <param name="language">界面语言。</param>
    /// <returns>本地化提示。</returns>
    internal static string AllowlistDeleteConfirmPrompt(DesktopLanguage language) =>
        language == DesktopLanguage.English
            ? "Delete this allowlist entry? Subsequent scans will no longer allow matching content."
            : "确认删除该白名单条目？删除后后续扫描将不再放行匹配内容。";

    /// <summary>确认删除按钮。</summary>
    /// <param name="language">界面语言。</param>
    /// <returns>本地化按钮文案。</returns>
    internal static string AllowlistConfirmDeleteButton(DesktopLanguage language) =>
        language == DesktopLanguage.English ? "Confirm delete" : "确认删除";

    /// <summary>取消删除按钮。</summary>
    /// <param name="language">界面语言。</param>
    /// <returns>本地化按钮文案。</returns>
    internal static string AllowlistCancelButton(DesktopLanguage language) =>
        language == DesktopLanguage.English ? "Cancel" : "取消";

    /// <summary>匹配条件缺失校验提示。</summary>
    /// <param name="language">界面语言。</param>
    /// <returns>本地化提示。</returns>
    internal static string AllowlistValidationAtLeastOne(DesktopLanguage language) =>
        language == DesktopLanguage.English
            ? "Provide at least one of rule ID, path glob, or fingerprint."
            : "请至少填写规则 ID、路径 glob 或指纹之一。";

    /// <summary>原因过短校验提示。</summary>
    /// <param name="language">界面语言。</param>
    /// <returns>本地化提示。</returns>
    internal static string AllowlistValidationReasonTooShort(DesktopLanguage language) =>
        language == DesktopLanguage.English
            ? "The reason must contain at least 5 characters."
            : "原因至少需要 5 个字符。";

    /// <summary>原因过长校验提示。</summary>
    /// <param name="language">界面语言。</param>
    /// <returns>本地化提示。</returns>
    internal static string AllowlistValidationReasonTooLong(DesktopLanguage language) =>
        language == DesktopLanguage.English
            ? "The reason cannot exceed 256 characters."
            : "原因不能超过 256 个字符。";

    /// <summary>规则 ID 格式校验提示。</summary>
    /// <param name="language">界面语言。</param>
    /// <returns>本地化提示。</returns>
    internal static string AllowlistValidationRuleId(DesktopLanguage language) =>
        language == DesktopLanguage.English
            ? "The rule ID format is invalid. Expected a value like GGD/SECRET/AWS_ACCESS_KEY."
            : "规则 ID 格式不合法，应形如 GGD/SECRET/AWS_ACCESS_KEY。";

    /// <summary>指纹格式校验提示。</summary>
    /// <param name="language">界面语言。</param>
    /// <returns>本地化提示。</returns>
    internal static string AllowlistValidationFingerprint(DesktopLanguage language) =>
        language == DesktopLanguage.English
            ? "The fingerprint must be a 64-character hexadecimal SHA-256 value."
            : "指纹必须是 64 位十六进制 SHA-256 文本。";

    /// <summary>白名单新增成功提示。</summary>
    /// <param name="language">界面语言。</param>
    /// <returns>本地化提示。</returns>
    internal static string AllowlistAddedMessage(DesktopLanguage language) =>
        language == DesktopLanguage.English ? "Allowlist entry added." : "白名单条目已添加。";

    /// <summary>白名单删除成功提示。</summary>
    /// <param name="language">界面语言。</param>
    /// <returns>本地化提示。</returns>
    internal static string AllowlistDeletedMessage(DesktopLanguage language) =>
        language == DesktopLanguage.English ? "Allowlist entry deleted." : "白名单条目已删除。";

    /// <summary>白名单条目登记信息。</summary>
    /// <param name="createdBy">登记人。</param>
    /// <param name="createdAt">登记时间（UTC，展示时转本地时区）。</param>
    /// <param name="language">界面语言。</param>
    /// <returns>本地化登记信息。</returns>
    internal static string AllowlistEntryMeta(string createdBy, DateTimeOffset createdAt, DesktopLanguage language)
    {
        string timestamp = createdAt.ToLocalTime().ToString("g", CultureInfo.CurrentCulture);
        return language == DesktopLanguage.English
            ? $"Added by {createdBy} on {timestamp}"
            : $"由 {createdBy} 于 {timestamp} 登记";
    }

    /// <summary>白名单条目过期时间。</summary>
    /// <param name="expiresAt">过期时间（UTC，展示时转本地时区）。</param>
    /// <param name="language">界面语言。</param>
    /// <returns>本地化过期时间。</returns>
    internal static string AllowlistExpires(DateTimeOffset expiresAt, DesktopLanguage language)
    {
        string timestamp = expiresAt.ToLocalTime().ToString("g", CultureInfo.CurrentCulture);
        return language == DesktopLanguage.English ? $"Expires {timestamp}" : $"过期时间 {timestamp}";
    }

    /// <summary>白名单管理错误提示（按稳定错误码映射；未知错误回退原始消息）。</summary>
    /// <param name="errorCode">错误码。</param>
    /// <param name="fallbackMessage">原始消息。</param>
    /// <param name="language">界面语言。</param>
    /// <returns>本地化提示。</returns>
    internal static string AllowlistError(string errorCode, string fallbackMessage, DesktopLanguage language) =>
        (errorCode, language) switch
        {
            (AllowlistException.ALLOWLIST_VALIDATION_FAILED, DesktopLanguage.English) =>
                "The allowlist entry is invalid. Check the rule ID, path glob, fingerprint, and reason.",
            (AllowlistException.ALLOWLIST_NOT_FOUND, DesktopLanguage.English) =>
                "The allowlist entry no longer exists.",
            (AllowlistException.ALLOWLIST_VALIDATION_FAILED, _) =>
                "白名单参数不合法，请检查规则 ID、路径 glob、指纹与原因。",
            (AllowlistException.ALLOWLIST_NOT_FOUND, _) =>
                "白名单条目已不存在。",
            _ => fallbackMessage,
        };

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

    /// <summary>规则库刷新按钮。</summary>
    /// <param name="language">界面语言。</param>
    /// <returns>本地化按钮文案。</returns>
    internal static string RulesRefreshButton(DesktopLanguage language) =>
        language == DesktopLanguage.English ? "Refresh" : "刷新";

    /// <summary>规则库搜索框水印。</summary>
    /// <param name="language">界面语言。</param>
    /// <returns>本地化水印。</returns>
    internal static string RulesSearchWatermark(DesktopLanguage language) =>
        language == DesktopLanguage.English ? "Search by rule ID or name" : "按规则 ID 或名称搜索";

    /// <summary>来源筛选标签。</summary>
    /// <param name="language">界面语言。</param>
    /// <returns>本地化标签。</returns>
    internal static string RulesSourceFilterLabel(DesktopLanguage language) =>
        language == DesktopLanguage.English ? "Source" : "来源";

    /// <summary>类别筛选标签。</summary>
    /// <param name="language">界面语言。</param>
    /// <returns>本地化标签。</returns>
    internal static string RulesCategoryFilterLabel(DesktopLanguage language) =>
        language == DesktopLanguage.English ? "Category" : "类别";

    /// <summary>仅显示已启用规则复选框。</summary>
    /// <param name="language">界面语言。</param>
    /// <returns>本地化文案。</returns>
    internal static string RulesEnabledOnly(DesktopLanguage language) =>
        language == DesktopLanguage.English ? "Enabled only" : "仅显示已启用";

    /// <summary>规则目录加载提示。</summary>
    /// <param name="language">界面语言。</param>
    /// <returns>本地化提示。</returns>
    internal static string RulesLoading(DesktopLanguage language) =>
        language == DesktopLanguage.English ? "Loading rule catalog…" : "正在加载规则目录…";

    /// <summary>规则列表标题。</summary>
    /// <param name="language">界面语言。</param>
    /// <returns>本地化标题。</returns>
    internal static string RulesListTitle(DesktopLanguage language) =>
        language == DesktopLanguage.English ? "Rules" : "规则列表";

    /// <summary>规则详情标题。</summary>
    /// <param name="language">界面语言。</param>
    /// <returns>本地化标题。</returns>
    internal static string RulesDetailTitle(DesktopLanguage language) =>
        language == DesktopLanguage.English ? "Rule details" : "规则详情";

    /// <summary>未选择规则时的提示。</summary>
    /// <param name="language">界面语言。</param>
    /// <returns>本地化提示。</returns>
    internal static string RulesSelectPrompt(DesktopLanguage language) =>
        language == DesktopLanguage.English
            ? "Select a rule to inspect its metadata. Regular expressions stay hidden until you reveal them."
            : "选择一条规则查看元信息；正则默认隐藏，需手动展开。";

    /// <summary>规则 ID 字段标签。</summary>
    /// <param name="language">界面语言。</param>
    /// <returns>本地化标签。</returns>
    internal static string RulesRuleIdLabel(DesktopLanguage language) =>
        language == DesktopLanguage.English ? "Rule ID" : "规则 ID";

    /// <summary>规则名称字段标签。</summary>
    /// <param name="language">界面语言。</param>
    /// <returns>本地化标签。</returns>
    internal static string RulesRuleNameLabel(DesktopLanguage language) =>
        language == DesktopLanguage.English ? "Name" : "名称";

    /// <summary>类别字段标签。</summary>
    /// <param name="language">界面语言。</param>
    /// <returns>本地化标签。</returns>
    internal static string RulesCategoryFieldLabel(DesktopLanguage language) =>
        language == DesktopLanguage.English ? "Category" : "类别";

    /// <summary>严重度字段标签。</summary>
    /// <param name="language">界面语言。</param>
    /// <returns>本地化标签。</returns>
    internal static string RulesSeverityFieldLabel(DesktopLanguage language) =>
        language == DesktopLanguage.English ? "Severity" : "严重度";

    /// <summary>来源字段标签。</summary>
    /// <param name="language">界面语言。</param>
    /// <returns>本地化标签。</returns>
    internal static string RulesSourceFieldLabel(DesktopLanguage language) =>
        language == DesktopLanguage.English ? "Source" : "来源";

    /// <summary>启用状态字段标签。</summary>
    /// <param name="language">界面语言。</param>
    /// <returns>本地化标签。</returns>
    internal static string RulesStatusFieldLabel(DesktopLanguage language) =>
        language == DesktopLanguage.English ? "Status" : "状态";

    /// <summary>正则字段标签。</summary>
    /// <param name="language">界面语言。</param>
    /// <returns>本地化标签。</returns>
    internal static string RulesPatternLabel(DesktopLanguage language) =>
        language == DesktopLanguage.English ? "Regular expression" : "正则表达式";

    /// <summary>正则默认隐藏提示。</summary>
    /// <param name="language">界面语言。</param>
    /// <returns>本地化提示。</returns>
    internal static string RulesPatternHint(DesktopLanguage language) =>
        language == DesktopLanguage.English
            ? "Regex content may reveal matching logic and is hidden by default."
            : "正则可能暴露匹配逻辑，默认隐藏。";

    /// <summary>显示正则按钮。</summary>
    /// <param name="language">界面语言。</param>
    /// <returns>本地化按钮文案。</returns>
    internal static string RulesPatternShow(DesktopLanguage language) =>
        language == DesktopLanguage.English ? "Show regex" : "显示正则";

    /// <summary>隐藏正则按钮。</summary>
    /// <param name="language">界面语言。</param>
    /// <returns>本地化按钮文案。</returns>
    internal static string RulesPatternHide(DesktopLanguage language) =>
        language == DesktopLanguage.English ? "Hide regex" : "隐藏正则";

    /// <summary>正则隐藏状态的占位文本。</summary>
    /// <param name="language">界面语言。</param>
    /// <returns>本地化占位文本。</returns>
    internal static string RulesPatternHidden(DesktopLanguage language) =>
        language == DesktopLanguage.English
            ? "Hidden by default. Use \"Show regex\" to reveal it."
            : "默认隐藏，点击「显示正则」查看。";

    /// <summary>规则列表空状态。</summary>
    /// <param name="language">界面语言。</param>
    /// <returns>本地化提示。</returns>
    internal static string RulesEmpty(DesktopLanguage language) =>
        language == DesktopLanguage.English
            ? "No rules match the current search and filters."
            : "没有符合当前搜索与筛选条件的规则。";

    /// <summary>Gitleaks 分类空状态。</summary>
    /// <param name="language">界面语言。</param>
    /// <returns>本地化提示。</returns>
    internal static string RulesGitleaksEmpty(DesktopLanguage language) =>
        language == DesktopLanguage.English
            ? "Gitleaks does not expose a static rule catalog. Switch to All to browse built-in and custom rules."
            : "Gitleaks 不提供静态规则清单；切换到「全部」浏览内置与自定义规则。";

    /// <summary>规则数量摘要。</summary>
    /// <param name="total">规则总数。</param>
    /// <param name="enabled">有效启用数量。</param>
    /// <param name="visible">当前显示数量。</param>
    /// <param name="language">界面语言。</param>
    /// <returns>本地化摘要。</returns>
    internal static string RulesSummary(int total, int enabled, int visible, DesktopLanguage language) =>
        language == DesktopLanguage.English
            ? $"{total} rules · {enabled} enabled · {visible} shown"
            : $"共 {total} 条 · 启用 {enabled} 条 · 当前显示 {visible} 条";

    /// <summary>规则目录加载失败提示。</summary>
    /// <param name="message">底层错误消息。</param>
    /// <param name="language">界面语言。</param>
    /// <returns>本地化提示。</returns>
    internal static string RulesLoadFailed(string message, DesktopLanguage language) =>
        language == DesktopLanguage.English
            ? $"Failed to load the rule catalog: {message}"
            : $"规则目录加载失败：{message}";

    /// <summary>Gitleaks 已启用说明。</summary>
    /// <param name="executable">可执行文件路径或 PATH 说明。</param>
    /// <param name="language">界面语言。</param>
    /// <returns>本地化说明。</returns>
    internal static string RulesGitleaksEnabledInfo(string executable, DesktopLanguage language) =>
        language == DesktopLanguage.English
            ? $"Gitleaks rules are produced dynamically by the external engine during scans (GLK/SECRET/*) and are not part of this static catalog. Adapter enabled; executable: {executable}."
            : $"Gitleaks 规则由外部引擎在扫描时动态产生（GLK/SECRET/*），不进入此静态规则目录。适配器已启用；可执行文件：{executable}。";

    /// <summary>Gitleaks 未启用说明。</summary>
    /// <param name="language">界面语言。</param>
    /// <returns>本地化说明。</returns>
    internal static string RulesGitleaksDisabledInfo(DesktopLanguage language) =>
        language == DesktopLanguage.English
            ? "Gitleaks rules are produced dynamically by the external engine during scans and are not part of this static catalog. Adapter disabled (rules.secrets.use_gitleaks = false)."
            : "Gitleaks 规则由外部引擎在扫描时动态产生，不进入此静态规则目录。适配器未启用（rules.secrets.use_gitleaks = false）。";

    /// <summary>Gitleaks 在 PATH 中查找时的说明。</summary>
    /// <param name="language">界面语言。</param>
    /// <returns>本地化说明。</returns>
    internal static string RulesGitleaksSearchPath(DesktopLanguage language) =>
        language == DesktopLanguage.English ? "gitleaks from PATH" : "PATH 中的 gitleaks";

    /// <summary>规则来源标签。</summary>
    /// <param name="source">规则来源。</param>
    /// <param name="language">界面语言。</param>
    /// <returns>本地化标签。</returns>
    internal static string RuleSourceLabel(RuleSource source, DesktopLanguage language) => source switch
    {
        RuleSource.Builtin => language == DesktopLanguage.English ? "Built-in" : "内置",
        RuleSource.User => language == DesktopLanguage.English ? "Custom" : "自定义",
        RuleSource.Gitleaks => "Gitleaks",
        _ => source.ToString(),
    };

    /// <summary>规则来源筛选项标签。</summary>
    /// <param name="key">筛选标识（ALL / BUILTIN / USER / GITLEAKS）。</param>
    /// <param name="language">界面语言。</param>
    /// <returns>本地化标签。</returns>
    internal static string RuleSourceFilterLabel(string key, DesktopLanguage language) => key switch
    {
        "ALL" => language == DesktopLanguage.English ? "All sources" : "全部来源",
        "BUILTIN" => language == DesktopLanguage.English ? "Built-in" : "内置",
        "USER" => language == DesktopLanguage.English ? "Custom" : "自定义",
        "GITLEAKS" => "Gitleaks",
        _ => key,
    };

    /// <summary>规则类别标签。</summary>
    /// <param name="category">类别段（SECRET / PRIVACY / LOCALPATH / URL）。</param>
    /// <param name="language">界面语言。</param>
    /// <returns>本地化标签。</returns>
    internal static string RuleCategoryLabel(string category, DesktopLanguage language) => category switch
    {
        "SECRET" => language == DesktopLanguage.English ? "Secret" : "秘密",
        "PRIVACY" => language == DesktopLanguage.English ? "Privacy" : "隐私",
        "LOCALPATH" => language == DesktopLanguage.English ? "Local path" : "本地路径",
        "URL" => "URL",
        _ => category,
    };

    /// <summary>规则类别筛选项标签。</summary>
    /// <param name="key">筛选标识（ALL / SECRET / PRIVACY / LOCALPATH / URL）。</param>
    /// <param name="language">界面语言。</param>
    /// <returns>本地化标签。</returns>
    internal static string RuleCategoryFilterLabel(string key, DesktopLanguage language) =>
        string.Equals(key, "ALL", StringComparison.Ordinal)
            ? language == DesktopLanguage.English ? "All categories" : "全部类别"
            : RuleCategoryLabel(key, language);

    /// <summary>规则启用状态标签。</summary>
    /// <param name="enabled">是否有效启用。</param>
    /// <param name="language">界面语言。</param>
    /// <returns>本地化标签。</returns>
    internal static string RuleEnabledLabel(bool enabled, DesktopLanguage language) =>
        enabled
            ? language == DesktopLanguage.English ? "Enabled" : "已启用"
            : language == DesktopLanguage.English ? "Disabled" : "已禁用";

    /// <summary>跨仓库日志加载提示。</summary>
    /// <param name="language">界面语言。</param>
    /// <returns>本地化提示。</returns>
    internal static string AuditLogLoading(DesktopLanguage language) =>
        language == DesktopLanguage.English ? "Loading audit logs…" : "正在加载审计日志…";

    /// <summary>跨仓库日志空状态。</summary>
    /// <param name="language">界面语言。</param>
    /// <returns>本地化提示。</returns>
    internal static string AuditLogEmpty(DesktopLanguage language) =>
        language == DesktopLanguage.English
            ? "No audit runs match the current filters."
            : "没有符合当前筛选条件的审计记录。";

    /// <summary>跨仓库日志刷新按钮。</summary>
    /// <param name="language">界面语言。</param>
    /// <returns>本地化按钮文案。</returns>
    internal static string AuditLogRefreshButton(DesktopLanguage language) =>
        language == DesktopLanguage.English ? "Refresh" : "刷新";

    /// <summary>跨仓库日志分页状态。</summary>
    /// <param name="currentPage">当前页（1-based）。</param>
    /// <param name="totalPages">总页数（至少 1）。</param>
    /// <param name="totalCount">审计记录总数。</param>
    /// <param name="language">界面语言。</param>
    /// <returns>本地化分页文本。</returns>
    internal static string AuditLogPage(int currentPage, int totalPages, int totalCount, DesktopLanguage language) =>
        language == DesktopLanguage.English
            ? $"Page {currentPage} of {totalPages} · {totalCount} runs"
            : $"第 {currentPage} / {totalPages} 页 · 共 {totalCount} 条审计记录";

    /// <summary>跨仓库日志总数摘要。</summary>
    /// <param name="totalCount">审计记录总数。</param>
    /// <param name="language">界面语言。</param>
    /// <returns>本地化摘要。</returns>
    internal static string AuditLogSummary(int totalCount, DesktopLanguage language) =>
        language == DesktopLanguage.English
            ? $"{totalCount} audit runs in total"
            : $"共 {totalCount} 条审计记录";

    /// <summary>导出说明（导出全部记录且与 CLI 契约一致）。</summary>
    /// <param name="language">界面语言。</param>
    /// <returns>本地化说明。</returns>
    internal static string AuditLogExportHint(DesktopLanguage language) =>
        language == DesktopLanguage.English
            ? "Exports every audit run in the database (not just this page) using the same JSON / CSV contract as the CLI."
            : "导出数据库中的全部审计记录（不仅是当前页），格式与 CLI 的 JSON / CSV 契约一致。";

    /// <summary>导出 JSON 按钮。</summary>
    /// <param name="language">界面语言。</param>
    /// <returns>本地化按钮文案。</returns>
    internal static string AuditLogExportJson(DesktopLanguage language) =>
        language == DesktopLanguage.English ? "Export JSON" : "导出 JSON";

    /// <summary>导出 CSV 按钮。</summary>
    /// <param name="language">界面语言。</param>
    /// <returns>本地化按钮文案。</returns>
    internal static string AuditLogExportCsv(DesktopLanguage language) =>
        language == DesktopLanguage.English ? "Export CSV" : "导出 CSV";

    /// <summary>结果筛选标签。</summary>
    /// <param name="language">界面语言。</param>
    /// <returns>本地化标签。</returns>
    internal static string AuditLogResultFilterLabelText(DesktopLanguage language) =>
        language == DesktopLanguage.English ? "Result" : "结果";

    /// <summary>时间范围筛选标签。</summary>
    /// <param name="language">界面语言。</param>
    /// <returns>本地化标签。</returns>
    internal static string AuditLogRangeFilterLabelText(DesktopLanguage language) =>
        language == DesktopLanguage.English ? "Time range" : "时间范围";

    /// <summary>时间列标题。</summary>
    /// <param name="language">界面语言。</param>
    /// <returns>本地化标题。</returns>
    internal static string AuditLogColumnTime(DesktopLanguage language) =>
        language == DesktopLanguage.English ? "Time" : "时间";

    /// <summary>仓库列标题。</summary>
    /// <param name="language">界面语言。</param>
    /// <returns>本地化标题。</returns>
    internal static string AuditLogColumnRepository(DesktopLanguage language) =>
        language == DesktopLanguage.English ? "Repository" : "仓库";

    /// <summary>结果列标题。</summary>
    /// <param name="language">界面语言。</param>
    /// <returns>本地化标题。</returns>
    internal static string AuditLogColumnResult(DesktopLanguage language) =>
        language == DesktopLanguage.English ? "Result" : "结果";

    /// <summary>明细列标题。</summary>
    /// <param name="language">界面语言。</param>
    /// <returns>本地化标题。</returns>
    internal static string AuditLogColumnDetail(DesktopLanguage language) =>
        language == DesktopLanguage.English ? "Detail" : "明细";

    /// <summary>结果筛选项标签。</summary>
    /// <param name="result">扫描结果；<see langword="null"/> 表示全部结果。</param>
    /// <param name="language">界面语言。</param>
    /// <returns>本地化标签。</returns>
    internal static string AuditLogResultFilterLabel(ScanResult? result, DesktopLanguage language) =>
        result is { } value
            ? ScanResultLabel(value, language)
            : language == DesktopLanguage.English ? "All results" : "全部结果";

    /// <summary>时间范围筛选项标签。</summary>
    /// <param name="days">最近天数；<see langword="null"/> 表示全部时间。</param>
    /// <param name="language">界面语言。</param>
    /// <returns>本地化标签。</returns>
    internal static string AuditLogRangeFilterLabel(int? days, DesktopLanguage language) =>
        days is { } value
            ? language == DesktopLanguage.English ? $"Last {value} days" : $"最近 {value} 天"
            : language == DesktopLanguage.English ? "All time" : "全部时间";

    /// <summary>导出文件选择器标题。</summary>
    /// <param name="format">导出格式。</param>
    /// <param name="language">界面语言。</param>
    /// <returns>本地化标题。</returns>
    internal static string AuditLogExportPickerTitle(AuditExportFormat format, DesktopLanguage language)
    {
        string formatName = format == AuditExportFormat.Json ? "JSON" : "CSV";
        return language == DesktopLanguage.English
            ? $"Export audit log as {formatName}"
            : $"导出审计日志（{formatName}）";
    }

    /// <summary>导出成功提示。</summary>
    /// <param name="report">导出结果。</param>
    /// <param name="language">界面语言。</param>
    /// <returns>本地化提示。</returns>
    internal static string AuditLogExportSucceeded(AuditExportReport report, DesktopLanguage language)
    {
        ArgumentNullException.ThrowIfNull(report);
        return language == DesktopLanguage.English
            ? $"Exported {report.RunCount} runs ({report.FindingCount} findings) to {report.OutputPath}."
            : $"已导出 {report.RunCount} 条审计记录（{report.FindingCount} 个发现）到 {report.OutputPath}。";
    }

    /// <summary>导出失败提示。</summary>
    /// <param name="language">界面语言。</param>
    /// <param name="message">底层错误消息。</param>
    /// <returns>本地化提示。</returns>
    internal static string AuditLogExportFailed(DesktopLanguage language, string message) =>
        language == DesktopLanguage.English
            ? $"Export failed: {message}"
            : $"导出失败：{message}";

    /// <summary>审计日志加载失败提示。</summary>
    /// <param name="language">界面语言。</param>
    /// <param name="message">底层错误消息。</param>
    /// <returns>本地化提示。</returns>
    internal static string AuditLogLoadFailed(DesktopLanguage language, string message) =>
        language == DesktopLanguage.English
            ? $"Failed to load audit logs: {message}"
            : $"审计日志加载失败：{message}";

    /// <summary>设置页加载提示。</summary>
    /// <param name="language">界面语言。</param>
    /// <returns>本地化提示。</returns>
    internal static string SettingsLoading(DesktopLanguage language) =>
        language == DesktopLanguage.English ? "Loading settings…" : "正在加载设置…";

    /// <summary>语言区块标题。</summary>
    /// <param name="language">界面语言。</param>
    /// <returns>本地化标题。</returns>
    internal static string SettingsLanguageLabel(DesktopLanguage language) =>
        language == DesktopLanguage.English ? "Interface language" : "界面语言";

    /// <summary>语言区块说明。</summary>
    /// <param name="language">界面语言。</param>
    /// <returns>本地化说明。</returns>
    internal static string SettingsLanguageHint(DesktopLanguage language) =>
        language == DesktopLanguage.English
            ? "Applies immediately and persists across restarts."
            : "立即生效，并在重启后保持。";

    /// <summary>主题区块标题。</summary>
    /// <param name="language">界面语言。</param>
    /// <returns>本地化标题。</returns>
    internal static string SettingsThemeLabel(DesktopLanguage language) =>
        language == DesktopLanguage.English ? "Theme" : "主题";

    /// <summary>主题区块说明。</summary>
    /// <param name="language">界面语言。</param>
    /// <returns>本地化说明。</returns>
    internal static string SettingsThemeHint(DesktopLanguage language) =>
        language == DesktopLanguage.English
            ? "Follow system switches with the operating system appearance; Light or Dark forces the theme."
            : "跟随系统会随操作系统外观切换；浅色 / 深色为强制主题。";

    /// <summary>主题选项标签。</summary>
    /// <param name="mode">主题模式。</param>
    /// <param name="language">界面语言。</param>
    /// <returns>本地化标签。</returns>
    internal static string ThemeOptionLabel(DesktopThemeMode mode, DesktopLanguage language) => mode switch
    {
        DesktopThemeMode.System => language == DesktopLanguage.English ? "Follow system" : "跟随系统",
        DesktopThemeMode.Light => language == DesktopLanguage.English ? "Light" : "浅色",
        DesktopThemeMode.Dark => language == DesktopLanguage.English ? "Dark" : "深色",
        _ => mode.ToString(),
    };

    /// <summary>Gitleaks 设置区块标题。</summary>
    /// <param name="language">界面语言。</param>
    /// <returns>本地化标题。</returns>
    internal static string SettingsGitleaksTitle(DesktopLanguage language) =>
        language == DesktopLanguage.English ? "Gitleaks integration" : "Gitleaks 集成";

    /// <summary>Gitleaks 启用开关标签。</summary>
    /// <param name="language">界面语言。</param>
    /// <returns>本地化标签。</returns>
    internal static string SettingsGitleaksEnabledLabel(DesktopLanguage language) =>
        language == DesktopLanguage.English ? "Use Gitleaks for desktop scans" : "桌面端扫描启用 Gitleaks";

    /// <summary>Gitleaks 启用开关说明。</summary>
    /// <param name="language">界面语言。</param>
    /// <returns>本地化说明。</returns>
    internal static string SettingsGitleaksEnabledHint(DesktopLanguage language) =>
        language == DesktopLanguage.English
            ? "When enabled, desktop scans run the external Gitleaks engine and merge its findings. CLI scans still follow config.yml."
            : "启用后桌面端扫描会运行外部 Gitleaks 引擎并合并其发现项；CLI 扫描仍遵循 config.yml。";

    /// <summary>Gitleaks 路径字段标签。</summary>
    /// <param name="language">界面语言。</param>
    /// <returns>本地化标签。</returns>
    internal static string SettingsGitleaksPathLabel(DesktopLanguage language) =>
        language == DesktopLanguage.English ? "Gitleaks executable" : "Gitleaks 可执行文件";

    /// <summary>Gitleaks 路径输入水印。</summary>
    /// <param name="language">界面语言。</param>
    /// <returns>本地化水印。</returns>
    internal static string SettingsGitleaksPathWatermark(DesktopLanguage language) =>
        language == DesktopLanguage.English
            ? @"e.g. C:\tools\gitleaks.exe or /usr/local/bin/gitleaks"
            : @"例如 C:\tools\gitleaks.exe 或 /usr/local/bin/gitleaks";

    /// <summary>Gitleaks 路径说明。</summary>
    /// <param name="language">界面语言。</param>
    /// <returns>本地化说明。</returns>
    internal static string SettingsGitleaksPathHint(DesktopLanguage language) =>
        language == DesktopLanguage.English
            ? "Leave empty to resolve gitleaks from PATH."
            : "留空表示从 PATH 查找 gitleaks。";

    /// <summary>保存 Gitleaks 覆盖按钮。</summary>
    /// <param name="language">界面语言。</param>
    /// <returns>本地化按钮文案。</returns>
    internal static string SettingsSaveGitleaksButton(DesktopLanguage language) =>
        language == DesktopLanguage.English ? "Apply to desktop scans" : "应用到桌面扫描";

    /// <summary>清除桌面覆盖按钮。</summary>
    /// <param name="language">界面语言。</param>
    /// <returns>本地化按钮文案。</returns>
    internal static string SettingsResetGitleaksButton(DesktopLanguage language) =>
        language == DesktopLanguage.English ? "Clear override" : "清除覆盖";

    /// <summary>Gitleaks 覆盖状态说明。</summary>
    /// <param name="hasOverride">是否存在桌面覆盖。</param>
    /// <param name="language">界面语言。</param>
    /// <returns>本地化说明。</returns>
    internal static string SettingsOverrideStatus(bool hasOverride, DesktopLanguage language) =>
        hasOverride
            ? language == DesktopLanguage.English
                ? "Desktop override active. config.yml is not modified."
                : "桌面覆盖已生效，不会写入 config.yml。"
            : language == DesktopLanguage.English
                ? "Using Gitleaks settings from config.yml."
                : "当前使用 config.yml 中的 Gitleaks 设置。";

    /// <summary>Gitleaks 覆盖保存成功提示。</summary>
    /// <param name="language">界面语言。</param>
    /// <returns>本地化提示。</returns>
    internal static string SettingsGitleaksSaved(DesktopLanguage language) =>
        language == DesktopLanguage.English ? "Gitleaks settings saved." : "Gitleaks 设置已保存。";

    /// <summary>桌面覆盖清除成功提示。</summary>
    /// <param name="language">界面语言。</param>
    /// <returns>本地化提示。</returns>
    internal static string SettingsOverrideCleared(DesktopLanguage language) =>
        language == DesktopLanguage.English
            ? "Desktop override cleared; config.yml values restored."
            : "已清除桌面覆盖，恢复 config.yml 中的值。";

    /// <summary>设置保存失败提示。</summary>
    /// <param name="language">界面语言。</param>
    /// <param name="message">底层错误消息。</param>
    /// <returns>本地化提示。</returns>
    internal static string SettingsSaveFailed(DesktopLanguage language, string message) =>
        language == DesktopLanguage.English
            ? $"Failed to save settings: {message}"
            : $"设置保存失败：{message}";

    /// <summary>设置加载失败提示。</summary>
    /// <param name="language">界面语言。</param>
    /// <param name="message">底层错误消息。</param>
    /// <returns>本地化提示。</returns>
    internal static string SettingsLoadFailed(DesktopLanguage language, string message) =>
        language == DesktopLanguage.English
            ? $"Failed to load settings: {message}"
            : $"设置加载失败：{message}";
}
