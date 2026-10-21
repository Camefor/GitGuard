using System.Diagnostics;

namespace GitGuard.Application.Scanning;

/// <summary>
/// 单条规则的耗时摘要（NFR-M-002）：<see cref="ElapsedTimestampTicks"/> 为 <see cref="Stopwatch"/> 时间戳差值，
/// 展示层通过 <see cref="ElapsedMicroseconds"/> / <see cref="ElapsedMilliseconds"/> 换算；
/// 仅在启用规则耗时采集（<c>--verbose</c>）时产生。
/// </summary>
public sealed record RuleTiming
{
    /// <summary>初始化规则耗时摘要。</summary>
    /// <param name="ruleId">规则 ID。</param>
    /// <param name="evaluationCount">规则求值次数（仅统计真正执行正则 / 校验器的求值）。</param>
    /// <param name="hitCount">命中次数。</param>
    /// <param name="elapsedTimestampTicks">累计耗时（<see cref="Stopwatch.GetTimestamp"/> 差值）。</param>
    /// <exception cref="ArgumentException"><paramref name="ruleId"/> 为空或全空白。</exception>
    /// <exception cref="ArgumentOutOfRangeException">任一计数或耗时为负。</exception>
    public RuleTiming(string ruleId, int evaluationCount, int hitCount, long elapsedTimestampTicks)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ruleId);
        ArgumentOutOfRangeException.ThrowIfNegative(evaluationCount, nameof(evaluationCount));
        ArgumentOutOfRangeException.ThrowIfNegative(hitCount, nameof(hitCount));
        ArgumentOutOfRangeException.ThrowIfNegative(elapsedTimestampTicks, nameof(elapsedTimestampTicks));

        RuleId = ruleId;
        EvaluationCount = evaluationCount;
        HitCount = hitCount;
        ElapsedTimestampTicks = elapsedTimestampTicks;
    }

    /// <summary>规则 ID。</summary>
    public string RuleId { get; }

    /// <summary>规则求值次数（文件 glob / 上下文门禁的短路不计入）。</summary>
    public int EvaluationCount { get; }

    /// <summary>命中次数（求值产出发现项）。</summary>
    public int HitCount { get; }

    /// <summary>累计耗时（<see cref="Stopwatch.GetTimestamp"/> 差值）。</summary>
    public long ElapsedTimestampTicks { get; }

    /// <summary>累计耗时（微秒）。</summary>
    public double ElapsedMicroseconds => ElapsedTimestampTicks * 1_000_000d / Stopwatch.Frequency;

    /// <summary>累计耗时（毫秒）。</summary>
    public double ElapsedMilliseconds => ElapsedTimestampTicks * 1_000d / Stopwatch.Frequency;
}
