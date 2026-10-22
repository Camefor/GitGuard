using GitGuard.Core.Domain;

namespace GitGuard.Core.Model;

/// <summary>
/// 白名单查询条件（<c>06-API接口设计</c> §5.5 / FR-040）：<c>gitguard allowlist list</c> 的分页参数。
/// </summary>
public sealed record AllowlistQuery
{
    /// <summary>默认每页条数（与审计查询保持一致）。</summary>
    public const int DEFAULT_LIMIT = 20;

    /// <summary>单页条数上限（防止一次查询拉取过多记录）。</summary>
    public const int MAX_LIMIT = 1000;

    /// <summary>
    /// 创建白名单查询条件。
    /// </summary>
    /// <param name="limit">每页条数，取值 1..<see cref="MAX_LIMIT"/>。</param>
    /// <param name="offset">跳过条数，必须 ≥ 0。</param>
    /// <exception cref="InvariantViolationException">任一参数越界时抛出。</exception>
    public AllowlistQuery(int limit = DEFAULT_LIMIT, int offset = 0)
    {
        DomainGuard.Require(limit is >= 1 and <= MAX_LIMIT, "06-API接口设计 §5.5", $"limit 必须在 1..{MAX_LIMIT} 之间（当前 {limit}）");
        DomainGuard.Require(offset >= 0, "06-API接口设计 §5.5", $"offset 不能为负（当前 {offset}）");

        Limit = limit;
        Offset = offset;
    }

    /// <summary>每页条数（1..<see cref="MAX_LIMIT"/>）。</summary>
    public int Limit { get; }

    /// <summary>跳过的记录数（≥ 0）。</summary>
    public int Offset { get; }
}
