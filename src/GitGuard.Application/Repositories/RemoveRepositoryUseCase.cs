using GitGuard.Core;
using GitGuard.Core.Abstractions;

namespace GitGuard.Application.Repositories;

/// <summary>
/// 仓库移除用例（GGD-0042）：仅删除本机仓库注册表中的展示入口，不删除仓库文件、扫描历史、白名单或 Hook 记录；
/// 二次确认由桌面端负责（<c>11-前端UI设计</c> §5）。
/// </summary>
public sealed class RemoveRepositoryUseCase
{
    private readonly IRepositoryStore _store;

    /// <summary>初始化仓库移除用例。</summary>
    /// <param name="store">仓库注册表抽象。</param>
    /// <exception cref="ArgumentNullException"><paramref name="store"/> 为 <see langword="null"/>。</exception>
    public RemoveRepositoryUseCase(IRepositoryStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        _store = store;
    }

    /// <summary>移除仓库登记。</summary>
    /// <param name="id">登记身份标识。</param>
    /// <exception cref="RepositoryException">条目不存在（<see cref="RepositoryException.REPOSITORY_NOT_FOUND"/>）。</exception>
    /// <exception cref="SqliteException">删除失败。</exception>
    public void Execute(Guid id)
    {
        if (id == Guid.Empty || !_store.Remove(id))
        {
            throw new RepositoryException(
                RepositoryException.REPOSITORY_NOT_FOUND,
                $"仓库登记不存在：{id}");
        }
    }
}
