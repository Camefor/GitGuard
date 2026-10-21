using GitGuard.Core;
using GitGuard.Core.Abstractions;
using GitGuard.Core.Model;

namespace GitGuard.Application.Bypass;

/// <summary>
/// 绕过检测服务（SRS FR-050 / DR-06）：post-commit 阶段（<see cref="Detect"/>）读取实际提交的树与首个父提交，
/// 与 pre-commit 阶段由 <see cref="PendingScanRecorder"/> 记录的预测进行比对。
/// <list type="bullet">
///   <item><description>匹配且未跳过：正常提交，消费标记，不写审计。</description></item>
///   <item><description>匹配且标记为跳过：<c>GITGUARD_SKIP=1</c> 绕过。</description></item>
///   <item><description>无标记或不匹配：提交未经过 GitGuard 扫描（<c>--no-verify</c>）。</description></item>
/// </list>
/// 绕过事件以 <see cref="ScanType.Bypass"/> 的 <see cref="ScanRun"/>（Clean / 退出码 0 / 无 Finding）登记，
/// 并追加 <c>BypassDetected</c> 审计事件（category = <c>Bypass</c>）。
/// </summary>
public sealed class BypassDetectionService
{
    private readonly IGitAccess _gitAccess;
    private readonly IAuditStore _auditStore;
    private readonly IPendingScanStore _pendingScanStore;
    private readonly IClock _clock;

    /// <summary>初始化绕过检测服务。</summary>
    /// <param name="gitAccess">Git 访问抽象（构造时已指向目标仓库）。</param>
    /// <param name="auditStore">审计存储（绕过事件落库）。</param>
    /// <param name="pendingScanStore">待提交标记存储（工作树级）。</param>
    /// <param name="clock">时钟抽象（事件时间戳）。</param>
    /// <exception cref="ArgumentNullException">任一引用参数为 <see langword="null"/>。</exception>
    public BypassDetectionService(
        IGitAccess gitAccess,
        IAuditStore auditStore,
        IPendingScanStore pendingScanStore,
        IClock clock)
    {
        ArgumentNullException.ThrowIfNull(gitAccess);
        ArgumentNullException.ThrowIfNull(auditStore);
        ArgumentNullException.ThrowIfNull(pendingScanStore);
        ArgumentNullException.ThrowIfNull(clock);

        _gitAccess = gitAccess;
        _auditStore = auditStore;
        _pendingScanStore = pendingScanStore;
        _clock = clock;
    }

    /// <summary>
    /// 在 post-commit 阶段检测刚发生的提交是否绕过扫描，并登记绕过事件。
    /// </summary>
    /// <returns>检测结果（是否绕过、原因、提交 SHA 与审计运行身份标识）。</returns>
    /// <exception cref="GitException">读取 HEAD 或提交对象失败（FailClosed，退出码 10）。</exception>
    /// <exception cref="SqliteException">绕过事件写入失败（FailClosed，退出码 14）。</exception>
    public BypassDetectionResult Detect()
    {
        string headSha = _gitAccess.GetHeadCommitSha();
        var metadata = _gitAccess.GetCommitMetadata(headSha);
        PendingScan? pendingScan = ReadPendingScanBestEffort();

        BypassReason? reason = null;
        if (pendingScan is not null && Matches(pendingScan, metadata))
        {
            reason = pendingScan.Skipped ? BypassReason.GitGuardSkip : null;
        }
        else
        {
            reason = BypassReason.NoVerify;
        }

        if (reason is null)
        {
            ClearPendingScanBestEffort();
            return new BypassDetectionResult(detected: false, reason: null, headSha, runId: null);
        }

        DateTimeOffset detectedAt = _clock.UtcNow;
        var run = ScanRun.Create(
            _gitAccess.GetRepositoryRoot(),
            ScanType.Bypass,
            detectedAt,
            durationMs: null,
            ScanResult.Clean,
            exitCode: 0);
        _auditStore.SaveBypassEvent(run, reason.Value, headSha);
        ClearPendingScanBestEffort();

        return new BypassDetectionResult(detected: true, reason, headSha, run.Id);
    }

    private static bool Matches(PendingScan pendingScan, CommitMetadata metadata)
    {
        if (!string.Equals(pendingScan.TreeSha, metadata.TreeSha, StringComparison.Ordinal))
        {
            return false;
        }

        return metadata.Parents.IsEmpty
            ? pendingScan.ParentSha is null
            : string.Equals(pendingScan.ParentSha, metadata.Parents[0], StringComparison.Ordinal);
    }

    // 标记读取失败或损坏时退化为「无标记」：宁可把提交登记为 --no-verify，也不漏记绕过（DR-06）
    private PendingScan? ReadPendingScanBestEffort()
    {
        try
        {
            return _pendingScanStore.Read();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private void ClearPendingScanBestEffort()
    {
        try
        {
            _pendingScanStore.Clear();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 标记清理失败不影响审计结论；残留标记会在下一次匹配失败时被覆盖或清除
        }
    }
}

/// <summary>
/// post-commit 绕过检测结果（供 CLI 渲染；正常提交时 <see cref="Detected"/> 为 <see langword="false"/>）。
/// </summary>
public sealed record BypassDetectionResult
{
    /// <summary>初始化检测结果。</summary>
    /// <param name="detected">是否检测到绕过。</param>
    /// <param name="reason">绕过原因；未检测到时为 <see langword="null"/>。</param>
    /// <param name="commitSha">被检测的提交 SHA。</param>
    /// <param name="runId">绕过事件对应的扫描运行身份标识；未检测到时为 <see langword="null"/>。</param>
    /// <exception cref="ArgumentException"><paramref name="commitSha"/> 为空或全空白。</exception>
    public BypassDetectionResult(bool detected, BypassReason? reason, string commitSha, Guid? runId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(commitSha);

        Detected = detected;
        Reason = reason;
        CommitSha = commitSha;
        RunId = runId;
    }

    /// <summary>是否检测到绕过。</summary>
    public bool Detected { get; }

    /// <summary>绕过原因；未检测到时为 <see langword="null"/>。</summary>
    public BypassReason? Reason { get; }

    /// <summary>被检测的提交 SHA。</summary>
    public string CommitSha { get; }

    /// <summary>绕过事件对应的扫描运行身份标识；未检测到时为 <see langword="null"/>。</summary>
    public Guid? RunId { get; }
}
