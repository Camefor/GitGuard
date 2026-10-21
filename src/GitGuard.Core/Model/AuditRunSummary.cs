using GitGuard.Core.Domain;

namespace GitGuard.Core.Model;

/// <summary>
/// 审计运行摘要（<c>06-API接口设计</c> §5.6 / FR-023）：<c>gitguard audit list</c> 的单行查询结果，
/// 与 <c>scan_runs</c> 行一一对应，但不加载 Finding 明细。
/// </summary>
public sealed record AuditRunSummary
{
    /// <summary>
    /// 创建审计运行摘要。
    /// </summary>
    /// <param name="runId">运行身份标识（<c>scan_runs.id</c>）。</param>
    /// <param name="repoPath">仓库绝对路径。</param>
    /// <param name="scanType">扫描类型。</param>
    /// <param name="startedAt">开始时间（UTC）。</param>
    /// <param name="durationMs">耗时（毫秒）；未记录时为 <see langword="null"/>。</param>
    /// <param name="result">扫描结果。</param>
    /// <param name="exitCode">CLI 退出码。</param>
    /// <param name="findingCount">该运行记录的 Finding 数量（≥ 0）。</param>
    /// <exception cref="InvariantViolationException">任一字段越界或未定义时抛出。</exception>
    public AuditRunSummary(
        Guid runId,
        string repoPath,
        ScanType scanType,
        DateTimeOffset startedAt,
        long? durationMs,
        ScanResult result,
        int exitCode,
        int findingCount)
    {
        DomainGuard.Require(runId != Guid.Empty, "DDD §3.1", "审计运行标识不能为空");
        DomainGuard.Require(!string.IsNullOrWhiteSpace(repoPath), "DDD §4.1", "RepoPath 不能为空");
        DomainGuard.Require(Path.IsPathRooted(repoPath), "05-数据库设计 §4.1", $"RepoPath 必须是绝对路径：{repoPath}");
        DomainGuard.Require(Enum.IsDefined(scanType), "DDD §4.2", $"未知的 ScanType 取值：{(int)scanType}");
        DomainGuard.Require(startedAt.Offset == TimeSpan.Zero, "13-开发规范 §4.1", $"StartedAt 必须以 UTC 记录（当前偏移 {startedAt.Offset}）");
        DomainGuard.Require(durationMs is null or >= 0, "DDD §4.1", $"DurationMs 不能为负（当前 {durationMs}）");
        DomainGuard.Require(Enum.IsDefined(result), "DDD §4.2", $"未知的 ScanResult 取值：{(int)result}");
        DomainGuard.Require(findingCount >= 0, "06-API接口设计 §5.6", $"FindingCount 不能为负（当前 {findingCount}）");

        RunId = runId;
        RepoPath = Path.TrimEndingDirectorySeparator(repoPath);
        ScanType = scanType;
        StartedAt = startedAt;
        DurationMs = durationMs;
        Result = result;
        ExitCode = exitCode;
        FindingCount = findingCount;
    }

    /// <summary>运行身份标识（<c>scan_runs.id</c>）。</summary>
    public Guid RunId { get; }

    /// <summary>规范化后的仓库绝对路径。</summary>
    public string RepoPath { get; }

    /// <summary>扫描类型。</summary>
    public ScanType ScanType { get; }

    /// <summary>开始时间（UTC）。</summary>
    public DateTimeOffset StartedAt { get; }

    /// <summary>扫描耗时（毫秒）；未记录时为 <see langword="null"/>。</summary>
    public long? DurationMs { get; }

    /// <summary>扫描结果。</summary>
    public ScanResult Result { get; }

    /// <summary>CLI 退出码。</summary>
    public int ExitCode { get; }

    /// <summary>该运行记录的 Finding 数量。</summary>
    public int FindingCount { get; }
}
