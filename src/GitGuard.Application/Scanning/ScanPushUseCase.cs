using GitGuard.Core.Abstractions;
using GitGuard.Core.Model;

namespace GitGuard.Application.Scanning;

/// <summary>
/// 推送扫描用例（FR-030 / SAD §7.2）：解析 pre-push stdin 推送信息行 → 逐推送取版本区间 →
/// <c>git rev-list</c> 枚举提交（跨引用去重、保持 git 顺序）→ 逐提交 <c>git diff-tree -p -U0 --root --no-commit-id</c> 扫描。
/// 退出码与 <see cref="ScanStagedUseCase"/> 一致；stdin 解析失败按 FailClosed 计（→ 10）。
/// </summary>
public sealed class ScanPushUseCase
{
    private readonly IGitAccess _gitAccess;
    private readonly ScanExecutor _executor;

    /// <summary>初始化推送扫描用例。</summary>
    /// <param name="gitAccess">Git 访问抽象（构造时已指向目标仓库）。</param>
    /// <param name="detectors">检测器列表；顺序即 FR-021 固定顺序，装配由组合根负责。</param>
    /// <param name="auditStore">审计存储。</param>
    /// <param name="clock">时钟抽象。</param>
    /// <param name="settings">扫描设置；<see langword="null"/> 使用 <see cref="ScanSettings.Default"/>。</param>
    /// <param name="policy">扫描级策略（映射 + 白名单）；<see langword="null"/> 使用 <see cref="ScanPolicy.Default"/>。</param>
    /// <param name="collectRuleTimings">是否采集规则级耗时（NFR-M-002）；<see langword="false"/> 时无计时开销。</param>
    /// <exception cref="ArgumentNullException">任一引用参数为 <see langword="null"/>。</exception>
    /// <exception cref="ArgumentException"><paramref name="detectors"/> 为空集合。</exception>
    public ScanPushUseCase(
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

    /// <summary>执行推送扫描。</summary>
    /// <param name="pushStdin">pre-push stdin 全文（<c>&lt;local-ref&gt; &lt;local-sha&gt; &lt;remote-ref&gt; &lt;remote-sha&gt;</c> 行）。</param>
    /// <param name="cancellationToken">调用方取消令牌；外部取消原样上抛（CLI 映射退出码 12）。</param>
    /// <returns>扫描结果（PrePush 类型的 ScanRun；发现项携带所属提交 SHA；含规则耗时摘要）。</returns>
    /// <exception cref="ArgumentNullException"><paramref name="pushStdin"/> 为 <see langword="null"/>。</exception>
    public Task<ScanOutcome> ExecuteAsync(string pushStdin, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pushStdin);

        return _executor.RunAsync(ScanType.PrePush, _ => AcquireDiffFiles(pushStdin), cancellationToken);
    }

    /// <summary>获取全部待扫描提交的差异：枚举各推送区间 → 去重 → 逐提交解析（发现项经 FileChange 绑定提交 SHA）。</summary>
    private List<ParsedDiffFile> AcquireDiffFiles(string pushStdin)
    {
        IReadOnlyList<PushUpdate> updates = PushUpdate.ParseAll(pushStdin);

        var orderedShas = new List<string>();
        var seenShas = new HashSet<string>(StringComparer.Ordinal);
        foreach (PushUpdate update in updates)
        {
            if (update.IsDeletion)
            {
                continue;
            }

            foreach (string sha in _gitAccess.ListCommitShas(update.RevisionRange))
            {
                if (seenShas.Add(sha))
                {
                    orderedShas.Add(sha);
                }
            }
        }

        var files = new List<ParsedDiffFile>();
        foreach (string sha in orderedShas)
        {
            files.AddRange(UnifiedDiffParser.Parse(_gitAccess.GetCommitDiff(sha), sha));
        }

        return files;
    }
}
