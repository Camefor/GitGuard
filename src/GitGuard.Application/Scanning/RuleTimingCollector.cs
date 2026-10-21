using System.Collections.Immutable;
using GitGuard.Core.Abstractions;

namespace GitGuard.Application.Scanning;

/// <summary>
/// 规则耗时聚合器（NFR-M-002）：实现 <see cref="IRuleTimingSink"/>，在一次扫描内按规则累计求值次数、
/// 命中次数与耗时；<see cref="Snapshot"/> 按规则 ID 排序输出，保证 <c>--verbose</c> 摘要稳定可读。
/// 仅在启用采集时创建，未启用时检测器不付出计时开销。
/// </summary>
internal sealed class RuleTimingCollector : IRuleTimingSink
{
    private readonly Dictionary<string, Accumulator> _entries = new Dictionary<string, Accumulator>(StringComparer.Ordinal);

    /// <summary>记录一次规则求值（由检测器在正则求值完成后调用）。</summary>
    /// <param name="ruleId">规则 ID。</param>
    /// <param name="elapsedTimestampTicks">本次求值耗时（<see cref="System.Diagnostics.Stopwatch.GetTimestamp"/> 差值）。</param>
    /// <param name="hit">本次求值是否产出发现项。</param>
    /// <exception cref="ArgumentException"><paramref name="ruleId"/> 为空或全空白。</exception>
    public void Record(string ruleId, long elapsedTimestampTicks, bool hit)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ruleId);

        if (!_entries.TryGetValue(ruleId, out Accumulator? entry))
        {
            entry = new Accumulator();
            _entries.Add(ruleId, entry);
        }

        entry.EvaluationCount++;
        entry.ElapsedTimestampTicks += elapsedTimestampTicks;
        if (hit)
        {
            entry.HitCount++;
        }
    }

    /// <summary>生成按规则 ID（序数排序）升序排列的耗时快照。</summary>
    /// <returns>不可变摘要列表；无记录时为空。</returns>
    public ImmutableArray<RuleTiming> Snapshot()
    {
        var timings = new List<RuleTiming>(_entries.Count);
        foreach ((string ruleId, Accumulator entry) in _entries)
        {
            timings.Add(new RuleTiming(ruleId, entry.EvaluationCount, entry.HitCount, entry.ElapsedTimestampTicks));
        }

        timings.Sort(static (left, right) => string.CompareOrdinal(left.RuleId, right.RuleId));
        return timings.ToImmutableArray();
    }

    /// <summary>单条规则的累计计数（按需分配，正常扫描不产生）。</summary>
    private sealed class Accumulator
    {
        /// <summary>求值次数。</summary>
        internal int EvaluationCount { get; set; }

        /// <summary>命中次数。</summary>
        internal int HitCount { get; set; }

        /// <summary>累计时间戳差值。</summary>
        internal long ElapsedTimestampTicks { get; set; }
    }
}
