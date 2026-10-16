using System.Text.Json;
using FluentAssertions;
using GitGuard.Core.Model;
using GitGuard.Core.Policy;
using GitGuard.Core.Services;
using GitGuard.Infrastructure.Sqlite;
using Microsoft.Data.Sqlite;

namespace GitGuard.Infrastructure.Tests;

/// <summary>SqliteAuditStore 读写集成测试（真实临时数据库文件；05-数据库设计 §4-§5、Audit.md §4）。</summary>
public sealed class SqliteAuditStoreTests
{
    private static readonly DateTimeOffset SampleStartedAt = new DateTimeOffset(2026, 10, 16, 8, 30, 0, TimeSpan.Zero);

    [Fact]
    public void SaveScanRun_CleanRun_RoundtripsThroughFindScanRun()
    {
        using var directory = new TempDirectory();
        string dbPath = Path.Combine(directory.Root, "audit.db");
        var store = new SqliteAuditStore(dbPath, "9.9.9-test");
        var run = ScanRun.Create(directory.Root, ScanType.PreCommit, SampleStartedAt, 25, ScanResult.Clean, 0);

        store.SaveScanRun(run, new Dictionary<Finding, PolicyDecision>());
        ScanRun? loaded = store.FindScanRun(run.Id);

        loaded.Should().NotBeNull();
        loaded!.Id.Should().Be(run.Id);
        loaded.RepoPath.Should().Be(run.RepoPath);
        loaded.ScanType.Should().Be(ScanType.PreCommit);
        loaded.StartedAt.Should().Be(SampleStartedAt);
        loaded.DurationMs.Should().Be(25);
        loaded.Result.Should().Be(ScanResult.Clean);
        loaded.ExitCode.Should().Be(0);
        loaded.Findings.Should().BeEmpty();
    }

    [Fact]
    public void SaveScanRun_BlockedRunWithFindings_RoundtripsFindingIdentityAndFields()
    {
        using var directory = new TempDirectory();
        string dbPath = Path.Combine(directory.Root, "audit.db");
        var store = new SqliteAuditStore(dbPath);
        var withCommit = Finding.Create(
            "GGD/SECRET/AWS-AKID",
            "src/appsettings.json",
            18,
            Severity.Critical,
            Fingerprint.FromHex(new string('b', 64)),
            SnippetMasked.Create("AKIA****************"),
            "0123456789abcdef0123456789abcdef01234567");
        var withoutCommit = Finding.Create(
            "GGD/SECRET/GITHUB-PAT",
            "deploy/pipeline.yml",
            42,
            Severity.High,
            Fingerprint.FromHex(new string('c', 64)),
            SnippetMasked.Create("ghp_****************"));
        var run = ScanRun.Create(
            directory.Root,
            ScanType.PrePush,
            SampleStartedAt,
            null,
            ScanResult.Blocked,
            1,
            new[] { withCommit, withoutCommit });
        var decisions = new Dictionary<Finding, PolicyDecision>
        {
            [withCommit] = new PolicyDecision(PolicyAction.Block, false, Severity.Critical),
            [withoutCommit] = new PolicyDecision(PolicyAction.Block, false, Severity.High),
        };

        store.SaveScanRun(run, decisions);
        ScanRun? loaded = store.FindScanRun(run.Id);

        loaded.Should().NotBeNull();
        loaded!.Findings.Select(finding => finding.Id).Should().Equal(withCommit.Id, withoutCommit.Id);

        Finding first = loaded.Findings[0];
        first.RuleId.Should().Be("GGD/SECRET/AWS-AKID");
        first.FilePath.Should().Be("src/appsettings.json");
        first.LineNumber.Should().Be(18);
        first.Severity.Should().Be(Severity.Critical);
        first.Fingerprint.Should().Be(withCommit.Fingerprint);
        first.SnippetMasked.Should().Be(withCommit.SnippetMasked);
        first.CommitSha.Should().Be("0123456789abcdef0123456789abcdef01234567");
        loaded.Findings[1].CommitSha.Should().BeNull();
    }

    [Fact]
    public void SaveScanRun_PersistsRawColumnsWithInjectedVersion()
    {
        using var directory = new TempDirectory();
        string dbPath = Path.Combine(directory.Root, "audit.db");
        var store = new SqliteAuditStore(dbPath, gitGuardVersion: "9.9.9-test");
        var run = ScanRun.Create(directory.Root, ScanType.PreCommit, SampleStartedAt, null, ScanResult.Clean, 0);

        store.SaveScanRun(run, new Dictionary<Finding, PolicyDecision>());

        using SqliteConnection connection = SqliteTestHelpers.OpenRawConnection(dbPath);
        SqliteTestHelpers.ReadScalarString(connection, "SELECT gitguard_version FROM scan_runs;").Should().Be("9.9.9-test");
        SqliteTestHelpers.ReadScalarString(connection, "SELECT scan_type FROM scan_runs;").Should().Be("PreCommit");
        SqliteTestHelpers.ReadScalarString(connection, "SELECT result FROM scan_runs;").Should().Be("Clean");
        SqliteTestHelpers.ReadScalarLong(connection, "SELECT exit_code FROM scan_runs;").Should().Be(0);
        SqliteTestHelpers.ReadScalarLong(connection, "SELECT started_at FROM scan_runs;")
            .Should().Be(SampleStartedAt.ToUnixTimeMilliseconds());
        SqliteTestHelpers.ReadScalarLong(connection, "SELECT COUNT(*) FROM scan_runs WHERE duration_ms IS NULL;").Should().Be(1);
    }

    [Fact]
    public void SaveScanRun_MissingDecisionForFinding_ThrowsArgumentException()
    {
        using var directory = new TempDirectory();
        string dbPath = Path.Combine(directory.Root, "audit.db");
        var store = new SqliteAuditStore(dbPath);
        var finding = CreateFinding();
        var run = ScanRun.Create(directory.Root, ScanType.PreCommit, SampleStartedAt, 12, ScanResult.Warned, 0, new[] { finding });

        Action act = () => store.SaveScanRun(run, new Dictionary<Finding, PolicyDecision>());

        act.Should().Throw<ArgumentException>().WithMessage($"*{finding.Id}*");
    }

    [Fact]
    public void SaveScanRun_WritesScanCompletedAuditLogEvent()
    {
        using var directory = new TempDirectory();
        string dbPath = Path.Combine(directory.Root, "audit.db");
        var store = new SqliteAuditStore(dbPath);
        var finding = CreateFinding();
        var run = ScanRun.Create(directory.Root, ScanType.PreCommit, SampleStartedAt, 12, ScanResult.Blocked, 1, new[] { finding });
        var decisions = new Dictionary<Finding, PolicyDecision>
        {
            [finding] = new PolicyDecision(PolicyAction.Block, false, Severity.Critical),
        };

        store.SaveScanRun(run, decisions);

        using SqliteConnection connection = SqliteTestHelpers.OpenRawConnection(dbPath);
        SqliteTestHelpers.ReadScalarString(connection, "SELECT category FROM audit_logs ORDER BY seq DESC LIMIT 1;").Should().Be("Scan");
        SqliteTestHelpers.ReadScalarString(connection, "SELECT event_type FROM audit_logs ORDER BY seq DESC LIMIT 1;").Should().Be("ScanCompleted");

        string payloadJson = SqliteTestHelpers.ReadScalarString(connection, "SELECT payload_json FROM audit_logs ORDER BY seq DESC LIMIT 1;");
        using var payload = JsonDocument.Parse(payloadJson);
        payload.RootElement.GetProperty("RunId").GetString().Should().Be(run.Id.ToString());
        payload.RootElement.GetProperty("Type").GetString().Should().Be("PreCommit");
        payload.RootElement.GetProperty("Result").GetString().Should().Be("Blocked");
        payload.RootElement.GetProperty("ExitCode").GetInt32().Should().Be(1);
        payload.RootElement.GetProperty("FindingsCount").GetInt32().Should().Be(1);
        payload.RootElement.GetProperty("BlockedCount").GetInt32().Should().Be(1);
    }

    [Fact]
    public void SaveScanRun_Decisions_PersistActionNameOrAllowlistMarker()
    {
        using var directory = new TempDirectory();
        string dbPath = Path.Combine(directory.Root, "audit.db");
        var store = new SqliteAuditStore(dbPath);
        var blocked = CreateFinding(ruleId: "GGD/SECRET/AWS-AKID");
        var ignored = CreateFinding(ruleId: "GGD/SECRET/GITHUB-PAT");
        var warned = CreateFinding(ruleId: "GGD/SECRET/SLACK-TOKEN");
        var run = ScanRun.Create(directory.Root, ScanType.PreCommit, SampleStartedAt, 12, ScanResult.Blocked, 1, new[] { blocked, ignored, warned });
        var decisions = new Dictionary<Finding, PolicyDecision>
        {
            [blocked] = new PolicyDecision(PolicyAction.Block, false, Severity.Critical),
            [ignored] = new PolicyDecision(PolicyAction.Allow, true, Severity.Critical),
            [warned] = new PolicyDecision(PolicyAction.Warn, false, Severity.Critical),
        };

        store.SaveScanRun(run, decisions);

        using SqliteConnection connection = SqliteTestHelpers.OpenRawConnection(dbPath);
        SqliteTestHelpers.ReadStringColumn(connection, "SELECT action FROM findings ORDER BY rowid;")
            .Should().Equal("Block", "AllowedByAllowlist", "Warn");
    }

    [Fact]
    public void SaveScanRun_CanaryToken_NotPresentInDatabaseBytes()
    {
        using var directory = new TempDirectory();
        string dbPath = Path.Combine(directory.Root, "audit.db");
        var store = new SqliteAuditStore(dbPath);
        var fingerprinter = new Fingerprinter(new byte[32]);
        var masked = Masker.Mask(TestData.CanaryToken);
        var finding = Finding.Create(
            "GGD/SECRET/AWS-AKID",
            "src/appsettings.json",
            18,
            Severity.Critical,
            fingerprinter.Compute(TestData.CanaryToken),
            masked);
        var run = ScanRun.Create(directory.Root, ScanType.PreCommit, SampleStartedAt, 12, ScanResult.Blocked, 1, new[] { finding });
        var decisions = new Dictionary<Finding, PolicyDecision>
        {
            [finding] = new PolicyDecision(PolicyAction.Block, false, Severity.Critical),
        };

        store.SaveScanRun(run, decisions);

        // TC-DR-02-01（09-日志设计 §9.3）：金丝雀原文不得出现在审计库任何落盘字节中
        string databaseText = SqliteTestHelpers.ReadDatabaseFilesText(dbPath);
        databaseText.Should().NotContain(TestData.CanaryToken);
        databaseText.Should().NotContain("AKIA_SENTINEL");

        ScanRun? loaded = store.FindScanRun(run.Id);
        loaded!.Findings[0].SnippetMasked.Value.Should().Be(masked.Value);
    }

    [Fact]
    public void SaveScanRun_ThenReopenNewStoreInstance_DataSurvives()
    {
        using var directory = new TempDirectory();
        string dbPath = Path.Combine(directory.Root, "audit.db");
        var run = ScanRun.Create(directory.Root, ScanType.Manual, SampleStartedAt, 3, ScanResult.Clean, 0);
        new SqliteAuditStore(dbPath).SaveScanRun(run, new Dictionary<Finding, PolicyDecision>());

        ScanRun? loaded = new SqliteAuditStore(dbPath).FindScanRun(run.Id);

        loaded.Should().NotBeNull();
        loaded!.Id.Should().Be(run.Id);
        loaded.Result.Should().Be(ScanResult.Clean);
    }

    [Fact]
    public void FindScanRun_UnknownId_ReturnsNull()
    {
        using var directory = new TempDirectory();
        string dbPath = Path.Combine(directory.Root, "audit.db");
        var store = new SqliteAuditStore(dbPath);

        ScanRun? loaded = store.FindScanRun(Guid.CreateVersion7());

        loaded.Should().BeNull();
    }

    [Fact]
    public void FindScanRun_EmptyId_ThrowsArgumentOutOfRangeException()
    {
        using var directory = new TempDirectory();
        string dbPath = Path.Combine(directory.Root, "audit.db");
        var store = new SqliteAuditStore(dbPath);

        Action act = () => store.FindScanRun(Guid.Empty);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void ListRecentScanRuns_OrdersByStartedAtDescendingAndHonorsLimit()
    {
        using var directory = new TempDirectory();
        string dbPath = Path.Combine(directory.Root, "audit.db");
        var store = new SqliteAuditStore(dbPath);
        var emptyDecisions = new Dictionary<Finding, PolicyDecision>();
        var oldest = ScanRun.Create(directory.Root, ScanType.Manual, SampleStartedAt.AddMinutes(-2), 1, ScanResult.Clean, 0);
        var middle = ScanRun.Create(directory.Root, ScanType.Manual, SampleStartedAt.AddMinutes(-1), 1, ScanResult.Clean, 0);
        var newest = ScanRun.Create(directory.Root, ScanType.Manual, SampleStartedAt, 1, ScanResult.Clean, 0);

        // 乱序写入：排序必须来自查询（started_at DESC），而非插入顺序
        store.SaveScanRun(middle, emptyDecisions);
        store.SaveScanRun(oldest, emptyDecisions);
        store.SaveScanRun(newest, emptyDecisions);

        IReadOnlyList<ScanRun> recent = store.ListRecentScanRuns(directory.Root, 2);

        recent.Select(run => run.Id).Should().Equal(newest.Id, middle.Id);
    }

    [Fact]
    public void ListRecentScanRuns_FiltersByRepoPathAndNormalizesTrailingSeparator()
    {
        using var directoryA = new TempDirectory();
        using var directoryB = new TempDirectory();
        string dbPath = Path.Combine(directoryA.Root, "audit.db");
        var store = new SqliteAuditStore(dbPath);
        var emptyDecisions = new Dictionary<Finding, PolicyDecision>();
        var runA = ScanRun.Create(directoryA.Root, ScanType.Manual, SampleStartedAt, 1, ScanResult.Clean, 0);
        var runB = ScanRun.Create(directoryB.Root, ScanType.Manual, SampleStartedAt, 1, ScanResult.Clean, 0);
        store.SaveScanRun(runA, emptyDecisions);
        store.SaveScanRun(runB, emptyDecisions);

        IReadOnlyList<ScanRun> recent = store.ListRecentScanRuns(directoryA.Root + Path.DirectorySeparatorChar, 10);

        recent.Should().ContainSingle();
        recent[0].Id.Should().Be(runA.Id);
    }

    [Fact]
    public void ListRecentScanRuns_NonPositiveLimit_ThrowsArgumentOutOfRangeException()
    {
        using var directory = new TempDirectory();
        string dbPath = Path.Combine(directory.Root, "audit.db");
        var store = new SqliteAuditStore(dbPath);

        Action act = () => store.ListRecentScanRuns(directory.Root, 0);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void ListRecentScanRuns_WhitespaceRepoPath_ThrowsArgumentException()
    {
        using var directory = new TempDirectory();
        string dbPath = Path.Combine(directory.Root, "audit.db");
        var store = new SqliteAuditStore(dbPath);

        Action act = () => store.ListRecentScanRuns("   ", 10);

        act.Should().Throw<ArgumentException>();
    }

    private static Finding CreateFinding(string ruleId = "GGD/SECRET/AWS-AKID") =>
        Finding.Create(
            ruleId,
            "src/appsettings.json",
            18,
            Severity.Critical,
            Fingerprint.FromHex(new string('a', 64)),
            SnippetMasked.Create("AKIA****************"));
}
