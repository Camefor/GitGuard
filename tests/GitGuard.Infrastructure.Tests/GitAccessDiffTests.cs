using FluentAssertions;
using GitGuard.Infrastructure.Git;

namespace GitGuard.Infrastructure.Tests;

/// <summary>GitAccess 暂存区差异集成测试（DR-04：唯一差异来源为 git diff --cached）。</summary>
public sealed class GitAccessDiffTests
{
    [Fact]
    public void GetStagedDiff_StagedModification_ReturnsUnifiedZeroDiff()
    {
        using var repo = new TempGitRepository();
        repo.WriteFile("app.txt", "first line\n");
        repo.Stage("app.txt");
        repo.Commit("init");
        repo.WriteFile("app.txt", "first line\nsecret=abc\n");
        repo.Stage("app.txt");
        GitAccess access = CreateAccess(repo);

        string diff = access.GetStagedDiff();

        diff.Should().StartWith("diff --git");
        diff.Should().Contain("@@");
        diff.Should().Contain("+secret=abc");

        // -U0：差异不含上下文行（上下文行以单个空格开头）
        diff.Split('\n').Should().NotContain(line => line.StartsWith(' '));
    }

    [Fact]
    public void GetStagedDiff_UnstagedOnlyChange_ReturnsEmpty()
    {
        using var repo = new TempGitRepository();
        repo.WriteFile("secret.txt", "SENTINEL original\n");
        repo.Stage("secret.txt");
        repo.Commit("init");
        repo.WriteFile("secret.txt", "SENTINEL modified but unstaged\n");
        GitAccess access = CreateAccess(repo);

        string diff = access.GetStagedDiff();

        // TC-FR-020-02 / DR-04：未暂存内容不得进入差异
        diff.Should().BeNullOrWhiteSpace();
        diff.Should().NotContain("SENTINEL modified");
    }

    [Fact]
    public void GetStagedDiff_FreshRepositoryWithoutCommits_ShowsAllStagedAsNew()
    {
        using var repo = new TempGitRepository();
        repo.WriteFile("new.txt", "hello\n");
        repo.Stage("new.txt");
        GitAccess access = CreateAccess(repo);

        string diff = access.GetStagedDiff();

        diff.Should().Contain("new file mode");
        diff.Should().Contain("+hello");
    }

    [Fact]
    public void GetStagedDiff_StagedDeletion_ShowsRemovedLines()
    {
        using var repo = new TempGitRepository();
        repo.WriteFile("gone.txt", "doomed\n");
        repo.Stage("gone.txt");
        repo.Commit("init");
        File.Delete(Path.Combine(repo.Root, "gone.txt"));
        repo.Stage("gone.txt");
        GitAccess access = CreateAccess(repo);

        string diff = access.GetStagedDiff();

        diff.Should().Contain("deleted file mode");
        diff.Should().Contain("-doomed");
    }

    [Fact]
    public void GetStagedDiff_CanaryToken_ReturnsRawUnmaskedText()
    {
        using var repo = new TempGitRepository();
        repo.WriteFile("config.env", $"token={TestData.CanaryToken}\n");
        repo.Stage("config.env");
        GitAccess access = CreateAccess(repo);

        string diff = access.GetStagedDiff();

        // 本层按契约返回 git 原始差异；DR-02 脱敏发生在 Finding 构建阶段（由 Core 负责）
        diff.Should().Contain(TestData.CanaryToken);
    }

    [Fact]
    public void GetStagedDiff_FromSubdirectory_CoversWholeRepository()
    {
        using var repo = new TempGitRepository();
        repo.WriteFile("root.txt", "root content\n");
        repo.WriteFile("sub/inner.txt", "inner content\n");
        repo.Stage("root.txt", "sub/inner.txt");
        GitAccess access = CreateAccess(Path.Combine(repo.Root, "sub"));

        string diff = access.GetStagedDiff();

        diff.Should().Contain("root content");
        diff.Should().Contain("inner content");
    }

    private static GitAccess CreateAccess(TempGitRepository repo) => new GitAccess(repo.Root);

    private static GitAccess CreateAccess(string repositoryPath) => new GitAccess(repositoryPath);
}
