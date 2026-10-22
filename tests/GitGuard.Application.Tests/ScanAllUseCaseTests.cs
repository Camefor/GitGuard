using FluentAssertions;
using GitGuard.Application.Scanning;
using GitGuard.Core;
using GitGuard.Core.Abstractions;
using GitGuard.Core.Model;

namespace GitGuard.Application.Tests;

/// <summary>
/// 全量扫描用例测试（FR-060）：HEAD 全量树差异、历史提交枚举与归属、
/// <c>--max-files</c> 提交边界截断 / 续扫点，以及选项校验与 FailClosed 分支。
/// </summary>
public sealed class ScanAllUseCaseTests
{
    private const string HEAD_SHA = "hhhhhhhhhhhhhhhhhhhhhhhhhhhhhhhhhhhhhhhh";
    private const string COMMIT_A = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string COMMIT_B = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
    private const string COMMIT_C = "cccccccccccccccccccccccccccccccccccccccc";

    private static readonly string _repoRoot = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "gitguard-fake-repo"));
    private static readonly DateTimeOffset _scanStart = new DateTimeOffset(2026, 10, 22, 8, 30, 0, TimeSpan.Zero);

    [Fact]
    public async Task ExecuteAsync_Default_ScansHeadTreeOnlyWithoutHistoryEnumeration()
    {
        var git = new FakeGitAccess(
            _repoRoot,
            headCommitSha: () => HEAD_SHA,
            fullTreeDiff: revision => revision == "HEAD" ? DiffSamples.SINGLE_ADDED_LINE : string.Empty);
        var detector = new StubDetector("secret", _ => Array.Empty<Finding>());
        ScanAllUseCase useCase = CreateUseCase(git, detector);

        ScanOutcome outcome = await useCase.ExecuteAsync(ScanAllOptions.Default, TestContext.Current.CancellationToken);

        outcome.Result.Should().Be(ScanResult.Clean);
        outcome.ExitCode.Should().Be(0);
        outcome.FilesScanned.Should().Be(1);
        outcome.Run.ScanType.Should().Be(ScanType.Manual);
        outcome.Run.RepoPath.Should().Be(_repoRoot);
        git.RequestedFullTreeRevisions.Should().ContainSingle().Which.Should().Be("HEAD");
        git.RequestedRanges.Should().BeEmpty();
        git.RequestedCommitShas.Should().BeEmpty();

        FullScanSummary summary = outcome.FullScanSummary.Should().BeOfType<FullScanSummary>().Subject;
        summary.IncludeHistory.Should().BeFalse();
        summary.CommitsScanned.Should().Be(0);
        summary.RemainingCommits.Should().Be(0);
        summary.Truncated.Should().BeFalse();
        summary.NextSinceSha.Should().BeNull();
    }

    [Fact]
    public async Task ExecuteAsync_Default_FindingFromHeadTreeCarriesNoCommitSha()
    {
        var git = new FakeGitAccess(
            _repoRoot,
            headCommitSha: () => HEAD_SHA,
            fullTreeDiff: _ => DiffSamples.SINGLE_ADDED_LINE);
        var detector = new StubDetector("secret", change => new List<Finding>
        {
            FindingTestData.Create("GGD/SECRET/AWS-AKID", Severity.Critical, commitSha: change.CommitSha),
        });
        ScanAllUseCase useCase = CreateUseCase(git, detector);

        ScanOutcome outcome = await useCase.ExecuteAsync(ScanAllOptions.Default, TestContext.Current.CancellationToken);

        outcome.Result.Should().Be(ScanResult.Blocked);
        outcome.ExitCode.Should().Be(1);
        outcome.Findings.Should().ContainSingle().Which.CommitSha.Should().BeNull();
    }

    [Fact]
    public async Task ExecuteAsync_IncludeHistory_ScansHeadThenOlderCommitsWithOwningSha()
    {
        var git = new FakeGitAccess(
            _repoRoot,
            headCommitSha: () => HEAD_SHA,
            fullTreeDiff: _ => DiffSamples.SINGLE_ADDED_LINE,
            listCommitShas: revision => revision == "HEAD"
                ? new[] { HEAD_SHA, COMMIT_A, COMMIT_B }
                : Array.Empty<string>(),
            commitDiff: _ => DiffSamples.SINGLE_ADDED_LINE);
        var detector = new StubDetector("secret", change => new List<Finding>
        {
            FindingTestData.Create("GGD/SECRET/AWS-AKID", Severity.Critical, commitSha: change.CommitSha),
        });
        ScanAllUseCase useCase = CreateUseCase(git, detector);

        ScanOutcome outcome = await useCase.ExecuteAsync(
            new ScanAllOptions { IncludeHistory = true },
            TestContext.Current.CancellationToken);

        outcome.FilesScanned.Should().Be(3);
        outcome.Findings.Should().HaveCount(3);
        outcome.Findings[0].CommitSha.Should().BeNull();
        outcome.Findings[1].CommitSha.Should().Be(COMMIT_A);
        outcome.Findings[2].CommitSha.Should().Be(COMMIT_B);

        git.RequestedFullTreeRevisions.Should().ContainSingle().Which.Should().Be("HEAD");
        git.RequestedRanges.Should().ContainSingle().Which.Should().Be("HEAD");
        git.RequestedCommitShas.Should().ContainInOrder(COMMIT_A, COMMIT_B);
        git.RequestedCommitShas.Should().NotContain(HEAD_SHA);

        FullScanSummary summary = outcome.FullScanSummary.Should().BeOfType<FullScanSummary>().Subject;
        summary.IncludeHistory.Should().BeTrue();
        summary.CommitsScanned.Should().Be(2);
        summary.RemainingCommits.Should().Be(0);
        summary.Truncated.Should().BeFalse();
        summary.NextSinceSha.Should().BeNull();
    }

    [Fact]
    public async Task ExecuteAsync_MaxFiles_TruncatesAtCommitBoundaryAndReportsResumePoint()
    {
        var git = new FakeGitAccess(
            _repoRoot,
            headCommitSha: () => HEAD_SHA,
            fullTreeDiff: _ => DiffSamples.SINGLE_ADDED_LINE,
            listCommitShas: _ => new[] { HEAD_SHA, COMMIT_A, COMMIT_B, COMMIT_C },
            commitDiff: _ => DiffSamples.SINGLE_ADDED_LINE);
        ScanAllUseCase useCase = CreateUseCase(git, new StubDetector("secret", _ => Array.Empty<Finding>()));

        ScanOutcome outcome = await useCase.ExecuteAsync(
            new ScanAllOptions { MaxFiles = 1 },
            TestContext.Current.CancellationToken);

        // --max-files 隐含历史模式；HEAD 不计入历史文件上限
        FullScanSummary summary = outcome.FullScanSummary.Should().BeOfType<FullScanSummary>().Subject;
        summary.IncludeHistory.Should().BeTrue();
        summary.CommitsScanned.Should().Be(1);
        summary.RemainingCommits.Should().Be(2);
        summary.Truncated.Should().BeTrue();
        summary.NextSinceSha.Should().Be(COMMIT_A);
        outcome.FilesScanned.Should().Be(2);
    }

    [Fact]
    public async Task ExecuteAsync_MaxFiles_AllowsCurrentCommitToSoftExceedLimit()
    {
        string twoFileDiff = DiffSamples.SINGLE_ADDED_LINE +
            "diff --git a/second.txt b/second.txt\n" +
            "--- a/second.txt\n" +
            "+++ b/second.txt\n" +
            "@@ -0,0 +1 @@\n" +
            "+second file\n";
        var git = new FakeGitAccess(
            _repoRoot,
            headCommitSha: () => HEAD_SHA,
            fullTreeDiff: _ => string.Empty,
            listCommitShas: _ => new[] { HEAD_SHA, COMMIT_A, COMMIT_B },
            commitDiff: sha => sha == COMMIT_A ? twoFileDiff : DiffSamples.SINGLE_ADDED_LINE);
        ScanAllUseCase useCase = CreateUseCase(git, new StubDetector("secret", _ => Array.Empty<Finding>()));

        ScanOutcome outcome = await useCase.ExecuteAsync(
            new ScanAllOptions { MaxFiles = 1 },
            TestContext.Current.CancellationToken);

        // 提交边界语义：A 整体扫描（2 个文件软超限），B 不再开始
        outcome.FilesScanned.Should().Be(2);
        FullScanSummary summary = outcome.FullScanSummary.Should().BeOfType<FullScanSummary>().Subject;
        summary.CommitsScanned.Should().Be(1);
        summary.RemainingCommits.Should().Be(1);
        summary.Truncated.Should().BeTrue();
        summary.NextSinceSha.Should().Be(COMMIT_A);
    }

    [Fact]
    public async Task ExecuteAsync_Since_EnablesHistoryAndSkipsSinceCommitItself()
    {
        var git = new FakeGitAccess(
            _repoRoot,
            headCommitSha: () => HEAD_SHA,
            fullTreeDiff: _ => DiffSamples.SINGLE_ADDED_LINE,
            listCommitShas: revision => revision == COMMIT_C
                ? new[] { COMMIT_C, COMMIT_B, COMMIT_A }
                : Array.Empty<string>(),
            commitDiff: _ => DiffSamples.SINGLE_ADDED_LINE);
        ScanAllUseCase useCase = CreateUseCase(git, new StubDetector("secret", _ => Array.Empty<Finding>()));

        ScanOutcome outcome = await useCase.ExecuteAsync(
            new ScanAllOptions { Since = COMMIT_C },
            TestContext.Current.CancellationToken);

        git.RequestedRanges.Should().ContainSingle().Which.Should().Be(COMMIT_C);
        git.RequestedCommitShas.Should().ContainInOrder(COMMIT_B, COMMIT_A);
        git.RequestedCommitShas.Should().NotContain(COMMIT_C);

        FullScanSummary summary = outcome.FullScanSummary.Should().BeOfType<FullScanSummary>().Subject;
        summary.IncludeHistory.Should().BeTrue();
        summary.CommitsScanned.Should().Be(2);
        summary.RemainingCommits.Should().Be(0);
        summary.Truncated.Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteAsync_EmptyRepository_ReturnsCleanWithoutTreeDiffOrHistory()
    {
        var git = new FakeGitAccess(_repoRoot, headCommitSha: () => null);
        var detector = new StubDetector("secret", _ => Array.Empty<Finding>());
        ScanAllUseCase useCase = CreateUseCase(git, detector);

        ScanOutcome outcome = await useCase.ExecuteAsync(
            new ScanAllOptions { IncludeHistory = true },
            TestContext.Current.CancellationToken);

        outcome.Result.Should().Be(ScanResult.Clean);
        outcome.FilesScanned.Should().Be(0);
        git.FullTreeDiffCalls.Should().Be(0);
        git.RequestedRanges.Should().BeEmpty();
        detector.SeenChanges.Should().BeEmpty();

        FullScanSummary summary = outcome.FullScanSummary.Should().BeOfType<FullScanSummary>().Subject;
        summary.IncludeHistory.Should().BeTrue();
        summary.CommitsScanned.Should().Be(0);
        summary.RemainingCommits.Should().Be(0);
        summary.Truncated.Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteAsync_GitFailure_ReturnsFailedWithExitCodeTen()
    {
        var git = new FakeGitAccess(
            _repoRoot,
            headCommitSha: () => HEAD_SHA,
            fullTreeDiff: _ => throw new GitException(GitException.GIT_COMMAND_FAILED, "diff 失败（测试注入）。"));
        var store = new RecordingAuditStore();
        ScanAllUseCase useCase = CreateUseCase(git, new StubDetector("secret", _ => Array.Empty<Finding>()), store);

        ScanOutcome outcome = await useCase.ExecuteAsync(ScanAllOptions.Default, TestContext.Current.CancellationToken);

        outcome.Result.Should().Be(ScanResult.Failed);
        outcome.ExitCode.Should().Be(10);
        outcome.FullScanSummary.Should().BeNull();
        store.SavedRuns.Should().ContainSingle().Which.Result.Should().Be(ScanResult.Failed);
    }

    [Fact]
    public async Task ExecuteAsync_CommitDiffFailure_ReturnsFailedWithExitCodeTen()
    {
        var git = new FakeGitAccess(
            _repoRoot,
            headCommitSha: () => HEAD_SHA,
            fullTreeDiff: _ => DiffSamples.SINGLE_ADDED_LINE,
            listCommitShas: _ => new[] { HEAD_SHA, COMMIT_A },
            commitDiff: _ => throw new GitException(GitException.GIT_COMMAND_FAILED, "批量差异失败（测试注入）。"));
        ScanAllUseCase useCase = CreateUseCase(git, new StubDetector("secret", _ => Array.Empty<Finding>()));

        ScanOutcome outcome = await useCase.ExecuteAsync(
            new ScanAllOptions { IncludeHistory = true },
            TestContext.Current.CancellationToken);

        outcome.Result.Should().Be(ScanResult.Failed);
        outcome.ExitCode.Should().Be(10);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task ExecuteAsync_NonPositiveMaxFiles_ThrowsArgumentOutOfRangeException(int maxFiles)
    {
        ScanAllUseCase useCase = CreateUseCase(new FakeGitAccess(_repoRoot), new StubDetector("secret", _ => Array.Empty<Finding>()));

        Func<Task> act = () => useCase.ExecuteAsync(
            new ScanAllOptions { MaxFiles = maxFiles },
            TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("-abc")]
    public async Task ExecuteAsync_InvalidSince_ThrowsArgumentException(string since)
    {
        ScanAllUseCase useCase = CreateUseCase(new FakeGitAccess(_repoRoot), new StubDetector("secret", _ => Array.Empty<Finding>()));

        Func<Task> act = () => useCase.ExecuteAsync(
            new ScanAllOptions { Since = since },
            TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task ExecuteAsync_NullOptions_ThrowsArgumentNullException()
    {
        ScanAllUseCase useCase = CreateUseCase(new FakeGitAccess(_repoRoot), new StubDetector("secret", _ => Array.Empty<Finding>()));

        Func<Task> act = () => useCase.ExecuteAsync(null!, TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_EmptyDetectors_ThrowsArgumentException()
    {
        Action act = () => _ = new ScanAllUseCase(
            new FakeGitAccess(_repoRoot),
            new List<IDetector>(),
            new RecordingAuditStore(),
            new FixedClock(_scanStart));

        act.Should().Throw<ArgumentException>();
    }

    private static ScanAllUseCase CreateUseCase(
        IGitAccess gitAccess,
        IDetector detector,
        IAuditStore? auditStore = null) =>
        new ScanAllUseCase(
            gitAccess,
            new List<IDetector> { detector },
            auditStore ?? new RecordingAuditStore(),
            new FixedClock(_scanStart));
}
