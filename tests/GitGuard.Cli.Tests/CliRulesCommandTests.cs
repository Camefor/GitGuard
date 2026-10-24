using System.Text.Json;
using FluentAssertions;
using GitGuard.Core;

namespace GitGuard.Cli.Tests;

/// <summary>
/// <c>gitguard rules</c> 端到端测试（<c>06-API接口设计</c> §5.7 / FR-080）：
/// <c>list</c> / <c>view</c> 的文本与 JSON 渲染、类别 / 启用筛选、<c>--show-pattern</c> 门禁、
/// 自定义规则参与扫描，以及退出码 7（类别非法）与 40（规则不存在）。
/// </summary>
public sealed class CliRulesCommandTests
{
    private const string CUSTOM_RULE_ID = "USR/SECRET/INTERNAL-TOKEN";

    private const string CUSTOM_RULE_CONFIG = """
        version: 1
        rules:
          custom:
            - id: USR/SECRET/INTERNAL-TOKEN
              name: 内部令牌
              severity: High
              pattern: "INT-[0-9]{4}"
              context_keywords:
                - token
        """;

    private const string DISABLED_CUSTOM_RULE_CONFIG = """
        version: 1
        rules:
          secrets:
            disabled_rules:
              - USR/SECRET/INTERNAL-TOKEN
          custom:
            - id: USR/SECRET/INTERNAL-TOKEN
              name: 内部令牌
              severity: High
              pattern: "INT-[0-9]{4}"
        """;

    [Fact]
    public async Task List_Json_FiltersByCategoryAndEnabled()
    {
        using var harness = new CliHarness(isRepository: false);

        int exitCode = await harness.RunAsync(
            "rules",
            "list",
            "--category",
            "Secret",
            "--enabled",
            "--format",
            "json");

        exitCode.Should().Be(ExitCodes.SUCCESS, "rules 只读配置与内置规则，不要求当前目录是 Git 仓库");
        using var document = JsonDocument.Parse(harness.Output);
        JsonElement root = document.RootElement;
        root.GetProperty("schema").GetString().Should().Be("gitguard.rules.list.v1");
        root.GetProperty("total").GetInt32().Should().Be(21);
        root.GetProperty("count").GetInt32().Should().Be(21);
        root.GetProperty("category").GetString().Should().Be("SECRET");
        root.GetProperty("enabled_only").GetBoolean().Should().BeTrue();
        JsonElement first = root.GetProperty("rules").EnumerateArray().First();
        first.GetProperty("rule_id").GetString().Should().Be("GGD/SECRET/AWS-AKID");
        first.GetProperty("category").GetString().Should().Be("SECRET");
        first.GetProperty("enabled").GetBoolean().Should().BeTrue();
        first.TryGetProperty("pattern", out _).Should().BeFalse("默认不输出正则原文");
    }

    [Fact]
    public async Task List_WithCustomRule_AppendsUserRuleAndHidesPattern()
    {
        using var harness = new CliHarness(isRepository: false);
        harness.WriteProjectConfig(CUSTOM_RULE_CONFIG);

        int exitCode = await harness.RunAsync("rules", "list", "--category", "Secret", "--format", "json");

        exitCode.Should().Be(ExitCodes.SUCCESS);
        using var document = JsonDocument.Parse(harness.Output);
        JsonElement root = document.RootElement;
        root.GetProperty("total").GetInt32().Should().Be(22);
        JsonElement custom = root.GetProperty("rules").EnumerateArray()
            .Single(rule => rule.GetProperty("rule_id").GetString() == CUSTOM_RULE_ID);
        custom.GetProperty("source").GetString().Should().Be("User");
        custom.GetProperty("enabled").GetBoolean().Should().BeTrue();
        custom.TryGetProperty("pattern", out _).Should().BeFalse();
    }

    [Fact]
    public async Task List_DisabledCustomRule_IsMarkedAndExcludedByEnabledFilter()
    {
        using (var harness = new CliHarness(isRepository: false))
        {
            harness.WriteProjectConfig(DISABLED_CUSTOM_RULE_CONFIG);

            (await harness.RunAsync("rules", "list", "--category", "Secret", "--enabled", "--format", "json"))
                .Should().Be(ExitCodes.SUCCESS);
            using var document = JsonDocument.Parse(harness.Output);
            document.RootElement.GetProperty("rules").EnumerateArray()
                .Should().NotContain(
                    rule => rule.GetProperty("rule_id").GetString() == CUSTOM_RULE_ID,
                    "disabled_rules 命中的自定义规则不是有效启用");
        }

        using (var harness = new CliHarness(isRepository: false))
        {
            harness.WriteProjectConfig(DISABLED_CUSTOM_RULE_CONFIG);

            (await harness.RunAsync("rules", "view", CUSTOM_RULE_ID, "--format", "json"))
                .Should().Be(ExitCodes.SUCCESS);
            using var viewDocument = JsonDocument.Parse(harness.Output);
            viewDocument.RootElement.GetProperty("rule").GetProperty("enabled").GetBoolean().Should().BeFalse();
        }
    }

    [Fact]
    public async Task View_Text_HidesPatternByDefaultAndShowsWithFlag()
    {
        using var harness = new CliHarness(isRepository: false);

        int exitCode = await harness.RunAsync("rules", "view", "GGD/SECRET/AWS-AKID");

        exitCode.Should().Be(ExitCodes.SUCCESS);
        harness.Output.Should().BeEmpty();
        harness.Error.Should()
            .Contain("GitGuard 规则详情")
            .And.Contain("GGD/SECRET/AWS-AKID")
            .And.Contain("Critical")
            .And.Contain("Builtin")
            .And.Contain("启用")
            .And.NotContain("AKIA[0-9A-Z]{16}");

        exitCode = await harness.RunAsync("rules", "view", "GGD/SECRET/AWS-AKID", "--show-pattern");

        exitCode.Should().Be(ExitCodes.SUCCESS);
        harness.Error.Should().Contain(@"\bAKIA[0-9A-Z]{16}\b");
    }

    [Fact]
    public async Task View_Json_IncludesPatternOnlyWhenRequested()
    {
        using (var harness = new CliHarness(isRepository: false))
        {
            harness.WriteProjectConfig(CUSTOM_RULE_CONFIG);

            (await harness.RunAsync("rules", "view", CUSTOM_RULE_ID, "--format", "json"))
                .Should().Be(ExitCodes.SUCCESS);
            using var document = JsonDocument.Parse(harness.Output);
            JsonElement root = document.RootElement;
            root.GetProperty("schema").GetString().Should().Be("gitguard.rules.view.v1");
            JsonElement rule = root.GetProperty("rule");
            rule.GetProperty("rule_id").GetString().Should().Be(CUSTOM_RULE_ID);
            rule.GetProperty("source").GetString().Should().Be("User");
            rule.TryGetProperty("pattern", out _).Should().BeFalse();
        }

        using (var harness = new CliHarness(isRepository: false))
        {
            harness.WriteProjectConfig(CUSTOM_RULE_CONFIG);

            (await harness.RunAsync("rules", "view", CUSTOM_RULE_ID, "--show-pattern", "--format", "json"))
                .Should().Be(ExitCodes.SUCCESS);
            using var patternDocument = JsonDocument.Parse(harness.Output);
            patternDocument.RootElement.GetProperty("rule").GetProperty("pattern").GetString()
                .Should().Be("INT-[0-9]{4}");
        }
    }

    [Fact]
    public async Task View_UnknownRule_ReturnsExitCode40()
    {
        using var harness = new CliHarness(isRepository: false);

        int exitCode = await harness.RunAsync(
            "rules",
            "view",
            "GGD/SECRET/DOES-NOT-EXIST",
            "--format",
            "json");

        exitCode.Should().Be(ExitCodes.RULE_NOT_FOUND);
        using var document = JsonDocument.Parse(harness.Output);
        JsonElement root = document.RootElement;
        root.GetProperty("schema").GetString().Should().Be("gitguard.error.v1");
        root.GetProperty("error_code").GetString().Should().Be(ExitCodeMapper.RULE_NOT_FOUND);
        root.GetProperty("exit_code").GetInt32().Should().Be(ExitCodes.RULE_NOT_FOUND);
        root.GetProperty("message").GetString().Should().Contain("GGD/SECRET/DOES-NOT-EXIST");
    }

    [Fact]
    public async Task List_InvalidCategory_ReturnsUsageError()
    {
        using var harness = new CliHarness(isRepository: false);

        int exitCode = await harness.RunAsync("rules", "list", "--category", "Secrets");

        exitCode.Should().Be(ExitCodes.USAGE_ERROR);
        harness.Error.Should().Contain("--category");
        harness.Output.Should().BeEmpty();
    }

    [Fact]
    public async Task Scan_WithCustomCriticalRule_BlocksStagedChange()
    {
        using var harness = new CliHarness();
        harness.WriteProjectConfig("""
            version: 1
            rules:
              custom:
                - id: USR/SECRET/INTERNAL-TOKEN
                  name: 内部令牌
                  severity: Critical
                  pattern: "INT-[0-9]{4}"
                  context_keywords:
                    - token
            """);
        harness.WriteFile("src/config.cs", "// token = \"INT-1234\"\n");
        harness.Stage("src/config.cs");

        int exitCode = await harness.RunAsync("scan", "--staged", "--format", "json");

        exitCode.Should().Be(ExitCodes.BLOCKED, "自定义 Critical 规则与内置规则走同一策略映射");
        using var document = JsonDocument.Parse(harness.Output);
        JsonElement finding = document.RootElement.GetProperty("findings").EnumerateArray().Single();
        finding.GetProperty("rule_id").GetString().Should().Be(CUSTOM_RULE_ID);
        finding.GetProperty("severity").GetString().Should().Be("Critical");
        document.RootElement.GetProperty("summary").GetProperty("rules_evaluated").GetInt32()
            .Should().Be(61, "内置 60 条 + 自定义 1 条");
    }
}
