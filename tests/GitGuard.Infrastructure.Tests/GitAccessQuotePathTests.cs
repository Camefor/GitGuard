using FluentAssertions;
using GitGuard.Infrastructure.Git;

namespace GitGuard.Infrastructure.Tests;

/// <summary>
/// GitAccess 路径编码集成测试：<c>-c core.quotepath=false</c> 下非 ASCII 路径以原始 UTF-8 输出（无八进制转义）；
/// 含空格路径不加引号、<c>+++</c> 行以 TAB 结尾（解析层剥离契约）。
/// </summary>
public sealed class GitAccessQuotePathTests
{
    [Fact]
    public void GetStagedDiff_NonAsciiPath_ReturnsRawUtf8PathWithoutOctalEscapes()
    {
        using var repo = new TempGitRepository();
        repo.WriteFile("文档/说明.txt", "内容\n");

        // 经 add -A 暂存，避免测试进程为暂存传递非 ASCII 参数（被测点是 diff 输出而非暂存方式）
        repo.RunGit("add", "-A");
        var access = new GitAccess(repo.Root);

        string diff = access.GetStagedDiff();

        diff.Should().Contain("diff --git a/文档/说明.txt b/文档/说明.txt");
        diff.Should().Contain("+++ b/文档/说明.txt");

        // 默认 core.quotepath=true 会输出 \346\226\207 形式的八进制转义（解析层无法直接还原真实路径）
        diff.Should().NotContain("\\346");
    }

    [Fact]
    public void GetCommitDiff_NonAsciiPath_ReturnsRawUtf8Path()
    {
        using var repo = new TempGitRepository();
        repo.WriteFile("文档/说明.txt", "内容\n");
        repo.RunGit("add", "-A");
        string commitSha = repo.Commit("add cjk file");
        var access = new GitAccess(repo.Root);

        string diff = access.GetCommitDiff(commitSha);

        diff.Should().Contain("+++ b/文档/说明.txt");
        diff.Should().NotContain("\\346");
    }

    [Fact]
    public void GetStagedDiff_PathWithSpace_ReturnsUnquotedHeaderAndTabTerminatedPathLine()
    {
        using var repo = new TempGitRepository();
        repo.WriteFile("my file.txt", "hello\n");
        repo.Stage("my file.txt");
        var access = new GitAccess(repo.Root);

        string diff = access.GetStagedDiff();

        // 含空格路径整体不加引号，diff --git 头可按 " b/" 分隔出目标路径
        diff.Should().Contain("diff --git a/my file.txt b/my file.txt");
        diff.Should().NotContain("\"b/my file.txt\"");

        // 实测契约（git diff-format）：未引用路径的 +++ 行末尾追加 TAB（与可选时间戳字段分隔）；
        // UnifiedDiffParser 依赖该形式剥离末尾 TAB 还原真实路径
        diff.Should().Contain("+++ b/my file.txt\t");
    }
}
