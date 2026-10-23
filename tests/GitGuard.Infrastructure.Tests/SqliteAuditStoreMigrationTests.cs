using FluentAssertions;
using GitGuard.Infrastructure.Sqlite;
using Microsoft.Data.Sqlite;
using GitGuardSqliteException = GitGuard.Core.SqliteException;

namespace GitGuard.Infrastructure.Tests;

/// <summary>SchemaMigrator / V001 迁移集成测试（真实临时数据库文件；05-数据库设计 §4-§6、Audit.md §4）。</summary>
public sealed class SqliteAuditStoreMigrationTests
{
    private static readonly string[] _expectedTables = new[]
    {
        "allowlist",
        "audit_logs",
        "findings",
        "hook_installations",
        "repositories",
        "scan_runs",
        "schema_version",
    };

    private static readonly string[] _expectedIndexes = new[]
    {
        "ix_allowlist_fingerprint",
        "ix_allowlist_rule_path",
        "ix_findings_fingerprint",
        "ix_findings_rule_severity",
        "ix_findings_run_id",
        "ix_repositories_added_at",
        "ix_scan_runs_repo_time",
    };

    [Fact]
    public void Ctor_FreshDatabase_CreatesAllTablesAndIndexes()
    {
        using var directory = new TempDirectory();
        string dbPath = Path.Combine(directory.Root, "audit.db");

        _ = CreateStore(dbPath);

        using SqliteConnection connection = SqliteTestHelpers.OpenRawConnection(dbPath);
        SqliteTestHelpers.ReadStringColumn(connection, "SELECT name FROM sqlite_master WHERE type = 'table' ORDER BY name;")
            .Should().Contain(_expectedTables);
        SqliteTestHelpers.ReadStringColumn(connection, "SELECT name FROM sqlite_master WHERE type = 'index' ORDER BY name;")
            .Should().Contain(_expectedIndexes);
    }

    [Fact]
    public void Ctor_FreshDatabase_RecordsAllMigrationVersions()
    {
        using var directory = new TempDirectory();
        string dbPath = Path.Combine(directory.Root, "audit.db");

        _ = CreateStore(dbPath);

        using SqliteConnection connection = SqliteTestHelpers.OpenRawConnection(dbPath);
        SqliteTestHelpers.ReadLongColumn(connection, "SELECT version FROM schema_version ORDER BY version;")
            .Should().Equal(1L, 2L, 3L);
    }

    [Fact]
    public void Ctor_ExistingDatabase_ReopenDoesNotReapplyMigrations()
    {
        using var directory = new TempDirectory();
        string dbPath = Path.Combine(directory.Root, "audit.db");

        _ = CreateStore(dbPath);
        _ = CreateStore(dbPath);

        using SqliteConnection connection = SqliteTestHelpers.OpenRawConnection(dbPath);
        SqliteTestHelpers.ReadLongColumn(connection, "SELECT version FROM schema_version ORDER BY version;")
            .Should().Equal(1L, 2L, 3L);
    }

    [Fact]
    public void Ctor_V001Database_PreservesExistingAuditRowsAndSequence()
    {
        using var directory = new TempDirectory();
        string dbPath = Path.Combine(directory.Root, "audit.db");
        using (SqliteConnection connection = SqliteTestHelpers.OpenRawConnection(dbPath))
        {
            ExecuteSql(connection, ReadMigrationScript("V001_initial_schema.sql"));
            ExecuteSql(connection, "CREATE TABLE schema_version (version INTEGER PRIMARY KEY, applied_at INTEGER NOT NULL);");
            ExecuteSql(connection, "INSERT INTO schema_version (version, applied_at) VALUES (1, 0);");
            const string insertLegacyEvent =
                "INSERT INTO audit_logs (occurred_at, category, event_type, payload_json) " +
                "VALUES (1234567890, 'Scan', 'ScanCompleted', '{\"RunId\":\"legacy-run\"}');";
            ExecuteSql(connection, insertLegacyEvent);
        }

        _ = CreateStore(dbPath);

        using SqliteConnection reopened = SqliteTestHelpers.OpenRawConnection(dbPath);
        SqliteTestHelpers.ReadLongColumn(reopened, "SELECT seq FROM audit_logs ORDER BY seq;").Should().Equal(1L);
        SqliteTestHelpers.ReadScalarString(
                reopened,
                "SELECT category || '|' || event_type || '|' || payload_json FROM audit_logs WHERE seq = 1;")
            .Should().Be("Scan|ScanCompleted|{\"RunId\":\"legacy-run\"}", "V002 重建 audit_logs 时必须保留既有行与自增序列");
        SqliteTestHelpers.ReadLongColumn(reopened, "SELECT version FROM schema_version ORDER BY version;")
            .Should().Equal(1L, 2L, 3L);
    }

    [Fact]
    public void Ctor_NullPath_ThrowsArgumentNullException()
    {
        Action act = () => CreateStore(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Ctor_EmptyOrWhitespacePath_ThrowsArgumentException(string path)
    {
        Action act = () => CreateStore(path);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Ctor_NonPositiveBusyRetryDelay_ThrowsArgumentOutOfRangeException()
    {
        using var directory = new TempDirectory();
        var retryDelays = new[] { TimeSpan.FromMilliseconds(50), TimeSpan.Zero };

        Action act = () => CreateStore(Path.Combine(directory.Root, "audit.db"), retryDelays);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Ctor_SymbolicLinkPath_Throws()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Skip("Windows 创建符号链接需要开发者模式或特权，本用例仅在 Unix 运行（08-权限设计 §9）");
        }

        using var directory = new TempDirectory();
        string targetPath = Path.Combine(directory.Root, "target.db");
        string linkPath = Path.Combine(directory.Root, "link.db");
        File.WriteAllText(targetPath, string.Empty);
        File.CreateSymbolicLink(linkPath, targetPath);

        Action act = () => CreateStore(linkPath);

        act.Should().Throw<GitGuardSqliteException>().WithMessage("*符号链接*");
    }

    private static SqliteAuditStore CreateStore(string databasePath, IReadOnlyList<TimeSpan>? busyRetryDelays = null) =>
        new SqliteAuditStore(databasePath, busyRetryDelays: busyRetryDelays);

    private static string ReadMigrationScript(string fileName)
    {
        var assembly = typeof(SqliteAuditStore).Assembly;
        string resourceName = assembly.GetManifestResourceNames()
            .Single(name => name.EndsWith("." + fileName, StringComparison.Ordinal));
        using Stream stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"内嵌迁移脚本缺失：{fileName}");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static void ExecuteSql(SqliteConnection connection, string sql)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
