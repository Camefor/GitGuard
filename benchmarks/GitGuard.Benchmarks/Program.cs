using System.ComponentModel;

namespace GitGuard.Benchmarks;

/// <summary>
/// 基准入口（GGD-0020）：解析场景 → 准备隔离工作区 → 逐项测量 → 控制台 / JSON 报告。
/// 基准完成后退出码为 0（NFR 未达标以报告 PASS / FAIL 呈现，不作为 CI 门禁）；参数错误为 2，运行失败为 1。
/// </summary>
internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        try
        {
            var options = BenchmarkOptions.Parse(args);
            using var workspace = new BenchmarkWorkspace(options.KeepWorkspace);
            var results = new List<BenchmarkResult>();
            CancellationToken cancellationToken = CancellationToken.None;

            if (options.Includes("startup"))
            {
                results.Add(await BenchmarkScenarios.RunStartupAsync(options, workspace, cancellationToken)
                    .ConfigureAwait(false));
            }

            if (options.Includes("staged"))
            {
                results.Add(await BenchmarkScenarios.RunStagedAsync(options, workspace, cancellationToken)
                    .ConfigureAwait(false));
            }

            if (options.Includes("push"))
            {
                results.Add(await BenchmarkScenarios.RunPushAsync(options, workspace, cancellationToken)
                    .ConfigureAwait(false));
            }

            if (options.Includes("throughput"))
            {
                results.Add(await ThroughputBenchmark.RunAsync(options, cancellationToken).ConfigureAwait(false));
            }

            var report = new BenchmarkReport(BenchmarkEnvironment.Create(options), results);
            report.Print();
            if (!string.IsNullOrWhiteSpace(options.JsonPath))
            {
                report.WriteJson(options.JsonPath);
            }

            return 0;
        }
        catch (ArgumentException ex)
        {
            Console.Error.WriteLine(ex.Message);
            Console.Error.WriteLine();
            Console.Error.WriteLine(BenchmarkOptions.USAGE_TEXT);
            return 2;
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or TimeoutException or Win32Exception)
        {
            Console.Error.WriteLine($"benchmark failed: {ex.Message}");
            return 1;
        }
    }
}
