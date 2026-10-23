using FluentAssertions;
using GitGuard.Application.Repositories;
using GitGuard.Core;
using GitGuard.Core.Model;
using GitGuard.Core.Policy;
using GitGuard.Infrastructure.Git;
using GitGuard.Infrastructure.Sqlite;

namespace GitGuard.Application.Tests;

/// <summary>
/// 仓库登记 / 移除 / 列表 / 最近扫描用例测试（GGD-0042、<c>06-API接口设计</c> §5）：真实 git 临时仓库与真实
/// SQLite 审计库验证路径规范化、重复登记防护与最近扫描摘要映射。
/// </summary>
public sealed class RepositoryUseCaseTests
{
    private static readonly DateTimeOffset _sampleUtcNow = new DateTimeOffset(2026, 10, 20, 9, 30, 0, TimeSpan.Zero);

    [Fact]
    public void Register_RepositorySubdirectory_StoresWorkspaceRootAndDirectoryName()
    {
        using var repo = new TempGitRepository();
        using var data = new TempDirectory();
        SqliteAuditStore store = CreateStore(data.Root);
        string nestedDirectory = Path.Combine(repo.Root, "src", "nested");
        Directory.CreateDirectory(nestedDirectory);

        RegisteredRepository registered = CreateRegisterUseCase(store).Execute(nestedDirectory);

        registered.RepoPath.Should().Be(repo.Root);
        registered.DisplayName.Should().Be(Path.GetFileName(repo.Root));
        registered.AddedAt.Should().Be(_sampleUtcNow);
        store.ListAll().Should().ContainSingle().Which.Id.Should().Be(registered.Id);
    }

    [Fact]
    public void Register_CustomDisplayName_TrimsAndPersists()
    {
        using var repo = new TempGitRepository();
        using var data = new TempDirectory();
        SqliteAuditStore store = CreateStore(data.Root);

        RegisteredRepository registered = CreateRegisterUseCase(store).Execute(repo.Root, "  核心仓库  ");

        registered.DisplayName.Should().Be("核心仓库");
        RegisteredRepository? found = store.FindByPath(repo.Root);
        found.Should().NotBeNull();
        found!.DisplayName.Should().Be("核心仓库");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Register_EmptyOrWhitespacePath_ThrowsValidationFailed(string path)
    {
        using var data = new TempDirectory();
        SqliteAuditStore store = CreateStore(data.Root);

        Action act = () => CreateRegisterUseCase(store).Execute(path);

        act.Should().Throw<RepositoryException>()
            .Which.ErrorCode.Should().Be(RepositoryException.REPOSITORY_VALIDATION_FAILED);
    }

    [Fact]
    public void Register_NullPath_ThrowsValidationFailed()
    {
        using var data = new TempDirectory();
        SqliteAuditStore store = CreateStore(data.Root);

        Action act = () => CreateRegisterUseCase(store).Execute(null!);

        act.Should().Throw<RepositoryException>()
            .Which.ErrorCode.Should().Be(RepositoryException.REPOSITORY_VALIDATION_FAILED);
    }

    [Fact]
    public void Register_MissingDirectory_ThrowsValidationFailed()
    {
        using var data = new TempDirectory();
        SqliteAuditStore store = CreateStore(data.Root);

        Action act = () => CreateRegisterUseCase(store).Execute(Path.Combine(data.Root, "missing"));

        act.Should().Throw<RepositoryException>()
            .Which.ErrorCode.Should().Be(RepositoryException.REPOSITORY_VALIDATION_FAILED);
    }

    [Fact]
    public void Register_InvalidPathCharacters_ThrowsValidationFailed()
    {
        using var data = new TempDirectory();
        SqliteAuditStore store = CreateStore(data.Root);

        Action act = () => CreateRegisterUseCase(store).Execute("bad\0path");

        act.Should().Throw<RepositoryException>()
            .Which.ErrorCode.Should().Be(RepositoryException.REPOSITORY_VALIDATION_FAILED);
    }

    [Fact]
    public void Register_PlainDirectory_ThrowsNotARepository()
    {
        using var data = new TempDirectory();
        SqliteAuditStore store = CreateStore(data.Root);
        string plainDirectory = Path.Combine(data.Root, "plain");
        Directory.CreateDirectory(plainDirectory);

        Action act = () => CreateRegisterUseCase(store).Execute(plainDirectory);

        act.Should().Throw<RepositoryException>()
            .Which.ErrorCode.Should().Be(RepositoryException.REPOSITORY_NOT_A_REPOSITORY);
    }

    [Fact]
    public void Register_DuplicatePath_ThrowsAlreadyRegisteredAndKeepsOriginal()
    {
        using var repo = new TempGitRepository();
        using var data = new TempDirectory();
        SqliteAuditStore store = CreateStore(data.Root);
        CreateRegisterUseCase(store).Execute(repo.Root);

        Action act = () => CreateRegisterUseCase(store).Execute(repo.Root + Path.DirectorySeparatorChar, "重复登记");

        act.Should().Throw<RepositoryException>()
            .Which.ErrorCode.Should().Be(RepositoryException.REPOSITORY_ALREADY_REGISTERED);
        store.ListAll().Should().ContainSingle();
    }

    [Fact]
    public void Register_WithoutDisplayName_FallsBackToRepositoryDirectoryName()
    {
        using var repo = new TempGitRepository();
        using var data = new TempDirectory();
        SqliteAuditStore store = CreateStore(data.Root);

        RegisteredRepository registered = CreateRegisterUseCase(store).Execute(repo.Root);

        registered.DisplayName.Should().Be(new DirectoryInfo(repo.Root).Name);
    }

    [Fact]
    public void Remove_ExistingRepository_DeletesRegistrationOnly()
    {
        using var repo = new TempGitRepository();
        using var data = new TempDirectory();
        SqliteAuditStore store = CreateStore(data.Root);
        RegisteredRepository registered = CreateRegisterUseCase(store).Execute(repo.Root);
        var run = ScanRun.Create(repo.Root, ScanType.Manual, _sampleUtcNow, 15, ScanResult.Clean, 0);
        store.SaveScanRun(run, new Dictionary<Finding, PolicyDecision>());

        new RemoveRepositoryUseCase(store).Execute(registered.Id);

        store.ListAll().Should().BeEmpty();
        store.FindScanRun(run.Id).Should().NotBeNull("移除登记不得删除扫描历史");
        Directory.Exists(repo.Root).Should().BeTrue("移除登记不得删除仓库文件");
    }

    [Fact]
    public void Remove_MissingOrEmptyId_ThrowsNotFound()
    {
        using var data = new TempDirectory();
        SqliteAuditStore store = CreateStore(data.Root);
        var useCase = new RemoveRepositoryUseCase(store);

        Action emptyId = () => useCase.Execute(Guid.Empty);
        Action missingId = () => useCase.Execute(Guid.CreateVersion7());

        emptyId.Should().Throw<RepositoryException>()
            .Which.ErrorCode.Should().Be(RepositoryException.REPOSITORY_NOT_FOUND);
        missingId.Should().Throw<RepositoryException>()
            .Which.ErrorCode.Should().Be(RepositoryException.REPOSITORY_NOT_FOUND);
    }

    [Fact]
    public void List_MultipleRepositories_ReturnsNewestFirst()
    {
        using var firstRepo = new TempGitRepository();
        using var secondRepo = new TempGitRepository();
        using var data = new TempDirectory();
        SqliteAuditStore store = CreateStore(data.Root);
        var useCase = new RegisterRepositoryUseCase(store, new GitAccessFactory(), new FixedClock(_sampleUtcNow));
        useCase.Execute(firstRepo.Root, "较早");
        new RegisterRepositoryUseCase(store, new GitAccessFactory(), new FixedClock(_sampleUtcNow.AddHours(1)))
            .Execute(secondRepo.Root, "较晚");

        IReadOnlyList<RegisteredRepository> repositories = new ListRepositoriesUseCase(store).Execute();

        repositories.Select(repository => repository.DisplayName).Should().Equal("较晚", "较早");
    }

    [Fact]
    public void LatestScan_NoRuns_ReturnsNull()
    {
        using var data = new TempDirectory();
        SqliteAuditStore store = CreateStore(data.Root);

        new LatestRepositoryScanUseCase(store).Execute(Path.Combine(data.Root, "repo")).Should().BeNull();
    }

    [Fact]
    public void LatestScan_MultipleRuns_ReturnsNewestRunSummary()
    {
        using var data = new TempDirectory();
        SqliteAuditStore store = CreateStore(data.Root);
        string repoPath = Path.Combine(data.Root, "repo");
        Directory.CreateDirectory(repoPath);
        var older = ScanRun.Create(repoPath, ScanType.PrePush, _sampleUtcNow.AddHours(-2), 20, ScanResult.Clean, 0);
        var finding = Finding.Create(
            "GGD/SECRET/AWS-AKID",
            "src/appsettings.json",
            18,
            Severity.Critical,
            Fingerprint.FromHex(new string('a', 64)),
            SnippetMasked.Create("AKIA****************"));
        var newest = ScanRun.Create(
            repoPath,
            ScanType.Manual,
            _sampleUtcNow,
            45,
            ScanResult.Blocked,
            1,
            new[] { finding });
        store.SaveScanRun(older, new Dictionary<Finding, PolicyDecision>());
        store.SaveScanRun(
            newest,
            new Dictionary<Finding, PolicyDecision>
            {
                [finding] = new PolicyDecision(PolicyAction.Block, false, Severity.Critical),
            });

        AuditRunSummary? summary = new LatestRepositoryScanUseCase(store).Execute(repoPath);

        summary.Should().NotBeNull();
        summary!.RunId.Should().Be(newest.Id);
        summary.RepoPath.Should().Be(repoPath);
        summary.ScanType.Should().Be(ScanType.Manual);
        summary.StartedAt.Should().Be(_sampleUtcNow);
        summary.DurationMs.Should().Be(45);
        summary.Result.Should().Be(ScanResult.Blocked);
        summary.ExitCode.Should().Be(1);
        summary.FindingCount.Should().Be(1);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void LatestScan_EmptyOrWhitespacePath_ThrowsArgumentException(string path)
    {
        using var data = new TempDirectory();
        SqliteAuditStore store = CreateStore(data.Root);

        Action act = () => new LatestRepositoryScanUseCase(store).Execute(path);

        act.Should().Throw<ArgumentException>();
    }

    private static SqliteAuditStore CreateStore(string dataDirectory) =>
        new SqliteAuditStore(Path.Combine(dataDirectory, "audit.db"));

    private static RegisterRepositoryUseCase CreateRegisterUseCase(SqliteAuditStore store) =>
        new RegisterRepositoryUseCase(store, new GitAccessFactory(), new FixedClock(_sampleUtcNow));

    /// <summary>真实临时目录夹具。</summary>
    private sealed class TempDirectory : IDisposable
    {
        internal TempDirectory()
        {
            Root = Path.Combine(Path.GetTempPath(), "gitguard-repository-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
        }

        internal string Root { get; }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Root, recursive: true);
            }
            catch (IOException)
            {
                // 句柄偶发被系统占用：清理失败不影响测试结论
            }
        }
    }
}
