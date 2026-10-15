namespace GitGuard.Core.Model;

/// <summary>
/// 严重度（DDD §4.2）。数值按危险程度升序编码，可直接比较：<c>Critical &gt; High &gt; Medium &gt; Low</c>。
/// </summary>
public enum Severity
{
    /// <summary>低：提示级风险。</summary>
    Low = 0,

    /// <summary>中：疑似泄漏，默认映射为警告。</summary>
    Medium = 1,

    /// <summary>高：高置信度泄漏，默认映射为阻断。</summary>
    High = 2,

    /// <summary>严重：最高等级；硬下限要求策略不允许将其降级为放行（FR-022）。</summary>
    Critical = 3,
}
