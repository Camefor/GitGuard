using FluentAssertions;
using GitGuard.Application.Scanning;
using GitGuard.Core.Abstractions;
using GitGuard.Core.Model;

namespace GitGuard.Application.Tests;

/// <summary>
/// 规则耗时采集测试（NFR-M-002）：验证 <see cref="ScanStagedUseCase"/> 的采集开关、
/// 按规则聚合与排序，以及非计时检测器不参与摘要。
/// </summary>
public sealed class ScanRuleTimingTests
{
    private static readonly string RepoRoot = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "gitguard-fake-repo"));
    private static readonly DateTimeOffset ScanStart = new DateTimeOffset(2026, 10, 16, 8, 30, 0, TimeSpan.Zero);

    [Fact]
    public async Task ExecuteAsync_RuleTimingEnabled_AggregatesAndSortsByRuleId()
    {
        var detector = new TimingAwareStubDetector();
        ScanStagedUseCase useCase = CreateUseCase(detector, collectRuleTimings: true, diff: DiffFor("a.cs") + DiffFor("b.cs"));

        ScanOutcome outcome = await useCase.ExecuteAsync(TestContext.Current.CancellationToken);

        detector.TimedCalls.Should().Be(2);
        detector.PlainCalls.Should().Be(0);
        outcome.RuleTimings.Should().HaveCount(2);
        outcome.RuleTimings[0].RuleId.Should().Be(TimingAwareStubDetector.HIT_RULE);
        outcome.RuleTimings[0].EvaluationCount.Should().Be(2);
        outcome.RuleTimings[0].HitCount.Should().Be(2);
        outcome.RuleTimings[0].ElapsedTimestampTicks.Should().Be(200);
        outcome.RuleTimings[1].RuleId.Should().Be(TimingAwareStubDetector.MISS_RULE);
        outcome.RuleTimings[1].EvaluationCount.Should().Be(2);
        outcome.RuleTimings[1].HitCount.Should().Be(0);
        outcome.RuleTimings[1].ElapsedTimestampTicks.Should().Be(400);
    }

    [Fact]
    public async Task ExecuteAsync_RuleTimingDisabled_UsesPlainDetectionAndReturnsEmptyTimings()
    {
        var detector = new TimingAwareStubDetector();
        ScanStagedUseCase useCase = CreateUseCase(detector, collectRuleTimings: false, diff: DiffFor("a.cs"));

        ScanOutcome outcome = await useCase.ExecuteAsync(TestContext.Current.CancellationToken);

        detector.PlainCalls.Should().Be(1);
        detector.TimedCalls.Should().Be(0);
        outcome.RuleTimings.Should().BeEmpty();
    }

    [Fact]
    public async Task ExecuteAsync_NonTimingAwareDetector_StillScansWithEmptyTimings()
    {
        var detector = new StubDetector("plain", _ => Array.Empty<Finding>());
        ScanStagedUseCase useCase = CreateUseCase(detector, collectRuleTimings: true, diff: DiffFor("a.cs"));

        ScanOutcome outcome = await useCase.ExecuteAsync(TestContext.Current.CancellationToken);

        outcome.Result.Should().Be(ScanResult.Clean);
        outcome.RuleTimings.Should().BeEmpty();
        detector.SeenChanges.Should().ContainSingle();
    }

    private static ScanStagedUseCase CreateUseCase(IDetector detector, bool collectRuleTimings, string diff) =>
        new ScanStagedUseCase(
            new FakeGitAccess(RepoRoot, stagedDiff: () => diff),
            new List<IDetector> { detector },
            new RecordingAuditStore(),
            new FixedClock(ScanStart),
            collectRuleTimings: collectRuleTimings);

    private static string DiffFor(string path) =>
        $"diff --git a/{path} b/{path}\n" +
        $"--- a/{path}\n" +
        $"+++ b/{path}\n" +
        "@@ -0,0 +1 @@\n" +
        "+added line\n";
}
