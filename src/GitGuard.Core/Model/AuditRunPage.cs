using System.Collections.Immutable;
using GitGuard.Core.Domain;

namespace GitGuard.Core.Model;

/// <summary>
/// 审计查询分页结果（<c>06-API接口设计</c> §4.8 / §5.6）：当前页的运行摘要与筛选条件下的总记录数。
/// </summary>
public sealed record AuditRunPage
{
    /// <summary>
    /// 创建分页结果。
    /// </summary>
    /// <param name="runs">当前页的运行摘要（可为空）。</param>
    /// <param name="totalCount">筛选条件下的记录总数（≥ 当前页条数）。</param>
    /// <exception cref="ArgumentNullException"><paramref name="runs"/> 为 <see langword="null"/>。</exception>
    /// <exception cref="InvariantViolationException">总数小于当前页条数或含 <see langword="null"/> 元素时抛出。</exception>
    public AuditRunPage(IEnumerable<AuditRunSummary> runs, int totalCount)
    {
        ArgumentNullException.ThrowIfNull(runs);

        var items = runs.ToImmutableArray();
        DomainGuard.Require(!items.Any(static run => run is null), "06-API接口设计 §5.6", "Runs 不允许包含 null 元素");
        DomainGuard.Require(totalCount >= items.Length, "06-API接口设计 §5.6", $"TotalCount 不能小于当前页条数（{totalCount} < {items.Length}）");

        Runs = items;
        TotalCount = totalCount;
    }

    /// <summary>当前页的运行摘要（按开始时间倒序）。</summary>
    public ImmutableArray<AuditRunSummary> Runs { get; }

    /// <summary>筛选条件下的记录总数。</summary>
    public int TotalCount { get; }
}
