using FluentAssertions;
using GitGuard.Core;
using GitGuard.Core.Model;
using GitGuard.Infrastructure.Git;

namespace GitGuard.Infrastructure.Tests;

/// <summary>
/// GitAccess 绕过检测支撑方法集成测试（SRS FR-050）：工作树 Git 目录、可空 HEAD、暂存树与提交元数据，
/// 一律使用真实临时仓库与真实 git 二进制（16-测试方案 §2）。
/// </summary>
public sealed class GitAccessBypassTests
{
    private const string EMPTY_TREE_SHA = "4b825dc642cb6eb9a060e54bf8d69288fbee4904";

    [Fact]
    public void GetGitDirectory_PlainRepository_ResolvesToDotGitDirectory()
    {
        using var repo = new TempGitRepository();
        GitAccess access = CreateAccess(repo);

        access.GetGitDirectory().Should().Be(Path.GetFullPath(Path.Combine(repo.Root, ".git")));
    }

    [Fact]
    public void GetGitDirectory_LinkedWorktree_IsWorktreeSpecific()
    {
        using var repo = new TempGitRepository();
        CreateCommit(repo, "a.txt", "one\n", "c1");
        string worktreePath = Path.Combine(Path.GetTempPath(), "gitguard-infra-tests", Guid.NewGuid().ToString("N"));
        try
        {
            // 不加 --quiet（Git 2.9 的 worktree add 尚不支持该选项）
            repo.RunGit("worktree", "add", worktreePath, "-b", "linked");

            var access = new GitAccess(worktreePath);

            access.GetGitDirectory().Should().NotBe(access.GetGitCommonDirectory(), "每个工作树必须拥有独立的待提交标记");
            access.GetGitDirectory().Should().Contain("worktrees");
            access.GetGitCommonDirectory().Should().Be(Path.GetFullPath(Path.Combine(repo.Root, ".git")));
        }
        finally
        {
            CleanupWorktree(repo, worktreePath);
        }
    }

    [Fact]
    public void TryGetHeadCommitSha_UnbornRepository_ReturnsNull()
    {
        using var repo = new TempGitRepository();
        GitAccess access = CreateAccess(repo);

        access.TryGetHeadCommitSha().Should().BeNull("尚无提交时 HEAD 不可解析，不得视为 git 失败");
    }

    [Fact]
    public void TryGetHeadCommitSha_AfterCommit_ReturnsHead()
    {
        using var repo = new TempGitRepository();
        string head = CreateCommit(repo, "a.txt", "one\n", "c1");
        GitAccess access = CreateAccess(repo);

        access.TryGetHeadCommitSha().Should().Be(head);
    }

    [Fact]
    public void GetStagedTreeSha_EmptyIndex_ReturnsCanonicalEmptyTree()
    {
        using var repo = new TempGitRepository();
        GitAccess access = CreateAccess(repo);

        access.GetStagedTreeSha().Should().Be(EMPTY_TREE_SHA);
    }

    [Fact]
    public void GetStagedTreeSha_StagedChanges_MatchGitWriteTree()
    {
        using var repo = new TempGitRepository();
        repo.WriteFile("a.txt", "one\n");
        repo.Stage("a.txt");
        GitAccess access = CreateAccess(repo);

        string treeSha = access.GetStagedTreeSha();

        treeSha.Should().NotBe(EMPTY_TREE_SHA);
        treeSha.Should().Be(repo.RunGit("write-tree"));
    }

    [Fact]
    public void GetCommitMetadata_RootCommit_HasTreeAndNoParents()
    {
        using var repo = new TempGitRepository();
        string rootSha = CreateCommit(repo, "a.txt", "root\n", "root");
        GitAccess access = CreateAccess(repo);

        CommitMetadata metadata = access.GetCommitMetadata(rootSha);

        metadata.Sha.Should().Be(rootSha);
        metadata.TreeSha.Should().Be(repo.RunGit("rev-parse", rootSha + "^{tree}"));
        metadata.Parents.Should().BeEmpty();
    }

    [Fact]
    public void GetCommitMetadata_NormalCommit_FirstParentIsPreviousHead()
    {
        using var repo = new TempGitRepository();
        string first = CreateCommit(repo, "a.txt", "one\n", "c1");
        string second = CreateCommit(repo, "b.txt", "two\n", "c2");
        GitAccess access = CreateAccess(repo);

        CommitMetadata metadata = access.GetCommitMetadata(second);

        metadata.Parents.Should().Equal(first);
        metadata.TreeSha.Should().Be(repo.RunGit("rev-parse", second + "^{tree}"));
    }

    [Fact]
    public void GetCommitMetadata_MergeCommit_ReturnsBothParentsInOrder()
    {
        using var repo = new TempGitRepository();
        CreateCommit(repo, "base.txt", "base\n", "base");
        string originalBranch = repo.RunGit("rev-parse", "--abbrev-ref", "HEAD");

        repo.RunGit("checkout", "--quiet", "-b", "feature");
        string featureSha = CreateCommit(repo, "feature.txt", "feature\n", "feature");

        repo.RunGit("checkout", "--quiet", originalBranch);
        string mainSha = CreateCommit(repo, "main.txt", "main\n", "main");
        repo.RunGit("merge", "--quiet", "--no-ff", "-m", "merge feature", "feature");
        string mergeSha = repo.RunGit("rev-parse", "HEAD");
        GitAccess access = CreateAccess(repo);

        CommitMetadata metadata = access.GetCommitMetadata(mergeSha);

        metadata.Parents.Should().Equal(mainSha, featureSha);
        metadata.TreeSha.Should().Be(repo.RunGit("rev-parse", mergeSha + "^{tree}"));
    }

    [Fact]
    public void GetCommitMetadata_UnknownSha_ThrowsGitCommandFailed()
    {
        using var repo = new TempGitRepository();
        CreateCommit(repo, "a.txt", "one\n", "c1");
        GitAccess access = CreateAccess(repo);

        Action act = () => access.GetCommitMetadata(new string('0', 40));

        act.Should().Throw<GitException>().Which.ErrorCode.Should().Be(GitException.GIT_COMMAND_FAILED);
    }

    [Fact]
    public void GetCommitMetadata_LeadingDash_ThrowsArgumentException()
    {
        using var repo = new TempGitRepository();
        GitAccess access = CreateAccess(repo);

        Action act = () => access.GetCommitMetadata("--all");

        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void GetCommitMetadata_EmptyOrNullSha_ThrowsArgumentException(string? commitSha)
    {
        using var repo = new TempGitRepository();
        GitAccess access = CreateAccess(repo);

        Action act = () => access.GetCommitMetadata(commitSha!);

        act.Should().Throw<ArgumentException>();
    }

    private static string CreateCommit(TempGitRepository repo, string fileName, string content, string message)
    {
        repo.WriteFile(fileName, content);
        repo.Stage(fileName);
        return repo.Commit(message);
    }

    private static GitAccess CreateAccess(TempGitRepository repo) => new GitAccess(repo.Root);

    private static void CleanupWorktree(TempGitRepository repo, string worktreePath)
    {
        try
        {
            repo.RunGit("worktree", "remove", "--force", worktreePath);
        }
        catch (InvalidOperationException)
        {
            // 工作树元数据清理失败不阻塞测试结论：目录仍由下方兜底删除
        }

        try
        {
            if (Directory.Exists(worktreePath))
            {
                Directory.Delete(worktreePath, recursive: true);
            }
        }
        catch (IOException)
        {
            // 句柄偶发被系统占用：留待系统清理临时目录
        }
        catch (UnauthorizedAccessException)
        {
            // 权限竞态：同上
        }
    }
}
