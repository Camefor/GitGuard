using FluentAssertions;
using GitGuard.Application.Scanning;
using GitGuard.Core;
using GitGuard.Core.Abstractions;
using GitGuard.Core.Model;

namespace GitGuard.Application.Tests;

/// <summary>推送扫描用例测试：stdin 解析、版本区间、跨引用去重、提交归属与 FailClosed 分支。</summary>
public sealed class ScanPushUseCaseTests
{
    private const string LOCAL_REF = "refs/heads/main";
    private const string LOCAL_SHA = "1111111111111111111111111111111111111111";
    private const string LOCAL_SHA_2 = "3333333333333333333333333333333333333333";
    private const string REMOTE_SHA = "2222222222222222222222222222222222222222";
    private const string ZERO_SHA = "0000000000000000000000000000000000000000";
    private const string COMMIT_A = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string COMMIT_B = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
    private const string COMMIT_C = "cccccccccccccccccccccccccccccccccccccccc";

    private static readonly string RepoRoot = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "gitguard-fake-repo"));
    private static readonly DateTimeOffset ScanStart = new DateTimeOffset(2026, 10, 16, 8, 30, 0, TimeSpan.Zero);

    [Fact]
    public async Task ExecuteAsync_EmptyStdin_ReturnsCleanWithoutGitCalls()
    {
        var git = new FakeGitAccess(RepoRoot);
        ScanPushUseCase useCase = CreateUseCase(git, new StubDetector("secret", _ => Array.Empty<Finding>()));

        ScanOutcome outcome = await useCase.ExecuteAsync(string.Empty, TestContext.Current.CancellationToken);

        outcome.Result.Should().Be(ScanResult.Clean);
        outcome.ExitCode.Should().Be(0);
        outcome.Run.ScanType.Should().Be(ScanType.PrePush);
        git.RequestedRanges.Should().BeEmpty();
        git.RequestedCommitShas.Should().BeEmpty();
    }

    [Fact]
    public async Task ExecuteAsync_NewBranchZeroRemoteSha_RevListsLocalShaRange()
    {
        string stdin = $"{LOCAL_REF} {LOCAL_SHA} {LOCAL_REF} {ZERO_SHA}\n";
        var git = new FakeGitAccess(
            RepoRoot,
            listCommitShas: range => range == LOCAL_SHA ? new[] { COMMIT_A } : Array.Empty<string>(),
            commitDiff: _ => DiffSamples.SingleAddedLine);
        ScanPushUseCase useCase = CreateUseCase(git, new StubDetector("secret", _ => Array.Empty<Finding>()));

        ScanOutcome outcome = await useCase.ExecuteAsync(stdin, TestContext.Current.CancellationToken);

        git.RequestedRanges.Should().ContainSingle().Which.Should().Be(LOCAL_SHA);
        outcome.FilesScanned.Should().Be(1);
    }

    [Fact]
    public async Task ExecuteAsync_ExistingBranch_RevListsRemoteDotDotLocalRange()
    {
        string stdin = $"{LOCAL_REF} {LOCAL_SHA} {LOCAL_REF} {REMOTE_SHA}\n";
        var git = new FakeGitAccess(
            RepoRoot,
            listCommitShas: range => range == $"{REMOTE_SHA}..{LOCAL_SHA}" ? new[] { COMMIT_A } : Array.Empty<string>(),
            commitDiff: _ => DiffSamples.SingleAddedLine);
        ScanPushUseCase useCase = CreateUseCase(git, new StubDetector("secret", _ => Array.Empty<Finding>()));

        await useCase.ExecuteAsync(stdin, TestContext.Current.CancellationToken);

        git.RequestedRanges.Should().ContainSingle().Which.Should().Be($"{REMOTE_SHA}..{LOCAL_SHA}");
    }

    [Fact]
    public async Task ExecuteAsync_DeletionUpdate_IsSkippedWithoutRevList()
    {
        string stdin = $"refs/heads/gone {ZERO_SHA} refs/heads/gone {REMOTE_SHA}\n";
        var git = new FakeGitAccess(RepoRoot);
        ScanPushUseCase useCase = CreateUseCase(git, new StubDetector("secret", _ => Array.Empty<Finding>()));

        ScanOutcome outcome = await useCase.ExecuteAsync(stdin, TestContext.Current.CancellationToken);

        outcome.Result.Should().Be(ScanResult.Clean);
        outcome.FilesScanned.Should().Be(0);
        git.RequestedRanges.Should().BeEmpty();
    }

    [Fact]
    public async Task ExecuteAsync_OverlappingRanges_DeduplicatesCommitsPreservingFirstSeenOrder()
    {
        string stdin =
            $"{LOCAL_REF} {LOCAL_SHA} {LOCAL_REF} {REMOTE_SHA}\n" +
            $"{LOCAL_REF} {LOCAL_SHA_2} {LOCAL_REF} {ZERO_SHA}\n";
        var git = new FakeGitAccess(
            RepoRoot,
            listCommitShas: range => range == $"{REMOTE_SHA}..{LOCAL_SHA}"
                ? new[] { COMMIT_A, COMMIT_B }
                : new[] { COMMIT_B, COMMIT_C },
            commitDiff: _ => DiffSamples.SingleAddedLine);
        ScanPushUseCase useCase = CreateUseCase(git, new StubDetector("secret", _ => Array.Empty<Finding>()));

        ScanOutcome outcome = await useCase.ExecuteAsync(stdin, TestContext.Current.CancellationToken);

        git.RequestedCommitShas.Should().ContainInOrder(COMMIT_A, COMMIT_B, COMMIT_C);
        git.RequestedCommitShas.Should().HaveCount(3);
        outcome.FilesScanned.Should().Be(3);
    }

    [Fact]
    public async Task ExecuteAsync_FindingsCarryOwningCommitSha()
    {
        string stdin = $"{LOCAL_REF} {LOCAL_SHA} {LOCAL_REF} {ZERO_SHA}\n";
        var git = new FakeGitAccess(
            RepoRoot,
            listCommitShas: _ => new[] { COMMIT_A },
            commitDiff: _ => DiffSamples.SingleAddedLine);
        var detector = new StubDetector("secret", change => new List<Finding>
        {
            FindingTestData.Create("GGD/SECRET/AWS-AKID", Severity.Critical, commitSha: change.CommitSha),
        });
        ScanPushUseCase useCase = CreateUseCase(git, detector);

        ScanOutcome outcome = await useCase.ExecuteAsync(stdin, TestContext.Current.CancellationToken);

        outcome.Result.Should().Be(ScanResult.Blocked);
        outcome.ExitCode.Should().Be(1);
        outcome.Findings.Should().ContainSingle().Which.CommitSha.Should().Be(COMMIT_A);
    }

    [Fact]
    public async Task ExecuteAsync_MalformedStdin_ReturnsFailedWithExitCodeTen()
    {
        var git = new FakeGitAccess(RepoRoot);
        var store = new RecordingAuditStore();
        var useCase = new ScanPushUseCase(git, new List<IDetector> { new StubDetector("secret", _ => Array.Empty<Finding>()) }, store, new FixedClock(ScanStart));

        ScanOutcome outcome = await useCase.ExecuteAsync("not-enough-fields\n", TestContext.Current.CancellationToken);

        outcome.Result.Should().Be(ScanResult.Failed);
        outcome.ExitCode.Should().Be(10);
        git.RequestedRanges.Should().BeEmpty();
        store.SavedRuns.Should().ContainSingle().Which.Result.Should().Be(ScanResult.Failed);
    }

    [Fact]
    public async Task ExecuteAsync_RevListFailure_ReturnsFailedWithExitCodeTen()
    {
        string stdin = $"{LOCAL_REF} {LOCAL_SHA} {LOCAL_REF} {ZERO_SHA}\n";
        var git = new FakeGitAccess(
            RepoRoot,
            listCommitShas: _ => throw new GitException(GitException.GIT_COMMAND_FAILED, "rev-list 失败（测试注入）。"));
        ScanPushUseCase useCase = CreateUseCase(git, new StubDetector("secret", _ => Array.Empty<Finding>()));

        ScanOutcome outcome = await useCase.ExecuteAsync(stdin, TestContext.Current.CancellationToken);

        outcome.Result.Should().Be(ScanResult.Failed);
        outcome.ExitCode.Should().Be(10);
    }

    [Fact]
    public async Task ExecuteAsync_CommitDiffFailure_ReturnsFailedWithExitCodeTen()
    {
        string stdin = $"{LOCAL_REF} {LOCAL_SHA} {LOCAL_REF} {ZERO_SHA}\n";
        var git = new FakeGitAccess(
            RepoRoot,
            listCommitShas: _ => new[] { COMMIT_A },
            commitDiff: _ => throw new GitException(GitException.GIT_COMMAND_FAILED, "diff-tree 失败（测试注入）。"));
        ScanPushUseCase useCase = CreateUseCase(git, new StubDetector("secret", _ => Array.Empty<Finding>()));

        ScanOutcome outcome = await useCase.ExecuteAsync(stdin, TestContext.Current.CancellationToken);

        outcome.Result.Should().Be(ScanResult.Failed);
        outcome.ExitCode.Should().Be(10);
    }

    private static ScanPushUseCase CreateUseCase(IGitAccess gitAccess, IDetector detector) =>
        new ScanPushUseCase(
            gitAccess,
            new List<IDetector> { detector },
            new RecordingAuditStore(),
            new FixedClock(ScanStart));
}
