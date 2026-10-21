using GitGuard.Core.Domain;

namespace GitGuard.Core.Model;

/// <summary>
/// 审计查询条件（<c>06-API接口设计</c> §5.6 / FR-023）：<c>gitguard audit list</c> 的分页与筛选参数。
/// 时间条件按 UTC 记录；结果筛选只作用于 <see cref="ScanRun.Result"/>。
/// </summary>
public sealed record AuditQuery
{
    /// <summary>默认每页条数（<c>06-API接口设计</c> §5.6）。</summary>
    public const int DEFAULT_LIMIT = 20;

    /// <summary>单页条数上限（防止一次查询拉取过多记录）。</summary>
    public const int MAX_LIMIT = 1000;

    /// <summary>
    /// 创建审计查询条件。
    /// </summary>
    /// <param name="limit">每页条数，取值 1..<see cref="MAX_LIMIT"/>。</param>
    /// <param name="offset">跳过条数，必须 ≥ 0。</param>
    /// <param name="since">起始时间（含）；<see langword="null"/> 表示不限制。必须为 UTC。</param>
    /// <param name="result">结果筛选；<see langword="null"/> 表示不限制。</param>
    /// <exception cref="InvariantViolationException">任一参数越界或未定义时抛出。</exception>
    public AuditQuery(int limit = DEFAULT_LIMIT, int offset = 0, DateTimeOffset? since = null, ScanResult? result = null)
    {
        DomainGuard.Require(limit is >= 1 and <= MAX_LIMIT, "06-API接口设计 §5.6", $"limit 必须在 1..{MAX_LIMIT} 之间（当前 {limit}）");
        DomainGuard.Require(offset >= 0, "06-API接口设计 §5.6", $"offset 不能为负（当前 {offset}）");
        DomainGuard.Require(
            since is null || since.Value.Offset == TimeSpan.Zero,
            "13-开发规范 §4.1",
            $"Since 必须以 UTC 记录（当前偏移 {since?.Offset}）");
        DomainGuard.Require(
            result is null || Enum.IsDefined(result.Value),
            "DDD §4.2",
            $"未知的 ScanResult 取值：{(int?)result}");

        Limit = limit;
        Offset = offset;
        Since = since;
        Result = result;
    }

    /// <summary>每页条数（1..<see cref="MAX_LIMIT"/>）。</summary>
    public int Limit { get; }

    /// <summary>跳过的记录数（≥ 0）。</summary>
    public int Offset { get; }

    /// <summary>起始时间（含，UTC）；<see langword="null"/> 表示不限制。</summary>
    public DateTimeOffset? Since { get; }

    /// <summary>结果筛选；<see langword="null"/> 表示不限制。</summary>
    public ScanResult? Result { get; }
}
