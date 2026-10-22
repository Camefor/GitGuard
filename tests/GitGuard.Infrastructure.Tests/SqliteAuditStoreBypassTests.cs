using System.Text.Json;
using FluentAssertions;
using GitGuard.Core.Model;
using GitGuard.Infrastructure.Sqlite;
using Microsoft.Data.Sqlite;

namespace GitGuard.Infrastructure.Tests;

/// <summary>
/// SqliteAuditStore 绕过事件写入集成测试（FR-050 / DR-06，05-数据库设计 §4.1 / §7）：
/// 绕过事件以 <c>scan_runs</c>（Bypass / Clean / 退出码 0 / 无耗时）与 <c>audit_logs</c>
/// （Bypass + BypassDetected）双写，同一事务提交。
/// </summary>
public sealed class SqliteAuditStoreBypassTests
{
    private const string COMMIT_SHA = "0123456789abcdef0123456789abcdef01234567";

    private static readonly DateTimeOffset _sampleDetectedAt = new DateTimeOffset(2026, 10, 16, 8, 30, 0, TimeSpan.Zero);

    [Fact]
    public void SaveBypassEvent_PersistsCleanBypassRunWithoutDuration()
    {
        using var directory = new TempDirectory();
        string dbPath = Path.Combine(directory.Root, "audit.db");
        var store = new SqliteAuditStore(dbPath);
        ScanRun run = CreateBypassRun(directory.Root);

        store.SaveBypassEvent(run, BypassReason.NoVerify, COMMIT_SHA);

        using SqliteConnection connection = SqliteTestHelpers.OpenRawConnection(dbPath);
        SqliteTestHelpers.ReadScalarLong(connection, "SELECT COUNT(*) FROM scan_runs;").Should().Be(1);
        SqliteTestHelpers.ReadScalarString(connection, "SELECT scan_type FROM scan_runs;").Should().Be("Bypass");
        SqliteTestHelpers.ReadScalarString(connection, "SELECT result FROM scan_runs;").Should().Be("Clean");
        SqliteTestHelpers.ReadScalarLong(connection, "SELECT exit_code FROM scan_runs;").Should().Be(0);
        SqliteTestHelpers.ReadScalarLong(connection, "SELECT COUNT(*) FROM scan_runs WHERE duration_ms IS NULL;").Should().Be(1);
        SqliteTestHelpers.ReadScalarString(connection, "SELECT repo_path FROM scan_runs;").Should().Be(directory.Root);
        SqliteTestHelpers.ReadScalarLong(connection, "SELECT COUNT(*) FROM findings;").Should().Be(0);
    }

    [Theory]
    [InlineData(BypassReason.NoVerify, "NoVerify")]
    [InlineData(BypassReason.GitGuardSkip, "GitGuardSkip")]
    public void SaveBypassEvent_WritesBypassDetectedEventWithReason(BypassReason reason, string expectedReason)
    {
        using var directory = new TempDirectory();
        string dbPath = Path.Combine(directory.Root, "audit.db");
        var store = new SqliteAuditStore(dbPath);
        ScanRun run = CreateBypassRun(directory.Root);

        store.SaveBypassEvent(run, reason, COMMIT_SHA);

        using SqliteConnection connection = SqliteTestHelpers.OpenRawConnection(dbPath);
        SqliteTestHelpers.ReadScalarLong(connection, "SELECT COUNT(*) FROM audit_logs;").Should().Be(1);
        SqliteTestHelpers.ReadScalarString(connection, "SELECT category FROM audit_logs;").Should().Be("Bypass");
        SqliteTestHelpers.ReadScalarString(connection, "SELECT event_type FROM audit_logs;").Should().Be("BypassDetected");
        SqliteTestHelpers.ReadScalarLong(connection, "SELECT occurred_at FROM audit_logs;")
            .Should().Be(_sampleDetectedAt.ToUnixTimeMilliseconds());

        string payloadJson = SqliteTestHelpers.ReadScalarString(connection, "SELECT payload_json FROM audit_logs;");
        using var payload = JsonDocument.Parse(payloadJson);
        payload.RootElement.GetProperty("RunId").GetString().Should().Be(run.Id.ToString());
        payload.RootElement.GetProperty("CommitSha").GetString().Should().Be(COMMIT_SHA);
        payload.RootElement.GetProperty("DetectedAt").GetInt64().Should().Be(_sampleDetectedAt.ToUnixTimeMilliseconds());
        payload.RootElement.GetProperty("Reason").GetString().Should().Be(expectedReason);
    }

    [Fact]
    public void SaveBypassEvent_MultipleEvents_EachRunAndLogRowInserted()
    {
        using var directory = new TempDirectory();
        string dbPath = Path.Combine(directory.Root, "audit.db");
        var store = new SqliteAuditStore(dbPath);

        store.SaveBypassEvent(CreateBypassRun(directory.Root), BypassReason.NoVerify, COMMIT_SHA);
        store.SaveBypassEvent(CreateBypassRun(directory.Root), BypassReason.GitGuardSkip, COMMIT_SHA);

        using SqliteConnection connection = SqliteTestHelpers.OpenRawConnection(dbPath);
        SqliteTestHelpers.ReadScalarLong(connection, "SELECT COUNT(*) FROM scan_runs;").Should().Be(2);
        SqliteTestHelpers.ReadStringColumn(connection, "SELECT event_type FROM audit_logs ORDER BY seq;")
            .Should().Equal("BypassDetected", "BypassDetected");
        SqliteTestHelpers.ReadStringColumn(connection, "SELECT payload_json FROM audit_logs ORDER BY seq;")
            .Should().ContainSingle(json => json.Contains("\"Reason\":\"NoVerify\"", StringComparison.Ordinal))
            .And.ContainSingle(json => json.Contains("\"Reason\":\"GitGuardSkip\"", StringComparison.Ordinal));
    }

    [Fact]
    public void SaveBypassEvent_Sha256LengthCommit_IsAccepted()
    {
        using var directory = new TempDirectory();
        string dbPath = Path.Combine(directory.Root, "audit.db");
        var store = new SqliteAuditStore(dbPath);
        string sha256 = new string('a', 64);

        store.SaveBypassEvent(CreateBypassRun(directory.Root), BypassReason.NoVerify, sha256);

        using SqliteConnection connection = SqliteTestHelpers.OpenRawConnection(dbPath);
        string payloadJson = SqliteTestHelpers.ReadScalarString(connection, "SELECT payload_json FROM audit_logs;");
        using var payload = JsonDocument.Parse(payloadJson);
        payload.RootElement.GetProperty("CommitSha").GetString().Should().Be(sha256);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("0123456789abcdef0123456789abcdef0123456")]
    [InlineData("0123456789abcdef0123456789abcdef012345678")]
    [InlineData("gggggggggggggggggggggggggggggggggggggggg")]
    public void SaveBypassEvent_InvalidCommitSha_ThrowsArgumentException(string commitSha)
    {
        using var directory = new TempDirectory();
        var store = new SqliteAuditStore(Path.Combine(directory.Root, "audit.db"));

        Action act = () => store.SaveBypassEvent(CreateBypassRun(directory.Root), BypassReason.NoVerify, commitSha);

        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void SaveBypassEvent_EmptyOrNullCommitSha_ThrowsArgumentException(string? commitSha)
    {
        using var directory = new TempDirectory();
        var store = new SqliteAuditStore(Path.Combine(directory.Root, "audit.db"));

        Action act = () => store.SaveBypassEvent(CreateBypassRun(directory.Root), BypassReason.NoVerify, commitSha!);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void SaveBypassEvent_NullRun_ThrowsArgumentNullException()
    {
        using var directory = new TempDirectory();
        var store = new SqliteAuditStore(Path.Combine(directory.Root, "audit.db"));

        Action act = () => store.SaveBypassEvent(null!, BypassReason.NoVerify, COMMIT_SHA);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void SaveBypassEvent_UndefinedReason_ThrowsArgumentOutOfRangeException()
    {
        using var directory = new TempDirectory();
        var store = new SqliteAuditStore(Path.Combine(directory.Root, "audit.db"));

        Action act = () => store.SaveBypassEvent(CreateBypassRun(directory.Root), (BypassReason)99, COMMIT_SHA);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void SaveBypassEvent_NonBypassRun_ThrowsArgumentException()
    {
        using var directory = new TempDirectory();
        var store = new SqliteAuditStore(Path.Combine(directory.Root, "audit.db"));
        var preCommit = ScanRun.Create(
            directory.Root,
            ScanType.PreCommit,
            _sampleDetectedAt,
            durationMs: 12,
            ScanResult.Clean,
            exitCode: 0);

        Action act = () => store.SaveBypassEvent(preCommit, BypassReason.NoVerify, COMMIT_SHA);

        act.Should().Throw<ArgumentException>();
    }

    private static ScanRun CreateBypassRun(string repoPath) =>
        ScanRun.Create(
            repoPath,
            ScanType.Bypass,
            _sampleDetectedAt,
            durationMs: null,
            ScanResult.Clean,
            exitCode: 0);
}
