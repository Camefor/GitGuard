using System.Collections.Immutable;
using GitGuard.Core.Domain;
using GitGuard.Core.Model;

namespace GitGuard.Core.Policy;

/// <summary>
/// 严重度 → 动作映射（FR-022 / <c>模块设计/Policy.md</c> §3），承载硬下限：
/// <see cref="Severity.Critical"/> 不允许被映射为 <see cref="PolicyAction.Allow"/>。
/// </summary>
public sealed record SeverityPolicy
{
    /// <summary>内置默认映射：Critical/High → Block，Medium/Low → Warn。</summary>
    public static SeverityPolicy Default { get; } = new SeverityPolicy(ImmutableDictionary<Severity, PolicyAction>.Empty
        .Add(Severity.Critical, PolicyAction.Block)
        .Add(Severity.High, PolicyAction.Block)
        .Add(Severity.Medium, PolicyAction.Warn)
        .Add(Severity.Low, PolicyAction.Warn));

    /// <summary>
    /// 创建映射策略；调用方（配置加载层）负责先与默认映射深度合并，再提交完整映射。
    /// </summary>
    /// <param name="mapping">覆盖全部四个严重度等级的动作映射。</param>
    /// <exception cref="ArgumentNullException"><paramref name="mapping"/> 为 <see langword="null"/>。</exception>
    /// <exception cref="InvariantViolationException">映射不完整、含未知取值，或试图把 Critical 降级为 Allow 时抛出。</exception>
    public SeverityPolicy(IReadOnlyDictionary<Severity, PolicyAction> mapping)
    {
        ArgumentNullException.ThrowIfNull(mapping);

        foreach (Severity severity in Enum.GetValues<Severity>())
        {
            DomainGuard.Require(mapping.ContainsKey(severity), "FR-022", $"策略映射必须覆盖全部严重度，缺少 {severity}");
        }

        foreach (PolicyAction action in mapping.Values)
        {
            DomainGuard.Require(Enum.IsDefined(action), "FR-022", $"未知的 PolicyAction 取值：{(int)action}");
        }

        DomainGuard.Require(mapping[Severity.Critical] != PolicyAction.Allow, "FR-022", "硬下限：Critical 不允许降级为 Allow（参见 FR-022 / 模块设计 Policy.md §3）");

        Mapping = mapping.ToImmutableDictionary();
    }

    /// <summary>严重度到动作的完整映射（不可变）。</summary>
    public ImmutableDictionary<Severity, PolicyAction> Mapping { get; }

    /// <summary>查询指定严重度对应的策略动作。</summary>
    /// <param name="severity">严重度。</param>
    /// <returns>该严重度的策略动作。</returns>
    /// <exception cref="InvariantViolationException">严重度取值未知时抛出。</exception>
    public PolicyAction ActionFor(Severity severity)
    {
        DomainGuard.Require(Enum.IsDefined(severity), "DDD §4.2", $"未知的 Severity 取值：{(int)severity}");

        return Mapping[severity];
    }
}
