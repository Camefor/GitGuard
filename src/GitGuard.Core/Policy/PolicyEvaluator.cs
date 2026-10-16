using System.Collections.Immutable;
using GitGuard.Core.Model;

namespace GitGuard.Core.Policy;

/// <summary>
/// 策略评估器（DDD §6 领域服务；<c>模块设计/Policy.md</c> §6.1）：把单条 Finding 的严重度映射为动作，
/// 并在白名单命中时整体放行。纯函数实现，无 IO；任一 <see cref="PolicyAction.Block"/> 由调用方
/// （Application 扫描管线）聚合为整次扫描退出码 1（FR-022）。
/// </summary>
public static class PolicyEvaluator
{
    /// <summary>
    /// 对发现项列表逐条评估并返回判定映射；映射键为 Finding 身份（UUIDv7），供审计存储写入
    /// <c>findings.action</c> 列。
    /// </summary>
    /// <param name="findings">发现项列表（保持输入顺序，不改变聚合语义）。</param>
    /// <param name="policy">严重度 → 动作映射（含 Critical 硬下限校验）。</param>
    /// <param name="now">当前时间（UTC），用于白名单过期判定。</param>
    /// <param name="allowlist">白名单条目；缺省 <see langword="null"/> 视为无白名单（FR-040 白名单读取属后续任务）。</param>
    /// <returns>每条 Finding 的策略判定（忽略白名单的 Finding 仍保留在映射中）。</returns>
    /// <exception cref="ArgumentNullException"><paramref name="findings"/> 或 <paramref name="policy"/> 为 <see langword="null"/>。</exception>
    public static ImmutableDictionary<Finding, PolicyDecision> Evaluate(
        IReadOnlyList<Finding> findings,
        SeverityPolicy policy,
        DateTimeOffset now,
        IReadOnlyList<AllowlistEntry>? allowlist = null)
    {
        ArgumentNullException.ThrowIfNull(findings);
        ArgumentNullException.ThrowIfNull(policy);

        var decisions = new Dictionary<Finding, PolicyDecision>(findings.Count);
        foreach (Finding finding in findings)
        {
            decisions[finding] = Decide(finding, policy, allowlist, now);
        }

        return decisions.ToImmutableDictionary();
    }

    /// <summary>单条评估：白名单命中（rule + path + fingerprint 三条件交集）即放行；否则按严重度映射动作。</summary>
    private static PolicyDecision Decide(
        Finding finding,
        SeverityPolicy policy,
        IReadOnlyList<AllowlistEntry>? allowlist,
        DateTimeOffset now)
    {
        if (allowlist is not null)
        {
            foreach (AllowlistEntry entry in allowlist)
            {
                if (AllowlistMatcher.Matches(entry, finding, now))
                {
                    return new PolicyDecision(PolicyAction.Allow, ignoredByAllowlist: true, finding.Severity);
                }
            }
        }

        return new PolicyDecision(policy.ActionFor(finding.Severity), ignoredByAllowlist: false, finding.Severity);
    }
}
