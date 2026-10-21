using FluentAssertions;
using GitGuard.Core;
using GitGuard.Core.Model;
using GitGuard.Infrastructure.Git;

namespace GitGuard.Infrastructure.Tests;

/// <summary>GitAccess 提交枚举与批量差异集成测试（FR-030 预推送扫描的数据来源）。</summary>
public sealed class GitAccessCommitTests
{
    private static readonly string[] UnknownSha = new[] { "0123456789abcdef0123456789abcdef01234567" };
    private static readonly string[] LeadingDashSha = new[] { "--help" };

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
    public void GetCommitDiffs_MultipleCommits_ReturnsRequestedOrderAndOwningDiff()
    {
        using var repo = new TempGitRepository();
        string first = CreateCommit(repo, "a.txt", "one\n", "c1");
        string second = CreateCommit(repo, "b.txt", "token=x\n", "c2");
        string third = CreateCommit(repo, "a.txt", "one\nchanged\n", "c3");
        GitAccess access = CreateAccess(repo);

        IReadOnlyList<CommitDiff> diffs = access.GetCommitDiffs(new[] { third, first, second });

        diffs.Select(diff => diff.Sha).Should().Equal(third, first, second);
        diffs[0].DiffText.Should().Contain("+changed");
        diffs[1].DiffText.Should().Contain("new file mode");
        diffs[1].DiffText.Should().Contain("+one");
        diffs[2].DiffText.Should().Contain("+token=x");
        diffs[0].DiffText.Should().StartWith("diff --git");
    }

    [Fact]
    public void GetCommitDiffs_RootCommit_ShowsNewFileAsAddition()
    {
        using var repo = new TempGitRepository();
        string first = CreateCommit(repo, "a.txt", "root-content\n", "c1");
        GitAccess access = CreateAccess(repo);

        // --root：初始提交视为全量新增；缺省时 git 对其输出为空（新分支推送会漏扫）
        CommitDiff diff = access.GetCommitDiffs(new[] { first }).Should().ContainSingle().Subject;

        diff.DiffText.Should().Contain("new file mode");
        diff.DiffText.Should().Contain("+root-content");
    }

    [Fact]
    public void GetCommitDiffs_CommitRemovingSecret_ShowsRemovalOnly()
    {
        using var repo = new TempGitRepository();
        string withSecret = CreateCommit(repo, "cfg.txt", $"token={TestData.CanaryToken}\n", "add secret");
        string withoutSecret = CreateCommit(repo, "cfg.txt", "token=REMOVED\n", "remove secret");
        GitAccess access = CreateAccess(repo);

        // FR-030 场景：第 2 个提交含密钥、第 3 个提交已删除 —— 命中来自各自的提交差异
        IReadOnlyList<CommitDiff> diffs = access.GetCommitDiffs(new[] { withSecret, withoutSecret });

        diffs[0].DiffText.Should().Contain($"+token={TestData.CanaryToken}");
        diffs[1].DiffText.Should().Contain($"-token={TestData.CanaryToken}");
        diffs[1].DiffText.Should().NotContain($"+token={TestData.CanaryToken}");
    }

    [Fact]
    public void GetCommitDiffs_EmptyCommit_ReturnsEmptyDiff()
    {
        using var repo = new TempGitRepository();
        CreateCommit(repo, "a.txt", "one\n", "c1");
        repo.RunGit("commit", "--allow-empty", "--quiet", "-m", "empty");
        string emptyCommit = repo.RunGit("rev-parse", "HEAD");
        GitAccess access = CreateAccess(repo);

        CommitDiff diff = access.GetCommitDiffs(new[] { emptyCommit }).Should().ContainSingle().Subject;

        diff.Sha.Should().Be(emptyCommit);
        diff.DiffText.Should().BeEmpty();
    }

    [Fact]
    public void GetCommitDiffs_EmptyList_ReturnsEmptyWithoutGitCall()
    {
        using var repo = new TempGitRepository();
        GitAccess access = CreateAccess(repo);

        access.GetCommitDiffs(Array.Empty<string>()).Should().BeEmpty();
    }

    [Fact]
    public void GetCommitDiffs_NullList_ThrowsArgumentNullException()
    {
        using var repo = new TempGitRepository();
        GitAccess access = CreateAccess(repo);

        Action act = () => access.GetCommitDiffs(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void GetCommitDiffs_UnknownSha_ThrowsGitCommandFailed()
    {
        using var repo = new TempGitRepository();
        CreateCommit(repo, "a.txt", "one\n", "c1");
        GitAccess access = CreateAccess(repo);

        Action act = () => access.GetCommitDiffs(UnknownSha);

        GitException exception = act.Should().Throw<GitException>().Which;
        exception.ErrorCode.Should().Be(GitException.GIT_COMMAND_FAILED);
        exception.ExitCode.Should().Be(128);
    }

    [Fact]
    public void GetCommitDiffs_MixedKnownAndUnknownSha_ThrowsInsteadOfSkippingUnknown()
    {
        using var repo = new TempGitRepository();
        string first = CreateCommit(repo, "a.txt", "one\n", "c1");
        GitAccess access = CreateAccess(repo);

        // 回归护栏：git diff-tree --stdin 对未知提交静默跳过且退出 0；批量接口必须 FailClosed，
        // 否则漏扫提交会直接放行其中的敏感内容。
        Action act = () => access.GetCommitDiffs(new[] { first, "0123456789abcdef0123456789abcdef01234567" });

        act.Should().Throw<GitException>()
            .Which.ErrorCode.Should().Be(GitException.GIT_COMMAND_FAILED);
    }

    [Fact]
    public void GetCommitDiffs_LeadingDash_ThrowsArgumentException()
    {
        using var repo = new TempGitRepository();
        GitAccess access = CreateAccess(repo);

        Action act = () => access.GetCommitDiffs(LeadingDashSha);

        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("zzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzz")]
    public void GetCommitDiffs_InvalidSha_ThrowsArgumentException(string? commitSha)
    {
        using var repo = new TempGitRepository();
        GitAccess access = CreateAccess(repo);

        Action act = () => access.GetCommitDiffs(new[] { commitSha! });

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void GetCommitDiffs_DuplicateSha_ThrowsArgumentException()
    {
        using var repo = new TempGitRepository();
        string first = CreateCommit(repo, "a.txt", "one\n", "c1");
        GitAccess access = CreateAccess(repo);

        Action act = () => access.GetCommitDiffs(new[] { first, first });

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
