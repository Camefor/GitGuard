using GitGuard.Core;
using GitGuard.Core.Abstractions;
using GitGuard.Core.Model;

namespace GitGuard.Application.Audit;

/// <summary>
/// 审计列表用例（<c>06-API接口设计</c> §5.6 / FR-023）：按分页与筛选条件读取本机审计库中的扫描运行摘要。
/// 只读：不写审计、不触发配置加载与检测器装配。查询范围为整个本地审计库（含所有仓库）。
/// </summary>
public sealed class AuditListUseCase
{
    private readonly IAuditStore _store;

    /// <summary>初始化审计列表用例。</summary>
    /// <param name="store">审计存储抽象。</param>
    /// <exception cref="ArgumentNullException"><paramref name="store"/> 为 <see langword="null"/>。</exception>
    public AuditListUseCase(IAuditStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        _store = store;
    }

    /// <summary>执行分页查询。</summary>
    /// <param name="query">分页与筛选条件。</param>
    /// <returns>当前页运行摘要与筛选条件下的总数。</returns>
    /// <exception cref="ArgumentNullException"><paramref name="query"/> 为 <see langword="null"/>。</exception>
    /// <exception cref="SqliteException">审计库读取失败（CLI 映射退出码 14）。</exception>
    public AuditRunPage Execute(AuditQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        return _store.QueryScanRuns(query);
    }
}
