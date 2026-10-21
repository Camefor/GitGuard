using FluentAssertions;
using GitGuard.Core;

namespace GitGuard.Cli.Tests;

/// <summary>
/// <c>--verbose</c> 规则耗时摘要测试（NFR-M-002）：验证 Debug 级别逐规则输出
/// <c>Scan.RuleTiming</c> 日志（含 <c>duration_us</c>），默认级别不输出。
/// </summary>
public sealed class CliRuleTimingTests
{
    [Fact]
    public async Task VerboseStagedScan_EmitsPerRuleTimingAtDebug()
    {
        using var harness = new CliHarness();
        harness.WriteFile("notes.txt", "clean\n");
        harness.Stage("notes.txt");

        int exitCode = await harness.RunAsync("scan", "--staged", "--verbose");

        exitCode.Should().Be(ExitCodes.SUCCESS);
        harness.Error.Should().Contain("[debug] Scan.RuleTiming:")
            .And.Contain("GGD/SECRET/AWS-AKID")
            .And.Contain("duration_us=")
            .And.Contain("rule_id=GGD/SECRET/AWS-AKID");
    }

    [Fact]
    public async Task DefaultStagedScan_DoesNotEmitRuleTiming()
    {
        using var harness = new CliHarness();
        harness.WriteFile("notes.txt", "clean\n");
        harness.Stage("notes.txt");

        int exitCode = await harness.RunAsync("scan", "--staged");

        exitCode.Should().Be(ExitCodes.SUCCESS);
        harness.Error.Should().NotContain("Scan.RuleTiming").And.NotContain("duration_us=");
    }

    [Fact]
    public async Task VerboseStagedScan_English_EmitsLocalizedTimingMessage()
    {
        using var harness = new CliHarness();
        harness.WriteFile("notes.txt", "clean\n");
        harness.Stage("notes.txt");

        int exitCode = await harness.RunAsync(
            ["scan", "--staged", "--verbose"],
            string.Empty,
            languageEnvironment: "en-US");

        exitCode.Should().Be(ExitCodes.SUCCESS);
        harness.Error.Should().Contain("Rule timing:").And.Contain("µs total");
    }
}
