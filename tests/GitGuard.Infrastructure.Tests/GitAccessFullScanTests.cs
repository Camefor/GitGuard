using FluentAssertions;
using GitGuard.Core;
using GitGuard.Infrastructure.Git;

namespace GitGuard.Infrastructure.Tests;

/// <summary>
/// GitAccess 全量树差异集成测试（FR-060 / DR-04）：以空树与目标修订的差异作为 HEAD 快照来源，
/// 覆盖未改动文件、已删除文件、工作区未暂存内容与空仓库边界。
/// </summary>
public sealed class GitAccessFullScanTests
{
    [Fact]
    public void GetFullTreeDiff_HeadSnapshot_ContainsAllFilesTrackedAtHeadAsAdditions()
    {
        using var repo = new TempGitRepository();
        repo.WriteFile("keep.txt", "keep content\n");
        repo.WriteFile("nested/keep-too.txt", "nested content\n");
        repo.WriteFile("removed.txt", "gone content\n");
        repo.Stage("keep.txt", "nested/keep-too.txt", "removed.txt");
        repo.Commit("first");
        File.Delete(Path.Combine(repo.Root, "removed.txt"));
        repo.WriteFile("added.txt", "added content\n");
        repo.Stage("removed.txt", "added.txt");
        repo.Commit("second");
        var access = new GitAccess(repo.Root);

        string diff = access.GetFullTreeDiff("HEAD");

        // 未在后续提交改动的文件仍属于 HEAD 树，必须以新增形式完整出现
        diff.Should().Contain("a/keep.txt");
        diff.Should().Contain("+keep content");
        diff.Should().Contain("a/nested/keep-too.txt");
        diff.Should().Contain("+nested content");
        diff.Should().Contain("a/added.txt");
        diff.Should().Contain("+added content");
        diff.Should().Contain("new file mode");

        // HEAD 之前已删除的文件不属于 HEAD 树
        diff.Should().NotContain("removed.txt");
        diff.Should().NotContain("gone content");
    }

    [Fact]
    public void GetFullTreeDiff_ModifiedFile_ReturnsCommittedContentNotWorkingTree()
    {
        using var repo = new TempGitRepository();
        repo.WriteFile("secret.txt", "committed value\n");
        repo.Stage("secret.txt");
        repo.Commit("init");
        repo.WriteFile("secret.txt", "worktree only\n");
        var access = new GitAccess(repo.Root);

        string diff = access.GetFullTreeDiff("HEAD");

        diff.Should().Contain("+committed value");
        diff.Should().NotContain("worktree only");
    }

    [Fact]
    public void GetFullTreeDiff_FromSubdirectory_CoversWholeRepository()
    {
        using var repo = new TempGitRepository();
        repo.WriteFile("root.txt", "root content\n");
        repo.WriteFile("sub/inner.txt", "inner content\n");
        repo.Stage("root.txt", "sub/inner.txt");
        repo.Commit("init");
        var access = new GitAccess(Path.Combine(repo.Root, "sub"));

        string diff = access.GetFullTreeDiff("HEAD");

        diff.Should().Contain("+root content");
        diff.Should().Contain("+inner content");
    }

    [Fact]
    public void GetFullTreeDiff_SpecificOlderRevision_ReturnsThatRevisionSnapshot()
    {
        using var repo = new TempGitRepository();
        repo.WriteFile("app.txt", "first version\n");
        repo.Stage("app.txt");
        string firstSha = repo.Commit("first");
        repo.WriteFile("app.txt", "second version\n");
        repo.Stage("app.txt");
        repo.Commit("second");
        var access = new GitAccess(repo.Root);

        string diff = access.GetFullTreeDiff(firstSha);

        diff.Should().Contain("+first version");
        diff.Should().NotContain("+second version");
    }

    [Fact]
    public void GetFullTreeDiff_EmptyRepository_ThrowsGitException()
    {
        using var repo = new TempGitRepository();
        var access = new GitAccess(repo.Root);

        access.TryGetHeadCommitSha().Should().BeNull();
        Action act = () => access.GetFullTreeDiff("HEAD");

        act.Should().Throw<GitException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("-abc")]
    public void GetFullTreeDiff_InvalidRevision_ThrowsArgumentException(string revision)
    {
        using var repo = new TempGitRepository();
        repo.WriteFile("app.txt", "content\n");
        repo.Stage("app.txt");
        repo.Commit("init");
        var access = new GitAccess(repo.Root);

        Action act = () => access.GetFullTreeDiff(revision);

        act.Should().Throw<ArgumentException>();
    }
}
