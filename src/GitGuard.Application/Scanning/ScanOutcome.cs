using System.Collections.Immutable;
using GitGuard.Core.Model;
using GitGuard.Core.Policy;

namespace GitGuard.Application.Scanning;

/// <summary>
/// 一次扫描的完整结果（SAD §7.1 <c>Result{ExitCode, Report}</c> 的落地形态），供 CLI 渲染报告与映射退出码；
/// 聚合本身（<see cref="ScanRun"/>）已按 Audit.md §3 单事务落库（写失败时 <see cref="AuditWriteFailed"/> 为
/// <see langword="true"/>，按 AuditWriteFailed 降级，不改变扫描判定）。
/// </summary>
public sealed class ScanOutcome
{
    /// <summary>初始化扫描结果。</summary>
    /// <param name="run">扫描运行聚合（含结果 / 退出码 / 发现项）。</param>
    /// <param name="decisions">每条发现项的策略判定（与 <c>findings.action</c> 落库一致）。</param>
    /// <param name="filesScanned">实际交付检测的文件数（不含跳过）。</param>
    /// <param name="skippedFiles">被跳过文件清单（二进制 / 超限）。</param>
    /// <param name="auditWriteFailed">审计写失败降级标记（Audit.md §4）。</param>
    /// <exception cref="ArgumentNullException">任一引用参数为 <see langword="null"/>。</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="filesScanned"/> 为负。</exception>
    public ScanOutcome(
        ScanRun run,
        IReadOnlyDictionary<Finding, PolicyDecision> decisions,
        int filesScanned,
        IReadOnlyList<SkippedFile> skippedFiles,
        bool auditWriteFailed)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(decisions);
        ArgumentNullException.ThrowIfNull(skippedFiles);
        ArgumentOutOfRangeException.ThrowIfNegative(filesScanned, nameof(filesScanned));

        Run = run;
        Decisions = decisions;
        FilesScanned = filesScanned;
        SkippedFiles = skippedFiles.ToImmutableArray();
        AuditWriteFailed = auditWriteFailed;
    }

    /// <summary>扫描运行聚合（<c>Id</c> 即报告中的 <c>run_id</c>）。</summary>
    public ScanRun Run { get; }

    /// <summary>扫描结果（Clean / Warned / Blocked / Failed）。</summary>
    public ScanResult Result => Run.Result;

    /// <summary>CLI 退出码（<c>06-API接口设计</c> §6）。</summary>
    public int ExitCode => Run.ExitCode;

    /// <summary>本次运行的发现项（Clean 时为空）。</summary>
    public ImmutableArray<Finding> Findings => Run.Findings;

    /// <summary>每条发现项的策略判定。</summary>
    public IReadOnlyDictionary<Finding, PolicyDecision> Decisions { get; }

    /// <summary>实际交付检测的文件数（不含跳过）。</summary>
    public int FilesScanned { get; }

    /// <summary>被跳过文件清单（二进制 / 超限）。</summary>
    public ImmutableArray<SkippedFile> SkippedFiles { get; }

    /// <summary>审计写失败降级标记（<see langword="true"/> 时报告应提示「审计写入失败」）。</summary>
    public bool AuditWriteFailed { get; }
}
