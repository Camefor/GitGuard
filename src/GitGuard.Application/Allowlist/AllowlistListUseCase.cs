using GitGuard.Core;
using GitGuard.Core.Abstractions;
using GitGuard.Core.Model;

namespace GitGuard.Application.Allowlist;

/// <summary>
/// 白名单列表用例（<c>06-API接口设计</c> §5.5 / FR-040）：按分页条件读取本地审计库中的白名单条目。
/// 只读：不写审计、不触发配置加载与检测器装配。
/// </summary>
public sealed class AllowlistListUseCase
{
    private readonly IAllowlistStore _store;

    /// <summary>初始化白名单列表用例。</summary>
    /// <param name="store">白名单存储抽象。</param>
    /// <exception cref="ArgumentNullException"><paramref name="store"/> 为 <see langword="null"/>。</exception>
    public AllowlistListUseCase(IAllowlistStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        _store = store;
    }

    /// <summary>执行分页查询。</summary>
    /// <param name="query">分页条件。</param>
    /// <returns>当前页条目与总数。</returns>
    /// <exception cref="ArgumentNullException"><paramref name="query"/> 为 <see langword="null"/>。</exception>
    /// <exception cref="SqliteException">读取失败（CLI 映射退出码 14）。</exception>
    public AllowlistPage Execute(AllowlistQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        return _store.QueryAllowlistEntries(query);
    }
}
