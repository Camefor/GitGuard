using FluentAssertions;
using GitGuard.Core.Model;
using GitGuard.Core.Policy;
using GitGuard.Infrastructure.Sqlite;

namespace GitGuard.Infrastructure.Tests;

/// <summary>SqliteAuditStore 审计查询与导出读取（06-API接口设计 §5.6 / FR-023）。</summary>
public sealed class SqliteAuditStoreQueryTests
{
    private static readonly DateTimeOffset _sampleStartedAt = new DateTimeOffset(2026, 10, 21, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void QueryScanRuns_OrdersByStartedAtDescendingAndPaginates()
    {
        using var directory = new TempDirectory();
        var store = CreateStore(directory);
        var oldest = SaveCleanRun(store, directory.Root, _sampleStartedAt.AddMinutes(-2));
        var middle = SaveCleanRun(store, directory.Root, _sampleStartedAt.AddMinutes(-1));
        var newest = SaveCleanRun(store, directory.Root, _sampleStartedAt);

        AuditRunPage firstPage = store.QueryScanRuns(new AuditQuery(limit: 2, offset: 0));
        AuditRunPage secondPage = store.QueryScanRuns(new AuditQuery(limit: 2, offset: 2));

        firstPage.TotalCount.Should().Be(3);
        firstPage.Runs.Select(run => run.RunId).Should().Equal(newest.Id, middle.Id);
        secondPage.TotalCount.Should().Be(3);
        secondPage.Runs.Select(run => run.RunId).Should().Equal(oldest.Id);
    }

    [Fact]
    public void QueryScanRuns_TieOnStartedAt_ReturnsLaterInsertedFirst()
    {
        using var directory = new TempDirectory();
        var store = CreateStore(directory);
        var firstInserted = SaveCleanRun(store, directory.Root, _sampleStartedAt);
        var secondInserted = SaveCleanRun(store, directory.Root, _sampleStartedAt);

        AuditRunPage page = store.QueryScanRuns(new AuditQuery());

        page.Runs.Select(run => run.RunId).Should().Equal(secondInserted.Id, firstInserted.Id);
    }

    [Fact]
    public void QueryScanRuns_FiltersSinceInclusiveAndResult()
    {
        using var directory = new TempDirectory();
        var store = CreateStore(directory);
        SaveCleanRun(store, directory.Root, _sampleStartedAt);
        var blocked = SaveBlockedRun(store, directory.Root, _sampleStartedAt.AddMinutes(1), findingCount: 1);
        SaveWarnedRun(store, directory.Root, _sampleStartedAt.AddMinutes(2));

        AuditRunPage sinceFiltered = store.QueryScanRuns(new AuditQuery(since: _sampleStartedAt.AddMinutes(1)));
        AuditRunPage resultFiltered = store.QueryScanRuns(new AuditQuery(result: ScanResult.Blocked));
        AuditRunPage combined = store.QueryScanRuns(
            new AuditQuery(since: _sampleStartedAt.AddMinutes(2), result: ScanResult.Blocked));

        sinceFiltered.TotalCount.Should().Be(2);
        sinceFiltered.Runs.Select(run => run.RunId).Should().Contain(blocked.Id);
        resultFiltered.TotalCount.Should().Be(1);
        resultFiltered.Runs[0].RunId.Should().Be(blocked.Id);
        resultFiltered.Runs[0].Result.Should().Be(ScanResult.Blocked);
        combined.TotalCount.Should().Be(0);
        combined.Runs.Should().BeEmpty();
    }

    [Fact]
    public void QueryScanRuns_ReportsFindingCountAndRunFields()
    {
        using var directory = new TempDirectory();
        var store = CreateStore(directory);
        ScanRun run = SaveBlockedRun(store, directory.Root, _sampleStartedAt, findingCount: 2);

        AuditRunSummary summary = store.QueryScanRuns(new AuditQuery()).Runs.Should().ContainSingle().Subject;

        summary.RunId.Should().Be(run.Id);
        summary.RepoPath.Should().Be(Path.TrimEndingDirectorySeparator(directory.Root));
        summary.ScanType.Should().Be(ScanType.PreCommit);
        summary.StartedAt.Should().Be(_sampleStartedAt);
        summary.DurationMs.Should().Be(15);
        summary.Result.Should().Be(ScanResult.Blocked);
        summary.ExitCode.Should().Be(1);
        summary.FindingCount.Should().Be(2);
    }

    [Fact]
    public void QueryScanRuns_IncludesBypassRuns()
    {
        using var directory = new TempDirectory();
        var store = CreateStore(directory);
        var run = ScanRun.Create(directory.Root, ScanType.Bypass, _sampleStartedAt, null, ScanResult.Clean, 0);
        store.SaveBypassEvent(run, BypassReason.NoVerify, "0123456789abcdef0123456789abcdef01234567");

        AuditRunSummary summary = store.QueryScanRuns(new AuditQuery()).Runs.Should().ContainSingle().Subject;

        summary.RunId.Should().Be(run.Id);
        summary.ScanType.Should().Be(ScanType.Bypass);
        summary.DurationMs.Should().BeNull();
        summary.FindingCount.Should().Be(0);
    }

    [Fact]
    public void QueryScanRuns_NullQuery_Throws()
    {
        using var directory = new TempDirectory();
        var store = CreateStore(directory);

        Action act = () => store.QueryScanRuns(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void ListScanRunsForExport_ReturnsAllRunsWithFindingsNewestFirst()
    {
        using var directory = new TempDirectory();
        var store = CreateStore(directory);
        var older = SaveBlockedRun(store, directory.Root, _sampleStartedAt.AddMinutes(-1), findingCount: 2);
        var newer = SaveBlockedRun(store, directory.Root, _sampleStartedAt, findingCount: 1);

        IReadOnlyList<ScanRun> runs = store.ListScanRunsForExport();

        runs.Select(run => run.Id).Should().Equal(newer.Id, older.Id);
        runs[0].Findings.Should().HaveCount(1);
        runs[1].Findings.Should().HaveCount(2);
        runs[1].Findings.Select(finding => finding.RuleId).Should().Equal("GGD/SECRET/AWS-AKID", "GGD/SECRET/GENERIC-PASSWORD");
    }

    private static SqliteAuditStore CreateStore(TempDirectory directory) =>
        new SqliteAuditStore(Path.Combine(directory.Root, "audit.db"));

    private static ScanRun SaveCleanRun(SqliteAuditStore store, string repoPath, DateTimeOffset startedAt)
    {
        var run = ScanRun.Create(repoPath, ScanType.PreCommit, startedAt, 5, ScanResult.Clean, 0);
        store.SaveScanRun(run, new Dictionary<Finding, PolicyDecision>());
        return run;
    }

    private static ScanRun SaveWarnedRun(SqliteAuditStore store, string repoPath, DateTimeOffset startedAt)
    {
        var finding = CreateFinding("GGD/SECRET/AWS-AKID");
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
        SqliteAuditStore store,
        string repoPath,
        DateTimeOffset startedAt,
        int findingCount)
    {
        var findings = new[]
        {
            CreateFinding("GGD/SECRET/AWS-AKID"),
            CreateFinding("GGD/SECRET/GENERIC-PASSWORD"),
        }.Take(findingCount).ToArray();
        var run = ScanRun.Create(repoPath, ScanType.PreCommit, startedAt, 15, ScanResult.Blocked, 1, findings);
        store.SaveScanRun(
            run,
            findings.ToDictionary(
                static finding => finding,
                static finding => new PolicyDecision(PolicyAction.Block, false, Severity.Critical)));
        return run;
    }

    private static Finding CreateFinding(string ruleId) =>
        Finding.Create(
            ruleId,
            "src/appsettings.json",
            18,
            Severity.Critical,
            Fingerprint.FromHex(new string('a', 64)),
            SnippetMasked.Create("AKIA****************"));
}
