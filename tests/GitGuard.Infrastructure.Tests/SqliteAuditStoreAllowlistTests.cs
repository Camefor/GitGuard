using System.Text.Json;
using FluentAssertions;
using GitGuard.Core.Model;
using GitGuard.Core.Policy;
using GitGuard.Infrastructure.Sqlite;
using Microsoft.Data.Sqlite;
using GitGuardSqliteException = GitGuard.Core.SqliteException;

namespace GitGuard.Infrastructure.Tests;

/// <summary>
/// 白名单持久化测试（<c>05-数据库设计</c> §4.3 / FR-040 / FR-041）：<c>allowlist</c> 表 CRUD、
/// <c>created_at DESC, rowid DESC</c> 分页排序、<c>AllowlistChanged</c> 审计事件，以及损坏行按 FailClosed 处理。
/// </summary>
public sealed class SqliteAuditStoreAllowlistTests
{
    private static readonly DateTimeOffset _now = new DateTimeOffset(2026, 10, 22, 8, 0, 0, TimeSpan.Zero);
    private static readonly Fingerprint _fingerprint = Fingerprint.FromHex(new string('a', 64));

    [Fact]
    public void AddAllowlistEntry_PersistsAllColumnsAndAuditEvent()
    {
        using var directory = new TempDirectory();
        string dbPath = Path.Combine(directory.Root, "audit.db");
        var store = new SqliteAuditStore(dbPath);
        var entry = AllowlistEntry.Create(
            "GGD/SECRET/AWS-AKID",
            "config/**",
            _fingerprint,
            "  已知误报登记  ",
            _now,
            "tester",
            _now.AddDays(1));

        store.AddAllowlistEntry(entry);

        using SqliteConnection connection = SqliteTestHelpers.OpenRawConnection(dbPath);
        SqliteTestHelpers.ReadScalarLong(connection, "SELECT COUNT(*) FROM allowlist;").Should().Be(1);
        SqliteTestHelpers.ReadScalarString(connection, "SELECT id FROM allowlist;").Should().Be(entry.Id.ToString());
        SqliteTestHelpers.ReadScalarString(connection, "SELECT rule_id FROM allowlist;").Should().Be("GGD/SECRET/AWS-AKID");
        SqliteTestHelpers.ReadScalarString(connection, "SELECT path_pattern FROM allowlist;").Should().Be("config/**");
        SqliteTestHelpers.ReadScalarString(connection, "SELECT fingerprint FROM allowlist;").Should().Be(_fingerprint.Hex);
        SqliteTestHelpers.ReadScalarString(connection, "SELECT reason FROM allowlist;").Should().Be("已知误报登记");
        SqliteTestHelpers.ReadScalarLong(connection, "SELECT created_at FROM allowlist;")
            .Should().Be(_now.ToUnixTimeMilliseconds());
        SqliteTestHelpers.ReadScalarString(connection, "SELECT created_by FROM allowlist;").Should().Be("tester");
        SqliteTestHelpers.ReadScalarLong(connection, "SELECT expires_at FROM allowlist;")
            .Should().Be(_now.AddDays(1).ToUnixTimeMilliseconds());

        SqliteTestHelpers.ReadScalarString(
            connection,
            "SELECT category FROM audit_logs WHERE event_type = 'AllowlistChanged';").Should().Be("Allowlist");
        using var payload = JsonDocument.Parse(SqliteTestHelpers.ReadScalarString(
            connection,
            "SELECT payload_json FROM audit_logs WHERE event_type = 'AllowlistChanged';"));
        JsonElement root = payload.RootElement;
        root.GetProperty("EntryId").GetString().Should().Be(entry.Id.ToString());
        root.GetProperty("Action").GetString().Should().Be("Add");
        root.GetProperty("RuleId").GetString().Should().Be("GGD/SECRET/AWS-AKID");
        root.GetProperty("PathPattern").GetString().Should().Be("config/**");
        root.GetProperty("Fingerprint").GetString().Should().Be(_fingerprint.Hex);
        root.GetProperty("Reason").GetString().Should().Be("已知误报登记");
    }

    [Fact]
    public void AddAllowlistEntry_Null_Throws()
    {
        using var directory = new TempDirectory();
        var store = new SqliteAuditStore(Path.Combine(directory.Root, "audit.db"));

        Action act = () => store.AddAllowlistEntry(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void RemoveAllowlistEntry_Existing_DeletesRowAndWritesRemoveEvent()
    {
        using var directory = new TempDirectory();
        string dbPath = Path.Combine(directory.Root, "audit.db");
        var store = new SqliteAuditStore(dbPath);
        var entry = AllowlistEntry.Create("GGD/SECRET/AWS-AKID", null, null, "待删除条目", _now, "tester");
        store.AddAllowlistEntry(entry);

        bool removed = store.RemoveAllowlistEntry(entry.Id);

        removed.Should().BeTrue();
        using SqliteConnection connection = SqliteTestHelpers.OpenRawConnection(dbPath);
        SqliteTestHelpers.ReadScalarLong(connection, "SELECT COUNT(*) FROM allowlist;").Should().Be(0);
        List<string> payloads = SqliteTestHelpers.ReadStringColumn(
            connection,
            "SELECT payload_json FROM audit_logs WHERE event_type = 'AllowlistChanged' ORDER BY rowid;");
        payloads.Should().HaveCount(2);
        using var addPayload = JsonDocument.Parse(payloads[0]);
        using var removePayload = JsonDocument.Parse(payloads[1]);
        addPayload.RootElement.GetProperty("Action").GetString().Should().Be("Add");
        removePayload.RootElement.GetProperty("Action").GetString().Should().Be("Remove");
        removePayload.RootElement.GetProperty("EntryId").GetString().Should().Be(entry.Id.ToString());
    }

    [Fact]
    public void RemoveAllowlistEntry_Missing_ReturnsFalseWithoutAuditEvent()
    {
        using var directory = new TempDirectory();
        string dbPath = Path.Combine(directory.Root, "audit.db");
        var store = new SqliteAuditStore(dbPath);

        bool removed = store.RemoveAllowlistEntry(Guid.CreateVersion7());

        removed.Should().BeFalse();
        using SqliteConnection connection = SqliteTestHelpers.OpenRawConnection(dbPath);
        SqliteTestHelpers.ReadScalarLong(
            connection,
            "SELECT COUNT(*) FROM audit_logs WHERE event_type = 'AllowlistChanged';").Should().Be(0);
    }

    [Fact]
    public void QueryAllowlistEntries_PaginatesNewestFirst()
    {
        using var directory = new TempDirectory();
        string dbPath = Path.Combine(directory.Root, "audit.db");
        var store = new SqliteAuditStore(dbPath);
        var oldest = AllowlistEntry.Create("GGD/SECRET/AWS-AKID", null, null, "最早条目记录", _now, "tester");
        var newest = AllowlistEntry.Create("GGD/SECRET/AWS-AKID", null, null, "最新条目记录", _now.AddMinutes(2), "tester");
        var middle = AllowlistEntry.Create("GGD/SECRET/AWS-AKID", null, null, "中间条目记录", _now.AddMinutes(1), "tester");

        // 故意乱序写入：查询结果必须由 created_at 决定，而不是 rowid
        store.AddAllowlistEntry(middle);
        store.AddAllowlistEntry(newest);
        store.AddAllowlistEntry(oldest);

        AllowlistPage firstPage = store.QueryAllowlistEntries(new AllowlistQuery(limit: 2, offset: 0));
        AllowlistPage secondPage = store.QueryAllowlistEntries(new AllowlistQuery(limit: 2, offset: 2));

        firstPage.TotalCount.Should().Be(3);
        firstPage.Entries.Select(static entry => entry.Id).Should().Equal(newest.Id, middle.Id);
        secondPage.TotalCount.Should().Be(3);
        secondPage.Entries.Select(static entry => entry.Id).Should().Equal(oldest.Id);
    }

    [Fact]
    public void ListAllAllowlistEntries_ReturnsEveryEntryNewestFirst()
    {
        using var directory = new TempDirectory();
        string dbPath = Path.Combine(directory.Root, "audit.db");
        var store = new SqliteAuditStore(dbPath);
        var first = AllowlistEntry.Create("GGD/SECRET/AWS-AKID", null, null, "第一条测试记录", _now, "tester");
        var second = AllowlistEntry.Create(null, "src/**", null, "第二条测试记录", _now.AddMinutes(1), "tester");
        store.AddAllowlistEntry(first);
        store.AddAllowlistEntry(second);

        IReadOnlyList<AllowlistEntry> entries = store.ListAllAllowlistEntries();

        entries.Select(static entry => entry.Id).Should().Equal(second.Id, first.Id);
        entries[1].RuleId.Should().Be("GGD/SECRET/AWS-AKID");
        entries[0].PathPattern.Should().Be("src/**");
    }

    [Fact]
    public void QueryAllowlistEntries_EmptyTable_ReturnsEmptyPage()
    {
        using var directory = new TempDirectory();
        var store = new SqliteAuditStore(Path.Combine(directory.Root, "audit.db"));

        AllowlistPage page = store.QueryAllowlistEntries(new AllowlistQuery());

        page.TotalCount.Should().Be(0);
        page.Entries.Should().BeEmpty();
    }

    [Fact]
    public void ListAllAllowlistEntries_CorruptedRow_ThrowsFailClosedException()
    {
        using var directory = new TempDirectory();
        string dbPath = Path.Combine(directory.Root, "audit.db");
        var store = new SqliteAuditStore(dbPath);
        using (SqliteConnection connection = SqliteTestHelpers.OpenRawConnection(dbPath))
        {
            using var command = connection.CreateCommand();
            command.CommandText =
                "INSERT INTO allowlist (id, rule_id, path_pattern, fingerprint, reason, created_at, created_by, expires_at) " +
                "VALUES ($id, $rule_id, NULL, NULL, '损坏条目数据', $created_at, 'tester', NULL);";
            command.Parameters.AddWithValue("$id", Guid.CreateVersion7().ToString());
            command.Parameters.AddWithValue("$rule_id", "not-a-valid-rule");
            command.Parameters.AddWithValue("$created_at", _now.ToUnixTimeMilliseconds());
            command.ExecuteNonQuery();
        }

        Action act = () => store.ListAllAllowlistEntries();

        act.Should().Throw<GitGuardSqliteException>().WithMessage("*数据损坏*");
    }
}
