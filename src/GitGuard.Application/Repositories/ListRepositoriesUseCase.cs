using GitGuard.Core;
using GitGuard.Core.Abstractions;
using GitGuard.Core.Model;

namespace GitGuard.Application.Repositories;

/// <summary>
/// 仓库列表用例（GGD-0042）：读取全部本机仓库登记（<c>added_at</c> 倒序）。
/// 只读：不触发 Git 访问、不打开检测器。
/// </summary>
public sealed class ListRepositoriesUseCase
{
    private readonly IRepositoryStore _store;

    /// <summary>初始化仓库列表用例。</summary>
    /// <param name="store">仓库注册表抽象。</param>
    /// <exception cref="ArgumentNullException"><paramref name="store"/> 为 <see langword="null"/>。</exception>
    public ListRepositoriesUseCase(IRepositoryStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        _store = store;
    }

    /// <summary>读取全部仓库登记。</summary>
    /// <returns>倒序排列的登记列表（可能为空）。</returns>
    /// <exception cref="SqliteException">注册表读取失败。</exception>
    public IReadOnlyList<RegisteredRepository> Execute() => _store.ListAll();
}
