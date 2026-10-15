using System.Collections.Immutable;
using GitGuard.Core.Domain;

namespace GitGuard.Core.Model;

/// <summary>
/// 扫描运行聚合根（DDD §3.1）；一次扫描的完整记录，与 <c>scan_runs</c> 行一一对应。
/// 不变式（构造期强制）：
/// <list type="bullet">
///   <item><description><c>DR-03</c>：<see cref="ScanResult.Failed"/> 要求 <see cref="ExitCode"/> ≥ 10。</description></item>
///   <item><description><c>DDD §3.1</c>：<see cref="ScanResult.Clean"/> 与 <see cref="Findings"/> 非空相互矛盾；Blocked 必须至少有一条 Finding。</description></item>
///   <item><description><c>DDD §4.2</c>：<see cref="ScanType.Bypass"/> 的运行不允许携带 Finding。</description></item>
///   <item><description><c>SRS FR-020</c>：结果与退出码一致 —— Clean/Warned ⇒ 0，Blocked ⇒ 1，Failed ⇒ ≥ 10。</description></item>
/// </list>
/// </summary>
public sealed class ScanRun : IEquatable<ScanRun>
{
    private ScanRun(
        Guid id,
        string repoPath,
        ScanType scanType,
        DateTimeOffset startedAt,
        long? durationMs,
        ScanResult result,
        int exitCode,
        ImmutableArray<Finding> findings)
    {
        Id = id;
        RepoPath = repoPath;
        ScanType = scanType;
        StartedAt = startedAt;
        DurationMs = durationMs;
        Result = result;
        ExitCode = exitCode;
        Findings = findings;
    }

    /// <summary>运行身份标识（UUIDv7，与 <c>scan_runs.id</c> 对应）。</summary>
    public Guid Id { get; }

    /// <summary>规范化后的仓库绝对路径（去除尾部分隔符）。</summary>
    public string RepoPath { get; }

    /// <summary>扫描类型。</summary>
    public ScanType ScanType { get; }

    /// <summary>开始时间（UTC）。</summary>
    public DateTimeOffset StartedAt { get; }

    /// <summary>扫描耗时（毫秒）；运行未结束时为 <see langword="null"/>。</summary>
    public long? DurationMs { get; }

    /// <summary>扫描结果。</summary>
    public ScanResult Result { get; }

    /// <summary>CLI 退出码（<c>06-API接口设计</c> §6 稳定契约）。</summary>
    public int ExitCode { get; }

    /// <summary>
    /// 本次运行记录的发现项（<see cref="ScanResult.Clean"/> 时必为空）。
    /// 被 allowlist 忽略的 Finding 仍属于记录范围，其忽略动作由 <c>PolicyDecision</c> 在持久化层另行登记。
    /// </summary>
    public ImmutableArray<Finding> Findings { get; }

    /// <summary>
    /// 创建扫描运行记录；身份标识在构造时生成（UUIDv7），并执行全部领域不变式校验。
    /// </summary>
    /// <param name="repoPath">仓库绝对路径；尾部分隔符会被规范化去除。</param>
    /// <param name="scanType">扫描类型。</param>
    /// <param name="startedAt">开始时间（UTC）。</param>
    /// <param name="durationMs">耗时（毫秒），未完成时为 <see langword="null"/>。</param>
    /// <param name="result">扫描结果。</param>
    /// <param name="exitCode">CLI 退出码。</param>
    /// <param name="findings">发现项列表；可为 <see langword="null"/>（等价于空列表）。</param>
    /// <returns>通过全部不变式校验的扫描运行记录。</returns>
    /// <exception cref="ArgumentNullException"><paramref name="repoPath"/> 为 <see langword="null"/>。</exception>
    /// <exception cref="InvariantViolationException">任一领域不变式被违反时抛出。</exception>
    public static ScanRun Create(
        string repoPath,
        ScanType scanType,
        DateTimeOffset startedAt,
        long? durationMs,
        ScanResult result,
        int exitCode,
        IEnumerable<Finding>? findings = null)
    {
        ArgumentNullException.ThrowIfNull(repoPath);

        DomainGuard.Require(!string.IsNullOrWhiteSpace(repoPath), "DDD §4.1", "RepoPath 不能为空");
        DomainGuard.Require(Path.IsPathRooted(repoPath), "05-数据库设计 §4.1", $"RepoPath 必须是绝对路径：{repoPath}");
        DomainGuard.Require(Enum.IsDefined(scanType), "DDD §4.2", $"未知的 ScanType 取值：{(int)scanType}");
        DomainGuard.Require(startedAt.Offset == TimeSpan.Zero, "13-开发规范 §4.1", $"StartedAt 必须以 UTC 记录（当前偏移 {startedAt.Offset}）");
        DomainGuard.Require(durationMs is null or >= 0, "DDD §4.1", $"DurationMs 不能为负（当前 {durationMs}）");

        ImmutableArray<Finding> items = findings?.ToImmutableArray() ?? ImmutableArray<Finding>.Empty;
        DomainGuard.Require(!items.Any(static f => f is null), "DDD §3.1", "Findings 不允许包含 null 元素");

        switch (result)
        {
            case ScanResult.Clean:
                DomainGuard.Require(items.IsEmpty, "DDD §3.1", "Findings 非空与 Result=Clean 相互矛盾");
                DomainGuard.Require(exitCode == 0, "SRS FR-020", $"Result=Clean 要求退出码为 0（当前 {exitCode}）");
                break;

            case ScanResult.Warned:
                DomainGuard.Require(exitCode == 0, "SRS FR-020", $"Result=Warned 要求退出码为 0（当前 {exitCode}）");
                break;

            case ScanResult.Blocked:
                DomainGuard.Require(!items.IsEmpty, "DDD §3.1", "Result=Blocked 必须至少包含一条 Finding");
                DomainGuard.Require(exitCode == 1, "SRS FR-020", $"Result=Blocked 要求退出码为 1（当前 {exitCode}）");
                break;

            case ScanResult.Failed:
                DomainGuard.Require(exitCode >= 10, "DR-03", $"Result=Failed 要求退出码 ≥ 10（当前 {exitCode}），异常路径禁止以成功码退出");
                break;

            default:
                DomainGuard.Require(false, "DDD §4.2", $"未知的 ScanResult 取值：{(int)result}");
                break;
        }

        DomainGuard.Require(scanType != ScanType.Bypass || items.IsEmpty, "DDD §4.2", "Bypass 类型的 ScanRun 不允许携带 Finding");

        string normalizedRepoPath = Path.TrimEndingDirectorySeparator(repoPath);

        return new ScanRun(Guid.CreateVersion7(), normalizedRepoPath, scanType, startedAt, durationMs, result, exitCode, items);
    }

    /// <summary>按身份标识（<see cref="Id"/>）判断实体相等性。</summary>
    /// <param name="other">另一次扫描运行。</param>
    /// <returns>身份标识相同返回 <see langword="true"/>。</returns>
    public bool Equals(ScanRun? other) =>
        other is not null && (ReferenceEquals(this, other) || Id == other.Id);

    /// <summary>按身份标识（<see cref="Id"/>）判断实体相等性。</summary>
    /// <param name="obj">比较对象。</param>
    /// <returns>为同一身份的 <see cref="ScanRun"/> 时返回 <see langword="true"/>。</returns>
    public override bool Equals(object? obj) => Equals(obj as ScanRun);

    /// <summary>返回身份标识的哈希值。</summary>
    /// <returns><see cref="Id"/> 的哈希值。</returns>
    public override int GetHashCode() => Id.GetHashCode();
}
