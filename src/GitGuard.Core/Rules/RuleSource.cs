namespace GitGuard.Core.Rules;

/// <summary>
/// 规则来源（DDD §4.2）：记录检测规则由何处载入，用于审计与去重优先级（ADR-0003：USR &gt; GLK &gt; GGD）。
/// </summary>
public enum RuleSource
{
    /// <summary>GitGuard 内置规则（<c>BuiltinRules.yaml</c>，规则 ID 前缀 <c>GGD/</c>）。</summary>
    Builtin = 0,

    /// <summary>用户自定义规则（P3 <c>UserRegexDetector</c>，规则 ID 前缀 <c>USR/</c>）。</summary>
    User = 1,

    /// <summary>Gitleaks 适配器来源（P2，规则 ID 前缀 <c>GLK/</c>）。</summary>
    Gitleaks = 2,
}
