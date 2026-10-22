using GitGuard.Core.Model;
using GitGuard.Core.Policy;

namespace GitGuard.Core.Abstractions;

/// <summary>
/// 白名单持久化抽象（<c>05-数据库设计</c> §4.3 / <c>模块设计/Policy.md</c> §3）：
/// <c>allowlist</c> 表的增删查，以及扫描策略加载全部有效条目的读取入口。
/// 实现为 <c>GitGuard.Infrastructure.Sqlite.SqliteAuditStore</c>（与审计数据共享同一本地数据库）。
/// </summary>
public interface IAllowlistStore
{
    /// <summary>
    /// 新增一条白名单并写入 <c>AllowlistChanged</c> 审计事件（category = <c>Allowlist</c>），单事务提交。
    /// </summary>
    /// <param name="entry">通过领域校验的白名单条目。</param>
    /// <exception cref="ArgumentNullException"><paramref name="entry"/> 为 <see langword="null"/>。</exception>
    /// <exception cref="SqliteException">写入失败（含 BUSY 重试耗尽）；调用方按 FailClosed 处理。</exception>
    void AddAllowlistEntry(AllowlistEntry entry);

    /// <summary>
    /// 删除指定身份标识的白名单条目并写入 <c>AllowlistChanged</c> 审计事件，单事务提交。
    /// </summary>
    /// <param name="id">条目身份标识。</param>
    /// <returns>条目存在并删除时返回 <see langword="true"/>；不存在返回 <see langword="false"/>（不写事件）。</returns>
    /// <exception cref="SqliteException">读写失败（含 BUSY 重试耗尽）。</exception>
    bool RemoveAllowlistEntry(Guid id);

    /// <summary>按分页条件查询白名单条目（<c>created_at</c> 倒序）与总记录数。</summary>
    /// <param name="query">分页条件。</param>
    /// <returns>当前页条目与总数。</returns>
    /// <exception cref="ArgumentNullException"><paramref name="query"/> 为 <see langword="null"/>。</exception>
    /// <exception cref="SqliteException">读取失败。</exception>
    AllowlistPage QueryAllowlistEntries(AllowlistQuery query);

    /// <summary>读取全部白名单条目（扫描策略合并用；过期判定由 <see cref="AllowlistMatcher"/> 完成）。</summary>
    /// <returns>全部条目（可能为空）。</returns>
    /// <exception cref="SqliteException">读取失败；调用方按 FailClosed 处理。</exception>
    IReadOnlyList<AllowlistEntry> ListAllAllowlistEntries();
}
