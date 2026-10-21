namespace GitGuard.Benchmarks;

/// <summary>
/// 单项基准结果：指标方向（越小越好 / 越大越好）、目标值与统计量；
/// <see cref="Passed"/> 按「延迟取 P95、吞吐取 P50」判定是否达到 NFR。
/// </summary>
internal sealed record BenchmarkResult
{
    /// <summary>初始化基准结果。</summary>
    /// <param name="scenario">场景名。</param>
    /// <param name="metric">指标描述。</param>
    /// <param name="unit">单位（ms / MB/s）。</param>
    /// <param name="target">NFR 目标值。</param>
    /// <param name="higherIsBetter"><see langword="true"/> 表示越大越好（吞吐）。</param>
    /// <param name="sampleCount">测量样本数。</param>
    /// <param name="p50">P50。</param>
    /// <param name="p95">P95。</param>
    /// <param name="min">最小值。</param>
    /// <param name="max">最大值。</param>
    /// <param name="mean">均值。</param>
    /// <param name="notes">口径说明。</param>
    internal BenchmarkResult(
        string scenario,
        string metric,
        string unit,
        double target,
        bool higherIsBetter,
        int sampleCount,
        double p50,
        double p95,
        double min,
        double max,
        double mean,
        string notes)
    {
        Scenario = scenario;
        Metric = metric;
        Unit = unit;
        Target = target;
        HigherIsBetter = higherIsBetter;
        SampleCount = sampleCount;
        P50 = p50;
        P95 = p95;
        Min = min;
        Max = max;
        Mean = mean;
        Notes = notes;
    }

    /// <summary>场景名。</summary>
    public string Scenario { get; }

    /// <summary>指标描述。</summary>
    public string Metric { get; }

    /// <summary>单位。</summary>
    public string Unit { get; }

    /// <summary>NFR 目标值。</summary>
    public double Target { get; }

    /// <summary>是否越大越好。</summary>
    public bool HigherIsBetter { get; }

    /// <summary>测量样本数。</summary>
    public int SampleCount { get; }

    /// <summary>P50。</summary>
    public double P50 { get; }

    /// <summary>P95。</summary>
    public double P95 { get; }

    /// <summary>最小值。</summary>
    public double Min { get; }

    /// <summary>最大值。</summary>
    public double Max { get; }

    /// <summary>均值。</summary>
    public double Mean { get; }

    /// <summary>口径说明。</summary>
    public string Notes { get; }

    /// <summary>是否达到 NFR 目标。</summary>
    public bool Passed => HigherIsBetter ? P50 >= Target : P95 <= Target;
}
