using GitGuard.Core.Model;

namespace GitGuard.Core.Abstractions;

/// <summary>
/// 本机仓库注册表抽象（<c>05-数据库设计</c> §4.8 / GGD-0042）：桌面端登记、移除与列出仓库。
/// MVP 实现为 <c>GitGuard.Infrastructure.Sqlite.SqliteAuditStore</c>（与审计数据共享 <c>audit.db</c>）。
/// 登记表仅保存展示入口，不级联删除扫描历史、白名单或 Hook 记录。
/// </summary>
public interface IRepositoryStore
{
    /// <summary>
    /// 新增一条仓库登记；同一路径（<see cref="RegisteredRepository.RepoPath"/>）已存在时不写入。
    /// </summary>
    /// <param name="repository">待登记的仓库。</param>
    /// <returns>写入成功返回 <see langword="true"/>；路径已登记返回 <see langword="false"/>。</returns>
    /// <exception cref="ArgumentNullException"><paramref name="repository"/> 为 <see langword="null"/>。</exception>
    /// <exception cref="SqliteException">写入失败（含 BUSY 重试耗尽）。</exception>
    bool TryAdd(RegisteredRepository repository);

    /// <summary>按身份标识移除仓库登记（不触碰仓库文件、扫描历史与 Hook）。</summary>
    /// <param name="id">登记身份标识。</param>
    /// <returns>删除成功返回 <see langword="true"/>；条目不存在返回 <see langword="false"/>。</returns>
    /// <exception cref="SqliteException">删除失败（含 BUSY 重试耗尽）。</exception>
    bool Remove(Guid id);

    /// <summary>列出全部仓库登记（<c>added_at</c> 倒序，同一时间按展示名排序）。</summary>
    /// <returns>仓库登记列表（可能为空）。</returns>
    /// <exception cref="SqliteException">读取失败。</exception>
    IReadOnlyList<RegisteredRepository> ListAll();

    /// <summary>按规范化路径查找仓库登记。</summary>
    /// <param name="repoPath">仓库工作区根目录绝对路径。</param>
    /// <returns>登记记录；不存在返回 <see langword="null"/>。</returns>
    /// <exception cref="ArgumentException"><paramref name="repoPath"/> 为空或全空白。</exception>
    /// <exception cref="SqliteException">读取失败。</exception>
    RegisteredRepository? FindByPath(string repoPath);
}
