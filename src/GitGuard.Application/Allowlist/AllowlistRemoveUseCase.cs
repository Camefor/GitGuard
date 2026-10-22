using GitGuard.Core;
using GitGuard.Core.Abstractions;

namespace GitGuard.Application.Allowlist;

/// <summary>
/// 白名单删除用例（<c>06-API接口设计</c> §5.5 / FR-040）：按身份标识删除条目，不存在时映射
/// <see cref="AllowlistException.ALLOWLIST_NOT_FOUND"/>（CLI 退出码 21）；删除成功由存储层登记
/// <c>AllowlistChanged</c> 审计事件。
/// </summary>
public sealed class AllowlistRemoveUseCase
{
    private readonly IAllowlistStore _store;

    /// <summary>初始化白名单删除用例。</summary>
    /// <param name="store">白名单存储抽象。</param>
    /// <exception cref="ArgumentNullException"><paramref name="store"/> 为 <see langword="null"/>。</exception>
    public AllowlistRemoveUseCase(IAllowlistStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        _store = store;
    }

    /// <summary>删除指定身份标识的白名单条目。</summary>
    /// <param name="id">条目身份标识。</param>
    /// <exception cref="AllowlistException">条目不存在（错误码 <see cref="AllowlistException.ALLOWLIST_NOT_FOUND"/>）。</exception>
    /// <exception cref="SqliteException">读写失败（CLI 映射退出码 14）。</exception>
    public void Execute(Guid id)
    {
        if (id == Guid.Empty || !_store.RemoveAllowlistEntry(id))
        {
            throw new AllowlistException(AllowlistException.ALLOWLIST_NOT_FOUND, $"白名单条目不存在：{id}");
        }
    }
}
