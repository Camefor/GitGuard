using System.Diagnostics;
using System.Globalization;
using GitGuard.Core.Abstractions;
using GitGuard.Core.Model;
using GitGuard.Core.Rules;
using GitGuard.Core.Services;
using GitGuard.Detection.Detectors;
using GitGuard.Detection.Rules;

namespace GitGuard.Benchmarks;

/// <summary>
/// NFR-P-004 吞吐基准：<c>scan --all</c>（FR-060）属 v0.2，因此直接以四类内置检测器
/// 组成与生产一致的管线（FR-021 顺序），在内存语料上测量扫描吞吐（P50 ≥ 20 MB/s）。
/// </summary>
internal static class ThroughputBenchmark
{
    private const double THROUGHPUT_TARGET_MB_S = 20;
    private const int DEFAULT_RUNS = 3;
    private const int DEFAULT_WARMUP = 1;

    /// <summary>运行检测器管线吞吐基准。</summary>
    /// <param name="options">基准选项（语料大小 / 次数 / 预热）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>基准结果。</returns>
    internal static async Task<BenchmarkResult> RunAsync(
        BenchmarkOptions options,
        CancellationToken cancellationToken)
    {
        (IReadOnlyList<FileChange> changes, long totalBytes) = BenchmarkCorpus.Create(options.SizeMb);
        IDetector[] detectors = CreateDetectors();
        int warmup = options.ResolveWarmup(DEFAULT_WARMUP);
        int runs = options.ResolveRuns(DEFAULT_RUNS);

        for (int index = 0; index < warmup; index++)
        {
            _ = await ScanAsync(changes, detectors, cancellationToken).ConfigureAwait(false);
        }

        var samples = new List<double>(runs);
        int findings = 0;
        for (int index = 0; index < runs; index++)
        {
            long startedTimestamp = Stopwatch.GetTimestamp();
            findings = await ScanAsync(changes, detectors, cancellationToken).ConfigureAwait(false);
            double seconds = Stopwatch.GetElapsedTime(startedTimestamp).TotalSeconds;
            samples.Add(totalBytes / 1_000_000d / seconds);
        }

        var statistics = BenchmarkStatistics.FromSamples(samples);
        string format = "NFR-P-004：检测器管线吞吐 P50 ≥ 20 MB/s；语料 {0:F1} MiB / {1} 个文件 / 命中 {2} 项。"
            + "scan --all（FR-060）属 v0.2，此处直接测量 Secret → Privacy → LocalPath → Url 管线。";
        string notes = string.Format(
            CultureInfo.InvariantCulture,
            format,
            totalBytes / (1024d * 1024d),
            changes.Count,
            findings);
        return new BenchmarkResult(
            "throughput",
            "detector pipeline throughput",
            "MB/s",
            THROUGHPUT_TARGET_MB_S,
            higherIsBetter: true,
            statistics.Count,
            statistics.P50,
            statistics.P95,
            statistics.Min,
            statistics.Max,
            statistics.Mean,
            notes);
    }

    private static IDetector[] CreateDetectors()
    {
        IReadOnlyList<DetectionRule> rules = BuiltinRuleLoader.LoadEmbedded();
        var factory = new FindingFactory(new Fingerprinter(new byte[Fingerprinter.SALT_BYTES]));
        return new IDetector[]
        {
            new SecretDetector(rules, factory),
            new PrivacyDetector(rules, factory),
            new LocalPathDetector(rules, factory),
            new UrlDetector(rules, factory),
        };
    }

    private static async Task<int> ScanAsync(
        IReadOnlyList<FileChange> changes,
        IReadOnlyList<IDetector> detectors,
        CancellationToken cancellationToken)
    {
        int findings = 0;
        foreach (FileChange change in changes)
        {
            foreach (IDetector detector in detectors)
            {
                await foreach (Finding finding in detector.DetectAsync(change, cancellationToken).ConfigureAwait(false))
                {
                    findings++;
                }
            }
        }

        return findings;
    }
}
