namespace GitGuard.Detection.Detectors;

/// <summary>
/// Secret 检测器配置（模块设计 Secret §7 的 <c>rules.secrets.*</c> 子集，由 Application 装配时注入）。
/// </summary>
public sealed record SecretDetectorOptions
{
    /// <summary>
    /// 高熵判定阈值：命中要求 Shannon 熵严格大于该值（模块设计 Secret §6：熵 &gt; 4.0 才命中，
    /// 对应 <c>rules.secrets.min_entropy</c>）。
    /// </summary>
    public double EntropyThreshold { get; init; } = 4.0;
}
