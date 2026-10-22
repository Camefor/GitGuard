using System.Collections.Immutable;
using GitGuard.Core.Domain;
using GitGuard.Core.Policy;

namespace GitGuard.Core.Model;

/// <summary>
/// 白名单分页结果（<c>06-API接口设计</c> §5.5）：当前页条目与总记录数。
/// </summary>
public sealed record AllowlistPage
{
    /// <summary>
    /// 创建分页结果。
    /// </summary>
    /// <param name="entries">当前页条目（可为空）。</param>
    /// <param name="totalCount">总记录数（≥ 当前页条数）。</param>
    /// <exception cref="ArgumentNullException"><paramref name="entries"/> 为 <see langword="null"/>。</exception>
    /// <exception cref="InvariantViolationException">总数小于当前页条数或含 <see langword="null"/> 元素时抛出。</exception>
    public AllowlistPage(IEnumerable<AllowlistEntry> entries, int totalCount)
    {
        ArgumentNullException.ThrowIfNull(entries);

        var items = entries.ToImmutableArray();
        DomainGuard.Require(!items.Any(static entry => entry is null), "06-API接口设计 §5.5", "Entries 不允许包含 null 元素");
        DomainGuard.Require(totalCount >= items.Length, "06-API接口设计 §5.5", $"TotalCount 不能小于当前页条数（{totalCount} < {items.Length}）");

        Entries = items;
        TotalCount = totalCount;
    }

    /// <summary>当前页条目（按登记时间倒序）。</summary>
    public ImmutableArray<AllowlistEntry> Entries { get; }

    /// <summary>总记录数。</summary>
    public int TotalCount { get; }
}
