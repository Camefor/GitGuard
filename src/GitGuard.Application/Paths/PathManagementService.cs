using GitGuard.Core.Abstractions;
using GitGuard.Core.Model;

namespace GitGuard.Application.Paths;

/// <summary>
/// CLI PATH 管理共享服务（GGD-0061）：Application 层对 <see cref="IPathManager"/> 的薄封装，
/// 使 CLI 无需引用 Infrastructure 即可完成用户级 PATH 的安装 / 卸载 / 状态查询。
/// </summary>
public sealed class PathManagementService
{
    private readonly IPathManager _pathManager;

    /// <summary>初始化 PATH 管理服务。</summary>
    /// <param name="pathManager">平台 PATH 管理器。</param>
    /// <exception cref="ArgumentNullException"><paramref name="pathManager"/> 为 <see langword="null"/>。</exception>
    public PathManagementService(IPathManager pathManager)
    {
        ArgumentNullException.ThrowIfNull(pathManager);
        _pathManager = pathManager;
    }

    /// <summary>把 GitGuard 可执行文件写入用户 PATH。</summary>
    /// <param name="executablePath">GitGuard 可执行文件绝对路径。</param>
    /// <param name="force">目标条目冲突时是否备份接管（Unix）。</param>
    /// <param name="updateProfile">是否同步写入 Shell profile 标记块（Unix）。</param>
    /// <returns>安装报告。</returns>
    public PathInstallReport Install(string executablePath, bool force = false, bool updateProfile = false) =>
        _pathManager.Install(executablePath, force, updateProfile);

    /// <summary>移除 GitGuard 托管的用户 PATH 条目。</summary>
    /// <param name="executablePath">当前 GitGuard 可执行文件绝对路径。</param>
    /// <returns>卸载报告。</returns>
    public PathUninstallReport Uninstall(string executablePath) =>
        _pathManager.Uninstall(executablePath);

    /// <summary>查询 GitGuard PATH 托管状态。</summary>
    /// <param name="executablePath">当前 GitGuard 可执行文件绝对路径。</param>
    /// <returns>状态快照。</returns>
    public PathStatusReport GetStatus(string executablePath) =>
        _pathManager.GetStatus(executablePath);
}
