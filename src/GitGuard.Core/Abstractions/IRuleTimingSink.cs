namespace GitGuard.Core.Abstractions;

/// <summary>
/// 规则级耗时接收器（NFR-M-002）：检测器每完成一次规则正则求值后上报耗时与是否命中，
/// 由应用层聚合为 <c>--verbose</c> 的规则耗时摘要；接收器不参与检测语义。
/// </summary>
public interface IRuleTimingSink
{
    /// <summary>记录一次规则求值。</summary>
    /// <param name="ruleId">规则 ID。</param>
    /// <param name="elapsedTimestampTicks">耗时（<see cref="System.Diagnostics.Stopwatch.GetTimestamp"/> 差值）。</param>
    /// <param name="hit">本次求值是否产出发现项。</param>
    void Record(string ruleId, long elapsedTimestampTicks, bool hit);
}
