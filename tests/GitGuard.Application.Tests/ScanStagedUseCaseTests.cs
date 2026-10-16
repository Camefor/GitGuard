using FluentAssertions;
using GitGuard.Application.Scanning;
using GitGuard.Core;
using GitGuard.Core.Abstractions;
using GitGuard.Core.Model;
using GitGuard.Core.Policy;

namespace GitGuard.Application.Tests;

/// <summary>暂存区扫描用例测试：判定聚合、退出码与全部 FailClosed 分支（子进程级行为由 E2E 覆盖）。</summary>
public sealed class ScanStagedUseCaseTests
{
    private static readonly string RepoRoot = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "gitguard-fake-repo"));
    private static readonly DateTimeOffset ScanStart = new DateTimeOffset(2026, 10, 16, 8, 30, 0, TimeSpan.Zero);

    [Fact]
    public async Task ExecuteAsync_NoStagedChanges_ReturnsCleanWithZeroFiles()
    {
        var git = new FakeGitAccess(RepoRoot, stagedDiff: () => string.Empty);
        var store = new RecordingAuditStore();
        var detector = new StubDetector("secret", _ => Array.Empty<Finding>());
        ScanStagedUseCase useCase = CreateUseCase(git, detector, store);

        ScanOutcome outcome = await useCase.ExecuteAsync(TestContext.Current.CancellationToken);

        outcome.Result.Should().Be(ScanResult.Clean);
        outcome.ExitCode.Should().Be(0);
        outcome.FilesScanned.Should().Be(0);
        outcome.Findings.Should().BeEmpty();
        outcome.SkippedFiles.Should().BeEmpty();
        outcome.AuditWriteFailed.Should().BeFalse();
        detector.SeenChanges.Should().BeEmpty();
        store.SavedRuns.Should().ContainSingle().Which.Result.Should().Be(ScanResult.Clean);
    }

    [Fact]
    public async Task ExecuteAsync_CriticalFinding_ReturnsBlockedWithExitCodeOne()
    {
        Finding finding = FindingTestData.Create("GGD/SECRET/AWS-AKID", Severity.Critical);
        var git = new FakeGitAccess(RepoRoot, stagedDiff: () => DiffSamples.SingleAddedLine);
        var store = new RecordingAuditStore();
        ScanStagedUseCase useCase = CreateUseCase(git, new StubDetector("secret", _ => new List<Finding> { finding }), store);

        ScanOutcome outcome = await useCase.ExecuteAsync(TestContext.Current.CancellationToken);

        outcome.Result.Should().Be(ScanResult.Blocked);
        outcome.ExitCode.Should().Be(1);
        outcome.FilesScanned.Should().Be(1);
        outcome.Findings.Should().ContainSingle().Which.Should().BeSameAs(finding);
        outcome.Decisions[finding].Action.Should().Be(PolicyAction.Block);
        store.LastDecisions![finding].Action.Should().Be(PolicyAction.Block);
    }

    [Fact]
    public async Task ExecuteAsync_MediumFinding_ReturnsWarnedWithExitCodeZero()
    {
        Finding finding = FindingTestData.Create("GGD/SECRET/JWT", Severity.Medium);
        var git = new FakeGitAccess(RepoRoot, stagedDiff: () => DiffSamples.SingleAddedLine);
        ScanStagedUseCase useCase = CreateUseCase(git, new StubDetector("secret", _ => new List<Finding> { finding }), new RecordingAuditStore());

        ScanOutcome outcome = await useCase.ExecuteAsync(TestContext.Current.CancellationToken);

        outcome.Result.Should().Be(ScanResult.Warned);
        outcome.ExitCode.Should().Be(0);
        outcome.Decisions[finding].Action.Should().Be(PolicyAction.Warn);
    }

    [Fact]
    public async Task ExecuteAsync_GitFailure_ReturnsFailedWithExitCodeTen()
    {
        var git = new FakeGitAccess(RepoRoot, stagedDiff: () => throw new GitException(GitException.GIT_COMMAND_FAILED, "git 失败（测试注入）。"));
        var store = new RecordingAuditStore();
        ScanStagedUseCase useCase = CreateUseCase(git, new StubDetector("secret", _ => Array.Empty<Finding>()), store);

        ScanOutcome outcome = await useCase.ExecuteAsync(TestContext.Current.CancellationToken);

        outcome.Result.Should().Be(ScanResult.Failed);
        outcome.ExitCode.Should().Be(10);
        outcome.Findings.Should().BeEmpty();
        store.SavedRuns.Should().ContainSingle().Which.Result.Should().Be(ScanResult.Failed);
    }

    [Fact]
    public async Task ExecuteAsync_DetectorException_ReturnsFailedWithExitCodeEleven()
    {
        var git = new FakeGitAccess(RepoRoot, stagedDiff: () => DiffSamples.SingleAddedLine);
        var detector = new StubDetector("secret", _ => throw new DetectionException("检测失败（测试注入）。"));
        ScanStagedUseCase useCase = CreateUseCase(git, detector, new RecordingAuditStore());

        ScanOutcome outcome = await useCase.ExecuteAsync(TestContext.Current.CancellationToken);

        outcome.Result.Should().Be(ScanResult.Failed);
        outcome.ExitCode.Should().Be(11);
    }

    [Fact]
    public async Task ExecuteAsync_GitFailureDuringDetection_ReturnsFailedWithExitCodeTen()
    {
        var git = new FakeGitAccess(RepoRoot, stagedDiff: () => DiffSamples.SingleAddedLine);
        var detector = new StubDetector("secret", _ => throw new GitException(GitException.GIT_COMMAND_FAILED, "git 失败（测试注入）。"));
        ScanStagedUseCase useCase = CreateUseCase(git, detector, new RecordingAuditStore());

        ScanOutcome outcome = await useCase.ExecuteAsync(TestContext.Current.CancellationToken);

        outcome.Result.Should().Be(ScanResult.Failed);
        outcome.ExitCode.Should().Be(10);
    }

    [Fact]
    public async Task ExecuteAsync_ScanTimeout_ReturnsFailedWithExitCodeTwelve()
    {
        var git = new FakeGitAccess(RepoRoot, stagedDiff: () => DiffSamples.SingleAddedLine);
        var store = new RecordingAuditStore();
        var settings = new ScanSettings { Timeout = TimeSpan.FromMilliseconds(50) };
        ScanStagedUseCase useCase = CreateUseCase(git, new BlockingDetector(), store, settings);

        ScanOutcome outcome = await useCase.ExecuteAsync(TestContext.Current.CancellationToken);

        outcome.Result.Should().Be(ScanResult.Failed);
        outcome.ExitCode.Should().Be(12);
        store.SavedRuns.Should().ContainSingle().Which.Result.Should().Be(ScanResult.Failed);
    }

    [Fact]
    public async Task ExecuteAsync_ExternalCancellation_PropagatesOperationCanceledException()
    {
        var git = new FakeGitAccess(RepoRoot, stagedDiff: () => DiffSamples.SingleAddedLine);
        var store = new RecordingAuditStore();
        using var cancellationSource = new CancellationTokenSource();
        cancellationSource.Cancel();
        ScanStagedUseCase useCase = CreateUseCase(git, new BlockingDetector(), store);

        Func<Task> act = () => useCase.ExecuteAsync(cancellationSource.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        store.SavedRuns.Should().BeEmpty();
    }

    [Fact]
    public async Task ExecuteAsync_AuditWriteFailure_ReportsDegradeFlagWithoutChangingResult()
    {
        Finding finding = FindingTestData.Create("GGD/SECRET/AWS-AKID", Severity.Critical);
        var git = new FakeGitAccess(RepoRoot, stagedDiff: () => DiffSamples.SingleAddedLine);
        ScanStagedUseCase useCase = CreateUseCase(git, new StubDetector("secret", _ => new List<Finding> { finding }), new FailingAuditStore());

        ScanOutcome outcome = await useCase.ExecuteAsync(TestContext.Current.CancellationToken);

        outcome.AuditWriteFailed.Should().BeTrue();
        outcome.Result.Should().Be(ScanResult.Blocked);
        outcome.ExitCode.Should().Be(1);
    }

    [Fact]
    public async Task ExecuteAsync_OversizeFile_IsSkippedAsTooLargeWithoutDetection()
    {
        const string diff =
            "diff --git a/big.txt b/big.txt\n" +
            "--- a/big.txt\n" +
            "+++ b/big.txt\n" +
            "@@ -0,0 +1 @@\n" +
            "+0123456789\n";
        var git = new FakeGitAccess(RepoRoot, stagedDiff: () => diff);
        var detector = new StubDetector("secret", _ => Array.Empty<Finding>());
        var settings = new ScanSettings { MaxFileBytes = 8 };
        ScanStagedUseCase useCase = CreateUseCase(git, detector, new RecordingAuditStore(), settings);

        ScanOutcome outcome = await useCase.ExecuteAsync(TestContext.Current.CancellationToken);

        outcome.Result.Should().Be(ScanResult.Clean);
        outcome.FilesScanned.Should().Be(0);
        SkippedFile skipped = outcome.SkippedFiles.Should().ContainSingle().Subject;
        skipped.Path.Should().Be("big.txt");
        skipped.Reason.Should().Be(SkipReason.TooLarge);
        detector.SeenChanges.Should().BeEmpty();
    }

    [Fact]
    public async Task ExecuteAsync_BinaryFile_IsSkippedAsBinary()
    {
        const string diff =
            "diff --git a/img.png b/img.png\n" +
            "new file mode 100644\n" +
            "Binary files /dev/null and b/img.png differ\n";
        var git = new FakeGitAccess(RepoRoot, stagedDiff: () => diff);
        var detector = new StubDetector("secret", _ => Array.Empty<Finding>());
        ScanStagedUseCase useCase = CreateUseCase(git, detector, new RecordingAuditStore());

        ScanOutcome outcome = await useCase.ExecuteAsync(TestContext.Current.CancellationToken);

        SkippedFile skipped = outcome.SkippedFiles.Should().ContainSingle().Subject;
        skipped.Path.Should().Be("img.png");
        skipped.Reason.Should().Be(SkipReason.Binary);
        outcome.FilesScanned.Should().Be(0);
        detector.SeenChanges.Should().BeEmpty();
    }

    [Fact]
    public async Task ExecuteAsync_PureDeletion_ProducesNeitherScanNorSkip()
    {
        const string diff =
            "diff --git a/gone.txt b/gone.txt\n" +
            "deleted file mode 100644\n" +
            "--- a/gone.txt\n" +
            "+++ /dev/null\n" +
            "@@ -1 +0,0 @@\n" +
            "-doomed\n";
        var git = new FakeGitAccess(RepoRoot, stagedDiff: () => diff);
        var detector = new StubDetector("secret", _ => Array.Empty<Finding>());
        ScanStagedUseCase useCase = CreateUseCase(git, detector, new RecordingAuditStore());

        ScanOutcome outcome = await useCase.ExecuteAsync(TestContext.Current.CancellationToken);

        outcome.Result.Should().Be(ScanResult.Clean);
        outcome.FilesScanned.Should().Be(0);
        outcome.SkippedFiles.Should().BeEmpty();
        detector.SeenChanges.Should().BeEmpty();
    }

    [Fact]
    public async Task ExecuteAsync_RunAggregate_CarriesRepoPathScanTypeAndClockStart()
    {
        var git = new FakeGitAccess(RepoRoot, stagedDiff: () => string.Empty);
        ScanStagedUseCase useCase = CreateUseCase(git, new StubDetector("secret", _ => Array.Empty<Finding>()), new RecordingAuditStore());

        ScanOutcome outcome = await useCase.ExecuteAsync(TestContext.Current.CancellationToken);

        outcome.Run.RepoPath.Should().Be(RepoRoot);
        outcome.Run.ScanType.Should().Be(ScanType.PreCommit);
        outcome.Run.StartedAt.Should().Be(ScanStart);
        outcome.Run.DurationMs.Should().BeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public void Constructor_EmptyDetectors_ThrowsArgumentException()
    {
        Action act = () => _ = new ScanStagedUseCase(
            new FakeGitAccess(RepoRoot),
            new List<IDetector>(),
            new RecordingAuditStore(),
            new FixedClock(ScanStart));

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Constructor_NonPositiveSettings_ThrowsArgumentOutOfRangeException()
    {
        Action act = () => _ = new ScanStagedUseCase(
            new FakeGitAccess(RepoRoot),
            new List<IDetector> { new BlockingDetector() },
            new RecordingAuditStore(),
            new FixedClock(ScanStart),
            new ScanSettings { MaxFileBytes = 0 });

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    private static ScanStagedUseCase CreateUseCase(
        IGitAccess gitAccess,
        IDetector detector,
        IAuditStore auditStore,
        ScanSettings? settings = null) =>
        new ScanStagedUseCase(
            gitAccess,
            new List<IDetector> { detector },
            auditStore,
            new FixedClock(ScanStart),
            settings);
}
