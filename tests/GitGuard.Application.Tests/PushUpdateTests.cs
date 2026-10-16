using FluentAssertions;
using GitGuard.Application.Scanning;
using GitGuard.Core;

namespace GitGuard.Application.Tests;

/// <summary>pre-push stdin 推送信息解析单元测试（FR-030；FailClosed 与 DR-02 不回显检查）。</summary>
public sealed class PushUpdateTests
{
    private const string ZERO_SHA = "0000000000000000000000000000000000000000";
    private const string LOCAL_SHA = "1111111111111111111111111111111111111111";
    private const string REMOTE_SHA = "2222222222222222222222222222222222222222";

    [Fact]
    public void ParseAll_EmptyInput_ReturnsEmptyList()
    {
        PushUpdate.ParseAll(string.Empty).Should().BeEmpty();
    }

    [Fact]
    public void ParseAll_BlankLines_AreIgnored()
    {
        string stdin = $"\n   \t\n{LOCAL_SHA} line with only one field\n\n";

        Action act = () => PushUpdate.ParseAll(stdin);

        act.Should().Throw<GitException>().Which.ErrorCode.Should().Be(GitException.GIT_COMMAND_FAILED);
        PushUpdate.ParseAll("\n\n  \n").Should().BeEmpty();
    }

    [Fact]
    public void ParseAll_NewBranchZeroRemoteSha_UsesLocalShaAsRevisionRange()
    {
        string stdin = $"refs/heads/feature {LOCAL_SHA} refs/heads/feature {ZERO_SHA}\n";

        PushUpdate update = PushUpdate.ParseAll(stdin).Should().ContainSingle().Subject;

        update.LocalRef.Should().Be("refs/heads/feature");
        update.IsNewBranch.Should().BeTrue();
        update.IsDeletion.Should().BeFalse();
        update.RevisionRange.Should().Be(LOCAL_SHA);
    }

    [Fact]
    public void ParseAll_ExistingBranch_UsesRemoteDotDotLocalRange()
    {
        string stdin = $"refs/heads/main {LOCAL_SHA} refs/heads/main {REMOTE_SHA}\n";

        PushUpdate update = PushUpdate.ParseAll(stdin).Should().ContainSingle().Subject;

        update.IsNewBranch.Should().BeFalse();
        update.RevisionRange.Should().Be($"{REMOTE_SHA}..{LOCAL_SHA}");
    }

    [Fact]
    public void ParseAll_DeletionZeroLocalSha_IsMarkedAsDeletion()
    {
        string stdin = $"(delete) {ZERO_SHA} refs/heads/gone {REMOTE_SHA}\n";

        PushUpdate update = PushUpdate.ParseAll(stdin).Should().ContainSingle().Subject;

        update.IsDeletion.Should().BeTrue();
        update.IsNewBranch.Should().BeFalse();
    }

    [Fact]
    public void ParseAll_TabSeparatedFields_AreAccepted()
    {
        string stdin = $"refs/heads/main\t{LOCAL_SHA}\trefs/heads/main\t{REMOTE_SHA}\n";

        PushUpdate.ParseAll(stdin).Should().ContainSingle();
    }

    [Fact]
    public void ParseAll_Sha256Lengths_AreAccepted()
    {
        string zero = new string('0', 64);
        string local = new string('a', 64);
        string stdin = $"refs/heads/main {local} refs/heads/main {zero}\n";

        PushUpdate update = PushUpdate.ParseAll(stdin).Should().ContainSingle().Subject;

        update.IsNewBranch.Should().BeTrue();
        update.RevisionRange.Should().Be(local);
    }

    [Fact]
    public void ParseAll_InvalidSha_ThrowsFailClosedWithoutEchoingLineContent()
    {
        string stdin = $"refs/heads/main SENTINEL_NOT_A_SHA refs/heads/main {ZERO_SHA}\n";

        Action act = () => PushUpdate.ParseAll(stdin);

        GitException exception = act.Should().Throw<GitException>().Which;
        exception.ErrorCode.Should().Be(GitException.GIT_COMMAND_FAILED);
        exception.Message.Should().NotContain("SENTINEL_NOT_A_SHA");
    }

    [Fact]
    public void ParseAll_ShortSha_ThrowsFailClosed()
    {
        string stdin = $"refs/heads/main 1234 refs/heads/main {ZERO_SHA}\n";

        Action act = () => PushUpdate.ParseAll(stdin);

        act.Should().Throw<GitException>().Which.ErrorCode.Should().Be(GitException.GIT_COMMAND_FAILED);
    }

    [Fact]
    public void Constructor_InvalidSha_ThrowsArgumentException()
    {
        Action act = () => _ = new PushUpdate("refs/heads/main", "not-a-sha", "refs/heads/main", ZERO_SHA);

        act.Should().Throw<ArgumentException>();
    }
}
