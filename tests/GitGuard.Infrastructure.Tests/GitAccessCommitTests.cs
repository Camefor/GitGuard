using FluentAssertions;
using GitGuard.Core;
using GitGuard.Infrastructure.Git;

namespace GitGuard.Infrastructure.Tests;

/// <summary>GitAccess 提交枚举与逐提交差异集成测试（FR-030 预推送扫描的数据来源）。</summary>
public sealed class GitAccessCommitTests
{
    [Fact]
    public void ListCommitShas_SingleTipCommit_ReturnsAllReachableNewestFirst()
    {
        using var repo = new TempGitRepository();
        string first = CreateCommit(repo, "a.txt", "one\n", "c1");
        string second = CreateCommit(repo, "b.txt", "two\n", "c2");
        string third = CreateCommit(repo, "c.txt", "three\n", "c3");
        GitAccess access = CreateAccess(repo);

        // FR-030：新分支（remote-sha 全零）范围为 local-sha 单值
        IReadOnlyList<string> shas = access.ListCommitShas(third);

        shas.Should().Equal(third, second, first);
    }

    [Fact]
    public void ListCommitShas_Range_ExcludesLowerBound()
    {
        using var repo = new TempGitRepository();
        string first = CreateCommit(repo, "a.txt", "one\n", "c1");
        string second = CreateCommit(repo, "b.txt", "two\n", "c2");
        string third = CreateCommit(repo, "c.txt", "three\n", "c3");
        GitAccess access = CreateAccess(repo);

        IReadOnlyList<string> shas = access.ListCommitShas($"{first}..{third}");

        shas.Should().Equal(third, second);
    }

    [Fact]
    public void ListCommitShas_EqualRange_ReturnsEmpty()
    {
        using var repo = new TempGitRepository();
        string first = CreateCommit(repo, "a.txt", "one\n", "c1");
        GitAccess access = CreateAccess(repo);

        access.ListCommitShas($"{first}..{first}").Should().BeEmpty();
    }

    [Fact]
    public void ListCommitShas_UnknownRevision_ThrowsGitCommandFailed()
    {
        using var repo = new TempGitRepository();
        GitAccess access = CreateAccess(repo);

        Action act = () => access.ListCommitShas("0123456789abcdef0123456789abcdef01234567");

        // TC-DR-03-01 同类场景（GitAccess 层）：git 非零退出 → FailClosed
        GitException exception = act.Should().Throw<GitException>().Which;
        exception.ErrorCode.Should().Be(GitException.GIT_COMMAND_FAILED);
        exception.ExitCode.Should().Be(128);
        exception.StandardError.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void ListCommitShas_LeadingDash_ThrowsArgumentException()
    {
        using var repo = new TempGitRepository();
        GitAccess access = CreateAccess(repo);

        Action act = () => access.ListCommitShas("--all");

        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ListCommitShas_EmptyOrNullRange_ThrowsArgumentException(string? revisionRange)
    {
        using var repo = new TempGitRepository();
        GitAccess access = CreateAccess(repo);

        Action act = () => access.ListCommitShas(revisionRange!);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void GetCommitDiff_AddedLine_ReturnsDiffWithoutCommitIdHeader()
    {
        using var repo = new TempGitRepository();
        CreateCommit(repo, "a.txt", "one\n", "c1");
        string second = CreateCommit(repo, "b.txt", "token=x\n", "c2");
        GitAccess access = CreateAccess(repo);

        string diff = access.GetCommitDiff(second);

        diff.Should().Contain("+token=x");
        diff.Split('\n')[0].Should().StartWith("diff --git");
    }

    [Fact]
    public void GetCommitDiff_RootCommit_ShowsNewFileAsAddition()
    {
        using var repo = new TempGitRepository();
        string first = CreateCommit(repo, "a.txt", "root-content\n", "c1");
        GitAccess access = CreateAccess(repo);

        // --root：初始提交视为全量新增；缺省时 git 对其输出为空（新分支推送会漏扫）
        string diff = access.GetCommitDiff(first);

        diff.Should().Contain("new file mode");
        diff.Should().Contain("+root-content");
    }

    [Fact]
    public void GetCommitDiff_CommitRemovingSecret_ShowsRemovalOnly()
    {
        using var repo = new TempGitRepository();
        string withSecret = CreateCommit(repo, "cfg.txt", $"token={TestData.CanaryToken}\n", "add secret");
        string withoutSecret = CreateCommit(repo, "cfg.txt", "token=REMOVED\n", "remove secret");
        GitAccess access = CreateAccess(repo);

        // FR-030 场景：第 2 个提交含密钥、第 3 个提交已删除 —— 命中来自各自的提交差异
        access.GetCommitDiff(withSecret).Should().Contain($"+token={TestData.CanaryToken}");

        string removal = access.GetCommitDiff(withoutSecret);
        removal.Should().Contain($"-token={TestData.CanaryToken}");
        removal.Should().NotContain($"+token={TestData.CanaryToken}");
    }

    [Fact]
    public void GetCommitDiff_UnknownSha_ThrowsGitCommandFailed()
    {
        using var repo = new TempGitRepository();
        CreateCommit(repo, "a.txt", "one\n", "c1");
        GitAccess access = CreateAccess(repo);

        Action act = () => access.GetCommitDiff("0123456789abcdef0123456789abcdef01234567");

        GitException exception = act.Should().Throw<GitException>().Which;
        exception.ErrorCode.Should().Be(GitException.GIT_COMMAND_FAILED);
        exception.ExitCode.Should().Be(128);
    }

    [Fact]
    public void GetCommitDiff_LeadingDash_ThrowsArgumentException()
    {
        using var repo = new TempGitRepository();
        GitAccess access = CreateAccess(repo);

        Action act = () => access.GetCommitDiff("--help");

        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void GetCommitDiff_EmptyOrNullSha_ThrowsArgumentException(string? commitSha)
    {
        using var repo = new TempGitRepository();
        GitAccess access = CreateAccess(repo);

        Action act = () => access.GetCommitDiff(commitSha!);

        act.Should().Throw<ArgumentException>();
    }

    private static string CreateCommit(TempGitRepository repo, string fileName, string content, string message)
    {
        repo.WriteFile(fileName, content);
        repo.Stage(fileName);
        return repo.Commit(message);
    }

    private static GitAccess CreateAccess(TempGitRepository repo) => new GitAccess(repo.Root);
}
