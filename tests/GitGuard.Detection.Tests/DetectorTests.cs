using FluentAssertions;
using GitGuard.Core;
using GitGuard.Core.Abstractions;
using GitGuard.Core.Model;
using GitGuard.Core.Rules;
using GitGuard.Detection.Detectors;

namespace GitGuard.Detection.Tests;

/// <summary>
/// 四类检测器行为测试：TC-FR-021-S-01 / S-02 / P-01 / P-02 / L-01 / U-01 六条验收用例，
/// 以及上下文门禁、声明式校验器、测试文件 / 文档文件 / 示例文件降级、长行与文件跳过等类别语义。
/// </summary>
public sealed class DetectorTests
{
    private static readonly string[] _unknownValidators = new[] { "unknown-validator" };

    private static readonly string[] _crossCategoryValidators = new[] { "shannon-entropy" };

    // ===== TC-FR-021：六条验收用例（docs/16-测试方案.md §4） =====
    [Fact]
    public async Task TC_FR_021_S_01_AwsAccessKeyId_ShouldHitWithCriticalSeverity()
    {
        IDetector detector = TestSupport.CreateDetector("SECRET");

        List<Finding> findings = await TestSupport.DetectSingleLineAsync(
            detector,
            TestSupport.NEUTRAL_SCAN_PATH,
            "aws_access_key_id = \"AKIAIOSFODNN7EXAMPLE\"");

        Finding finding = SingleFinding(findings, "GGD/SECRET/AWS-AKID");
        finding.Severity.Should().Be(Severity.Critical);
        finding.LineNumber.Should().Be(1);
    }

    [Fact]
    public async Task TC_FR_021_S_02_SlackToken_ShouldHitWithHighSeverity()
    {
        IDetector detector = TestSupport.CreateDetector("SECRET");

        List<Finding> findings = await TestSupport.DetectSingleLineAsync(
            detector,
            TestSupport.NEUTRAL_SCAN_PATH,
            "slack = \"xoxb-123456789012-ABCDEFGHIJKLMNOP\"");

        Finding finding = SingleFinding(findings, "GGD/SECRET/SLACK-TOKEN");
        finding.Severity.Should().Be(Severity.High);
    }

    [Fact]
    public async Task TC_FR_021_P_01_CnMobileNumberInProfileContext_ShouldHitWithMediumSeverity()
    {
        IDetector detector = TestSupport.CreateDetector("PRIVACY");

        List<Finding> findings = await TestSupport.DetectSingleLineAsync(
            detector,
            TestSupport.NEUTRAL_SCAN_PATH,
            "用户资料 phone: 13812345678");

        findings.Should().ContainSingle();
        findings[0].RuleId.Should().Be("GGD/PRIVACY/CN-MOBILE");
        findings[0].Severity.Should().Be(Severity.Medium);
    }

    [Fact]
    public async Task TC_FR_021_P_02_CnIdCardWithValidChecksum_ShouldHitWithHighSeverity()
    {
        IDetector detector = TestSupport.CreateDetector("PRIVACY");

        List<Finding> findings = await TestSupport.DetectSingleLineAsync(
            detector,
            TestSupport.NEUTRAL_SCAN_PATH,
            "身份证号：11010519491231002X");

        findings.Should().ContainSingle();
        findings[0].RuleId.Should().Be("GGD/PRIVACY/CN-IDCARD-18");
        findings[0].Severity.Should().Be(Severity.High);
    }

    [Fact]
    public async Task TC_FR_021_L_01_WindowsUserPath_ShouldHitWithLowSeverity()
    {
        IDetector detector = TestSupport.CreateDetector("LOCALPATH");

        List<Finding> findings = await TestSupport.DetectSingleLineAsync(
            detector,
            TestSupport.NEUTRAL_SCAN_PATH,
            "output = \"C:/Users/rushe/Desktop/report.html\"");

        Finding finding = SingleFinding(findings, "GGD/LOCALPATH/WIN-USER");
        finding.Severity.Should().Be(Severity.Low);
    }

    [Fact]
    public async Task TC_FR_021_U_01_UrlWithCredentials_ShouldHitWithHighSeverityAndMaskUserInfo()
    {
        IDetector detector = TestSupport.CreateDetector("URL");

        List<Finding> findings = await TestSupport.DetectSingleLineAsync(
            detector,
            TestSupport.NEUTRAL_SCAN_PATH,
            "clone = \"https://user:pass@git.example.com/org/repo.git\"");

        Finding finding = SingleFinding(findings, "GGD/URL/CREDENTIALS-IN-URL");
        finding.Severity.Should().Be(Severity.High);
        finding.SnippetMasked.Value.Should().Be("us*****ss", "命中值为第 1 捕获组 user:pass，经 Masker 保留前后各 2 字符");
    }

    // ===== Secret 类别语义（模块设计 Secret §6） =====
    [Fact]
    public async Task SecretDetector_LockFile_ShouldSkipWholeFile()
    {
        IDetector detector = TestSupport.CreateDetector("SECRET");

        List<Finding> findings = await TestSupport.DetectLinesAsync(
            detector,
            "pnpm.lock",
            "aws_access_key_id = \"AKIAIOSFODNN7EXAMPLE\"");

        findings.Should().BeEmpty("锁定文件不参与扫描");
    }

    [Fact]
    public async Task SecretDetector_MinifiedBundle_ShouldSkipWholeFile()
    {
        IDetector detector = TestSupport.CreateDetector("SECRET");

        List<Finding> findings = await TestSupport.DetectLinesAsync(
            detector,
            "static/app.min.js",
            "aws_access_key_id = \"AKIAIOSFODNN7EXAMPLE\"");

        findings.Should().BeEmpty("压缩产物不参与扫描");
    }

    [Fact]
    public async Task SecretDetector_LongLine_ShouldOnlyKeepHighConfidenceRules()
    {
        IDetector detector = TestSupport.CreateDetector("SECRET");
        string longPrefix = new string('x', 2100);

        List<Finding> findings = await TestSupport.DetectLinesAsync(
            detector,
            TestSupport.NEUTRAL_SCAN_PATH,
            longPrefix + " AKIAIOSFODNN7EXAMPLE",
            longPrefix + " password = \"S3cret!Pass\"");

        findings.Should().ContainSingle("超长行仅保留高置信度规则（AWS AKID 命中、GENERIC-PASSWORD 跳过）");
        findings[0].RuleId.Should().Be("GGD/SECRET/AWS-AKID");
    }

    [Fact]
    public async Task SecretDetector_ShortLineWithHardcodedPassword_ShouldHit()
    {
        IDetector detector = TestSupport.CreateDetector("SECRET");

        List<Finding> findings = await TestSupport.DetectSingleLineAsync(
            detector,
            TestSupport.NEUTRAL_SCAN_PATH,
            "password = \"S3cret!Pass\"");

        SingleFinding(findings, "GGD/SECRET/GENERIC-PASSWORD");
    }

    [Fact]
    public async Task SecretDetector_AwsSecretBelowEntropyThreshold_ShouldNotHit()
    {
        IDetector detector = TestSupport.CreateDetector("SECRET");

        List<Finding> findings = await TestSupport.DetectSingleLineAsync(
            detector,
            TestSupport.NEUTRAL_SCAN_PATH,
            "aws_secret_access_key = \"" + new string('a', 40) + "\"");

        findings.Should().BeEmpty("40 个相同字符熵为 0，低于默认阈值 4.0");
    }

    [Fact]
    public async Task SecretDetector_HighEntropyAwsSecret_ShouldHit()
    {
        IDetector detector = TestSupport.CreateDetector("SECRET");

        List<Finding> findings = await TestSupport.DetectSingleLineAsync(
            detector,
            TestSupport.NEUTRAL_SCAN_PATH,
            "aws_secret_access_key = \"wJalrXUtnFEMI/K7MDENG/bPxRfiCYEXAMPLEKEY\"");

        Finding finding = SingleFinding(findings, "GGD/SECRET/AWS-SECRET");
        finding.Severity.Should().Be(Severity.Critical);
    }

    // ===== Privacy 类别语义（模块设计 Privacy §5-§7） =====
    [Fact]
    public async Task PrivacyDetector_TestFilePath_ShouldDowngradeOneLevel()
    {
        IDetector detector = TestSupport.CreateDetector("PRIVACY");

        List<Finding> findings = await TestSupport.DetectSingleLineAsync(
            detector,
            "tests/profile_fixture.cs",
            "phone: 13812345678");

        Finding finding = SingleFinding(findings, "GGD/PRIVACY/CN-MOBILE");
        finding.Severity.Should().Be(Severity.Low, "测试文件中的命中降一级：Medium → Low");
    }

    [Fact]
    public async Task PrivacyDetector_EmailInTestFile_ShouldKeepLowSeverity()
    {
        IDetector detector = TestSupport.CreateDetector("PRIVACY");

        List<Finding> findings = await TestSupport.DetectSingleLineAsync(
            detector,
            "tests/contacts_sample.txt",
            "mail: alice@example.com");

        Finding finding = SingleFinding(findings, "GGD/PRIVACY/EMAIL");
        finding.Severity.Should().Be(Severity.Low, "Low 已无可降级空间（StepOrFloor 语义）");
    }

    [Fact]
    public async Task PrivacyDetector_RequireContextDisabled_ShouldHitWithoutContextKeyword()
    {
        var options = new PrivacyDetectorOptions { RequireContext = false };
        var detector = TestSupport.CreatePrivacyDetector(options);

        List<Finding> findings = await TestSupport.DetectSingleLineAsync(
            detector,
            TestSupport.NEUTRAL_SCAN_PATH,
            "13812345678");

        SingleFinding(findings, "GGD/PRIVACY/CN-MOBILE");
    }

    [Fact]
    public async Task PrivacyDetector_InvalidLuhnCardNumber_ShouldNotHit()
    {
        IDetector detector = TestSupport.CreateDetector("PRIVACY");

        List<Finding> findings = await TestSupport.DetectSingleLineAsync(
            detector,
            TestSupport.NEUTRAL_SCAN_PATH,
            "card_no = \"6222021234567895\"");

        findings.Should().BeEmpty("银行卡号必须通过 Luhn 校验");
    }

    [Fact]
    public async Task PrivacyDetector_InvalidIdCardChecksum_ShouldNotHit()
    {
        IDetector detector = TestSupport.CreateDetector("PRIVACY");

        List<Finding> findings = await TestSupport.DetectSingleLineAsync(
            detector,
            TestSupport.NEUTRAL_SCAN_PATH,
            "身份证号：110105194912310021");

        findings.Should().BeEmpty("身份证校验位不合法");
    }

    // ===== LocalPath / Url 类别语义（模块设计 LocalPathUrl §3） =====
    [Fact]
    public async Task LocalPathDetector_DocFileGlob_ShouldDropLowSeverityHits()
    {
        IDetector detector = TestSupport.CreateDetector("LOCALPATH");

        List<Finding> findings = await TestSupport.DetectSingleLineAsync(
            detector,
            "docs/guide.md",
            "cd /home/alice/projects/app");

        findings.Should().BeEmpty("文档文件中 Low 级路径信息无可降级空间，直接丢弃");
    }

    [Fact]
    public async Task UrlDetector_SampleFilePrefix_ShouldDowngradeOneLevel()
    {
        IDetector detector = TestSupport.CreateDetector("URL");

        List<Finding> findings = await TestSupport.DetectSingleLineAsync(
            detector,
            "examples/sample_urls.txt",
            "https://user:pass@host.example.com/x");

        Finding finding = SingleFinding(findings, "GGD/URL/CREDENTIALS-IN-URL");
        finding.Severity.Should().Be(Severity.Medium, "示例文件中的命中降一级：High → Medium");
    }

    [Fact]
    public async Task UrlDetector_PublicUrlWithoutSecret_ShouldNotHit()
    {
        IDetector detector = TestSupport.CreateDetector("URL");

        List<Finding> findings = await TestSupport.DetectSingleLineAsync(
            detector,
            TestSupport.NEUTRAL_SCAN_PATH,
            "docs_url = \"https://api.github.com/repos/org/project\"");

        findings.Should().BeEmpty("公开 URL 不含凭据 / token / 私网地址");
    }

    // ===== 装配期契约 =====
    [Fact]
    public void Detectors_Names_ShouldMatchDesignContracts()
    {
        TestSupport.CreateDetector("SECRET").Name.Should().Be("secret");
        TestSupport.CreateDetector("PRIVACY").Name.Should().Be("privacy");
        TestSupport.CreateDetector("LOCALPATH").Name.Should().Be("local_path");
        TestSupport.CreateDetector("URL").Name.Should().Be("url");
    }

    [Fact]
    public void UnsupportedValidatorReference_SecretDetector_ShouldFailClosedAtConstruction()
    {
        var rule = DetectionRule.Create(
            "GGD/SECRET/AWS-AKID",
            "引用未知校验器的规则",
            Severity.High,
            RulePattern.Create("AKIA[0-9A-Z]{16}", validators: _unknownValidators));

        Action act = () => _ = new SecretDetector(new[] { rule }, TestSupport.CreateFactory());

        act.Should().Throw<DetectionException>().WithMessage("*unknown-validator*");
    }

    [Fact]
    public void UnsupportedValidatorReference_PrivacyDetector_ShouldFailClosedAtConstruction()
    {
        var rule = DetectionRule.Create(
            "GGD/PRIVACY/EMAIL",
            "引用跨类别校验器的规则",
            Severity.Low,
            RulePattern.Create("[A-Za-z]+", validators: _crossCategoryValidators));

        Action act = () => _ = new PrivacyDetector(new[] { rule }, TestSupport.CreateFactory());

        act.Should().Throw<DetectionException>().WithMessage("*shannon-entropy*");
    }

    [Fact]
    public async Task DisabledRule_ShouldNotParticipateInScan()
    {
        var disabled = DetectionRule.Create(
            "GGD/PRIVACY/EMAIL",
            "禁用的邮箱规则",
            Severity.Low,
            RulePattern.Create("alice@example.com"),
            enabled: false);
        var detector = new PrivacyDetector(new[] { disabled }, TestSupport.CreateFactory());

        List<Finding> findings = await TestSupport.DetectSingleLineAsync(
            detector,
            TestSupport.NEUTRAL_SCAN_PATH,
            "mail: alice@example.com");

        findings.Should().BeEmpty("enabled=false 的规则不参与扫描");
    }

    private static Finding SingleFinding(List<Finding> findings, string ruleId)
    {
        Finding[] target = findings.Where(finding => finding.RuleId == ruleId).ToArray();
        target.Should().ContainSingle($"{ruleId} 应恰好产出一条发现项");
        return target[0];
    }
}
