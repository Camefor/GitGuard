using System.Globalization;
using System.Text;

namespace GitGuard.Benchmarks;

/// <summary>
/// 进程级基准场景（NFR-P-001 / P-002 / P-003）：以发布版可执行文件测量完整调用链
/// （进程启动 + git 差异 + 检测 + 审计落库），每次运行使用同一隔离工作区。
/// </summary>
internal static class BenchmarkScenarios
{
    private const int STARTUP_RUNS = 50;
    private const int STARTUP_WARMUP = 3;
    private const double STARTUP_TARGET_MS = 150;
    private const int SCAN_RUNS = 10;
    private const int SCAN_WARMUP = 1;
    private const double STAGED_TARGET_MS = 500;
    private const double PUSH_TARGET_MS = 2000;
    private static readonly TimeSpan PROCESS_TIMEOUT = TimeSpan.FromSeconds(120);

    /// <summary>NFR-P-001：冷启动（<c>gitguard version</c>）P95 ≤ 150 ms。</summary>
    /// <param name="options">基准选项。</param>
    /// <param name="workspace">临时工作区。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>基准结果。</returns>
    internal static Task<BenchmarkResult> RunStartupAsync(
        BenchmarkOptions options,
        BenchmarkWorkspace workspace,
        CancellationToken cancellationToken)
    {
        string[] arguments = { "version" };
        return MeasureProcessAsync(
            "startup",
            "process wall time (gitguard version)",
            STARTUP_TARGET_MS,
            higherIsBetter: false,
            "NFR-P-001：Native AOT 冷启动 P95 ≤ 150 ms。",
            options,
            workspace,
            workspace.Root,
            arguments,
            standardInput: null,
            STARTUP_RUNS,
            STARTUP_WARMUP,
            cancellationToken);
    }

    /// <summary>NFR-P-002：暂存区扫描（默认 500 行差异）P95 ≤ 500 ms。</summary>
    /// <param name="options">基准选项。</param>
    /// <param name="workspace">临时工作区。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>基准结果。</returns>
    internal static async Task<BenchmarkResult> RunStagedAsync(
        BenchmarkOptions options,
        BenchmarkWorkspace workspace,
        CancellationToken cancellationToken)
    {
        string repository = workspace.StagedRepositoryPath;
        BenchmarkWorkspace.CreateRepository(repository);
        string sourceDirectory = Path.Combine(repository, "src");
        Directory.CreateDirectory(sourceDirectory);
        string filePath = Path.Combine(sourceDirectory, "sample.cs");
        File.WriteAllText(filePath, BuildStagedContent(options.StagedLines));
        BenchmarkWorkspace.RunGit(repository, "add", "--", "src/sample.cs");

        string[] arguments = { "scan", "--staged" };
        string metric = string.Create(
            CultureInfo.InvariantCulture,
            $"staged scan wall time ({options.StagedLines}-line diff)");
        return await MeasureProcessAsync(
            "staged",
            metric,
            STAGED_TARGET_MS,
            higherIsBetter: false,
            "NFR-P-002：暂存区扫描 P95 ≤ 500 ms（≤500 行差异；含进程启动与真实 git diff）。",
            options,
            workspace,
            repository,
            arguments,
            standardInput: null,
            SCAN_RUNS,
            SCAN_WARMUP,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>NFR-P-003：推送扫描（默认 50 提交 / 5000 行）P95 ≤ 2 s。</summary>
    /// <param name="options">基准选项。</param>
    /// <param name="workspace">临时工作区。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>基准结果。</returns>
    internal static async Task<BenchmarkResult> RunPushAsync(
        BenchmarkOptions options,
        BenchmarkWorkspace workspace,
        CancellationToken cancellationToken)
    {
        string repository = workspace.PushRepositoryPath;
        BenchmarkWorkspace.CreateRepository(repository);
        string sourceDirectory = Path.Combine(repository, "src");
        Directory.CreateDirectory(sourceDirectory);
        string filePath = Path.Combine(sourceDirectory, "history.txt");
        File.WriteAllText(filePath, "# benchmark base\n");
        BenchmarkWorkspace.RunGit(repository, "add", "--", "src/history.txt");
        BenchmarkWorkspace.RunGit(repository, "commit", "--quiet", "-m", "base");

        for (int commit = 0; commit < options.PushCommits; commit++)
        {
            File.AppendAllText(filePath, BuildCommitContent(commit, options.LinesPerCommit));
            BenchmarkWorkspace.RunGit(repository, "commit", "--quiet", "-am", $"bench {commit}");
        }

        string head = BenchmarkWorkspace.RunGit(repository, "rev-parse", "HEAD");
        string rangeBase = BenchmarkWorkspace.RunGit(repository, "rev-parse", $"HEAD~{options.PushCommits}");
        string standardInput = $"refs/heads/master {head} refs/heads/master {rangeBase}\n";
        string[] arguments = { "scan", "--push" };
        int totalLines = options.PushCommits * options.LinesPerCommit;
        string metric = string.Create(
            CultureInfo.InvariantCulture,
            $"push scan wall time ({options.PushCommits} commits / {totalLines}-line diff)");
        return await MeasureProcessAsync(
            "push",
            metric,
            PUSH_TARGET_MS,
            higherIsBetter: false,
            "NFR-P-003：推送扫描 P95 ≤ 2 s（50 提交 / 5000 行；含进程启动与逐提交 git diff-tree）。",
            options,
            workspace,
            repository,
            arguments,
            standardInput,
            SCAN_RUNS,
            SCAN_WARMUP,
            cancellationToken).ConfigureAwait(false);
    }

    private static async Task<BenchmarkResult> MeasureProcessAsync(
        string scenario,
        string metric,
        double target,
        bool higherIsBetter,
        string notes,
        BenchmarkOptions options,
        BenchmarkWorkspace workspace,
        string workingDirectory,
        IReadOnlyList<string> arguments,
        string? standardInput,
        int defaultRuns,
        int defaultWarmup,
        CancellationToken cancellationToken)
    {
        string executable = options.ExecutablePath
            ?? throw new InvalidOperationException("该场景需要发布版可执行文件。");
        IReadOnlyDictionary<string, string?> environment = workspace.CreateEnvironment();
        int warmup = options.ResolveWarmup(defaultWarmup);
        int runs = options.ResolveRuns(defaultRuns);

        for (int index = 0; index < warmup; index++)
        {
            await RunCheckedAsync(executable, arguments, workingDirectory, standardInput, environment, cancellationToken)
                .ConfigureAwait(false);
        }

        var samples = new List<double>(runs);
        for (int index = 0; index < runs; index++)
        {
            ProcessRunResult result = await RunCheckedAsync(
                executable, arguments, workingDirectory, standardInput, environment, cancellationToken).ConfigureAwait(false);
            samples.Add(result.ElapsedMilliseconds);
        }

        var statistics = BenchmarkStatistics.FromSamples(samples);
        return new BenchmarkResult(
            scenario,
            metric,
            "ms",
            target,
            higherIsBetter,
            statistics.Count,
            statistics.P50,
            statistics.P95,
            statistics.Min,
            statistics.Max,
            statistics.Mean,
            notes);
    }

    private static async Task<ProcessRunResult> RunCheckedAsync(
        string executable,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        string? standardInput,
        IReadOnlyDictionary<string, string?> environment,
        CancellationToken cancellationToken)
    {
        ProcessRunResult result = await ProcessRunner.RunAsync(
            executable,
            arguments,
            workingDirectory,
            standardInput,
            environment,
            PROCESS_TIMEOUT,
            cancellationToken).ConfigureAwait(false);
        if (result.ExitCode != 0)
        {
            string error = result.StandardError.Trim();
            if (error.Length > 500)
            {
                error = error.Substring(0, 500);
            }

            string exitCode = result.ExitCode.ToString(CultureInfo.InvariantCulture);
            throw new InvalidOperationException($"基准进程退出码 {exitCode}：{error}");
        }

        return result;
    }

    private static string BuildStagedContent(int lineCount)
    {
        var builder = new StringBuilder(lineCount * 80);
        builder.Append("namespace Sample;\n\ninternal static class BenchSample\n{\n");
        for (int index = 0; index < lineCount - 5; index++)
        {
            builder.Append("    private static int Compute").Append(index)
                .Append("(int value) => (value * ").Append((index % 97) + 1)
                .Append(") + ").Append(index % 31).Append(";\n");
        }

        builder.Append("}\n");
        return builder.ToString();
    }

    private static string BuildCommitContent(int commitIndex, int lineCount)
    {
        var builder = new StringBuilder(lineCount * 32);
        for (int line = 0; line < lineCount; line++)
        {
            builder.Append("module_").Append(commitIndex).Append("_line_").Append(line)
                .Append(" = value_").Append(line).Append('\n');
        }

        return builder.ToString();
    }
}
