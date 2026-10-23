using GitGuard.Core.Model;
using Microsoft.Data.Sqlite;

namespace GitGuard.Infrastructure.Sqlite;

/// <summary>
/// 仓库注册表持久化（<c>05-数据库设计</c> §4.8 / GGD-0042）：<c>repositories</c> 表 CRUD。
/// 与审计数据共享 <c>audit.db</c> 与 BUSY 重试策略；登记与移除均不触碰仓库文件及其他表。
/// </summary>
public sealed partial class SqliteAuditStore
{
    /// <inheritdoc />
    public bool TryAdd(RegisteredRepository repository)
    {
        ArgumentNullException.ThrowIfNull(repository);

        return ExecuteWithBusyRetry("登记仓库", connection =>
        {
            using var command = connection.CreateCommand();
            command.CommandText =
                "INSERT INTO repositories (id, repo_path, display_name, added_at) " +
                "VALUES ($id, $path, $name, $added) " +
                "ON CONFLICT (repo_path) DO NOTHING;";
            command.Parameters.AddWithValue("$id", repository.Id.ToString());
            command.Parameters.AddWithValue("$path", repository.RepoPath);
            command.Parameters.AddWithValue("$name", repository.DisplayName);
            command.Parameters.AddWithValue("$added", repository.AddedAt.ToUnixTimeMilliseconds());
            return command.ExecuteNonQuery() > 0;
        });
    }

    /// <inheritdoc />
    public bool Remove(Guid id)
    {
        return ExecuteWithBusyRetry("移除仓库登记", connection =>
        {
            using var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM repositories WHERE id = $id;";
            command.Parameters.AddWithValue("$id", id.ToString());
            return command.ExecuteNonQuery() > 0;
        });
    }

    /// <inheritdoc />
    public IReadOnlyList<RegisteredRepository> ListAll() =>
        ExecuteWithBusyRetry("读取仓库登记", static connection =>
        {
            using var command = connection.CreateCommand();
            command.CommandText =
                "SELECT id, repo_path, display_name, added_at FROM repositories " +
                "ORDER BY added_at DESC, display_name ASC;";
            using SqliteDataReader reader = command.ExecuteReader();
            var repositories = new List<RegisteredRepository>();
            while (reader.Read())
            {
                repositories.Add(ReadRegisteredRepository(reader));
            }

            return repositories;
        });

    /// <inheritdoc />
    public RegisteredRepository? FindByPath(string repoPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repoPath);
        string normalizedRepoPath = Path.TrimEndingDirectorySeparator(repoPath);

        return ExecuteWithBusyRetry("按路径读取仓库登记", connection =>
        {
            using var command = connection.CreateCommand();
            command.CommandText =
                "SELECT id, repo_path, display_name, added_at FROM repositories WHERE repo_path = $path;";
            command.Parameters.AddWithValue("$path", normalizedRepoPath);
            using SqliteDataReader reader = command.ExecuteReader();
            return reader.Read() ? ReadRegisteredRepository(reader) : null;
        });
    }

    private static RegisteredRepository ReadRegisteredRepository(SqliteDataReader reader)
    {
        var id = Guid.Parse(reader.GetString(0));
        string repoPath = reader.GetString(1);
        string displayName = reader.GetString(2);
        var addedAt = DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(3));
        return RegisteredRepository.Restore(id, repoPath, displayName, addedAt);
    }
}
