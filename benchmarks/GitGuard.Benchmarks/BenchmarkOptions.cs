using System.Globalization;

namespace GitGuard.Benchmarks;

/// <summary>基准命令行选项：场景 + 样本规模 + 输出路径（内部工具，参数解析保持最少依赖）。</summary>
internal sealed class BenchmarkOptions
{
    internal const string USAGE_TEXT = """
        用法：dotnet run -c Release --project benchmarks/GitGuard.Benchmarks -- <startup|staged|push|throughput|all> [选项]

        选项：
          --exe <path>              发布版 gitguard 可执行文件（startup/staged/push 必填；也可用 GITGUARD_BENCH_EXE）
          --runs <n>                测量次数（默认：startup 50，扫描 10，吞吐 3）
          --warmup <n>              预热次数（默认：startup 3，其余 1）
          --staged-lines <n>        暂存区样本新增行数（默认 500，NFR-P-002）
          --commits <n>             推送样本提交数（默认 50，NFR-P-003）
          --lines-per-commit <n>    推送样本每提交新增行数（默认 100，合计 5000 行）
          --size-mb <n>             吞吐样本大小（默认 100，NFR-P-004）
          --json <path>             同时写出 JSON 结果
          --keep                    保留临时基准仓库与隔离目录
        """;

    private const string EXECUTABLE_ENV = "GITGUARD_BENCH_EXE";
    private const string DEFAULT_EXECUTABLE = "artifacts/publish/gitguard-win-x64/gitguard.exe";

    private BenchmarkOptions()
    {
    }

    /// <summary>场景名（startup / staged / push / throughput / all）。</summary>
    internal string Scenario { get; private set; } = "all";

    /// <summary>发布版可执行文件绝对路径；纯吞吐基准时为 <see langword="null"/>。</summary>
    internal string? ExecutablePath { get; private set; }

    /// <summary>显式测量次数；<see langword="null"/> 时按场景取默认值。</summary>
    internal int? Runs { get; private set; }

    /// <summary>显式预热次数；<see langword="null"/> 时按场景取默认值。</summary>
    internal int? Warmup { get; private set; }

    /// <summary>暂存区样本新增行数。</summary>
    internal int StagedLines { get; private set; } = 500;

    /// <summary>推送样本提交数。</summary>
    internal int PushCommits { get; private set; } = 50;

    /// <summary>推送样本每提交新增行数。</summary>
    internal int LinesPerCommit { get; private set; } = 100;

    /// <summary>吞吐样本大小（MB）。</summary>
    internal int SizeMb { get; private set; } = 100;

    /// <summary>JSON 结果输出路径；<see langword="null"/> 表示仅控制台输出。</summary>
    internal string? JsonPath { get; private set; }

    /// <summary>是否保留临时工作目录（便于事后检查样本仓库）。</summary>
    internal bool KeepWorkspace { get; private set; }

    /// <summary>解析命令行参数；非法输入抛 <see cref="ArgumentException"/>。</summary>
    /// <param name="args">命令行参数。</param>
    /// <returns>已校验的选项。</returns>
    /// <exception cref="ArgumentException">未知参数、缺少取值或取值非法。</exception>
    internal static BenchmarkOptions Parse(string[] args)
    {
        var options = new BenchmarkOptions();
        for (int index = 0; index < args.Length; index++)
        {
            string argument = args[index];
            switch (argument)
            {
                case "startup":
                case "staged":
                case "push":
                case "throughput":
                case "all":
                    options.Scenario = argument;
                    break;
                case "--exe":
                    options.ExecutablePath = NextValue(args, ref index, argument);
                    break;
                case "--runs":
                    options.Runs = ParsePositive(NextValue(args, ref index, argument), argument);
                    break;
                case "--warmup":
                    options.Warmup = ParseNonNegative(NextValue(args, ref index, argument), argument);
                    break;
                case "--staged-lines":
                    options.StagedLines = ParsePositive(NextValue(args, ref index, argument), argument);
                    break;
                case "--commits":
                    options.PushCommits = ParsePositive(NextValue(args, ref index, argument), argument);
                    break;
                case "--lines-per-commit":
                    options.LinesPerCommit = ParsePositive(NextValue(args, ref index, argument), argument);
                    break;
                case "--size-mb":
                    options.SizeMb = ParsePositive(NextValue(args, ref index, argument), argument);
                    break;
                case "--json":
                    options.JsonPath = NextValue(args, ref index, argument);
                    break;
                case "--keep":
                    options.KeepWorkspace = true;
                    break;
                default:
                    throw new ArgumentException($"未知参数：{argument}", nameof(args));
            }
        }

        options.ExecutablePath = ResolveExecutable(options.ExecutablePath);
        options.Validate();
        return options;
    }

    /// <summary>判断本次运行是否包含指定场景（<c>all</c> 包含全部）。</summary>
    /// <param name="scenario">场景名。</param>
    /// <returns>包含返回 <see langword="true"/>。</returns>
    internal bool Includes(string scenario) =>
        Scenario == "all" || string.Equals(Scenario, scenario, StringComparison.Ordinal);

    /// <summary>取测量次数（未显式指定时使用场景默认值）。</summary>
    /// <param name="fallback">场景默认值。</param>
    /// <returns>测量次数。</returns>
    internal int ResolveRuns(int fallback) => Runs ?? fallback;

    /// <summary>取预热次数（未显式指定时使用场景默认值）。</summary>
    /// <param name="fallback">场景默认值。</param>
    /// <returns>预热次数。</returns>
    internal int ResolveWarmup(int fallback) => Warmup ?? fallback;

    private static string? ResolveExecutable(string? explicitPath)
    {
        string? candidate = string.IsNullOrWhiteSpace(explicitPath)
            ? Environment.GetEnvironmentVariable(EXECUTABLE_ENV)
            : explicitPath;
        if (string.IsNullOrWhiteSpace(candidate))
        {
            string fallback = Path.GetFullPath(DEFAULT_EXECUTABLE);
            return File.Exists(fallback) ? fallback : null;
        }

        return Path.GetFullPath(candidate);
    }

    private static string NextValue(string[] args, ref int index, string option)
    {
        if (index + 1 >= args.Length)
        {
            throw new ArgumentException($"{option} 缺少取值。", nameof(args));
        }

        index++;
        return args[index];
    }

    private static int ParsePositive(string value, string option)
    {
        int parsed = ParseNonNegative(value, option);
        if (parsed == 0)
        {
            throw new ArgumentException($"{option} 必须为正整数：{value}", nameof(value));
        }

        return parsed;
    }

    private static int ParseNonNegative(string value, string option)
    {
        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed) || parsed < 0)
        {
            throw new ArgumentException($"{option} 必须为非负整数：{value}", nameof(value));
        }

        return parsed;
    }

    private void Validate()
    {
        if (Includes("startup") || Includes("staged") || Includes("push"))
        {
            if (ExecutablePath is null)
            {
                throw new ArgumentException(
                    "startup / staged / push 需要发布版可执行文件：请传 --exe <path> 或设置 GITGUARD_BENCH_EXE。");
            }

            if (!File.Exists(ExecutablePath))
            {
                throw new ArgumentException($"可执行文件不存在：{ExecutablePath}");
            }
        }

        if (StagedLines < 10)
        {
            throw new ArgumentException("--staged-lines 至少为 10。");
        }
    }
}
