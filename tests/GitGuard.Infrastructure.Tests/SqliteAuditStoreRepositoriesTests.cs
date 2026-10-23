using FluentAssertions;
using GitGuard.Core.Model;
using GitGuard.Core.Policy;
using GitGuard.Infrastructure.Sqlite;
using Microsoft.Data.Sqlite;

namespace GitGuard.Infrastructure.Tests;

/// <summary>仓库注册表持久化集成测试（05-数据库设计 §4.8 / GGD-0042）：真实临时数据库验证 CRUD、唯一性与排序。</summary>
public sealed class SqliteAuditStoreRepositoriesTests
{
    private static readonly DateTimeOffset _sampleAddedAt = new DateTimeOffset(2026, 10, 20, 9, 30, 0, TimeSpan.Zero);

    [Fact]
    public void TryAdd_NewRepository_ListAllRoundtripsAllFields()
    {
        using var directory = new TempDirectory();
        var store = new SqliteAuditStore(Path.Combine(directory.Root, "audit.db"));
        RegisteredRepository repository = CreateRepository(Path.Combine(directory.Root, "repo-a"), "示例仓库", _sampleAddedAt);

        bool added = store.TryAdd(repository);

        added.Should().BeTrue();
        RegisteredRepository loaded = store.ListAll().Should().ContainSingle().Subject;
        loaded.Id.Should().Be(repository.Id);
        loaded.RepoPath.Should().Be(repository.RepoPath);
        loaded.DisplayName.Should().Be(repository.DisplayName);
        loaded.AddedAt.Should().Be(_sampleAddedAt);
    }

    [Fact]
    public void TryAdd_NullRepository_ThrowsArgumentNullException()
    {
        using var directory = new TempDirectory();
        var store = new SqliteAuditStore(Path.Combine(directory.Root, "audit.db"));

        Action act = () => store.TryAdd(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void TryAdd_DuplicatePath_ReturnsFalseAndKeepsOriginal()
    {
        using var directory = new TempDirectory();
        var store = new SqliteAuditStore(Path.Combine(directory.Root, "audit.db"));
        string repoPath = Path.Combine(directory.Root, "repo-a");
        RegisteredRepository original = CreateRepository(repoPath, "原始名称", _sampleAddedAt);
        RegisteredRepository duplicate = CreateRepository(repoPath, "重复名称", _sampleAddedAt.AddMinutes(5));

        store.TryAdd(original).Should().BeTrue();
        bool secondAdd = store.TryAdd(duplicate);

        secondAdd.Should().BeFalse();
        store.ListAll().Should().ContainSingle().Which.DisplayName.Should().Be("原始名称");
    }

    [Fact]
    public void Remove_ExistingId_DeletesOnlyRepositoryRow()
    {
        using var directory = new TempDirectory();
        string dbPath = Path.Combine(directory.Root, "audit.db");
        var store = new SqliteAuditStore(dbPath);
        RegisteredRepository repository = CreateRepository(Path.Combine(directory.Root, "repo-a"), "示例仓库", _sampleAddedAt);
        store.TryAdd(repository);
        var run = ScanRun.Create(repository.RepoPath, ScanType.Manual, _sampleAddedAt, 30, ScanResult.Clean, 0);
        store.SaveScanRun(run, new Dictionary<Finding, PolicyDecision>());

        bool removed = store.Remove(repository.Id);

        removed.Should().BeTrue();
        store.ListAll().Should().BeEmpty();
        store.FindScanRun(run.Id).Should().NotBeNull("移除登记不得级联删除扫描历史");
        using SqliteConnection connection = SqliteTestHelpers.OpenRawConnection(dbPath);
        SqliteTestHelpers.ReadScalarLong(connection, "SELECT COUNT(*) FROM scan_runs;").Should().Be(1);
    }

    [Fact]
    public void Remove_MissingId_ReturnsFalse()
    {
        using var directory = new TempDirectory();
        var store = new SqliteAuditStore(Path.Combine(directory.Root, "audit.db"));

        store.Remove(Guid.CreateVersion7()).Should().BeFalse();
    }

    [Fact]
    public void ListAll_MultipleEntries_OrdersByAddedAtDescendingThenDisplayName()
    {
        using var directory = new TempDirectory();
        var store = new SqliteAuditStore(Path.Combine(directory.Root, "audit.db"));
        RegisteredRepository oldest = CreateRepository(Path.Combine(directory.Root, "repo-old"), "旧仓库", _sampleAddedAt);
        RegisteredRepository secondSameTime = CreateRepository(Path.Combine(directory.Root, "repo-b"), "乙仓库", _sampleAddedAt.AddDays(1));
        RegisteredRepository firstSameTime = CreateRepository(Path.Combine(directory.Root, "repo-a"), "甲仓库", _sampleAddedAt.AddDays(1));
        store.TryAdd(oldest);
        store.TryAdd(secondSameTime);
        store.TryAdd(firstSameTime);

        IReadOnlyList<RegisteredRepository> repositories = store.ListAll();

        repositories.Select(repository => repository.DisplayName).Should().Equal("乙仓库", "甲仓库", "旧仓库");
    }

    [Fact]
    public void FindByPath_ExistingPath_ReturnsMatchingRepository()
    {
        using var directory = new TempDirectory();
        var store = new SqliteAuditStore(Path.Combine(directory.Root, "audit.db"));
        RegisteredRepository repository = CreateRepository(Path.Combine(directory.Root, "repo-a"), "示例仓库", _sampleAddedAt);
        store.TryAdd(repository);

        RegisteredRepository? found = store.FindByPath(repository.RepoPath + Path.DirectorySeparatorChar);

        found.Should().NotBeNull();
        found!.Id.Should().Be(repository.Id);
    }

    [Fact]
    public void FindByPath_MissingPath_ReturnsNull()
    {
        using var directory = new TempDirectory();
        var store = new SqliteAuditStore(Path.Combine(directory.Root, "audit.db"));

        store.FindByPath(Path.Combine(directory.Root, "unknown")).Should().BeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void FindByPath_EmptyOrWhitespacePath_ThrowsArgumentException(string path)
    {
        using var directory = new TempDirectory();
        var store = new SqliteAuditStore(Path.Combine(directory.Root, "audit.db"));

        Action act = () => store.FindByPath(path);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void TryAdd_ThenReopenNewStoreInstance_DataSurvives()
    {
        using var directory = new TempDirectory();
        string dbPath = Path.Combine(directory.Root, "audit.db");
        var firstStore = new SqliteAuditStore(dbPath);
        RegisteredRepository repository = CreateRepository(Path.Combine(directory.Root, "repo-a"), "示例仓库", _sampleAddedAt);
        firstStore.TryAdd(repository);

        var reopenedStore = new SqliteAuditStore(dbPath);

        reopenedStore.ListAll().Should().ContainSingle().Which.Id.Should().Be(repository.Id);
    }

    [Fact]
    public void TryAdd_PersistsRawColumnsInUnixMilliseconds()
    {
        using var directory = new TempDirectory();
        string dbPath = Path.Combine(directory.Root, "audit.db");
        var store = new SqliteAuditStore(dbPath);
        RegisteredRepository repository = CreateRepository(Path.Combine(directory.Root, "repo-a"), "示例仓库", _sampleAddedAt);

        store.TryAdd(repository);

        using SqliteConnection connection = SqliteTestHelpers.OpenRawConnection(dbPath);
        SqliteTestHelpers.ReadScalarString(connection, "SELECT id FROM repositories;").Should().Be(repository.Id.ToString());
        SqliteTestHelpers.ReadScalarString(connection, "SELECT repo_path FROM repositories;").Should().Be(repository.RepoPath);
        SqliteTestHelpers.ReadScalarLong(connection, "SELECT added_at FROM repositories;")
            .Should().Be(_sampleAddedAt.ToUnixTimeMilliseconds());
    }

    private static RegisteredRepository CreateRepository(string repoPath, string displayName, DateTimeOffset addedAt) =>
        RegisteredRepository.Create(repoPath, displayName, addedAt);
}
