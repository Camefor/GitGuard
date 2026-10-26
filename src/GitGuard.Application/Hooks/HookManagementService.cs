using GitGuard.Application.Configuration;
using GitGuard.Core.Model;

namespace GitGuard.Application.Hooks;

/// <summary>
/// Hook 管理共享服务（GGD-0054）：把「按配置选择 Hook 子集 + 解析 GitGuard CLI 路径 + 调用安装 / 卸载用例」
/// 收敛到 Application 层，供 CLI 与桌面端复用。CLI 保持显式 <c>--hook</c> 优先语义；
/// 桌面端不提供当前进程路径，避免把 GUI 可执行文件固化为 Hook 入口。
/// </summary>
public sealed class HookManagementService
{
    private readonly InstallHookUseCase _installUseCase;
    private readonly UninstallHookUseCase _uninstallUseCase;
    private readonly HookExecutableResolver _executableResolver;
    private readonly Func<HooksSettings> _settingsProvider;

    /// <summary>初始化共享 Hook 管理服务。</summary>
    /// <param name="installUseCase">安装用例。</param>
    /// <param name="uninstallUseCase">卸载用例。</param>
    /// <param name="executableResolver">GitGuard CLI 可执行文件解析器。</param>
    /// <param name="settingsProvider">
    /// 惰性读取已合并配置中的 <c>hooks</c> 设置；仅在安装路径解析时访问，卸载不要求配置可用。
    /// </param>
    /// <exception cref="ArgumentNullException">任一引用参数为 <see langword="null"/>。</exception>
    public HookManagementService(
        InstallHookUseCase installUseCase,
        UninstallHookUseCase uninstallUseCase,
        HookExecutableResolver executableResolver,
        Func<HooksSettings> settingsProvider)
    {
        ArgumentNullException.ThrowIfNull(installUseCase);
        ArgumentNullException.ThrowIfNull(uninstallUseCase);
        ArgumentNullException.ThrowIfNull(executableResolver);
        ArgumentNullException.ThrowIfNull(settingsProvider);

        _installUseCase = installUseCase;
        _uninstallUseCase = uninstallUseCase;
        _executableResolver = executableResolver;
        _settingsProvider = settingsProvider;
    }

    /// <summary>解析本次安装的目标 Hook：显式列表优先，否则按 <c>hooks.*</c> 选择。</summary>
    /// <param name="explicitHooks">显式 Hook 列表；<see langword="null"/> 表示按配置选择。</param>
    /// <returns>非空 Hook 列表。</returns>
    /// <exception cref="GitGuard.Core.ConfigurationException">配置禁用全部 Hook 且未显式指定。</exception>
    public IReadOnlyList<HookType> ResolveHooks(IReadOnlyList<HookType>? explicitHooks = null) =>
        HookSelection.Resolve(_settingsProvider(), explicitHooks);

    /// <summary>按统一优先级解析 GitGuard CLI 可执行文件。</summary>
    /// <param name="preferredPath">
    /// 宿主提供的优先路径（CLI 传当前进程路径）；桌面端传 <see langword="null"/> 以启用 PATH / 已知位置探测。
    /// </param>
    /// <returns>可执行文件绝对路径；未找到时为 <see langword="null"/>。</returns>
    /// <exception cref="GitGuard.Core.ConfigurationException">显式配置路径不存在。</exception>
    public string? ResolveExecutablePath(string? preferredPath = null) =>
        _executableResolver.Resolve(_settingsProvider().ExecutablePath, preferredPath);

    /// <summary>执行 Hook 安装。</summary>
    /// <param name="executablePath">固化到分发脚本的 GitGuard CLI 绝对路径。</param>
    /// <param name="force">是否允许接管二进制 / 符号链接 Hook。</param>
    /// <param name="hooks">显式 Hook 列表；<see langword="null"/> 表示按配置选择。</param>
    /// <returns>安装报告。</returns>
    public HookInstallReport Install(
        string executablePath,
        bool force = false,
        IReadOnlyList<HookType>? hooks = null) =>
        _installUseCase.Execute(executablePath, force, ResolveHooks(hooks));

    /// <summary>
    /// 执行 Hook 卸载。缺省卸载全部三类 Hook（而非仅配置启用的子集），确保配置变更后仍可清理历史安装。
    /// </summary>
    /// <param name="force">是否允许在存在 GitGuard 备份时接管已被手工替换的目标。</param>
    /// <param name="hooks">显式 Hook 列表；<see langword="null"/> 表示全部三类。</param>
    /// <returns>卸载报告。</returns>
    public HookUninstallReport Uninstall(bool force = false, IReadOnlyList<HookType>? hooks = null) =>
        _uninstallUseCase.Execute(force, hooks);
}
