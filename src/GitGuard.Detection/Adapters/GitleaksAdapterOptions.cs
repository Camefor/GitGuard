using GitGuard.Core.Model;

namespace GitGuard.Detection.Adapters;

/// <summary>
/// Gitleaks 适配器配置（<c>rules.secrets</c> 段落接线）：
/// 可执行路径由启动期探测提供；禁用规则沿用 <c>disabled_rules</c>（支持 <c>GLK/...</c> 或 Gitleaks 原始规则 ID）。
/// </summary>
public sealed class GitleaksAdapterOptions
{
    /// <summary>Gitleaks 可执行文件绝对路径（必填，由 <see cref="GitleaksProbe"/> 定位）。</summary>
    public string ExecutablePath { get; init; } = string.Empty;

    /// <summary>禁用的规则 ID 集合；<c>GGD/</c> / <c>USR/</c> 条目由内置检测器消费，适配器忽略。</summary>
    public IReadOnlyCollection<string> DisabledRuleIds { get; init; } = Array.Empty<string>();

    /// <summary>单次调用超时（模块设计 Secret §4：10s）。</summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>Gitleaks 命中的默认严重度（Gitleaks 报告不携带严重度，统一取 High）。</summary>
    public Severity Severity { get; init; } = Severity.High;
}
