using System.Text.Json;
using FluentAssertions;

namespace GitGuard.Cli.Tests;

/// <summary>
/// CLI 输出语言（SRS <c>NFR-U-002</c>）测试：默认中文；<c>--language</c> 优先于 <c>GITGUARD_LANG</c>；
/// 非法取值回退到环境变量 / 中文；JSON 契约与日志字段名不随语言变化。
/// </summary>
public sealed class CliLocalizationTests
{
    private static readonly string[] VERSION_ARGS = new[] { "version" };
    private static readonly string[] VERSION_CHINESE_ARGS = new[] { "version", "--language", "zh-CN" };
    private static readonly string[] VERSION_INVALID_LANGUAGE_ARGS = new[] { "version", "--language", "fr-FR" };

    [Fact]
    public async Task Default_OutputsChinese()
    {
        using var harness = new CliHarness();

        int exitCode = await harness.RunAsync("version");

        exitCode.Should().Be(ExitCodes.SUCCESS);
        harness.Error.Should().Contain("规则库版本").And.Contain("本机 git").And.NotContain("Rules version");
    }

    [Fact]
    public async Task LanguageOption_BeforeSubcommand_SwitchesToEnglish()
    {
        using var harness = new CliHarness();

        int exitCode = await harness.RunAsync("--language", "en-US", "version");

        exitCode.Should().Be(ExitCodes.SUCCESS);
        harness.Error.Should().Contain("Rules version").And.Contain("Local git").And.Contain("Runtime");
        harness.Error.Should().NotContain("规则库版本");
    }

    [Fact]
    public async Task LanguageOption_AfterSubcommand_SwitchesToEnglish()
    {
        using var harness = new CliHarness();

        int exitCode = await harness.RunAsync("version", "--language", "en-US");

        exitCode.Should().Be(ExitCodes.SUCCESS);
        harness.Error.Should().Contain("Rules version");
    }

    [Fact]
    public async Task LanguageOption_EqualsForm_IsHonored()
    {
        using var harness = new CliHarness();

        int exitCode = await harness.RunAsync("version", "--language=en-US");

        exitCode.Should().Be(ExitCodes.SUCCESS);
        harness.Error.Should().Contain("Rules version");
    }

    [Fact]
    public async Task LanguageEnvironment_SwitchesToEnglish()
    {
        using var harness = new CliHarness();

        int exitCode = await harness.RunAsync(VERSION_ARGS, string.Empty, languageEnvironment: "en");

        exitCode.Should().Be(ExitCodes.SUCCESS);
        harness.Error.Should().Contain("Rules version");
    }

    [Fact]
    public async Task LanguageOption_OverridesEnvironment()
    {
        using var harness = new CliHarness();

        int exitCode = await harness.RunAsync(
            VERSION_CHINESE_ARGS,
            string.Empty,
            languageEnvironment: "en-US");

        exitCode.Should().Be(ExitCodes.SUCCESS);
        harness.Error.Should().Contain("规则库版本").And.NotContain("Rules version");
    }

    [Fact]
    public async Task InvalidLanguage_ReturnsUsageErrorInDefaultChinese()
    {
        using var harness = new CliHarness();

        int exitCode = await harness.RunAsync("version", "--language", "fr-FR");

        exitCode.Should().Be(ExitCodes.USAGE_ERROR);
        harness.Error.Should().Contain("用法错误").And.NotContain("Usage error");
    }

    [Fact]
    public async Task InvalidLanguage_WithEnglishEnvironment_UsesEnglishFallback()
    {
        using var harness = new CliHarness();

        int exitCode = await harness.RunAsync(
            VERSION_INVALID_LANGUAGE_ARGS,
            string.Empty,
            languageEnvironment: "en");

        exitCode.Should().Be(ExitCodes.USAGE_ERROR);
        harness.Error.Should().Contain("Usage error").And.NotContain("用法错误");
    }

    [Fact]
    public async Task EnglishUsageError_TranslatesCommandMessagesAndHint()
    {
        using var harness = new CliHarness();

        int exitCode = await harness.RunAsync("--language", "en-US", "scan");

        exitCode.Should().Be(ExitCodes.USAGE_ERROR);
        harness.Error.Should()
            .Contain("Usage error")
            .And.Contain("scan requires exactly one of --staged or --push")
            .And.Contain("Run gitguard --help for usage.");
    }

    [Fact]
    public async Task HelpText_FollowsLanguage()
    {
        using var chinese = new CliHarness();
        int chineseExitCode = await chinese.RunAsync("--help");

        chineseExitCode.Should().Be(ExitCodes.SUCCESS);
        chinese.Output.Should().Contain("扫描 Git 变更内容").And.NotContain("Scan Git changes");

        using var english = new CliHarness();
        int englishExitCode = await english.RunAsync("--language", "en-US", "--help");

        englishExitCode.Should().Be(ExitCodes.SUCCESS);
        english.Output.Should().Contain("Scan Git changes").And.Contain("Output language: zh-CN (default) or en-US");
        english.Output.Should().NotContain("扫描 Git 变更内容");
    }

    [Fact]
    public async Task JsonOutput_RemainsMachineReadableUnderEnglish()
    {
        using var harness = new CliHarness();

        int exitCode = await harness.RunAsync("version", "--format", "json", "--language", "en-US");

        exitCode.Should().Be(ExitCodes.SUCCESS);
        harness.Error.Should().BeEmpty();

        using var document = JsonDocument.Parse(harness.Output);
        JsonElement root = document.RootElement;
        root.GetProperty("schema").GetString().Should().Be("gitguard.version.v1");
        root.GetProperty("gitguard_version").GetString().Should().Be(CliHarness.TEST_VERSION);
        root.GetProperty("runtime").GetProperty("runtime_identifier").GetString().Should().NotBeNullOrWhiteSpace();
    }
}
