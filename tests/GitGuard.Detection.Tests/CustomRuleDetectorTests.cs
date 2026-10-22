using FluentAssertions;
using GitGuard.Core;
using GitGuard.Core.Model;
using GitGuard.Core.Rules;
using GitGuard.Detection.Detectors;

namespace GitGuard.Detection.Tests;

/// <summary>
/// 自定义规则（<c>rules.custom</c>，FR-080）的检测语义：USR 规则经四类检测器同一管线执行，
/// 上下文关键字 / 文件 glob / 禁用开关与 200ms 正则超时（FailClosed）语义保持一致。
/// </summary>
public sealed class CustomRuleDetectorTests
{
    private const string RULE_ID = "USR/SECRET/INTERNAL-TOKEN";

    private static readonly string[] _contextKeywords = new[] { "token" };

    private static readonly string[] _filePathGlobs = new[] { "src/**" };

    [Fact]
    public async Task CustomSecretRule_ShouldHitAndMaskValue()
    {
        var detector = CreateDetector(CreateRule());

        List<Finding> findings = await TestSupport.DetectSingleLineAsync(
            detector,
            TestSupport.NEUTRAL_SCAN_PATH,
            "token = \"INT-1234\"");

        Finding finding = findings.Should().ContainSingle().Subject;
        finding.RuleId.Should().Be(RULE_ID);
        finding.Severity.Should().Be(Severity.High);
        finding.SnippetMasked.Value.Should().NotContain("INT-1234", "命中值统一经 Core Masker 脱敏");
    }

    [Fact]
    public async Task CustomSecretRule_ContextKeywordMissing_ShouldNotHit()
    {
        var detector = CreateDetector(CreateRule());

        List<Finding> findings = await TestSupport.DetectSingleLineAsync(
            detector,
            TestSupport.NEUTRAL_SCAN_PATH,
            "value = \"INT-1234\"");

        findings.Should().BeEmpty("规则声明 context_keywords 时命中行必须包含任一关键字");
    }

    [Fact]
    public async Task CustomSecretRule_FileGlobMismatch_ShouldNotHit()
    {
        var detector = CreateDetector(CreateRule());

        List<Finding> findings = await TestSupport.DetectSingleLineAsync(
            detector,
            "docs/notes.md",
            "token = \"INT-1234\"");

        findings.Should().BeEmpty("规则声明 file_path_globs 时仅扫描匹配文件");
    }

    [Fact]
    public async Task CustomSecretRule_LongLine_ShouldStillHit()
    {
        var detector = CreateDetector(CreateRule());
        string line = new string('x', 2049) + " token = \"INT-1234\"";

        List<Finding> findings = await TestSupport.DetectSingleLineAsync(
            detector,
            TestSupport.NEUTRAL_SCAN_PATH,
            line);

        findings.Should().ContainSingle("自定义规则由用户显式声明，超长行（疑似 minified）不静默跳过");
    }

    [Fact]
    public async Task CustomSecretRule_Disabled_ShouldNotBeSelected()
    {
        var detector = CreateDetector(CreateRule(enabled: false));

        List<Finding> findings = await TestSupport.DetectSingleLineAsync(
            detector,
            TestSupport.NEUTRAL_SCAN_PATH,
            "token = \"INT-1234\"");

        findings.Should().BeEmpty("enabled: false 的自定义规则不参与扫描");
    }

    [Fact]
    public async Task CustomRule_RegexTimeout_ShouldFailClosed()
    {
        var rule = DetectionRule.Create(
            "USR/SECRET/RE-DOS",
            "自定义灾难性回溯规则",
            Severity.High,
            RulePattern.Create("^(a+)+$"),
            RuleSource.User);
        var detector = CreateDetector(rule);

        Func<Task> act = async () =>
        {
            await TestSupport.DetectSingleLineAsync(
                detector,
                TestSupport.NEUTRAL_SCAN_PATH,
                new string('a', 64) + "!");
        };

        var assertion = await act.Should().ThrowAsync<DetectionException>();
        assertion.Which.RuleId.Should().Be("USR/SECRET/RE-DOS");
        assertion.Which.ErrorCode.Should().Be(DetectionException.DETECTOR_FAILED);
    }

    private static DetectionRule CreateRule(bool enabled = true) => DetectionRule.Create(
        RULE_ID,
        "内部令牌",
        Severity.High,
        RulePattern.Create(
            "INT-[0-9]{4}",
            contextKeywords: _contextKeywords,
            filePathGlobs: _filePathGlobs),
        RuleSource.User,
        enabled);

    private static SecretDetector CreateDetector(DetectionRule rule) =>
        new SecretDetector(new[] { rule }, TestSupport.CreateFactory());
}
