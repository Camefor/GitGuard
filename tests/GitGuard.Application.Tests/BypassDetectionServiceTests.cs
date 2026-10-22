using FluentAssertions;
using GitGuard.Application.Bypass;
using GitGuard.Core;
using GitGuard.Core.Abstractions;
using GitGuard.Core.Model;
using SqliteException = GitGuard.Core.SqliteException;

namespace GitGuard.Application.Tests;

/// <summary>
/// post-commit 绕过检测用例测试（TC-FR-050-01 / TC-DR-06-01，SRS FR-050 / DR-06）：
/// 待提交预测与实际提交树 / 首个父提交的匹配、<c>GITGUARD_SKIP=1</c> 与 <c>--no-verify</c> 归类、
/// 标记消费与审计写入；Git / 标记存储以替身注入（真实 git 行为由基础设施与 E2E 测试覆盖）。
/// </summary>
public sealed class BypassDetectionServiceTests
{
    private const string PARENT_SHA = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string SECOND_PARENT_SHA = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
    private const string TREE_SHA = "cccccccccccccccccccccccccccccccccccccccc";
    private const string HEAD_SHA = "dddddddddddddddddddddddddddddddddddddddd";

    private static readonly string _repoRoot = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "gitguard-fake-repo"));
    private static readonly DateTimeOffset _sampleUtcNow = new DateTimeOffset(2026, 10, 16, 8, 30, 0, TimeSpan.Zero);

    [Fact]
    public void Detect_MatchingPendingScan_ClearsMarkerWithoutAudit()
    {
        var auditStore = new RecordingAuditStore();
        var pendingStore = new RecordingPendingScanStore
        {
            Stored = new PendingScan(PARENT_SHA, TREE_SHA, skipped: false, _sampleUtcNow),
        };

        BypassDetectionResult result = CreateService(auditStore, pendingStore).Detect();

        result.Detected.Should().BeFalse();
        result.Reason.Should().BeNull();
        result.RunId.Should().BeNull();
        result.CommitSha.Should().Be(HEAD_SHA);
        auditStore.SavedBypassEvents.Should().BeEmpty("正常提交不得产生绕过记录");
        pendingStore.ClearCalls.Should().Be(1, "预测标记必须在比对后被消费");
        pendingStore.Stored.Should().BeNull();
    }

    [Fact]
    public void Detect_MatchingSkippedPendingScan_RecordsGitGuardSkip()
    {
        var auditStore = new RecordingAuditStore();
        var pendingStore = new RecordingPendingScanStore
        {
            Stored = new PendingScan(PARENT_SHA, TREE_SHA, skipped: true, _sampleUtcNow),
        };

        BypassDetectionResult result = CreateService(auditStore, pendingStore).Detect();

        result.Detected.Should().BeTrue();
        result.Reason.Should().Be(BypassReason.GitGuardSkip);
        result.CommitSha.Should().Be(HEAD_SHA);
        result.RunId.Should().NotBeNull();

        var (run, reason, commitSha) = auditStore.SavedBypassEvents.Should().ContainSingle().Subject;
        reason.Should().Be(BypassReason.GitGuardSkip);
        commitSha.Should().Be(HEAD_SHA);
        run.Id.Should().Be(result.RunId!.Value);
        run.RepoPath.Should().Be(_repoRoot);
        run.ScanType.Should().Be(ScanType.Bypass);
        run.StartedAt.Should().Be(_sampleUtcNow);
        run.DurationMs.Should().BeNull("绕过事件不对应真实扫描耗时");
        run.Result.Should().Be(ScanResult.Clean);
        run.ExitCode.Should().Be(0);
        run.Findings.Should().BeEmpty();
        pendingStore.ClearCalls.Should().Be(1);
    }

    [Fact]
    public void Detect_MissingPendingScan_RecordsNoVerify()
    {
        var auditStore = new RecordingAuditStore();

        BypassDetectionResult result = CreateService(auditStore, new RecordingPendingScanStore()).Detect();

        result.Detected.Should().BeTrue();
        result.Reason.Should().Be(BypassReason.NoVerify);
        auditStore.SavedBypassEvents.Should().ContainSingle()
            .Which.Reason.Should().Be(BypassReason.NoVerify);
    }

    [Fact]
    public void Detect_TreeMismatch_RecordsNoVerify()
    {
        var auditStore = new RecordingAuditStore();
        var pendingStore = new RecordingPendingScanStore
        {
            Stored = new PendingScan(
                PARENT_SHA,
                "eeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee",
                skipped: false,
                _sampleUtcNow),
        };

        BypassDetectionResult result = CreateService(auditStore, pendingStore).Detect();

        result.Reason.Should().Be(BypassReason.NoVerify, "提交树与暂存预测不一致说明提交未经过本次扫描");
        auditStore.SavedBypassEvents.Should().ContainSingle();
    }

    [Fact]
    public void Detect_ParentMismatch_RecordsNoVerify()
    {
        var auditStore = new RecordingAuditStore();
        var pendingStore = new RecordingPendingScanStore
        {
            Stored = new PendingScan(SECOND_PARENT_SHA, TREE_SHA, skipped: false, _sampleUtcNow),
        };

        BypassDetectionResult result = CreateService(auditStore, pendingStore).Detect();

        result.Reason.Should().Be(BypassReason.NoVerify);
    }

    // 首个提交：pre-commit 时 HEAD 缺失，预测的 ParentSha 为 null，与实际提交「无父提交」匹配
    [Fact]
    public void Detect_RootCommitWithNullParent_Matches()
    {
        var auditStore = new RecordingAuditStore();
        var pendingStore = new RecordingPendingScanStore
        {
            Stored = new PendingScan(parentSha: null, TREE_SHA, skipped: false, _sampleUtcNow),
        };

        BypassDetectionResult result = CreateService(auditStore, pendingStore, parents: Array.Empty<string>()).Detect();

        result.Detected.Should().BeFalse();
        auditStore.SavedBypassEvents.Should().BeEmpty();
    }

    [Fact]
    public void Detect_RootCommitWithRecordedParent_RecordsNoVerify()
    {
        var auditStore = new RecordingAuditStore();
        var pendingStore = new RecordingPendingScanStore
        {
            Stored = new PendingScan(PARENT_SHA, TREE_SHA, skipped: false, _sampleUtcNow),
        };

        BypassDetectionResult result = CreateService(auditStore, pendingStore, parents: Array.Empty<string>()).Detect();

        result.Reason.Should().Be(BypassReason.NoVerify);
    }

    // 合并提交：首个父提交是 pre-commit 时的 HEAD，应视为经过扫描
    [Fact]
    public void Detect_MergeCommit_PendingFirstParent_Matches()
    {
        var auditStore = new RecordingAuditStore();
        var pendingStore = new RecordingPendingScanStore
        {
            Stored = new PendingScan(PARENT_SHA, TREE_SHA, skipped: false, _sampleUtcNow),
        };

        BypassDetectionResult result = CreateService(auditStore, pendingStore, parents: new[] { PARENT_SHA, SECOND_PARENT_SHA }).Detect();

        result.Detected.Should().BeFalse();
    }

    [Fact]
    public void Detect_MergeCommit_PendingSecondParent_RecordsNoVerify()
    {
        var auditStore = new RecordingAuditStore();
        var pendingStore = new RecordingPendingScanStore
        {
            Stored = new PendingScan(SECOND_PARENT_SHA, TREE_SHA, skipped: false, _sampleUtcNow),
        };

        BypassDetectionResult result = CreateService(auditStore, pendingStore, parents: new[] { PARENT_SHA, SECOND_PARENT_SHA }).Detect();

        result.Reason.Should().Be(BypassReason.NoVerify);
    }

    // 标记读取失败退化为「无标记」：宁可多记绕过，也不漏记（DR-06）
    [Fact]
    public void Detect_PendingScanReadFailure_TreatedAsNoVerify()
    {
        var auditStore = new RecordingAuditStore();
        var pendingStore = new RecordingPendingScanStore
        {
            ReadException = new IOException("标记读取失败（测试注入）。"),
        };

        BypassDetectionResult result = CreateService(auditStore, pendingStore).Detect();

        result.Reason.Should().Be(BypassReason.NoVerify);
        auditStore.SavedBypassEvents.Should().ContainSingle();
    }

    [Fact]
    public void Detect_PendingScanClearFailure_StillReturnsDetectionResult()
    {
        var auditStore = new RecordingAuditStore();
        var pendingStore = new RecordingPendingScanStore
        {
            Stored = new PendingScan(PARENT_SHA, TREE_SHA, skipped: true, _sampleUtcNow),
            ClearException = new IOException("标记清理失败（测试注入）。"),
        };

        BypassDetectionResult result = CreateService(auditStore, pendingStore).Detect();

        result.Detected.Should().BeTrue();
        auditStore.SavedBypassEvents.Should().ContainSingle("清理失败不得回滚已登记的绕过事件");
    }

    [Fact]
    public void Detect_AuditWriteFailure_PropagatesAndSkipsMarkerClear()
    {
        var pendingStore = new RecordingPendingScanStore
        {
            Stored = new PendingScan(PARENT_SHA, TREE_SHA, skipped: true, _sampleUtcNow),
        };
        var service = CreateService(new FailingAuditStore(), pendingStore);

        Action act = () => service.Detect();

        act.Should().Throw<SqliteException>();
        pendingStore.ClearCalls.Should().Be(0, "审计未落库时不能消费标记");
    }

    [Fact]
    public void Detect_GitFailure_PropagatesWithoutAudit()
    {
        var auditStore = new RecordingAuditStore();
        var failingGit = new FakeGitAccess(
            _repoRoot,
            headCommitSha: () => throw new GitException(GitException.GIT_COMMAND_FAILED, "HEAD 读取失败（测试注入）。"));
        var service = new BypassDetectionService(failingGit, auditStore, new RecordingPendingScanStore(), new FixedClock(_sampleUtcNow));

        Action act = () => service.Detect();

        act.Should().Throw<GitException>().Which.ErrorCode.Should().Be(GitException.GIT_COMMAND_FAILED);
        auditStore.SavedBypassEvents.Should().BeEmpty();
    }

    [Fact]
    public void Ctor_NullArguments_ThrowArgumentNullException()
    {
        var git = new FakeGitAccess(_repoRoot);
        var auditStore = new RecordingAuditStore();
        var pendingStore = new RecordingPendingScanStore();
        var clock = new FixedClock(_sampleUtcNow);

        Action nullGit = () => _ = new BypassDetectionService(null!, auditStore, pendingStore, clock);
        Action nullAudit = () => _ = new BypassDetectionService(git, null!, pendingStore, clock);
        Action nullPending = () => _ = new BypassDetectionService(git, auditStore, null!, clock);
        Action nullClock = () => _ = new BypassDetectionService(git, auditStore, pendingStore, null!);

        nullGit.Should().Throw<ArgumentNullException>();
        nullAudit.Should().Throw<ArgumentNullException>();
        nullPending.Should().Throw<ArgumentNullException>();
        nullClock.Should().Throw<ArgumentNullException>();
    }

    private static BypassDetectionService CreateService(
        IAuditStore auditStore,
        RecordingPendingScanStore pendingStore,
        IEnumerable<string>? parents = null)
    {
        var git = new FakeGitAccess(
            _repoRoot,
            headCommitSha: () => HEAD_SHA,
            commitMetadata: sha => new CommitMetadata(sha, TREE_SHA, parents ?? new[] { PARENT_SHA }));
        return new BypassDetectionService(git, auditStore, pendingStore, new FixedClock(_sampleUtcNow));
    }
}
