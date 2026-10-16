using System.Globalization;
using Microsoft.Data.Sqlite;

namespace GitGuard.Infrastructure.Sqlite;

/// <summary>
/// 轻量 schema 迁移器（05-数据库设计 §6）：按 VNNN 版本序应用内嵌迁移脚本（<c>Sqlite/Migrations/VNNN_&lt;description&gt;.sql</c>），
/// 每个脚本在单事务内执行并登记 <c>schema_version</c>；脚本本身以 <c>IF NOT EXISTS</c> 保证崩溃重放安全。
/// 不引入 EF Core Migration（避免 Native AOT 运行时反射依赖，05-数据库设计 §6 工具约定）。
/// </summary>
internal static class SchemaMigrator
{
    private const string SCRIPT_MARKER = ".Sqlite.Migrations.V";
    private const string SCHEMA_VERSION_DDL = "CREATE TABLE IF NOT EXISTS schema_version (version INTEGER PRIMARY KEY, applied_at INTEGER NOT NULL);";

    /// <summary>应用全部未执行的迁移脚本；单脚本事务失败时不写入 schema_version（FailClosed，库保持原版本）。</summary>
    /// <param name="connection">已打开的数据库连接。</param>
    internal static void Apply(SqliteConnection connection)
    {
        EnsureSchemaVersionTable(connection);

        long currentVersion = ReadCurrentVersion(connection);
        foreach ((int version, string sql) in LoadScripts())
        {
            if (version <= currentVersion)
            {
                continue;
            }

            using SqliteTransaction transaction = connection.BeginTransaction();
            ExecuteSql(connection, transaction, sql);
            RecordVersion(connection, transaction, version);
            transaction.Commit();
        }
    }

    private static List<(int Version, string Sql)> LoadScripts()
    {
        var scripts = new List<(int Version, string Sql)>();
        var assembly = typeof(SchemaMigrator).Assembly;
        foreach (string resourceName in assembly.GetManifestResourceNames())
        {
            int markerIndex = resourceName.IndexOf(SCRIPT_MARKER, StringComparison.Ordinal);
            if (markerIndex < 0)
            {
                continue;
            }

            // 不用范围运算符 [..]：StyleCop 1.1.118 对 ").." 误报 SA1009
            string fileName = resourceName.Substring(markerIndex + SCRIPT_MARKER.Length);
            int digitsLength = 0;
            while (digitsLength < fileName.Length && char.IsAsciiDigit(fileName[digitsLength]))
            {
                digitsLength++;
            }

            if (digitsLength == 0)
            {
                throw new InvalidOperationException($"迁移脚本命名不符合 VNNN_<description>.sql 约定：{resourceName}");
            }

            int version = int.Parse(fileName.AsSpan(0, digitsLength), CultureInfo.InvariantCulture);
            using Stream stream = assembly.GetManifestResourceStream(resourceName)
                ?? throw new InvalidOperationException($"内嵌迁移脚本缺失：{resourceName}");
            using var reader = new StreamReader(stream);
            scripts.Add((version, reader.ReadToEnd()));
        }

        scripts.Sort(static (left, right) => left.Version.CompareTo(right.Version));
        return scripts;
    }

    private static void EnsureSchemaVersionTable(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = SCHEMA_VERSION_DDL;
        command.ExecuteNonQuery();
    }

    private static long ReadCurrentVersion(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COALESCE(MAX(version), 0) FROM schema_version;";

        // 不用 "()!" 空包容：StyleCop 1.1.118 对 ")!;" 误报 SA1009
        return Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    private static void ExecuteSql(SqliteConnection connection, SqliteTransaction transaction, string sql)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static void RecordVersion(SqliteConnection connection, SqliteTransaction transaction, int version)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "INSERT INTO schema_version (version, applied_at) VALUES ($version, $applied_at);";
        command.Parameters.AddWithValue("$version", version);
        command.Parameters.AddWithValue("$applied_at", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        command.ExecuteNonQuery();
    }
}
