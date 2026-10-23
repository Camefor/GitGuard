using System.Globalization;
using System.Text.Json;
using GitGuard.Application.Audit;
using GitGuard.Application.Composition;
using GitGuard.Core.Model;
using GitGuard.Desktop.Services;

namespace GitGuard.Desktop.Smoke;

/// <summary>
/// 桌面端无界面冒烟模式（GGD-0047）：在 Avalonia 初始化之前执行，读取与日志页相同的
/// <see cref="IAuditOperations"/>，验证安装包能够展示 CLI 已写入的全部审计数据。
/// 仅用于发行流水线与本机打包验证，不作为面向用户的常规入口。
/// </summary>
internal static class DesktopSmokeTest
{
    /// <summary>冒烟模式命令行开关。</summary>
    internal const string ARGUMENT = "--smoke-test";

    private const string DATA_DIRECTORY_ARGUMENT = "--data-directory";
    private const string EXPECTED_RUNS_ARGUMENT = "--expected-runs";
    private const string REPORT_ARGUMENT = "--report";
    private const string EXPORT_ARGUMENT = "--export";

    /// <summary>判断参数中是否包含冒烟模式开关。</summary>
    /// <param name="args">进程命令行参数。</param>
    /// <returns>包含 <c>--smoke-test</c> 时返回 <see langword="true"/>。</returns>
    /// <exception cref="ArgumentNullException"><paramref name="args"/> 为 <see langword="null"/>。</exception>
    internal static bool IsRequested(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);
        return args.Contains(ARGUMENT, StringComparer.Ordinal);
    }

    /// <summary>执行冒烟检查并返回进程退出码。</summary>
    /// <param name="args">进程命令行参数。</param>
    /// <returns>检查通过返回 0；参数错误、读取失败或断言不满足返回 1。</returns>
    /// <exception cref="ArgumentNullException"><paramref name="args"/> 为 <see langword="null"/>。</exception>
    internal static int Run(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        if (!TryParse(args, out SmokeOptions? options, out string? parseError) || options is null)
        {
            Console.Error.WriteLine($"desktop smoke: {parseError}");
            return 1;
        }

        string version = GitGuardSession.ResolveDefaultVersion();
        SmokeResult result;
        try
        {
            var session = new GitGuardSession(
                new GitGuardSessionOptions
                {
                    WorkingDirectory = AppContext.BaseDirectory,
                    DataDirectory = options.DataDirectory,
                });
            version = session.Version;
            result = Execute(session, options);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            result = new SmokeResult(
                isSuccessful: false,
                version: version,
                dataDirectory: options.DataDirectory ?? string.Empty,
                auditRunCount: 0,
                expectedRuns: options.ExpectedRuns,
                latestRunId: null,
                latestRunResult: null,
                latestRunRepoPath: null,
                latestRunFindingCount: 0,
                exportedRunCount: null,
                exportedFindingCount: null,
                error: $"{ex.GetType().Name}: {ex.Message}");
        }

        if (options.ReportPath is not null)
        {
            try
            {
                WriteReport(options.ReportPath, result);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
                Console.Error.WriteLine($"desktop smoke: failed to write report: {ex.Message}");
                return 1;
            }
        }

        if (!result.IsSuccessful)
        {
            Console.Error.WriteLine($"desktop smoke FAILED: {result.Error}");
            return 1;
        }

        Console.Out.WriteLine(
            $"desktop smoke OK: {result.AuditRunCount} audit run(s) visible; latest findings={result.LatestRunFindingCount}");
        if (result.ExportedRunCount is { } exportedRuns)
        {
            Console.Out.WriteLine($"desktop smoke export OK: {exportedRuns} audit run(s)");
        }

        return 0;
    }

    private static SmokeResult Execute(GitGuardSession session, SmokeOptions options)
    {
        // 复用日志页的 IAuditOperations，确保冒烟覆盖与 UI 相同的查询路径
        var operations = new AuditOperations(session);
        AuditRunPage page = operations.QueryAsync(new AuditQuery()).GetAwaiter().GetResult();
        AuditRunSummary? latest = page.Runs.IsDefaultOrEmpty ? null : page.Runs[0];

        int latestFindingCount = 0;
        if (latest is not null)
        {
            // 读取单次运行明细，验证 Finding 脱敏数据同样可被桌面端展示
            ScanRun detail = session.CreateAuditView().Execute(latest.RunId)
                ?? throw new InvalidOperationException($"审计运行 {latest.RunId} 的明细不可读取");
            latestFindingCount = detail.Findings.Length;
        }

        int? exportedRunCount = null;
        int? exportedFindingCount = null;
        if (options.ExportPath is not null)
        {
            AuditExportReport export = operations
                .ExportAsync(options.ExportPath, AuditExportFormat.Json)
                .GetAwaiter()
                .GetResult();
            exportedRunCount = export.RunCount;
            exportedFindingCount = export.FindingCount;
        }

        int exported = exportedRunCount.GetValueOrDefault();
        bool exportMatches = exportedRunCount is null || exported == page.TotalCount;
        bool isSuccessful = page.TotalCount >= options.ExpectedRuns && exportMatches;
        string? error = null;
        if (page.TotalCount < options.ExpectedRuns)
        {
            error = $"expected at least {options.ExpectedRuns.ToString(CultureInfo.InvariantCulture)} audit run(s), found {page.TotalCount.ToString(CultureInfo.InvariantCulture)}";
        }
        else if (!exportMatches)
        {
            error = $"desktop export contained {exported.ToString(CultureInfo.InvariantCulture)} run(s), query reported {page.TotalCount.ToString(CultureInfo.InvariantCulture)}";
        }

        return new SmokeResult(
            isSuccessful,
            session.Version,
            session.UserDataDirectory,
            page.TotalCount,
            options.ExpectedRuns,
            latest?.RunId,
            latest?.Result,
            latest?.RepoPath,
            latestFindingCount,
            exportedRunCount,
            exportedFindingCount,
            error);
    }

    private static bool TryParse(string[] args, out SmokeOptions? options, out string? error)
    {
        options = null;
        error = null;
        string? dataDirectory = null;
        string? reportPath = null;
        string? exportPath = null;
        int expectedRuns = 0;
        bool requested = false;

        for (int index = 0; index < args.Length; index++)
        {
            string argument = args[index];
            switch (argument)
            {
                case ARGUMENT:
                    requested = true;
                    break;

                case DATA_DIRECTORY_ARGUMENT:
                    if (!TryReadValue(args, ref index, argument, out dataDirectory, out error))
                    {
                        return false;
                    }

                    break;

                case EXPECTED_RUNS_ARGUMENT:
                    if (!TryReadValue(args, ref index, argument, out string? rawExpected, out error))
                    {
                        return false;
                    }

                    if (!int.TryParse(rawExpected, NumberStyles.None, CultureInfo.InvariantCulture, out expectedRuns))
                    {
                        error = $"{EXPECTED_RUNS_ARGUMENT} requires a non-negative integer";
                        return false;
                    }

                    break;

                case REPORT_ARGUMENT:
                    if (!TryReadValue(args, ref index, argument, out reportPath, out error))
                    {
                        return false;
                    }

                    break;

                case EXPORT_ARGUMENT:
                    if (!TryReadValue(args, ref index, argument, out exportPath, out error))
                    {
                        return false;
                    }

                    break;

                default:
                    error = $"unknown argument '{argument}'";
                    return false;
            }
        }

        if (!requested)
        {
            error = $"missing {ARGUMENT}";
            return false;
        }

        options = new SmokeOptions(dataDirectory, expectedRuns, reportPath, exportPath);
        return true;
    }

    private static bool TryReadValue(
        string[] args,
        ref int index,
        string argument,
        out string? value,
        out string? error)
    {
        value = null;
        error = null;
        if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
        {
            error = $"{argument} requires a value";
            return false;
        }

        value = args[++index];
        return true;
    }

    private static void WriteReport(string reportPath, SmokeResult result)
    {
        string fullPath = Path.GetFullPath(reportPath);
        string? directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        using var stream = File.Create(fullPath);
        using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true });
        writer.WriteStartObject();
        writer.WriteString("schema", "gitguard.desktop.smoke.v1");
        writer.WriteBoolean("ok", result.IsSuccessful);
        writer.WriteString("version", result.Version);
        writer.WriteString("dataDirectory", result.DataDirectory);
        writer.WriteNumber("auditRunCount", result.AuditRunCount);
        writer.WriteNumber("expectedRuns", result.ExpectedRuns);

        if (result.LatestRunId is { } runId)
        {
            writer.WriteString("latestRunId", runId);
        }
        else
        {
            writer.WriteNull("latestRunId");
        }

        if (result.LatestRunResult is { } scanResult)
        {
            writer.WriteString("latestRunResult", scanResult.ToString());
        }
        else
        {
            writer.WriteNull("latestRunResult");
        }

        if (result.LatestRunRepoPath is not null)
        {
            writer.WriteString("latestRunRepoPath", result.LatestRunRepoPath);
        }
        else
        {
            writer.WriteNull("latestRunRepoPath");
        }

        writer.WriteNumber("latestRunFindingCount", result.LatestRunFindingCount);

        if (result.ExportedRunCount is { } exportedRuns)
        {
            writer.WriteNumber("exportedRunCount", exportedRuns);
        }
        else
        {
            writer.WriteNull("exportedRunCount");
        }

        if (result.ExportedFindingCount is { } exportedFindings)
        {
            writer.WriteNumber("exportedFindingCount", exportedFindings);
        }
        else
        {
            writer.WriteNull("exportedFindingCount");
        }

        if (result.Error is not null)
        {
            writer.WriteString("error", result.Error);
        }
        else
        {
            writer.WriteNull("error");
        }

        writer.WriteEndObject();
    }

    private sealed class SmokeOptions
    {
        internal SmokeOptions(string? dataDirectory, int expectedRuns, string? reportPath, string? exportPath)
        {
            DataDirectory = dataDirectory;
            ExpectedRuns = expectedRuns;
            ReportPath = reportPath;
            ExportPath = exportPath;
        }

        internal string? DataDirectory { get; }

        internal int ExpectedRuns { get; }

        internal string? ReportPath { get; }

        internal string? ExportPath { get; }
    }

    private sealed class SmokeResult
    {
        internal SmokeResult(
            bool isSuccessful,
            string version,
            string dataDirectory,
            int auditRunCount,
            int expectedRuns,
            Guid? latestRunId,
            ScanResult? latestRunResult,
            string? latestRunRepoPath,
            int latestRunFindingCount,
            int? exportedRunCount,
            int? exportedFindingCount,
            string? error)
        {
            IsSuccessful = isSuccessful;
            Version = version;
            DataDirectory = dataDirectory;
            AuditRunCount = auditRunCount;
            ExpectedRuns = expectedRuns;
            LatestRunId = latestRunId;
            LatestRunResult = latestRunResult;
            LatestRunRepoPath = latestRunRepoPath;
            LatestRunFindingCount = latestRunFindingCount;
            ExportedRunCount = exportedRunCount;
            ExportedFindingCount = exportedFindingCount;
            Error = error;
        }

        internal bool IsSuccessful { get; }

        internal string Version { get; }

        internal string DataDirectory { get; }

        internal int AuditRunCount { get; }

        internal int ExpectedRuns { get; }

        internal Guid? LatestRunId { get; }

        internal ScanResult? LatestRunResult { get; }

        internal string? LatestRunRepoPath { get; }

        internal int LatestRunFindingCount { get; }

        internal int? ExportedRunCount { get; }

        internal int? ExportedFindingCount { get; }

        internal string? Error { get; }
    }
}
