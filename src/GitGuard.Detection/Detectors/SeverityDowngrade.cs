using GitGuard.Core.Model;

namespace GitGuard.Detection.Detectors;

/// <summary>
/// 严重度降级辅助（模块设计 Privacy §6 / LocalPathUrl §6）：上下文降级统一为「降一级」，
/// 由各检测器决定 Low 是否可再降（Privacy / Url 保留 Low 命中记录，LocalPath 直接不命中）。
/// </summary>
internal static class SeverityDowngrade
{
    /// <summary>降一级；已是 <see cref="Severity.Low"/> 时返回 <see langword="null"/>（无可降级别）。</summary>
    /// <param name="severity">原严重度。</param>
    /// <returns>降级后的严重度；无法再降为 <see langword="null"/>。</returns>
    public static Severity? Step(Severity severity) =>
        severity == Severity.Low ? null : (Severity)((int)severity - 1);

    /// <summary>降一级，Low 保持 Low（「降级后仍保留命中记录」语义）。</summary>
    /// <param name="severity">原严重度。</param>
    /// <returns>降级后的严重度，最低为 <see cref="Severity.Low"/>。</returns>
    public static Severity StepOrFloor(Severity severity) => Step(severity) ?? Severity.Low;
}
