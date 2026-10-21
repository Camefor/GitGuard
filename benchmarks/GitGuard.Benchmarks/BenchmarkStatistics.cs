namespace GitGuard.Benchmarks;

/// <summary>样本统计（最近秩百分位）：P50 / P95 / 极值 / 均值。</summary>
internal sealed record BenchmarkStatistics
{
    /// <summary>初始化统计结果。</summary>
    /// <param name="count">样本数。</param>
    /// <param name="p50">P50。</param>
    /// <param name="p95">P95。</param>
    /// <param name="min">最小值。</param>
    /// <param name="max">最大值。</param>
    /// <param name="mean">均值。</param>
    internal BenchmarkStatistics(int count, double p50, double p95, double min, double max, double mean)
    {
        Count = count;
        P50 = p50;
        P95 = p95;
        Min = min;
        Max = max;
        Mean = mean;
    }

    /// <summary>样本数。</summary>
    public int Count { get; }

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

    /// <summary>由原始样本计算统计量。</summary>
    /// <param name="samples">原始样本（单位由调用方决定）。</param>
    /// <returns>统计结果。</returns>
    /// <exception cref="ArgumentException"><paramref name="samples"/> 为空。</exception>
    internal static BenchmarkStatistics FromSamples(IReadOnlyList<double> samples)
    {
        if (samples.Count == 0)
        {
            throw new ArgumentException("至少需要一个样本。", nameof(samples));
        }

        double[] sorted = new double[samples.Count];
        double sum = 0;
        for (int index = 0; index < samples.Count; index++)
        {
            sorted[index] = samples[index];
            sum += samples[index];
        }

        Array.Sort(sorted);
        return new BenchmarkStatistics(
            sorted.Length,
            Percentile(sorted, 0.50),
            Percentile(sorted, 0.95),
            sorted[0],
            sorted[^1],
            sum / sorted.Length);
    }

    private static double Percentile(double[] sorted, double percentile)
    {
        int rank = (int)Math.Ceiling(percentile * sorted.Length);
        return sorted[Math.Clamp(rank - 1, 0, sorted.Length - 1)];
    }
}
