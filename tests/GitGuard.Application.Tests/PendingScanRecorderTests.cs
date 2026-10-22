using FluentAssertions;
using GitGuard.Application.Bypass;
using GitGuard.Core.Model;

namespace GitGuard.Application.Tests;

/// <summary>
/// 待提交预测记录器测试（SRS FR-050）：pre-commit 阶段记录父提交 + 暂存树，
/// 记录失败原样上抛（由 CLI 降级为警告，不阻断提交）。
/// </summary>
public sealed class PendingScanRecorderTests
{
    private const string PARENT_SHA = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string TREE_SHA = "cccccccccccccccccccccccccccccccccccccccc";

    private static readonly string _repoRoot = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "gitguard-fake-repo"));
    private static readonly DateTimeOffset _sampleUtcNow = new DateTimeOffset(2026, 10, 16, 8, 30, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Record_WritesParentTreeSkippedAndTimestamp(bool skipped)
    {
        var store = new RecordingPendingScanStore();
        var recorder = CreateRecorder(store, parentSha: PARENT_SHA);

        recorder.Record(skipped);

        PendingScan written = store.Writes.Should().ContainSingle().Subject;
        written.ParentSha.Should().Be(PARENT_SHA);
        written.TreeSha.Should().Be(TREE_SHA);
        written.Skipped.Should().Be(skipped);
        written.RecordedAt.Should().Be(_sampleUtcNow);
    }

    // 首个提交：HEAD 尚无指向，预测的父提交必须为空
    [Fact]
    public void Record_UnbornHead_WritesNullParent()
    {
        var store = new RecordingPendingScanStore();
        var recorder = CreateRecorder(store, parentSha: null);

        recorder.Record(skipped: false);

        store.Writes.Should().ContainSingle().Which.ParentSha.Should().BeNull();
    }

    [Fact]
    public void Record_StoreWriteFailure_Propagates()
    {
        var store = new RecordingPendingScanStore { WriteException = new IOException("标记写入失败（测试注入）。") };
        var recorder = CreateRecorder(store, parentSha: PARENT_SHA);

        Action act = () => recorder.Record(skipped: false);

        act.Should().Throw<IOException>();
    }

    [Fact]
    public void Clear_DelegatesToStore()
    {
        var store = new RecordingPendingScanStore
        {
            Stored = new PendingScan(PARENT_SHA, TREE_SHA, skipped: false, _sampleUtcNow),
        };
        var recorder = CreateRecorder(store, parentSha: PARENT_SHA);

        recorder.Clear();

        store.ClearCalls.Should().Be(1);
        store.Stored.Should().BeNull();
    }

    [Fact]
    public void Ctor_NullArguments_ThrowArgumentNullException()
    {
        var git = new FakeGitAccess(_repoRoot);
        var store = new RecordingPendingScanStore();
        var clock = new FixedClock(_sampleUtcNow);

        Action nullGit = () => _ = new PendingScanRecorder(null!, store, clock);
        Action nullStore = () => _ = new PendingScanRecorder(git, null!, clock);
        Action nullClock = () => _ = new PendingScanRecorder(git, store, null!);

        nullGit.Should().Throw<ArgumentNullException>();
        nullStore.Should().Throw<ArgumentNullException>();
        nullClock.Should().Throw<ArgumentNullException>();
    }

    private static PendingScanRecorder CreateRecorder(RecordingPendingScanStore store, string? parentSha)
    {
        var git = new FakeGitAccess(
            _repoRoot,
            headCommitSha: () => parentSha,
            stagedTreeSha: () => TREE_SHA);
        return new PendingScanRecorder(git, store, new FixedClock(_sampleUtcNow));
    }
}
