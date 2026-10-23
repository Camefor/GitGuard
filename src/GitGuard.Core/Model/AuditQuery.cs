using GitGuard.Core.Domain;

namespace GitGuard.Core.Model;

/// <summary>
/// 审计查询条件（<c>06-API接口设计</c> §5.6 / FR-023）：<c>gitguard audit list</c> 的分页与筛选参数。
/// 时间条件按 UTC 记录；结果筛选只作用于 <see cref="ScanRun.Result"/>；可选仓库筛选供桌面端扫描历史使用。
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
    /// <param name="repoPath">
    /// 仓库绝对路径筛选；<see langword="null"/> 表示跨仓库不限制。尾部目录分隔符会被移除，
    /// 与 <c>scan_runs.repo_path</c> 的持久化形态一致。
    /// </param>
    /// <exception cref="InvariantViolationException">任一参数越界、未定义或仓库路径非法时抛出。</exception>
    public AuditQuery(
        int limit = DEFAULT_LIMIT,
        int offset = 0,
        DateTimeOffset? since = null,
        ScanResult? result = null,
        string? repoPath = null)
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
        DomainGuard.Require(
            repoPath is null || (!string.IsNullOrWhiteSpace(repoPath) && Path.IsPathFullyQualified(repoPath)),
            "06-API接口设计 §5.6",
            $"repoPath 必须为绝对路径：{repoPath}");

        Limit = limit;
        Offset = offset;
        Since = since;
        Result = result;
        RepoPath = repoPath is null ? null : Path.TrimEndingDirectorySeparator(repoPath);
    }

    /// <summary>每页条数（1..<see cref="MAX_LIMIT"/>）。</summary>
    public int Limit { get; }

    /// <summary>跳过的记录数（≥ 0）。</summary>
    public int Offset { get; }

    /// <summary>起始时间（含，UTC）；<see langword="null"/> 表示不限制。</summary>
    public DateTimeOffset? Since { get; }

    /// <summary>结果筛选；<see langword="null"/> 表示不限制。</summary>
    public ScanResult? Result { get; }

    /// <summary>仓库绝对路径筛选（去尾分隔符）；<see langword="null"/> 表示跨仓库不限制。</summary>
    public string? RepoPath { get; }
}
