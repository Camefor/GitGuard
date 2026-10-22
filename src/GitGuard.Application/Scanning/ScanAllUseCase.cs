using GitGuard.Core.Abstractions;
using GitGuard.Core.Model;

namespace GitGuard.Application.Scanning;

/// <summary>
/// 全量扫描用例（FR-060 / SAD §7.1）：默认扫描 HEAD 下全部被跟踪文件
/// （以空树与 HEAD 的差异作为差异唯一入口，不读取工作区，沿用 DR-04 约束）；
/// 历史模式按 <c>git rev-list</c> 从 HEAD 向更早提交枚举，逐批取回提交差异并扫描。
/// <c>--max-files</c> 为按提交边界生效的软上限，截断时通过 <see cref="FullScanSummary.NextSinceSha"/>
/// 交给下一次 <c>--since</c> 继续扫描更早历史（<c>--since</c> 不含该提交本身）。
/// 退出码与 <see cref="ScanStagedUseCase"/> 一致（0 / 1 / 10 / 11 / 12）。
/// </summary>
public sealed class ScanAllUseCase
{
    /// <summary>历史扫描每次批量取差异的提交数（降低子进程开销，同时限制单批内存）。</summary>
    private const int HISTORY_BATCH_SIZE = 20;

    private readonly IGitAccess _gitAccess;
    private readonly ScanExecutor _executor;

    /// <summary>初始化全量扫描用例。</summary>
    /// <param name="gitAccess">Git 访问抽象（构造时已指向目标仓库）。</param>
    /// <param name="detectors">检测器列表；顺序即 FR-021 固定顺序，装配由组合根负责。</param>
    /// <param name="auditStore">审计存储。</param>
    /// <param name="clock">时钟抽象。</param>
    /// <param name="settings">扫描设置；<see langword="null"/> 使用 <see cref="ScanSettings.Default"/>。</param>
    /// <param name="policy">扫描级策略（映射 + 白名单）；<see langword="null"/> 使用 <see cref="ScanPolicy.Default"/>。</param>
    /// <param name="collectRuleTimings">是否采集规则级耗时（NFR-M-002）；<see langword="false"/> 时无计时开销。</param>
    /// <exception cref="ArgumentNullException">任一引用参数为 <see langword="null"/>。</exception>
    /// <exception cref="ArgumentException"><paramref name="detectors"/> 为空集合。</exception>
    public ScanAllUseCase(
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

    /// <summary>执行全量扫描。</summary>
    /// <param name="options">全量扫描选项；<see cref="ScanAllOptions.Since"/> 非空或 <see cref="ScanAllOptions.MaxFiles"/> 非空时自动启用历史模式。</param>
    /// <param name="cancellationToken">调用方取消令牌；外部取消原样上抛（CLI 映射退出码 12）。</param>
    /// <returns>扫描结果（含 ScanRun 聚合、历史扫描摘要与规则耗时）。</returns>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> 为 <see langword="null"/>。</exception>
    /// <exception cref="ArgumentException"><see cref="ScanAllOptions.Since"/> 为空、全空白或以 <c>-</c> 开头。</exception>
    /// <exception cref="ArgumentOutOfRangeException"><see cref="ScanAllOptions.MaxFiles"/> 不为正数。</exception>
    public Task<ScanOutcome> ExecuteAsync(ScanAllOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        ValidateOptions(options);

        return _executor.RunAsync(ScanType.Manual, _ => AcquireInput(options), cancellationToken);
    }

    /// <summary>校验选项：历史起点必须是合法修订标识，文件数上限必须为正数。</summary>
    private static void ValidateOptions(ScanAllOptions options)
    {
        if (options.Since is { } since)
        {
            if (string.IsNullOrWhiteSpace(since))
            {
                throw new ArgumentException("--since 不能为空或全空白。", nameof(options));
            }

            if (since.StartsWith('-'))
            {
                throw new ArgumentException("--since 不得以 '-' 开头。", nameof(options));
            }
        }

        if (options.MaxFiles is { } maxFiles)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxFiles, nameof(options));
        }
    }

    /// <summary>
    /// 获取 HEAD 全量树差异与（可选）历史提交差异，并生成历史扫描摘要。
    /// 历史模式启用条件：显式 <c>--include-history</c>，或提供了 <c>--since</c> / <c>--max-files</c>。
    /// </summary>
    private ScanInput AcquireInput(ScanAllOptions options)
    {
        var files = new List<ParsedDiffFile>();
        bool includeHistory = options.IncludeHistory || options.Since is not null || options.MaxFiles is not null;

        // HEAD 无提交时没有可扫描的跟踪文件；历史同样为空，按 Clean 处理而非 Git 失败
        bool hasHead = _gitAccess.TryGetHeadCommitSha() is not null;
        if (hasHead)
        {
            files.AddRange(UnifiedDiffParser.Parse(_gitAccess.GetFullTreeDiff("HEAD")));
        }

        if (!includeHistory)
        {
            return new ScanInput(files, new FullScanSummary(includeHistory: false, 0, 0, truncated: false, nextSinceSha: null));
        }

        // 空仓库没有历史可枚举：跳过 rev-list，避免把「无提交」误判为 git 失败
        if (!hasHead)
        {
            return new ScanInput(files, new FullScanSummary(includeHistory: true, 0, 0, truncated: false, nextSinceSha: null));
        }

        // --since 表示「从该提交的父提交开始」：git rev-list <since> 的首项是 since 自身，需跳过；
        // 未指定时从 HEAD 开始，同样跳过 HEAD（其完整快照已由全量树差异覆盖）。
        IReadOnlyList<string> enumerated = _gitAccess.ListCommitShas(options.Since ?? "HEAD");
        var historyShas = new List<string>(Math.Max(0, enumerated.Count - 1));
        for (int index = 1; index < enumerated.Count; index++)
        {
            historyShas.Add(enumerated[index]);
        }

        int commitsScanned = 0;
        int historyFileCount = 0;
        string? lastScannedSha = null;
        bool truncated = false;

        for (int offset = 0; offset < historyShas.Count && !truncated; offset += HISTORY_BATCH_SIZE)
        {
            int batchCount = Math.Min(HISTORY_BATCH_SIZE, historyShas.Count - offset);
            IReadOnlyList<CommitDiff> commitDiffs = _gitAccess.GetCommitDiffs(historyShas.GetRange(offset, batchCount));

            foreach (CommitDiff commitDiff in commitDiffs)
            {
                // 软上限按提交边界生效：达到上限后不再开始新的提交，当前提交允许超过上限
                if (options.MaxFiles is { } maxFiles && historyFileCount >= maxFiles)
                {
                    truncated = true;
                    break;
                }

                IReadOnlyList<ParsedDiffFile> parsed = UnifiedDiffParser.Parse(commitDiff.DiffText, commitDiff.Sha);
                files.AddRange(parsed);
                historyFileCount += parsed.Count;
                commitsScanned++;
                lastScannedSha = commitDiff.Sha;
            }
        }

        var summary = new FullScanSummary(
            includeHistory: true,
            commitsScanned,
            remainingCommits: historyShas.Count - commitsScanned,
            truncated,
            truncated ? lastScannedSha : null);
        return new ScanInput(files, summary);
    }
}
