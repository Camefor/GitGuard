using GitGuard.Core;
using GitGuard.Core.Abstractions;
using GitGuard.Core.Model;

namespace GitGuard.Application.Repositories;

/// <summary>
/// 仓库最近扫描摘要用例（GGD-0042）：读取指定仓库最新一次扫描运行并转换为
/// <see cref="AuditRunSummary"/>（不含 Finding 明细，明细由 GGD-0043 加载）。
/// </summary>
public sealed class LatestRepositoryScanUseCase
{
    private const int LATEST_RUN_LIMIT = 1;

    private readonly IAuditStore _auditStore;

    /// <summary>初始化最近扫描摘要用例。</summary>
    /// <param name="auditStore">审计存储抽象。</param>
    /// <exception cref="ArgumentNullException"><paramref name="auditStore"/> 为 <see langword="null"/>。</exception>
    public LatestRepositoryScanUseCase(IAuditStore auditStore)
    {
        ArgumentNullException.ThrowIfNull(auditStore);
        _auditStore = auditStore;
    }

    /// <summary>读取指定仓库的最新扫描摘要。</summary>
    /// <param name="repoPath">规范化后的仓库绝对路径。</param>
    /// <returns>最新扫描摘要；该仓库尚无扫描记录时返回 <see langword="null"/>。</returns>
    /// <exception cref="ArgumentException"><paramref name="repoPath"/> 为空或全空白。</exception>
    /// <exception cref="SqliteException">审计库读取失败。</exception>
    public AuditRunSummary? Execute(string repoPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repoPath);

        IReadOnlyList<ScanRun> runs = _auditStore.ListRecentScanRuns(repoPath, LATEST_RUN_LIMIT);
        ScanRun? run = runs.Count == 0 ? null : runs[0];
        return run is null
            ? null
            : new AuditRunSummary(
                run.Id,
                run.RepoPath,
                run.ScanType,
                run.StartedAt,
                run.DurationMs,
                run.Result,
                run.ExitCode,
                run.Findings.Length);
    }
}
