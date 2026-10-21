using FluentAssertions;
using GitGuard.Core.Abstractions;
using GitGuard.Core.Model;
using GitGuard.Detection.Detectors;

namespace GitGuard.Detection.Tests;

/// <summary>
/// 规则级耗时上报测试（NFR-M-002）：验证 <see cref="IRuleTimingAwareDetector.DetectWithTimingAsync"/> 的
/// 命中 / 未命中记录、与普通扫描的语义一致性，以及空接收器拒绝。
/// </summary>
public sealed class RuleTimingTests
{
    private const string AWS_RULE = "GGD/SECRET/AWS-AKID";

    [Fact]
    public async Task DetectWithTimingAsync_Hit_RecordsRuleEvaluationWithHit()
    {
        var detector = (IRuleTimingAwareDetector)TestSupport.CreateDetector("SECRET");
        var sink = new RecordingTimingSink();
        var change = new FileChange(
            TestSupport.NEUTRAL_SCAN_PATH,
            new[] { new ChangedLine(1, "aws_key=AKIAABCDEFGHIJKLMNOP") });

        List<Finding> findings = await CollectAsync(detector, change, sink);

        findings.Should().ContainSingle().Which.RuleId.Should().Be(AWS_RULE);
        sink.Records.Should().Contain(record => record.RuleId == AWS_RULE && record.Hit);
        sink.Records.Should().OnlyContain(record => record.ElapsedTimestampTicks >= 0);
    }

    [Fact]
    public async Task DetectWithTimingAsync_NoMatch_RecordsEvaluationsWithoutHit()
    {
        var detector = (IRuleTimingAwareDetector)TestSupport.CreateDetector("SECRET");
        var sink = new RecordingTimingSink();
        var change = new FileChange(
            TestSupport.NEUTRAL_SCAN_PATH,
            new[] { new ChangedLine(1, "const answer = 42;") });

        List<Finding> findings = await CollectAsync(detector, change, sink);

        findings.Should().BeEmpty();
        sink.Records.Should().NotBeEmpty("规则仍被执行了正则求值");
        sink.Records.Should().OnlyContain(record => !record.Hit);
    }

    [Fact]
    public async Task DetectWithTimingAsync_ProducesSameFindingsAsDetectAsync()
    {
        IDetector detector = TestSupport.CreateDetector("SECRET");
        const string line = "aws_key=AKIAABCDEFGHIJKLMNOP";
        List<Finding> plain = await TestSupport.DetectSingleLineAsync(
            detector, TestSupport.NEUTRAL_SCAN_PATH, line);
        var sink = new RecordingTimingSink();

        List<Finding> timed = await CollectAsync(
            (IRuleTimingAwareDetector)detector,
            new FileChange(TestSupport.NEUTRAL_SCAN_PATH, new[] { new ChangedLine(1, line) }),
            sink);

        plain.Should().ContainSingle();
        timed.Should().ContainSingle();
        timed[0].RuleId.Should().Be(plain[0].RuleId);
        timed[0].FilePath.Should().Be(plain[0].FilePath);
        timed[0].LineNumber.Should().Be(plain[0].LineNumber);
        timed[0].Severity.Should().Be(plain[0].Severity);
        timed[0].Fingerprint.Should().Be(plain[0].Fingerprint);
        timed[0].SnippetMasked.Should().Be(plain[0].SnippetMasked);
    }

    [Fact]
    public void DetectWithTimingAsync_NullSink_ThrowsArgumentNullException()
    {
        var detector = (IRuleTimingAwareDetector)TestSupport.CreateDetector("SECRET");
        var change = new FileChange(TestSupport.NEUTRAL_SCAN_PATH, new[] { new ChangedLine(1, "clean") });

        Action act = () => detector.DetectWithTimingAsync(change, null!);

        act.Should().Throw<ArgumentNullException>();
    }

    private static async Task<List<Finding>> CollectAsync(
        IRuleTimingAwareDetector detector,
        FileChange change,
        RecordingTimingSink sink)
    {
        var findings = new List<Finding>();
        await foreach (Finding finding in detector.DetectWithTimingAsync(
            change, sink, TestContext.Current.CancellationToken))
        {
            findings.Add(finding);
        }

        return findings;
    }

    /// <summary>测试用耗时接收器：保留检测器的每次上报（规则 ID / 耗时 / 是否命中）。</summary>
    private sealed class RecordingTimingSink : IRuleTimingSink
    {
        internal List<(string RuleId, long ElapsedTimestampTicks, bool Hit)> Records { get; } =
            new List<(string, long, bool)>();

        public void Record(string ruleId, long elapsedTimestampTicks, bool hit) =>
            Records.Add((ruleId, elapsedTimestampTicks, hit));
    }
}
