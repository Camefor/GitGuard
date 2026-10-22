using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace GitGuard.Benchmarks;

/// <summary>基准运行环境快照（写入控制台与 JSON，便于结果可比性判断）。</summary>
internal sealed record BenchmarkEnvironment
{
    /// <summary>初始化环境快照。</summary>
    /// <param name="timestampUtc">采集时间（UTC，ISO 8601）。</param>
    /// <param name="operatingSystem">操作系统描述。</param>
    /// <param name="runtime">运行时描述。</param>
    /// <param name="architecture">进程架构。</param>
    /// <param name="processorCount">逻辑处理器数。</param>
    /// <param name="executablePath">被测可执行文件路径。</param>
    internal BenchmarkEnvironment(
        string timestampUtc,
        string operatingSystem,
        string runtime,
        string architecture,
        int processorCount,
        string? executablePath)
    {
        TimestampUtc = timestampUtc;
        OperatingSystem = operatingSystem;
        Runtime = runtime;
        Architecture = architecture;
        ProcessorCount = processorCount;
        ExecutablePath = executablePath;
    }

    /// <summary>采集时间（UTC，ISO 8601）。</summary>
    public string TimestampUtc { get; }

    /// <summary>操作系统描述。</summary>
    public string OperatingSystem { get; }

    /// <summary>运行时描述。</summary>
    public string Runtime { get; }

    /// <summary>进程架构。</summary>
    public string Architecture { get; }

    /// <summary>逻辑处理器数。</summary>
    public int ProcessorCount { get; }

    /// <summary>被测可执行文件路径。</summary>
    public string? ExecutablePath { get; }

    /// <summary>采集当前运行环境。</summary>
    /// <param name="options">基准选项（提供被测可执行文件路径）。</param>
    /// <returns>环境快照。</returns>
    internal static BenchmarkEnvironment Create(BenchmarkOptions options) => new BenchmarkEnvironment(
        DateTimeOffset.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture),
        RuntimeInformation.OSDescription,
        RuntimeInformation.FrameworkDescription,
        RuntimeInformation.ProcessArchitecture.ToString(),
        Environment.ProcessorCount,
        options.ExecutablePath);
}

/// <summary>JSON 报告负载。</summary>
internal sealed record BenchmarkReportDocument
{
    /// <summary>初始化报告负载。</summary>
    /// <param name="environment">运行环境快照。</param>
    /// <param name="results">各项基准结果。</param>
    internal BenchmarkReportDocument(BenchmarkEnvironment environment, IReadOnlyList<BenchmarkResult> results)
    {
        Environment = environment;
        Results = results;
    }

    /// <summary>运行环境快照。</summary>
    public BenchmarkEnvironment Environment { get; }

    /// <summary>各项基准结果。</summary>
    public IReadOnlyList<BenchmarkResult> Results { get; }
}

/// <summary>基准报告：控制台逐项输出 PASS / FAIL 与统计量，可另写 JSON 供归档比较。</summary>
internal sealed class BenchmarkReport
{
    private static readonly JsonSerializerOptions _jsonOptions = new JsonSerializerOptions { WriteIndented = true };

    private readonly BenchmarkEnvironment _environment;
    private readonly IReadOnlyList<BenchmarkResult> _results;

    internal BenchmarkReport(BenchmarkEnvironment environment, IReadOnlyList<BenchmarkResult> results)
    {
        _environment = environment;
        _results = results;
    }

    /// <summary>输出人类可读报告（每项含目标方向、P50 / P95 与判定）。</summary>
    internal void Print()
    {
        Console.Out.WriteLine("GitGuard performance benchmarks");
        Console.Out.WriteLine($"  time:       {_environment.TimestampUtc}");
        Console.Out.WriteLine($"  os:         {_environment.OperatingSystem}");
        Console.Out.WriteLine(
            $"  runtime:    {_environment.Runtime} ({_environment.Architecture}, {_environment.ProcessorCount} cores)");
        Console.Out.WriteLine($"  executable: {_environment.ExecutablePath ?? "(in-process only)"}");
        Console.Out.WriteLine();

        foreach (BenchmarkResult result in _results)
        {
            string verdict = result.Passed ? "PASS" : "FAIL";
            string comparison = result.HigherIsBetter ? ">=" : "<=";
            string p50 = result.P50.ToString("F2", CultureInfo.InvariantCulture);
            string p95 = result.P95.ToString("F2", CultureInfo.InvariantCulture);
            string min = result.Min.ToString("F2", CultureInfo.InvariantCulture);
            string max = result.Max.ToString("F2", CultureInfo.InvariantCulture);
            string mean = result.Mean.ToString("F2", CultureInfo.InvariantCulture);
            string target = result.Target.ToString("F2", CultureInfo.InvariantCulture);
            Console.Out.WriteLine($"[{verdict}] {result.Scenario}: {result.Metric}");
            Console.Out.WriteLine(
                $"        p50={p50} {result.Unit}  p95={p95} {result.Unit}  min={min}  max={max}  mean={mean}  "
                + $"n={result.SampleCount}  target {comparison} {target} {result.Unit}");
            Console.Out.WriteLine($"        {result.Notes}");
        }
    }

    /// <summary>写出 JSON 结果（自动创建父目录）。</summary>
    /// <param name="path">输出路径。</param>
    internal void WriteJson(string path)
    {
        string fullPath = Path.GetFullPath(path);
        string? directory = Path.GetDirectoryName(fullPath);
        if (directory is not null)
        {
            Directory.CreateDirectory(directory);
        }

        var document = new BenchmarkReportDocument(_environment, _results);
        File.WriteAllText(fullPath, JsonSerializer.Serialize(document, _jsonOptions));
        Console.Out.WriteLine();
        Console.Out.WriteLine($"JSON result written: {fullPath}");
    }
}
