using System.Text;
using Microsoft.Data.Sqlite;

namespace GitGuard.Application.Tests;

/// <summary>审计库直读辅助（16-测试方案：集成测试禁止 mock，直接校验落库真实状态）。</summary>
internal static class SqliteTestHelpers
{
    /// <summary>打开绕过 SqliteAuditStore 的原始连接；Pooling=false 确保句柄随即释放，不阻塞临时目录清理。</summary>
    internal static SqliteConnection OpenRawConnection(string databasePath)
    {
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Pooling = false,
        };
        var connection = new SqliteConnection(builder.ToString());
        connection.Open();
        return connection;
    }

    /// <summary>读取单列文本值列表。</summary>
    internal static List<string> ReadStringColumn(SqliteConnection connection, string commandText)
    {
        using var command = connection.CreateCommand();
        command.CommandText = commandText;
        var values = new List<string>();
        using SqliteDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            values.Add(reader.GetString(0));
        }

        return values;
    }

    /// <summary>读取单行单列文本值。</summary>
    internal static string ReadScalarString(SqliteConnection connection, string commandText)
    {
        using var command = connection.CreateCommand();
        command.CommandText = commandText;
        object? value = command.ExecuteScalar();
        return value is string text ? text : throw new InvalidOperationException($"查询未返回文本：{commandText}");
    }

    /// <summary>拼接数据库主文件与 WAL/SHM 附属文件的文本内容（金丝雀检查需覆盖全部落盘形态）。</summary>
    internal static string ReadDatabaseFilesText(string databasePath)
    {
        var builder = new StringBuilder();
        foreach (string path in new[] { databasePath, databasePath + "-wal", databasePath + "-shm" })
        {
            if (File.Exists(path))
            {
                builder.Append(Encoding.UTF8.GetString(File.ReadAllBytes(path)));
            }
        }

        return builder.ToString();
    }
}
