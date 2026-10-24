using FluentAssertions;
using GitGuard.Core;
using GitGuard.Core.Domain;
using GitGuard.Core.Model;
using GitGuard.Core.Rules;
using GitGuard.Detection.Rules;

namespace GitGuard.Detection.Tests;

/// <summary>
/// 内置规则加载器测试：覆盖 GGD-0006 / GGD-0033 / GGD-0048 验收项——45 条规则完整载入、ID 契约、严重度映射、
/// 版本 / 重复 ID / 必填字段 / 正则编译等 FailClosed 行为。
/// </summary>
public sealed class BuiltinRuleLoaderTests
{
    /// <summary>内置规则全量「ID → 严重度」期望表，顺序即 YAML 书写顺序（FR-021 稳定顺序）。</summary>
    private static readonly (string RuleId, Severity Severity)[] _expectedRules =
    [
        ("GGD/SECRET/AWS-AKID", Severity.Critical),
        ("GGD/SECRET/AWS-SECRET", Severity.Critical),
        ("GGD/SECRET/GITHUB-TOKEN", Severity.Critical),
        ("GGD/SECRET/PRIVATE-KEY", Severity.Critical),
        ("GGD/SECRET/SLACK-TOKEN", Severity.High),
        ("GGD/SECRET/GENERIC-PASSWORD", Severity.High),
        ("GGD/SECRET/DB-CONN-STRING", Severity.High),
        ("GGD/SECRET/JWT", Severity.Medium),
        ("GGD/SECRET/GOOGLE-API-KEY", Severity.High),
        ("GGD/SECRET/STRIPE-KEY", Severity.Critical),
        ("GGD/SECRET/NPM-TOKEN", Severity.High),
        ("GGD/SECRET/GITLAB-TOKEN", Severity.Critical),
        ("GGD/SECRET/OPENAI-KEY", Severity.Critical),
        ("GGD/SECRET/SENDGRID-KEY", Severity.High),
        ("GGD/SECRET/HUGGINGFACE-TOKEN", Severity.High),
        ("GGD/SECRET/AZURE-STORAGE-KEY", Severity.High),
        ("GGD/SECRET/DIGITALOCEAN-TOKEN", Severity.Critical),
        ("GGD/SECRET/HASHICORP-VAULT-TOKEN", Severity.Critical),
        ("GGD/SECRET/POSTMAN-TOKEN", Severity.High),
        ("GGD/SECRET/SHOPIFY-TOKEN", Severity.Critical),
        ("GGD/SECRET/PYPI-TOKEN", Severity.Critical),
        ("GGD/PRIVACY/CN-IDCARD-18", Severity.High),
        ("GGD/PRIVACY/CN-BANKCARD", Severity.High),
        ("GGD/PRIVACY/CN-MOBILE", Severity.Medium),
        ("GGD/PRIVACY/CN-IDCARD-15", Severity.Medium),
        ("GGD/PRIVACY/EMAIL", Severity.Low),
        ("GGD/PRIVACY/CN-PASSPORT", Severity.High),
        ("GGD/PRIVACY/CN-LANDLINE", Severity.Medium),
        ("GGD/PRIVACY/US-SSN", Severity.Medium),
        ("GGD/PRIVACY/CN-NAME-ADDRESS", Severity.Medium),
        ("GGD/LOCALPATH/UNIX-HOME", Severity.Low),
        ("GGD/LOCALPATH/WIN-USER", Severity.Low),
        ("GGD/LOCALPATH/WIN-DRIVE", Severity.Low),
        ("GGD/LOCALPATH/TEMP-WORK", Severity.Low),
        ("GGD/LOCALPATH/UNC-SHARE", Severity.Medium),
        ("GGD/LOCALPATH/DEV-DIRS", Severity.Low),
        ("GGD/LOCALPATH/HOME-TILDE", Severity.Low),
        ("GGD/URL/CREDENTIALS-IN-URL", Severity.High),
        ("GGD/URL/TOKEN-IN-QUERY", Severity.High),
        ("GGD/URL/PRIVATE-IP", Severity.Medium),
        ("GGD/URL/LOCAL-HOSTNAME", Severity.Medium),
        ("GGD/URL/SLACK-WEBHOOK", Severity.High),
        ("GGD/URL/DISCORD-WEBHOOK", Severity.High),
        ("GGD/URL/PASSWORD-QUERY", Severity.High),
        ("GGD/URL/AWS-SIGNED-URL", Severity.High),
    ];

    [Fact]
    public void LoadEmbedded_ShouldLoadAllBuiltinRulesInStableOrder()
    {
        var rules = BuiltinRuleLoader.LoadEmbedded();

        rules.Select(rule => rule.RuleId).Should().Equal(_expectedRules.Select(expected => expected.RuleId));
    }

    [Fact]
    public void LoadEmbedded_Severity_ShouldMatchDesignTables()
    {
        var rules = BuiltinRuleLoader.LoadEmbedded();

        var actual = rules.Select(rule => (rule.RuleId, rule.Severity)).ToArray();
        actual.Should().Equal(_expectedRules);
    }

    [Fact]
    public void LoadEmbedded_EveryRule_ShouldFollowNamingContractAndBeEnabled()
    {
        var rules = BuiltinRuleLoader.LoadEmbedded();

        foreach (var rule in rules)
        {
            RuleIdFormat.IsValid(rule.RuleId).Should().BeTrue($"规则 ID 必须符合命名契约：{rule.RuleId}");
            rule.RuleId.Should().StartWith("GGD/");
            rule.Name.Should().NotBeNullOrWhiteSpace();
            rule.Source.Should().Be(RuleSource.Builtin);
            rule.Enabled.Should().BeTrue("内置规则默认全部启用");
        }
    }

    [Fact]
    public void LoadEmbedded_ShouldAssignCategoryCountsPerDesign()
    {
        var rules = BuiltinRuleLoader.LoadEmbedded();

        rules.Where(rule => rule.RuleId.Split('/')[1] == "SECRET").Should().HaveCount(21);
        rules.Where(rule => rule.RuleId.Split('/')[1] == "PRIVACY").Should().HaveCount(9);
        rules.Where(rule => rule.RuleId.Split('/')[1] == "LOCALPATH").Should().HaveCount(7);
        rules.Where(rule => rule.RuleId.Split('/')[1] == "URL").Should().HaveCount(8);
        rules.Should().HaveCount(45);
    }

    [Fact]
    public void LoadEmbedded_ContextKeywordsAndValidators_ShouldMatchDesign()
    {
        var rules = BuiltinRuleLoader.LoadEmbedded().ToDictionary(rule => rule.RuleId, StringComparer.Ordinal);

        rules["GGD/SECRET/AWS-SECRET"].Pattern.ContextKeywords.Should().Equal("aws_secret", "secret_access_key");
        rules["GGD/SECRET/AWS-SECRET"].Pattern.Validators.Should().Equal("shannon-entropy");
        rules["GGD/PRIVACY/CN-IDCARD-18"].Pattern.Validators.Should().Equal("cn-id-checksum");
        rules["GGD/PRIVACY/CN-IDCARD-18"].Pattern.ContextKeywords.Should().Contain("身份证");
        rules["GGD/PRIVACY/CN-BANKCARD"].Pattern.Validators.Should().Equal("luhn");
        rules["GGD/PRIVACY/CN-MOBILE"].Pattern.ContextKeywords.Should().Contain("手机");
        rules["GGD/PRIVACY/CN-BANKCARD"].Pattern.ContextKeywords.Should().BeEmpty();
        rules["GGD/SECRET/AWS-AKID"].Pattern.ContextKeywords.Should().BeEmpty();
        rules["GGD/SECRET/AWS-AKID"].Pattern.Validators.Should().BeEmpty();
        rules["GGD/SECRET/AWS-AKID"].Pattern.FilePathGlobs.Should().BeEmpty();
    }

    [Fact]
    public void LoadEmbedded_Regex_ShouldBePrecompiledWithMatchTimeout()
    {
        var rule = BuiltinRuleLoader.LoadEmbedded().Single(r => r.RuleId == "GGD/SECRET/AWS-AKID");

        rule.Pattern.Regex.MatchTimeout.Should().Be(TimeSpan.FromMilliseconds(RulePattern.MATCH_TIMEOUT_MS));
        rule.Pattern.Regex.IsMatch("key = AKIAIOSFODNN7EXAMPLE").Should().BeTrue();
    }

    [Fact]
    public void Load_EnabledOmitted_ShouldDefaultToTrue()
    {
        const string yaml = """
            version: 1
            rules:
              - id: GGD/SECRET/AWS-AKID
                name: AWS Access Key ID
                severity: Critical
                pattern: '\bAKIA[0-9A-Z]{16}\b'
            """;

        var rules = BuiltinRuleLoader.Load(yaml);

        rules.Should().ContainSingle().Which.Enabled.Should().BeTrue();
    }

    [Fact]
    public void Load_ExplicitlyDisabledRule_ShouldBeDisabled()
    {
        const string yaml = """
            version: 1
            rules:
              - id: GGD/SECRET/AWS-AKID
                name: AWS Access Key ID
                severity: Critical
                pattern: '\bAKIA[0-9A-Z]{16}\b'
                enabled: false
            """;

        BuiltinRuleLoader.Load(yaml).Should().ContainSingle().Which.Enabled.Should().BeFalse();
    }

    [Theory]
    [InlineData("2", "版本不受支持")]
    [InlineData("0", "版本不受支持")]
    public void Load_UnsupportedVersion_ShouldThrowDetectionException(string version, string expectedFragment)
    {
        string yaml = $$"""
            version: {{version}}
            rules:
              - id: GGD/SECRET/AWS-AKID
                name: AWS Access Key ID
                severity: Critical
                pattern: '\bAKIA[0-9A-Z]{16}\b'
            """;

        var act = () => BuiltinRuleLoader.Load(yaml);

        act.Should().Throw<DetectionException>()
            .WithMessage($"*{expectedFragment}*")
            .Which.RuleId.Should().BeNull("文档级失败不归属具体规则");
    }

    [Fact]
    public void Load_EmptyRules_ShouldThrowDetectionException()
    {
        const string yaml = "version: 1\nrules: []";

        var act = () => BuiltinRuleLoader.Load(yaml);

        act.Should().Throw<DetectionException>().WithMessage("*规则集为空*");
    }

    [Fact]
    public void Load_DuplicateRuleIds_ShouldThrowDetectionException()
    {
        const string yaml = """
            version: 1
            rules:
              - id: GGD/SECRET/AWS-AKID
                name: AWS Access Key ID
                severity: Critical
                pattern: '\bAKIA[0-9A-Z]{16}\b'
              - id: GGD/SECRET/AWS-AKID
                name: 重复规则
                severity: Low
                pattern: 'AKIA[0-9A-Z]{16}'
            """;

        var act = () => BuiltinRuleLoader.Load(yaml);

        act.Should().Throw<DetectionException>()
            .WithMessage("*重复*GGD/SECRET/AWS-AKID*")
            .Which.RuleId.Should().BeNull("重复检测发生在映射完成后，按文档级错误报告");
    }

    [Theory]
    [InlineData("", "缺少 id")]
    [InlineData("USR/SECRET/AWS-AKID", "必须以 GGD/ 开头")]
    [InlineData("GGD/SECRET/aws-akid", "规则 ID 不符合")]
    public void Load_InvalidRuleId_ShouldThrowDetectionException(string ruleId, string expectedFragment)
    {
        string yaml = $$"""
            version: 1
            rules:
              - id: '{{ruleId}}'
                name: 测试规则
                severity: Critical
                pattern: 'AKIA[0-9A-Z]{16}'
            """;

        var act = () => BuiltinRuleLoader.Load(yaml);

        act.Should().Throw<DetectionException>().WithMessage($"*{expectedFragment}*");
    }

    [Fact]
    public void Load_MissingName_ShouldThrowDetectionException()
    {
        const string yaml = """
            version: 1
            rules:
              - id: GGD/SECRET/AWS-AKID
                severity: Critical
                pattern: 'AKIA[0-9A-Z]{16}'
            """;

        var act = () => BuiltinRuleLoader.Load(yaml);

        act.Should().Throw<DetectionException>().WithMessage("*缺少 name 字段*");
    }

    [Theory]
    [InlineData("Ultra", "severity 非法")]
    [InlineData("4", "severity 非法")]
    public void Load_InvalidSeverity_ShouldThrowDetectionException(string severity, string expectedFragment)
    {
        string yaml = $$"""
            version: 1
            rules:
              - id: GGD/SECRET/AWS-AKID
                name: 测试规则
                severity: '{{severity}}'
                pattern: 'AKIA[0-9A-Z]{16}'
            """;

        var act = () => BuiltinRuleLoader.Load(yaml);

        act.Should().Throw<DetectionException>().WithMessage($"*{expectedFragment}*");
    }

    [Fact]
    public void Load_UncompilableRegex_ShouldThrowDetectionExceptionWithRuleId()
    {
        const string yaml = """
            version: 1
            rules:
              - id: GGD/SECRET/AWS-AKID
                name: 测试规则
                severity: Critical
                pattern: '(未闭合分组'
            """;

        var act = () => BuiltinRuleLoader.Load(yaml);

        act.Should().Throw<DetectionException>()
            .WithMessage("*校验失败*")
            .Which.RuleId.Should().Be("GGD/SECRET/AWS-AKID");
    }

    [Fact]
    public void Load_UnknownField_ShouldThrowDetectionException()
    {
        const string yaml = """
            version: 1
            rules:
              - id: GGD/SECRET/AWS-AKID
                name: 测试规则
                severity: Critical
                pattern: 'AKIA[0-9A-Z]{16}'
                keyword_typo: value
            """;

        var act = () => BuiltinRuleLoader.Load(yaml);

        act.Should().Throw<DetectionException>().WithMessage("*YAML 解析失败*");
    }

    [Fact]
    public void Load_NotYaml_ShouldThrowDetectionException()
    {
        const string yaml = "{ this is: [not, valid";

        var act = () => BuiltinRuleLoader.Load(yaml);

        act.Should().Throw<DetectionException>().WithMessage("*YAML 解析失败*");
    }
}
