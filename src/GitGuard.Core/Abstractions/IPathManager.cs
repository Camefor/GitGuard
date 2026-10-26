using GitGuard.Core.Model;

namespace GitGuard.Core.Abstractions;

/// <summary>
/// 用户级 PATH 管理抽象（原始需求 3）：把 GitGuard CLI 写入当前用户的 PATH，并提供可逆的查询 / 卸载。
/// 实现必须只触碰用户级环境（Windows <c>HKCU\Environment</c> / Unix <c>~/.local/bin</c> 与用户 Shell profile），
/// 不修改系统级 PATH；测试通过注入假实现验证 CLI，禁止在测试中修改真实 PATH。
/// </summary>
public interface IPathManager
{
    /// <summary>把 GitGuard 可执行文件所在目录加入用户 PATH（Unix 同时创建 <c>~/.local/bin/gitguard</c> 符号链接）。</summary>
    /// <param name="executablePath">GitGuard 可执行文件绝对路径。</param>
    /// <param name="force">目标条目已存在且非 GitGuard 托管时是否允许备份接管。</param>
    /// <param name="updateProfile">Unix 上是否同步写入 Shell profile 标记块（Windows 忽略）。</param>
    /// <returns>安装报告。</returns>
    /// <exception cref="PathException">写入权限不足 / 条目冲突 / 平台不支持 / PATH 超长。</exception>
    PathInstallReport Install(string executablePath, bool force, bool updateProfile);

    /// <summary>移除 GitGuard 托管的用户 PATH 条目、Unix 符号链接与 Shell profile 标记块；其他条目保持不变。</summary>
    /// <param name="executablePath">当前 GitGuard 可执行文件绝对路径（状态缺失时的回退匹配依据）。</param>
    /// <returns>卸载报告。</returns>
    /// <exception cref="PathException">写入权限不足 / 平台不支持。</exception>
    PathUninstallReport Uninstall(string executablePath);

    /// <summary>查询 GitGuard PATH 托管状态（只读，不修改环境）。</summary>
    /// <param name="executablePath">当前 GitGuard 可执行文件绝对路径（状态缺失时的回退匹配依据）。</param>
    /// <returns>状态快照。</returns>
    /// <exception cref="PathException">平台不支持。</exception>
    PathStatusReport GetStatus(string executablePath);
}
