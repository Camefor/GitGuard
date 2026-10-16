using System.Diagnostics;
using GitGuard.Core;
using GitGuard.Core.Abstractions;
using GitGuard.Core.Model;
using GitGuard.Core.Policy;

namespace GitGuard.Application.Scanning;

/// <summary>
/// 扫描管线（SAD §7.1/§7.2）：差异获取 → 跳过判定 → 检测 → 策略判定 → ScanRun 聚合与单事务落库。
/// 承接两个扫描用例的共享编排与全部 FailClosed 分支（10-异常处理设计 §2 / §5、SAD §7.4）：
/// git 失败 → <c>Failed(10)</c>、检测器异常 → <c>Failed(11)</c>、扫描超时 → <c>Failed(12)</c>、
/// 审计写失败 → 判定不变、以 AuditWriteFailed 降级标记返回。
/// 失败记录同样落库（Failed ⇒ 退出码 ≥ 10 由 <see cref="ScanRun"/> 不变式强制）。
/// </summary>
internal sealed class ScanExecutor
{
    private const int EXIT_CODE_SUCCESS = 0;
    private const int EXIT_CODE_BLOCKED = 1;
    private const int EXIT_CODE_GIT_FAILED = 10;
    private const int EXIT_CODE_DETECTOR_FAILED = 11;
    private const int EXIT_CODE_TIMEOUT = 12;

    private readonly IGitAccess _gitAccess;
    private readonly IReadOnlyList<IDetector> _detectors;
    private readonly IAuditStore _auditStore;
    private readonly IClock _clock;
    private readonly ScanSettings _settings;
    private readonly ScanPolicy _policy;

    /// <summary>初始化扫描管线。</summary>
    /// <param name="gitAccess">Git 访问抽象。</param>
    /// <param name="detectors">检测器列表（调用顺序即 FR-021 固定顺序）。</param>
    /// <param name="auditStore">审计存储。</param>
    /// <param name="clock">时钟抽象。</param>
    /// <param name="settings">扫描设置（已合并默认值）。</param>
    /// <param name="policy">扫描级策略（映射 + 白名单）；<see langword="null"/> 使用 <see cref="ScanPolicy.Default"/>。</param>
    /// <exception cref="ArgumentNullException">任一引用参数为 <see langword="null"/>。</exception>
    /// <exception cref="ArgumentException"><paramref name="detectors"/> 为空集合（无检测器的扫描会无意义地放行全部内容）。</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="settings"/> 的阈值非正。</exception>
    internal ScanExecutor(
        IGitAccess gitAccess,
        IReadOnlyList<IDetector> detectors,
        IAuditStore auditStore,
        IClock clock,
        ScanSettings settings,
        ScanPolicy? policy = null)
    {
        ArgumentNullException.ThrowIfNull(gitAccess);
        ArgumentNullException.ThrowIfNull(detectors);
        ArgumentNullException.ThrowIfNull(auditStore);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(settings);
        if (detectors.Count == 0)
        {
            throw new ArgumentException("至少需要一个检测器；空集合会使扫描无意义地放行全部内容。", nameof(detectors));
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(settings.MaxFileBytes, nameof(settings));
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(settings.Timeout, TimeSpan.Zero, nameof(settings));

        _gitAccess = gitAccess;
        _detectors = detectors;
        _auditStore = auditStore;
        _clock = clock;
        _settings = settings;
        _policy = policy ?? ScanPolicy.Default;
    }

    /// <summary>
    /// 执行一次扫描；差异获取与检测共享扫描级超时窗口（<c>scan.timeout_ms</c>）。
    /// </summary>
    /// <param name="scanType">扫描类型（PreCommit / PrePush 等）。</param>
    /// <param name="acquireDiffFiles">差异获取委托：返回按提交归属的逐文件解析结果；git 失败抛 <see cref="GitException"/>。</param>
    /// <param name="cancellationToken">调用方取消令牌；外部取消原样上抛（CLI 映射退出码 12），超时则内部转 FailClosed。</param>
    /// <returns>扫描结果（成功与 FailClosed 均返回；不抛 GitException / DetectionException）。</returns>
    internal async Task<ScanOutcome> RunAsync(
        ScanType scanType,
        Func<CancellationToken, IReadOnlyList<ParsedDiffFile>> acquireDiffFiles,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(acquireDiffFiles);

        DateTimeOffset startedAt = _clock.UtcNow;
        var stopwatch = Stopwatch.StartNew();

        // 扫描级超时与调用方令牌链接：超时 → Failed(12)；git 子进程另有 30s 单命令超时（模块设计 Hook §4）兜底
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(_settings.Timeout);

        try
        {
            IReadOnlyList<ParsedDiffFile> parsedFiles = acquireDiffFiles(timeoutSource.Token);
            var changes = new List<FileChange>();
            var skippedFiles = new List<SkippedFile>();
            PartitionFiles(parsedFiles, changes, skippedFiles);

            var findings = new List<Finding>();
            foreach (FileChange change in changes)
            {
                foreach (IDetector detector in _detectors)
                {
                    await foreach (Finding finding in detector.DetectAsync(change, timeoutSource.Token).ConfigureAwait(false))
                    {
                        findings.Add(finding);
                    }
                }
            }

            return Complete(scanType, startedAt, stopwatch.ElapsedMilliseconds, changes.Count, findings, skippedFiles);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Fail(scanType, EXIT_CODE_TIMEOUT, startedAt, stopwatch.ElapsedMilliseconds);
        }
        catch (GitException)
        {
            return Fail(scanType, EXIT_CODE_GIT_FAILED, startedAt, stopwatch.ElapsedMilliseconds);
        }
        catch (DetectionException)
        {
            return Fail(scanType, EXIT_CODE_DETECTOR_FAILED, startedAt, stopwatch.ElapsedMilliseconds);
        }
    }

    /// <summary>跳过判定：二进制 → skipped(Binary)；新增内容超限 → skipped(TooLarge)；纯删除（无新增行）不产生扫描单元。</summary>
    private void PartitionFiles(IReadOnlyList<ParsedDiffFile> parsedFiles, List<FileChange> changes, List<SkippedFile> skippedFiles)
    {
        foreach (ParsedDiffFile file in parsedFiles)
        {
            if (file.IsBinary)
            {
                skippedFiles.Add(new SkippedFile(file.Path, SkipReason.Binary, file.CommitSha));
                continue;
            }

            if (file.AddedBytes > _settings.MaxFileBytes)
            {
                skippedFiles.Add(new SkippedFile(file.Path, SkipReason.TooLarge, file.CommitSha));
                continue;
            }

            if (file.Lines.Count == 0)
            {
                continue;
            }

            changes.Add(file.ToFileChange());
        }
    }

    /// <summary>成功路径收尾：策略判定 → 结果与退出码聚合（任一 Block → Blocked(1)）→ ScanRun 聚合与落库。</summary>
    private ScanOutcome Complete(
        ScanType scanType,
        DateTimeOffset startedAt,
        long durationMs,
        int filesScanned,
        List<Finding> findings,
        IReadOnlyList<SkippedFile> skippedFiles)
    {
        var decisions = PolicyEvaluator.Evaluate(findings, _policy.Mapping, _clock.UtcNow, _policy.Allowlist);

        bool blocked = decisions.Values.Any(static decision => decision.Action == PolicyAction.Block);
        ScanResult result = blocked
            ? ScanResult.Blocked
            : findings.Count > 0 ? ScanResult.Warned : ScanResult.Clean;
        int exitCode = blocked ? EXIT_CODE_BLOCKED : EXIT_CODE_SUCCESS;

        var run = ScanRun.Create(_gitAccess.GetRepositoryRoot(), scanType, startedAt, durationMs, result, exitCode, findings);
        bool auditWriteFailed = !TryPersistAudit(run, decisions);

        return new ScanOutcome(run, decisions, filesScanned, skippedFiles, auditWriteFailed);
    }

    /// <summary>FailClosed 路径收尾：以 Failed + 指定退出码（10 / 11 / 12）落库并返回（不携带 Findings）。</summary>
    private ScanOutcome Fail(ScanType scanType, int exitCode, DateTimeOffset startedAt, long durationMs)
    {
        var decisions = PolicyEvaluator.Evaluate(
            Array.Empty<Finding>(), _policy.Mapping, _clock.UtcNow, _policy.Allowlist);

        var run = ScanRun.Create(_gitAccess.GetRepositoryRoot(), scanType, startedAt, durationMs, ScanResult.Failed, exitCode);
        bool auditWriteFailed = !TryPersistAudit(run, decisions);

        return new ScanOutcome(run, decisions, filesScanned: 0, Array.Empty<SkippedFile>(), auditWriteFailed);
    }

    /// <summary>单事务落库；失败按 AuditWriteFailed 降级（10-异常处理设计 §5 / Audit.md §4），不改变扫描判定。</summary>
    private bool TryPersistAudit(ScanRun run, IReadOnlyDictionary<Finding, PolicyDecision> decisions)
    {
        try
        {
            _auditStore.SaveScanRun(run, decisions);
            return true;
        }
        catch (SqliteException)
        {
            return false;
        }
    }
}
