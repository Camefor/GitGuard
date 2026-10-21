using GitGuard.Core.Abstractions;
using GitGuard.Core.Model;

namespace GitGuard.Application.Scanning;

/// <summary>
/// 暂存区扫描用例（FR-020 / SAD §7.1）：差异唯一入口为 <see cref="IGitAccess.GetStagedDiff"/>
/// （<c>git diff --cached -U0</c>，DR-04 真实暂存区），不读取工作区文件、不启动除 git 外的子进程（DR-01）。
/// 退出码：Clean / Warned → 0，Blocked → 1，FailClosed → 10 / 11 / 12（SAD §7.4）。
/// </summary>
public sealed class ScanStagedUseCase
{
    private readonly IGitAccess _gitAccess;
    private readonly ScanExecutor _executor;

    /// <summary>初始化暂存区扫描用例。</summary>
    /// <param name="gitAccess">Git 访问抽象（构造时已指向目标仓库）。</param>
    /// <param name="detectors">检测器列表；顺序即 FR-021 固定顺序（Secret → Privacy → LocalPath → Url），装配由组合根负责。</param>
    /// <param name="auditStore">审计存储。</param>
    /// <param name="clock">时钟抽象。</param>
    /// <param name="settings">扫描设置；<see langword="null"/> 使用 <see cref="ScanSettings.Default"/>。</param>
    /// <param name="policy">扫描级策略（映射 + 白名单）；<see langword="null"/> 使用 <see cref="ScanPolicy.Default"/>。</param>
    /// <param name="collectRuleTimings">是否采集规则级耗时（NFR-M-002）；<see langword="false"/> 时无计时开销。</param>
    /// <exception cref="ArgumentNullException">任一引用参数为 <see langword="null"/>。</exception>
    /// <exception cref="ArgumentException"><paramref name="detectors"/> 为空集合。</exception>
    public ScanStagedUseCase(
        IGitAccess gitAccess,
        IReadOnlyList<IDetector> detectors,
        IAuditStore auditStore,
        IClock clock,
        ScanSettings? settings = null,
        ScanPolicy? policy = null,
        bool collectRuleTimings = false)
    {
        ArgumentNullException.ThrowIfNull(gitAccess);

        _gitAccess = gitAccess;
        _executor = new ScanExecutor(
            gitAccess, detectors, auditStore, clock, settings ?? ScanSettings.Default, policy, collectRuleTimings);
    }

    /// <summary>执行暂存区扫描。</summary>
    /// <param name="cancellationToken">调用方取消令牌；外部取消原样上抛（CLI 映射退出码 12）。</param>
    /// <returns>扫描结果（含 ScanRun 聚合、逐条策略判定、跳过文件清单、审计写失败标记与规则耗时摘要）。</returns>
    public Task<ScanOutcome> ExecuteAsync(CancellationToken cancellationToken = default) =>
        _executor.RunAsync(ScanType.PreCommit, _ => UnifiedDiffParser.Parse(_gitAccess.GetStagedDiff()), cancellationToken);
}
