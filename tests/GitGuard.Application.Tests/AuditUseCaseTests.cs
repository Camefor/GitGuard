using System.Text;
using System.Text.Json;
using FluentAssertions;
using GitGuard.Application.Audit;
using GitGuard.Core;
using GitGuard.Core.Model;
using GitGuard.Core.Policy;

namespace GitGuard.Application.Tests;

/// <summary>
/// 审计列表 / 导出用例测试（<c>06-API接口设计</c> §5.6 / FR-023）：筛选分页透传、导出字段仅含脱敏形态、
/// CSV 转义与公式注入防护，以及原子写入与失败映射（<see cref="AuditExportException"/> → 退出码 31）。
/// </summary>
public sealed class AuditUseCaseTests
{
    private const string CSV_HEADER =
        "run_id,repo_path,scan_type,started_at,duration_ms,result,exit_code," +
        "finding_id,rule_id,file_path,line_number,severity,fingerprint,snippet_masked,commit_sha";

    private static readonly DateTimeOffset SampleNow = new DateTimeOffset(2026, 10, 21, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void List_PassesQueryThroughAndReturnsPage()
    {
        using var directory = new TempDirectory();
        var store = new RecordingAuditStore();
        SaveCleanRun(store, directory.Root, SampleNow.AddMinutes(-2));
        SaveWarnedRun(store, directory.Root, SampleNow.AddMinutes(-1));
        SaveBlockedRun(store, directory.Root, SampleNow, findingCount: 1);
        var useCase = new AuditListUseCase(store);

        AuditRunPage all = useCase.Execute(new AuditQuery());
        AuditRunPage filtered = useCase.Execute(
            new AuditQuery(limit: 1, offset: 0, since: SampleNow.AddMinutes(-1), result: ScanResult.Warned));

        all.TotalCount.Should().Be(3);
        all.Runs.Should().HaveCount(3);
        filtered.TotalCount.Should().Be(1);
        filtered.Runs.Should().ContainSingle().Which.Result.Should().Be(ScanResult.Warned);
    }

    [Fact]
    public void List_NullArguments_Throw()
    {
        Action createWithoutStore = () => _ = new AuditListUseCase(null!);
        Action executeWithoutQuery = () => new AuditListUseCase(new RecordingAuditStore()).Execute(null!);

        createWithoutStore.Should().Throw<ArgumentNullException>();
        executeWithoutQuery.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Export_Json_ContainsMaskedFieldsAndSummary()
    {
        using var directory = new TempDirectory();
        var store = new RecordingAuditStore();
        var blocked = SaveBlockedRun(store, directory.Root, SampleNow, findingCount: 2);
        SaveCleanRun(store, directory.Root, SampleNow.AddMinutes(1));
        string outputPath = Path.Combine(directory.Root, "audit.json");

        AuditExportReport report = CreateExportUseCase(store).Execute(outputPath, AuditExportFormat.Json);

        report.OutputPath.Should().Be(Path.GetFullPath(outputPath));
        report.Format.Should().Be(AuditExportFormat.Json);
        report.RunCount.Should().Be(2);
        report.FindingCount.Should().Be(2);

        using var document = JsonDocument.Parse(File.ReadAllText(outputPath));
        var root = document.RootElement;
        root.GetProperty("schema").GetString().Should().Be("gitguard.audit.export.v1");
        root.GetProperty("exported_at").GetString().Should().Be("2026-10-21T08:00:00.000Z");
        root.GetProperty("run_count").GetInt32().Should().Be(2);
        root.GetProperty("finding_count").GetInt32().Should().Be(2);

        var firstRun = root.GetProperty("runs")[0];
        firstRun.GetProperty("run_id").GetString().Should().Be(blocked.Id.ToString());
        firstRun.GetProperty("duration_ms").GetInt64().Should().Be(15);
        firstRun.GetProperty("findings").GetArrayLength().Should().Be(2);

        var finding = firstRun.GetProperty("findings")[0];
        finding.EnumerateObject().Select(static property => property.Name).Should().BeEquivalentTo(
            "finding_id",
            "rule_id",
            "file_path",
            "line_number",
            "severity",
            "fingerprint",
            "snippet_masked",
            "commit_sha");
        finding.GetProperty("snippet_masked").GetString().Should().Be("AKIA****************************");
        finding.GetProperty("fingerprint").GetString().Should().Be(new string('a', 64));
    }

    [Fact]
    public void Export_Csv_EscapesFieldsAndAddsFormulaGuard()
    {
        using var directory = new TempDirectory();
        var store = new RecordingAuditStore();
        SaveBlockedRunWithRiskyFields(store, directory.Root);
        SaveCleanRun(store, directory.Root, SampleNow.AddMinutes(1));
        string outputPath = Path.Combine(directory.Root, "audit.csv");

        AuditExportReport report = CreateExportUseCase(store).Execute(outputPath, AuditExportFormat.Csv);

        report.Format.Should().Be(AuditExportFormat.Csv);
        report.RunCount.Should().Be(2);
        report.FindingCount.Should().Be(2);

        byte[] bytes = File.ReadAllBytes(outputPath);
        bytes.Take(3).Should().Equal(new byte[] { 0xEF, 0xBB, 0xBF }, "CSV 需以 UTF-8 BOM 开头以便 Excel 识别编码");
        string content = Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        content.Should().Contain("\r\n");

        string[] lines = content.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        lines.Should().HaveCount(4, "表头 + 两条 Finding + 一条无 Finding 的运行级记录");
        lines[0].Should().Be(CSV_HEADER);
        lines[1].Should().Contain("\"src/a,b\"\"c.env\"", "含逗号与引号的字段需按 RFC 4180 转义");
        lines[2].Should().Contain("'=cmd.env", "以 '=' 开头的字段需加单引号前缀防公式注入");

        string[] cleanFields = lines[3].Split(',');
        cleanFields.Should().HaveCount(15);
        cleanFields[4].Should().BeEmpty("无耗时的运行导出空字段");
        cleanFields[6].Should().Be("0");
        cleanFields.Skip(7).Should().OnlyContain(static field => field.Length == 0);
    }

    [Fact]
    public void Export_OverwritesExistingFile()
    {
        using var directory = new TempDirectory();
        var store = new RecordingAuditStore();
        SaveCleanRun(store, directory.Root, SampleNow);
        string outputPath = Path.Combine(directory.Root, "audit.csv");
        File.WriteAllText(outputPath, "stale");

        CreateExportUseCase(store).Execute(outputPath, AuditExportFormat.Csv);

        File.ReadAllText(outputPath).Should().StartWith(CSV_HEADER).And.NotContain("stale");
    }

    [Fact]
    public void Export_OutputPathIsDirectory_ThrowsAuditExportException()
    {
        using var directory = new TempDirectory();
        var store = new RecordingAuditStore();
        SaveCleanRun(store, directory.Root, SampleNow);

        Action act = () => CreateExportUseCase(store).Execute(directory.Root, AuditExportFormat.Json);

        act.Should().Throw<AuditExportException>()
            .Which.ErrorCode.Should().Be(AuditExportException.AUDIT_EXPORT_FAILED);
    }

    [Fact]
    public void Export_EmptyOutputPathOrUndefinedFormat_Throws()
    {
        var useCase = CreateExportUseCase(new RecordingAuditStore());

        Action emptyPath = () => useCase.Execute("  ", AuditExportFormat.Json);
        Action undefinedFormat = () => useCase.Execute("audit.json", (AuditExportFormat)99);

        emptyPath.Should().Throw<ArgumentException>();
        undefinedFormat.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void StoreReadFailure_PropagatesSqliteException()
    {
        var store = new FailingAuditStore();

        Action list = () => new AuditListUseCase(store).Execute(new AuditQuery());
        Action export = () => new AuditExportUseCase(store, new FixedClock(SampleNow))
            .Execute("audit.json", AuditExportFormat.Json);

        list.Should().Throw<SqliteException>();
        export.Should().Throw<SqliteException>();
    }

    private static AuditExportUseCase CreateExportUseCase(RecordingAuditStore store) =>
        new AuditExportUseCase(store, new FixedClock(SampleNow));

    private static ScanRun SaveCleanRun(RecordingAuditStore store, string repoPath, DateTimeOffset startedAt)
    {
        var run = ScanRun.Create(repoPath, ScanType.PreCommit, startedAt, null, ScanResult.Clean, 0);
        store.SaveScanRun(run, new Dictionary<Finding, PolicyDecision>());
        return run;
    }

    private static ScanRun SaveWarnedRun(RecordingAuditStore store, string repoPath, DateTimeOffset startedAt)
    {
        var finding = CreateFinding("GGD/SECRET/AWS-AKID", "config/app.env", "AKIA****************************");
        var run = ScanRun.Create(repoPath, ScanType.PreCommit, startedAt, 8, ScanResult.Warned, 0, new[] { finding });
        store.SaveScanRun(
            run,
            new Dictionary<Finding, PolicyDecision>
            {
                [finding] = new PolicyDecision(PolicyAction.Warn, false, Severity.High),
            });
        return run;
    }

    private static ScanRun SaveBlockedRun(
        RecordingAuditStore store,
        string repoPath,
        DateTimeOffset startedAt,
        int findingCount)
    {
        var findings = new[]
        {
            CreateFinding("GGD/SECRET/AWS-AKID", "config/app.env", "AKIA****************************"),
            CreateFinding("GGD/SECRET/GENERIC-PASSWORD", "config/db.yml", "password=****************"),
        }.Take(findingCount).ToArray();
        var run = ScanRun.Create(repoPath, ScanType.PreCommit, startedAt, 15, ScanResult.Blocked, 1, findings);
        store.SaveScanRun(
            run,
            findings.ToDictionary(
                static finding => finding,
                static finding => new PolicyDecision(PolicyAction.Block, false, Severity.Critical)));
        return run;
    }

    private static void SaveBlockedRunWithRiskyFields(RecordingAuditStore store, string repoPath)
    {
        var findings = new[]
        {
            CreateFinding("GGD/SECRET/AWS-AKID", "src/a,b\"c.env", "AK,***"),
            CreateFinding("GGD/SECRET/GENERIC-PASSWORD", "=cmd.env", "AKIA****"),
        };
        var run = ScanRun.Create(repoPath, ScanType.PreCommit, SampleNow, 12, ScanResult.Blocked, 1, findings);
        store.SaveScanRun(
            run,
            findings.ToDictionary(
                static finding => finding,
                static finding => new PolicyDecision(PolicyAction.Block, false, Severity.Critical)));
    }

    private static Finding CreateFinding(string ruleId, string filePath, string snippetMasked) =>
        Finding.Create(
            ruleId,
            filePath,
            1,
            Severity.Critical,
            Fingerprint.FromHex(new string('a', 64)),
            SnippetMasked.Create(snippetMasked));

    private sealed class TempDirectory : IDisposable
    {
        internal TempDirectory()
        {
            Root = Path.Combine(Path.GetTempPath(), "gitguard-app-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
        }

        internal string Root { get; }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Root, recursive: true);
            }
            catch (IOException)
            {
                // 清理失败不影响测试结论，留待系统清理临时目录
            }
            catch (UnauthorizedAccessException)
            {
                // 权限竞态：同上
            }
        }
    }
}
