using FluentAssertions;
using GitGuard.Core.Abstractions;
using GitGuard.Core.Model;

namespace GitGuard.Detection.Tests;

/// <summary>
/// DR-02 零明文留存（检测层部分）：发现项只允许携带指纹与脱敏片段，
/// 命中原文不得出现在任何可持久化 / 可展示字段中（对应 TC-DR-02-01）。
/// </summary>
public sealed class CanaryLeakageTests
{
    private const string CANARY_AWS_KEY = "AKIAIOSFODNN7EXAMPLE";

    [Fact]
    public async Task Finding_ShouldOnlyCarryFingerprintAndMaskedSnippet()
    {
        IDetector detector = TestSupport.CreateDetector("SECRET");

        List<Finding> findings = await TestSupport.DetectSingleLineAsync(
            detector,
            TestSupport.NEUTRAL_SCAN_PATH,
            $"aws_access_key_id = \"{CANARY_AWS_KEY}\"");

        Finding finding = findings.Single();
        finding.SnippetMasked.Value.Should().Be("AK****************LE", "20 字符原文保留前后各 2 字符、中间全部掩码");
        finding.SnippetMasked.Value.Should().NotContain(CANARY_AWS_KEY);
        finding.Fingerprint.Hex.Should().MatchRegex("^[0-9a-f]{64}$");
        finding.FilePath.Should().NotContain(CANARY_AWS_KEY);
        finding.CommitSha.Should().BeNull();
    }

    [Theory]
    [InlineData("SECRET", "aws_access_key_id = \"AKIAIOSFODNN7EXAMPLE\"", "AKIAIOSFODNN7EXAMPLE")]
    [InlineData("SECRET", "slack = \"xoxb-123456789012-ABCDEFGHIJKLMNOP\"", "xoxb-123456789012-ABCDEFGHIJKLMNOP")]
    [InlineData("SECRET", "aws_secret_access_key = \"wJalrXUtnFEMI/K7MDENG/bPxRfiCYEXAMPLEKEY\"", "wJalrXUtnFEMI/K7MDENG/bPxRfiCYEXAMPLEKEY")]
    [InlineData("PRIVACY", "phone: 13812345678", "13812345678")]
    [InlineData("LOCALPATH", "path = D:\\Users\\alice\\app", "D:\\Users\\alice\\")]
    [InlineData("URL", "clone = \"https://user:pass@git.example.com/repo.git\"", "user:pass")]
    public async Task FindingFields_ShouldNeverContainRawSecret(string category, string line, string rawSecret)
    {
        IDetector detector = TestSupport.CreateDetector(category);

        List<Finding> findings = await TestSupport.DetectSingleLineAsync(detector, TestSupport.NEUTRAL_SCAN_PATH, line);

        findings.Should().NotBeEmpty($"「{line}」应至少产出一条发现项（原始值：{rawSecret}）");
        foreach (Finding finding in findings)
        {
            finding.SnippetMasked.Value.Should().NotContain(rawSecret);
            finding.RuleId.Should().NotContain(rawSecret);
            finding.FilePath.Should().NotContain(rawSecret);
        }
    }

    [Fact]
    public async Task Fingerprint_ShouldBeStableForSameSecretAcrossFiles()
    {
        IDetector detector = TestSupport.CreateDetector("SECRET");
        string line = $"aws_access_key_id = \"{CANARY_AWS_KEY}\"";

        List<Finding> first = await TestSupport.DetectSingleLineAsync(detector, "src/a.cs", line);
        List<Finding> second = await TestSupport.DetectSingleLineAsync(detector, "src/b.cs", line);

        first.Single().Fingerprint.Should().Be(second.Single().Fingerprint, "同一盐下相同原文指纹稳定（白名单比对依赖该性质）");
    }
}
