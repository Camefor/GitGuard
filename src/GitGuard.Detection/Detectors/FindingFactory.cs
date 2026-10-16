using GitGuard.Core.Model;
using GitGuard.Core.Rules;
using GitGuard.Core.Services;

namespace GitGuard.Detection.Detectors;

/// <summary>
/// 发现项工厂：检测器命中后经 <see cref="Fingerprinter"/> 与 <see cref="Masker"/> 构造 <see cref="Finding"/>，
/// 保证 DR-02 约束——原文只在本调用栈内短暂存在，进入 <see cref="Finding"/> 的仅为指纹与脱敏片段。
/// </summary>
public sealed class FindingFactory
{
    private readonly Fingerprinter _fingerprinter;

    /// <summary>初始化发现项工厂。</summary>
    /// <param name="fingerprinter">注入本次扫描的指纹器（盐跨 run 稳定，07-缓存设计 §3）。</param>
    /// <exception cref="ArgumentNullException"><paramref name="fingerprinter"/> 为 <see langword="null"/>。</exception>
    public FindingFactory(Fingerprinter fingerprinter)
    {
        ArgumentNullException.ThrowIfNull(fingerprinter);
        _fingerprinter = fingerprinter;
    }

    /// <summary>构造发现项。</summary>
    /// <param name="rule">命中规则（提供规则 ID 与默认严重度）。</param>
    /// <param name="change">所属文件变更（提供路径与提交 SHA）。</param>
    /// <param name="lineNumber">命中所在行号（1-based）。</param>
    /// <param name="rawSecret">命中的敏感原文；本方法调用后调用方不得再保留。</param>
    /// <param name="severity">最终严重度（含降级结果）；未降级时取 <paramref name="rule"/> 的严重度。</param>
    /// <returns>通过 DR-02 校验的发现项。</returns>
    /// <exception cref="ArgumentNullException">任一引用参数为 <see langword="null"/>。</exception>
    public Finding Create(
        DetectionRule rule,
        FileChange change,
        int lineNumber,
        string rawSecret,
        Severity severity)
    {
        ArgumentNullException.ThrowIfNull(rule);
        ArgumentNullException.ThrowIfNull(change);
        ArgumentNullException.ThrowIfNull(rawSecret);

        return Finding.Create(
            rule.RuleId,
            change.Path,
            lineNumber,
            severity,
            _fingerprinter.Compute(rawSecret),
            Masker.Mask(rawSecret),
            change.CommitSha);
    }
}
