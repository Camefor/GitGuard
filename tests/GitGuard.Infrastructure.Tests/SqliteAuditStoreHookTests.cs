using System.Text.Json;
using FluentAssertions;
using GitGuard.Core.Model;
using GitGuard.Infrastructure.Sqlite;
using Microsoft.Data.Sqlite;

namespace GitGuard.Infrastructure.Tests;

/// <summary>
/// SqliteAuditStore Hook 安装记录读写集成测试（真实临时数据库文件；05-数据库设计 §4.1 / §7、DDD §3.4、Audit.md §4）：
/// 覆盖 FR-010 安装 upsert 与 FR-011 卸载状态翻转，以及 HookInstalled / HookUninstalled 事件载荷。
/// </summary>
public sealed class SqliteAuditStoreHookTests
{
    private static readonly DateTimeOffset SampleInstalledAt = new DateTimeOffset(2026, 10, 16, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void SaveHookInstallation_NewRecord_InsertsActiveRowWithRawColumns()
    {
        using var directory = new TempDirectory();
        string dbPath = Path.Combine(directory.Root, "audit.db");
        var store = new SqliteAuditStore(dbPath);
        var installation = HookInstallation.Create(directory.Root, HookType.PreCommit, SampleInstalledAt, "1.2.3");

        store.SaveHookInstallation(installation);

        using SqliteConnection connection = SqliteTestHelpers.OpenRawConnection(dbPath);
        SqliteTestHelpers.ReadScalarString(connection, "SELECT repo_path FROM hook_installations;").Should().Be(directory.Root);
        SqliteTestHelpers.ReadScalarString(connection, "SELECT hook_type FROM hook_installations;").Should().Be("pre-commit");
        SqliteTestHelpers.ReadScalarLong(connection, "SELECT installed_at FROM hook_installations;")
            .Should().Be(SampleInstalledAt.ToUnixTimeMilliseconds());
        SqliteTestHelpers.ReadScalarString(connection, "SELECT version FROM hook_installations;").Should().Be("1.2.3");
        SqliteTestHelpers.ReadScalarString(connection, "SELECT status FROM hook_installations;").Should().Be("Active");
        SqliteTestHelpers.ReadScalarLong(connection, "SELECT COUNT(*) FROM hook_installations WHERE original_hook_backup_path IS NULL;")
            .Should().Be(1);
    }

    [Fact]
    public void SaveHookInstallation_WithBackupFileName_PersistsBackupPath()
    {
        using var directory = new TempDirectory();
        string dbPath = Path.Combine(directory.Root, "audit.db");
        var store = new SqliteAuditStore(dbPath);
        var installation = HookInstallation.Create(
            directory.Root,
            HookType.PrePush,
            SampleInstalledAt,
            "1.2.3",
            "pre-push.local.bak");

        store.SaveHookInstallation(installation);

        using SqliteConnection connection = SqliteTestHelpers.OpenRawConnection(dbPath);
        SqliteTestHelpers.ReadScalarString(connection, "SELECT original_hook_backup_path FROM hook_installations;")
            .Should().Be("pre-push.local.bak");
    }

    [Fact]
    public void SaveHookInstallation_WritesHookInstalledEventWithTimestampAndPayload()
    {
        using var directory = new TempDirectory();
        string dbPath = Path.Combine(directory.Root, "audit.db");
        var store = new SqliteAuditStore(dbPath);
        var installation = HookInstallation.Create(directory.Root, HookType.PreCommit, SampleInstalledAt, "1.2.3");

        store.SaveHookInstallation(installation);

        using SqliteConnection connection = SqliteTestHelpers.OpenRawConnection(dbPath);
        SqliteTestHelpers.ReadScalarString(connection, "SELECT category FROM audit_logs ORDER BY seq DESC LIMIT 1;").Should().Be("Hook");
        SqliteTestHelpers.ReadScalarString(connection, "SELECT event_type FROM audit_logs ORDER BY seq DESC LIMIT 1;")
            .Should().Be("HookInstalled");
        SqliteTestHelpers.ReadScalarLong(connection, "SELECT occurred_at FROM audit_logs ORDER BY seq DESC LIMIT 1;")
            .Should().Be(SampleInstalledAt.ToUnixTimeMilliseconds());

        string payloadJson = SqliteTestHelpers.ReadScalarString(connection, "SELECT payload_json FROM audit_logs ORDER BY seq DESC LIMIT 1;");
        using var payload = JsonDocument.Parse(payloadJson);
        payload.RootElement.GetProperty("RepoPath").GetString().Should().Be(directory.Root);
        payload.RootElement.GetProperty("HookType").GetString().Should().Be("pre-commit");
        payload.RootElement.GetProperty("Version").GetString().Should().Be("1.2.3");
        payload.RootElement.GetProperty("HadLegacyHook").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public void SaveHookInstallation_WithBackup_PayloadHadLegacyHookTrue()
    {
        using var directory = new TempDirectory();
        string dbPath = Path.Combine(directory.Root, "audit.db");
        var store = new SqliteAuditStore(dbPath);
        var installation = HookInstallation.Create(
            directory.Root,
            HookType.PreCommit,
            SampleInstalledAt,
            "1.2.3",
            "pre-commit.local.bak");

        store.SaveHookInstallation(installation);

        using SqliteConnection connection = SqliteTestHelpers.OpenRawConnection(dbPath);
        string payloadJson = SqliteTestHelpers.ReadScalarString(connection, "SELECT payload_json FROM audit_logs ORDER BY seq DESC LIMIT 1;");
        using var payload = JsonDocument.Parse(payloadJson);
        payload.RootElement.GetProperty("HadLegacyHook").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public void SaveHookInstallation_SameRepoAndHookTwice_UpsertsSingleRowRefreshingFields()
    {
        using var directory = new TempDirectory();
        string dbPath = Path.Combine(directory.Root, "audit.db");
        var store = new SqliteAuditStore(dbPath);
        var first = HookInstallation.Create(directory.Root, HookType.PreCommit, SampleInstalledAt, "1.2.3");
        var second = HookInstallation.Create(
            directory.Root,
            HookType.PreCommit,
            SampleInstalledAt.AddDays(1),
            "2.0.0",
            "pre-commit.local.bak");

        store.SaveHookInstallation(first);
        store.SaveHookInstallation(second);

        using SqliteConnection connection = SqliteTestHelpers.OpenRawConnection(dbPath);
        SqliteTestHelpers.ReadScalarLong(connection, "SELECT COUNT(*) FROM hook_installations;").Should().Be(1);
        SqliteTestHelpers.ReadScalarLong(connection, "SELECT installed_at FROM hook_installations;")
            .Should().Be(SampleInstalledAt.AddDays(1).ToUnixTimeMilliseconds());
        SqliteTestHelpers.ReadScalarString(connection, "SELECT version FROM hook_installations;").Should().Be("2.0.0");
        SqliteTestHelpers.ReadScalarString(connection, "SELECT original_hook_backup_path FROM hook_installations;")
            .Should().Be("pre-commit.local.bak");
        SqliteTestHelpers.ReadScalarString(connection, "SELECT status FROM hook_installations;").Should().Be("Active");
        SqliteTestHelpers.ReadStringColumn(connection, "SELECT event_type FROM audit_logs ORDER BY seq;")
            .Should().Equal("HookInstalled", "HookInstalled");
    }

    [Fact]
    public void SaveHookInstallation_DifferentHookTypes_InsertTwoRows()
    {
        using var directory = new TempDirectory();
        string dbPath = Path.Combine(directory.Root, "audit.db");
        var store = new SqliteAuditStore(dbPath);

        store.SaveHookInstallation(HookInstallation.Create(directory.Root, HookType.PreCommit, SampleInstalledAt, "1.2.3"));
        store.SaveHookInstallation(HookInstallation.Create(directory.Root, HookType.PrePush, SampleInstalledAt, "1.2.3"));

        using SqliteConnection connection = SqliteTestHelpers.OpenRawConnection(dbPath);
        SqliteTestHelpers.ReadStringColumn(connection, "SELECT hook_type FROM hook_installations ORDER BY hook_type;")
            .Should().Equal("pre-commit", "pre-push");
    }

    [Fact]
    public void SaveHookUninstallation_ExistingRow_MarksUninstalledPreservingInstallFields()
    {
        using var directory = new TempDirectory();
        string dbPath = Path.Combine(directory.Root, "audit.db");
        var store = new SqliteAuditStore(dbPath);
        var installed = HookInstallation.Create(
            directory.Root,
            HookType.PreCommit,
            SampleInstalledAt,
            "1.2.3",
            "pre-commit.local.bak");
        store.SaveHookInstallation(installed);
        var uninstalled = HookInstallation.Create(
            directory.Root,
            HookType.PreCommit,
            SampleInstalledAt.AddDays(3),
            "2.0.0",
            "pre-commit.local.bak");

        store.SaveHookUninstallation(uninstalled, restoredLegacyHook: true);

        using SqliteConnection connection = SqliteTestHelpers.OpenRawConnection(dbPath);
        SqliteTestHelpers.ReadScalarLong(connection, "SELECT COUNT(*) FROM hook_installations;").Should().Be(1);
        SqliteTestHelpers.ReadScalarString(connection, "SELECT status FROM hook_installations;").Should().Be("Uninstalled");

        // 05-数据库设计 §7：卸载仅翻转状态，保留首次安装的 installed_at / version / 备份路径
        SqliteTestHelpers.ReadScalarLong(connection, "SELECT installed_at FROM hook_installations;")
            .Should().Be(SampleInstalledAt.ToUnixTimeMilliseconds());
        SqliteTestHelpers.ReadScalarString(connection, "SELECT version FROM hook_installations;").Should().Be("1.2.3");
        SqliteTestHelpers.ReadScalarString(connection, "SELECT original_hook_backup_path FROM hook_installations;")
            .Should().Be("pre-commit.local.bak");

        string payloadJson = SqliteTestHelpers.ReadScalarString(connection, "SELECT payload_json FROM audit_logs ORDER BY seq DESC LIMIT 1;");
        using var payload = JsonDocument.Parse(payloadJson);
        payload.RootElement.GetProperty("RepoPath").GetString().Should().Be(directory.Root);
        payload.RootElement.GetProperty("HookType").GetString().Should().Be("pre-commit");
        payload.RootElement.GetProperty("RestoredLegacyHook").GetBoolean().Should().BeTrue();
        payload.RootElement.TryGetProperty("Version", out _).Should().BeFalse();
    }

    [Fact]
    public void SaveHookUninstallation_NoExistingRow_InsertsUninstalledRow()
    {
        using var directory = new TempDirectory();
        string dbPath = Path.Combine(directory.Root, "audit.db");
        var store = new SqliteAuditStore(dbPath);
        var uninstalled = HookInstallation.Create(directory.Root, HookType.PrePush, SampleInstalledAt, "1.2.3");

        store.SaveHookUninstallation(uninstalled, restoredLegacyHook: false);

        using SqliteConnection connection = SqliteTestHelpers.OpenRawConnection(dbPath);
        SqliteTestHelpers.ReadScalarString(connection, "SELECT status FROM hook_installations;").Should().Be("Uninstalled");
        SqliteTestHelpers.ReadScalarString(connection, "SELECT hook_type FROM hook_installations;").Should().Be("pre-push");
        SqliteTestHelpers.ReadScalarString(connection, "SELECT event_type FROM audit_logs ORDER BY seq DESC LIMIT 1;")
            .Should().Be("HookUninstalled");
    }

    [Fact]
    public void SaveHookUninstallation_ReinstallAfterUninstall_FlipsBackToActive()
    {
        using var directory = new TempDirectory();
        string dbPath = Path.Combine(directory.Root, "audit.db");
        var store = new SqliteAuditStore(dbPath);
        store.SaveHookInstallation(HookInstallation.Create(directory.Root, HookType.PreCommit, SampleInstalledAt, "1.2.3"));
        store.SaveHookUninstallation(
            HookInstallation.Create(directory.Root, HookType.PreCommit, SampleInstalledAt, "1.2.3"),
            restoredLegacyHook: false);

        var reinstalled = HookInstallation.Create(directory.Root, HookType.PreCommit, SampleInstalledAt.AddDays(1), "2.0.0");
        store.SaveHookInstallation(reinstalled);

        using SqliteConnection connection = SqliteTestHelpers.OpenRawConnection(dbPath);
        SqliteTestHelpers.ReadScalarLong(connection, "SELECT COUNT(*) FROM hook_installations;").Should().Be(1);
        SqliteTestHelpers.ReadScalarString(connection, "SELECT status FROM hook_installations;").Should().Be("Active");
        SqliteTestHelpers.ReadScalarString(connection, "SELECT version FROM hook_installations;").Should().Be("2.0.0");
        SqliteTestHelpers.ReadStringColumn(connection, "SELECT event_type FROM audit_logs ORDER BY seq;")
            .Should().Equal("HookInstalled", "HookUninstalled", "HookInstalled");
    }

    [Fact]
    public void SaveHookUninstallation_OccurredAt_IsCurrentTime()
    {
        using var directory = new TempDirectory();
        string dbPath = Path.Combine(directory.Root, "audit.db");
        var store = new SqliteAuditStore(dbPath);
        long before = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        store.SaveHookUninstallation(
            HookInstallation.Create(directory.Root, HookType.PreCommit, SampleInstalledAt, "1.2.3"),
            restoredLegacyHook: false);

        using SqliteConnection connection = SqliteTestHelpers.OpenRawConnection(dbPath);
        long occurredAt = SqliteTestHelpers.ReadScalarLong(connection, "SELECT occurred_at FROM audit_logs ORDER BY seq DESC LIMIT 1;");
        occurredAt.Should().BeGreaterThanOrEqualTo(before).And.BeLessThanOrEqualTo(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
    }

    [Fact]
    public void SaveHookInstallation_NullArgument_ThrowsArgumentNullException()
    {
        using var directory = new TempDirectory();
        var store = new SqliteAuditStore(Path.Combine(directory.Root, "audit.db"));

        Action act = () => store.SaveHookInstallation(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void SaveHookUninstallation_NullArgument_ThrowsArgumentNullException()
    {
        using var directory = new TempDirectory();
        var store = new SqliteAuditStore(Path.Combine(directory.Root, "audit.db"));

        Action act = () => store.SaveHookUninstallation(null!, restoredLegacyHook: false);

        act.Should().Throw<ArgumentNullException>();
    }
}
