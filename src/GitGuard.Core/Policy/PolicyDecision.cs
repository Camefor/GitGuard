using GitGuard.Core.Domain;
using GitGuard.Core.Model;

namespace GitGuard.Core.Policy;

/// <summary>
/// 单条 Finding 的策略判定结果（<c>模块设计/Policy.md</c> §2.2）。
/// 持久化层据此写入 <c>findings.action</c>：<see cref="IgnoredByAllowlist"/> 为 <see langword="true"/> 时记
/// <c>AllowedByAllowlist</c>；<see cref="IgnoredByBaseline"/> 为 <see langword="true"/> 时记
/// <c>AllowedByBaseline</c>；否则记 <see cref="Action"/> 的名称。
/// </summary>
public sealed record PolicyDecision
{
    /// <summary>
    /// 创建策略判定结果；命中白名单或基线必然放行（对应忽略标记为 <see langword="true"/> 时
    /// <see cref="Action"/> 必须为 <see cref="PolicyAction.Allow"/>）。
    /// </summary>
    /// <param name="action">策略动作。</param>
    /// <param name="ignoredByAllowlist">是否被 allowlist 忽略。</param>
    /// <param name="severity">该 Finding 的严重度。</param>
    /// <param name="ignoredByBaseline">是否被基线（历史噪声）忽略；不能与 <paramref name="ignoredByAllowlist"/> 同时为 <see langword="true"/>。</param>
    /// <exception cref="InvariantViolationException">参数取值非法时抛出。</exception>
    public PolicyDecision(
        PolicyAction action,
        bool ignoredByAllowlist,
        Severity severity,
        bool ignoredByBaseline = false)
    {
        DomainGuard.Require(Enum.IsDefined(action), "FR-022", $"未知的 PolicyAction 取值：{(int)action}");
        DomainGuard.Require(Enum.IsDefined(severity), "DDD §4.2", $"未知的 Severity 取值：{(int)severity}");
        DomainGuard.Require(!ignoredByAllowlist || action == PolicyAction.Allow, "FR-040", "被 allowlist 忽略的 Finding 必然是放行动作");
        DomainGuard.Require(!ignoredByBaseline || action == PolicyAction.Allow, "FR-061", "被基线忽略的 Finding 必然是放行动作");
        DomainGuard.Require(!(ignoredByAllowlist && ignoredByBaseline), "FR-061", "白名单与基线忽略互斥，优先记录白名单");

        Action = action;
        IgnoredByAllowlist = ignoredByAllowlist;
        IgnoredByBaseline = ignoredByBaseline;
        Severity = severity;
    }

    /// <summary>策略动作。</summary>
    public PolicyAction Action { get; }

    /// <summary>是否由 allowlist 命中而忽略（忽略的 Finding 仍进入报告与审计）。</summary>
    public bool IgnoredByAllowlist { get; }

    /// <summary>是否由基线（历史噪声）忽略；生成基线时恒为 <see langword="false"/>。</summary>
    public bool IgnoredByBaseline { get; }

    /// <summary>该 Finding 的严重度。</summary>
    public Severity Severity { get; }
}
