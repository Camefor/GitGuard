using System.ComponentModel;
using FluentAssertions;
using GitGuard.Core;
using GitGuard.Infrastructure.Git;

namespace GitGuard.Infrastructure.Tests;

/// <summary>GitAccess 仓库预检与元数据查询集成测试（真实 git 仓库）。</summary>
public sealed class GitAccessRepositoryTests
{
    [Fact]
    public void Ctor_NotAGitRepository_ThrowsNotAGitRepository()
    {
        using var directory = new TempDirectory();

        Action act = () => CreateAccess(directory.Root);

        act.Should().Throw<GitException>()
            .Which.ErrorCode.Should().Be(GitException.NOT_A_GIT_REPOSITORY);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Ctor_EmptyOrWhitespacePath_ThrowsArgumentException(string path)
    {
        Action act = () => CreateAccess(path);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Ctor_NullPath_ThrowsArgumentNullException()
    {
        Action act = () => CreateAccess((string)null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Ctor_MissingDirectory_ThrowsGitCommandFailed()
    {
        string missingDirectory = Path.Combine(Path.GetTempPath(), "gitguard-infra-tests", Guid.NewGuid().ToString("N"));

        Action act = () => CreateAccess(missingDirectory);

        GitException exception = act.Should().Throw<GitException>().Which;
        exception.ErrorCode.Should().Be(GitException.GIT_COMMAND_FAILED);
        exception.InnerException.Should().BeOfType<Win32Exception>();
    }

    [Fact]
    public void Ctor_MissingGitExecutable_ThrowsGitCommandFailed()
    {
        using var repo = new TempGitRepository();

        Action act = () => CreateAccess(repo.Root, "gitguard-test-missing-git-binary");

        act.Should().Throw<GitException>()
            .Which.ErrorCode.Should().Be(GitException.GIT_COMMAND_FAILED);
    }

    [Fact]
    public void Ctor_NonPositiveTimeout_ThrowsArgumentOutOfRangeException()
    {
        using var repo = new TempGitRepository();

        Action act = () => CreateAccess(repo.Root, TimeSpan.Zero);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Ctor_PreflightExceedsTimeout_ThrowsGitCommandFailed()
    {
        using var repo = new TempGitRepository();

        // 1 tick 超时：WaitForExit(0) 立即超时，覆盖超时终止与 FailClosed 路径（DR-03 / 10-异常处理设计 §6）
        Action act = () => CreateAccess(repo.Root, TimeSpan.FromTicks(1));

        GitException exception = act.Should().Throw<GitException>().Which;
        exception.ErrorCode.Should().Be(GitException.GIT_COMMAND_FAILED);
        exception.ExitCode.Should().BeNull();
        exception.Message.Should().Contain("超时");
    }

    [Fact]
    public void GetRepositoryRoot_InsideRepository_ReturnsNormalizedAbsolutePath()
    {
        using var repo = new TempGitRepository();
        GitAccess access = CreateAccess(repo);

        access.GetRepositoryRoot().Should().Be(Path.GetFullPath(repo.Root));
    }

    [Fact]
    public void GetRepositoryRoot_FromSubdirectory_ReturnsWorkingTreeRoot()
    {
        using var repo = new TempGitRepository();
        string subdirectory = Path.Combine(repo.Root, "sub");
        Directory.CreateDirectory(subdirectory);

        GitAccess access = CreateAccess(subdirectory);

        access.GetRepositoryRoot().Should().Be(Path.GetFullPath(repo.Root));
    }

    [Fact]
    public void GetGitCommonDirectory_PlainRepository_ResolvesToDotGitDirectory()
    {
        using var repo = new TempGitRepository();
        GitAccess access = CreateAccess(repo);

        string commonDirectory = access.GetGitCommonDirectory();

        commonDirectory.Should().Be(Path.GetFullPath(Path.Combine(repo.Root, ".git")));
        File.Exists(Path.Combine(commonDirectory, "HEAD")).Should().BeTrue();
    }

    [Fact]
    public void GetGitCommonDirectory_FromSubdirectory_ReturnsSamePath()
    {
        using var repo = new TempGitRepository();
        string subdirectory = Path.Combine(repo.Root, "sub");
        Directory.CreateDirectory(subdirectory);
        GitAccess fromRoot = CreateAccess(repo);
        GitAccess fromSubdirectory = CreateAccess(subdirectory);

        fromSubdirectory.GetGitCommonDirectory().Should().Be(fromRoot.GetGitCommonDirectory());
    }

    [Fact]
    public void GetConfiguredHooksPath_Unset_ReturnsNull()
    {
        using var repo = new TempGitRepository();
        GitAccess access = CreateAccess(repo);

        access.GetConfiguredHooksPath().Should().BeNull();
    }

    [Fact]
    public void GetConfiguredHooksPath_RelativePath_ResolvedAgainstRepositoryRoot()
    {
        using var repo = new TempGitRepository();
        repo.RunGit("config", "core.hooksPath", ".githooks");
        GitAccess access = CreateAccess(repo);

        access.GetConfiguredHooksPath().Should().Be(Path.GetFullPath(Path.Combine(repo.Root, ".githooks")));
    }

    [Fact]
    public void GetConfiguredHooksPath_AbsolutePath_ReturnedNormalized()
    {
        using var repo = new TempGitRepository();
        string configured = Path.Combine(repo.Root, "custom-hooks");
        repo.RunGit("config", "core.hooksPath", configured);
        GitAccess access = CreateAccess(repo);

        access.GetConfiguredHooksPath().Should().Be(Path.GetFullPath(configured));
    }

    [Fact]
    public void GetConfiguredHooksPath_ConfigChangedAfterFirstRead_ReturnsCachedValue()
    {
        using var repo = new TempGitRepository();
        GitAccess access = CreateAccess(repo);
        access.GetConfiguredHooksPath().Should().BeNull();

        repo.RunGit("config", "core.hooksPath", ".githooks");

        // 07-缓存设计 §4：Git 元数据单次进程内只查询一次
        access.GetConfiguredHooksPath().Should().BeNull();
    }

    [Fact]
    public void GetHeadCommitSha_AfterCommit_ReturnsHead()
    {
        using var repo = new TempGitRepository();
        repo.WriteFile("a.txt", "content\n");
        repo.Stage("a.txt");
        string commit = repo.Commit("first");
        GitAccess access = CreateAccess(repo);

        access.GetHeadCommitSha().Should().Be(commit);
    }

    [Fact]
    public void GetHeadCommitSha_NewCommitAfterFirstRead_ReturnsCachedSha()
    {
        using var repo = new TempGitRepository();
        repo.WriteFile("a.txt", "one\n");
        repo.Stage("a.txt");
        string first = repo.Commit("first");
        GitAccess access = CreateAccess(repo);
        string cached = access.GetHeadCommitSha();

        repo.WriteFile("a.txt", "two\n");
        repo.Stage("a.txt");
        string second = repo.Commit("second");

        cached.Should().Be(first);
        second.Should().NotBe(first);

        // 07-缓存设计 §4：HEAD 元数据单次进程内只查询一次
        access.GetHeadCommitSha().Should().Be(first);
    }

    [Fact]
    public void GetHeadCommitSha_UnbornHead_ThrowsGitCommandFailed()
    {
        using var repo = new TempGitRepository();
        GitAccess access = CreateAccess(repo);

        Action act = () => access.GetHeadCommitSha();

        act.Should().Throw<GitException>()
            .Which.ErrorCode.Should().Be(GitException.GIT_COMMAND_FAILED);
    }

    [Fact]
    public void GetVersion_ReturnsGitVersionLine()
    {
        using var repo = new TempGitRepository();
        GitAccess access = CreateAccess(repo);

        access.GetVersion().Should().StartWith("git version");
    }

    private static GitAccess CreateAccess(TempGitRepository repo) => CreateAccess(repo.Root);

    private static GitAccess CreateAccess(string repositoryPath) => new GitAccess(repositoryPath);

    private static GitAccess CreateAccess(string repositoryPath, string gitExecutable) => new GitAccess(repositoryPath, gitExecutable);

    private static GitAccess CreateAccess(string repositoryPath, TimeSpan commandTimeout) => new GitAccess(repositoryPath, commandTimeout: commandTimeout);
}
