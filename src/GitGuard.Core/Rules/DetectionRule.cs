using GitGuard.Core.Domain;
using GitGuard.Core.Model;

namespace GitGuard.Core.Rules;

/// <summary>
/// 检测规则聚合根（DDD §3.2）：身份由 <see cref="RuleId"/> 标识，模式经 <see cref="RulePattern"/> 编译后无状态执行。
/// 规则一律从 <c>BuiltinRules.yaml</c> 等外部来源载入（DDD §9 反模式：禁止在检测器代码中写死正则）。
/// </summary>
public sealed class DetectionRule : IEquatable<DetectionRule>
{
    private DetectionRule(
        string ruleId,
        string name,
        Severity severity,
        RulePattern pattern,
        RuleSource source,
        bool enabled)
    {
        RuleId = ruleId;
        Name = name;
        Severity = severity;
        Pattern = pattern;
        Source = source;
        Enabled = enabled;
    }

    /// <summary>规则 ID，形如 <c>GGD/SECRET/AWS-AKID</c>，必须符合命名契约（13-开发规范 §4.1）。</summary>
    public string RuleId { get; }

    /// <summary>规则名称（面向用户展示）。</summary>
    public string Name { get; }

    /// <summary>规则命中时的严重度（FR-022 策略映射的输入）。</summary>
    public Severity Severity { get; }

    /// <summary>规则模式（正则 + 上下文关键字 + 文件 glob + 校验器）。</summary>
    public RulePattern Pattern { get; }

    /// <summary>规则来源（审计与去重优先级依据）。</summary>
    public RuleSource Source { get; }

    /// <summary>是否启用；禁用规则不参与扫描（模块设计 Secret §7 <c>disabled_rules</c>）。</summary>
    public bool Enabled { get; }

    /// <summary>
    /// 创建检测规则。
    /// </summary>
    /// <param name="ruleId">规则 ID。</param>
    /// <param name="name">规则名称。</param>
    /// <param name="severity">严重度。</param>
    /// <param name="pattern">规则模式（已编译）。</param>
    /// <param name="source">规则来源。</param>
    /// <param name="enabled">是否启用。</param>
    /// <returns>通过全部不变式校验的规则。</returns>
    /// <exception cref="ArgumentNullException"><paramref name="ruleId"/> / <paramref name="name"/> / <paramref name="pattern"/> 为 <see langword="null"/>。</exception>
    /// <exception cref="InvariantViolationException">规则 ID 不符合命名契约、名称为空或枚举取值非法时抛出（DDD §3.2）。</exception>
    public static DetectionRule Create(
        string ruleId,
        string name,
        Severity severity,
        RulePattern pattern,
        RuleSource source = RuleSource.Builtin,
        bool enabled = true)
    {
        ArgumentNullException.ThrowIfNull(ruleId);
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(pattern);

        DomainGuard.Require(RuleIdFormat.IsValid(ruleId), "DDD §3.2", $"规则 ID 不符合 ^(GGD|USR|GLK)/[A-Z]+/[A-Z0-9_-]+$ 契约：{ruleId}");
        DomainGuard.Require(!string.IsNullOrWhiteSpace(name), "DDD §3.2", $"规则 {ruleId} 的名称不能为空");
        DomainGuard.Require(Enum.IsDefined(severity), "DDD §3.2", $"规则 {ruleId} 的严重度取值非法：{(int)severity}");
        DomainGuard.Require(Enum.IsDefined(source), "DDD §3.2", $"规则 {ruleId} 的来源取值非法：{(int)source}");

        return new DetectionRule(ruleId, name, severity, pattern, source, enabled);
    }

    /// <summary>按规则 ID 判断聚合相等性。</summary>
    /// <param name="other">另一规则。</param>
    /// <returns>规则 ID 相同返回 <see langword="true"/>。</returns>
    public bool Equals(DetectionRule? other) =>
        other is not null && (ReferenceEquals(this, other) || string.Equals(RuleId, other.RuleId, StringComparison.Ordinal));

    /// <summary>按规则 ID 判断聚合相等性。</summary>
    /// <param name="obj">比较对象。</param>
    /// <returns>为同一规则 ID 的 <see cref="DetectionRule"/> 时返回 <see langword="true"/>。</returns>
    public override bool Equals(object? obj) => Equals(obj as DetectionRule);

    /// <summary>返回规则 ID 的哈希值。</summary>
    /// <returns><see cref="RuleId"/> 的哈希值。</returns>
    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(RuleId);
}
