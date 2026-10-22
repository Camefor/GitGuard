using FluentAssertions;
using GitGuard.Core;
using GitGuard.Core.Abstractions;
using GitGuard.Core.Model;
using GitGuard.Core.Rules;
using GitGuard.Detection.Detectors;
using GitGuard.Detection.Rules;

namespace GitGuard.Detection.Tests;

/// <summary>
/// 检测层健壮性测试：内置规则正则对灾难性回溯（ReDoS）的韧性、正则超时的 FailClosed 转换
/// （<see cref="DetectionException"/> → CLI 退出码 11）与取消令牌响应。
/// </summary>
public sealed class DetectorRobustnessTests
{
    /// <summary>对抗性输入集：重复字符、嵌套括号、超长连续段与正则热点形态。</summary>
    private static readonly string[] _evilInputs = new[]
    {
        new string('a', 64) + "!",
        new string('(', 512) + new string('a', 512) + new string(')', 512),
        new string('/', 2048),
        new string('A', 4096),
        new string('0', 2048),
        new string('1', 512) + "x",
        new string('9', 1024) + "@",
        "xoxb-" + new string('-', 1024),
        "eyJ" + new string('_', 1024) + ".eyJ" + new string('_', 1024) + "." + new string('_', 1024),
        "-----BEGIN " + new string('A', 1024) + " PRIVATE KEY-----",
        "Server=" + new string('a', 2048) + ";Password=" + new string('b', 2048),
        "http://" + new string('u', 4096) + "@",
        new string('.', 1024) + "internal",
        new string(' ', 2048) + "~",
    };

    [Fact]
    public void BuiltinRuleRegexes_ShouldResistAdversarialInputs()
    {
        var rules = BuiltinRuleLoader.LoadEmbedded();

        foreach (DetectionRule rule in rules)
        {
            foreach (string input in _evilInputs)
            {
                Action act = () => _ = rule.Pattern.Regex.IsMatch(input);
                act.Should().NotThrow($"{rule.RuleId} 在对抗输入下不应发生灾难性回溯（200ms 超时）");
            }
        }
    }

    [Fact]
    public async Task RegexMatchTimeout_ShouldFailClosedWithDetectionException()
    {
        // 灾难性回溯规则（仅测试用，不进入内置规则集）：64 个 'a' + '!' 足以触发 2^n 回溯
        var catastrophic = DetectionRule.Create(
            "GGD/SECRET/RE-DOS-TEST",
            "灾难性回溯测试规则",
            Severity.High,
            RulePattern.Create("^(a+)+$"));
        var detector = new SecretDetector(new[] { catastrophic }, TestSupport.CreateFactory());

        Func<Task> act = async () =>
        {
            await TestSupport.DetectSingleLineAsync(detector, TestSupport.NEUTRAL_SCAN_PATH, new string('a', 64) + "!");
        };

        var assertion = await act.Should().ThrowAsync<DetectionException>();
        assertion.Which.RuleId.Should().Be("GGD/SECRET/RE-DOS-TEST");
        assertion.Which.ErrorCode.Should().Be(DetectionException.DETECTOR_FAILED, "CLI 依据 ErrorCode 映射退出码 11");
    }

    [Fact]
    public async Task DetectAsync_PreCanceledToken_ShouldThrowOperationCanceledException()
    {
        IDetector detector = TestSupport.CreateDetector("SECRET");
        var change = new FileChange(
            TestSupport.NEUTRAL_SCAN_PATH,
            new[] { new ChangedLine(1, "aws_access_key_id = \"AKIAIOSFODNN7EXAMPLE\"") });
        using var source = new CancellationTokenSource();
        source.Cancel();

        Func<Task> act = async () =>
        {
            await foreach (Finding finding in detector.DetectAsync(change, source.Token))
            {
                throw new InvalidOperationException($"已取消的令牌不应产出发现项：{finding.RuleId}");
            }
        };

        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
