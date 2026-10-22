using System.Text.Json;
using FluentAssertions;
using GitGuard.Core;

namespace GitGuard.Cli.Tests;

/// <summary>
/// <c>gitguard scan --all --baseline</c> 端到端测试（FR-061）：基线生成不抑制本次命中、
/// 后续扫描按指纹放行历史噪声、内容变化后重新阻断，以及基线损坏 / 选项组合的退出码契约。
/// </summary>
public sealed class CliScanBaselineTests
{
    [Fact]
    public async Task ScanAll_Baseline_GeneratesThenSuppressesKnownFingerprint()
    {
        using var harness = new CliHarness();
        string canary = CliHarness.CreateCanaryToken();
        harness.WriteFile("config/app.env", $"aws_key={canary}\n");
        harness.Stage("config/app.env");
        harness.Commit("feat: add config");
        string baselinePath = harness.ResolvePath(".gitguard-baseline.json");

        int generateExit = await harness.RunAsync(
            "scan", "--all", "--baseline", baselinePath, "--format", "json");

        generateExit.Should().Be(ExitCodes.BLOCKED);
        using (var generatedDocument = JsonDocument.Parse(harness.Output))
        {
            JsonElement generatedRoot = generatedDocument.RootElement;
            generatedRoot.GetProperty("result").GetString().Should().Be("Blocked");
            JsonElement baseline = generatedRoot.GetProperty("baseline");
            baseline.GetProperty("path").GetString().Should().Be(baselinePath);
            baseline.GetProperty("generated").GetBoolean().Should().BeTrue();
            baseline.GetProperty("entries").GetInt32().Should().Be(1);
            baseline.GetProperty("suppressed_findings").GetInt32().Should().Be(0);
            generatedRoot.GetProperty("findings").EnumerateArray().Single()
                .GetProperty("action").GetString().Should().Be("Block");
        }

        File.Exists(baselinePath).Should().BeTrue();
        string baselineJson = File.ReadAllText(baselinePath);
        baselineJson.Should().Contain("gitguard.baseline.v1");
        baselineJson.Should().NotContain(canary);

        // CliHarness 的输出流在多次 RunAsync 之间累加：只解析第二次运行的 JSON
        int secondOutputOffset = harness.Output.Length;
        int consumeExit = await harness.RunAsync("scan", "--all", "--format", "json");

        consumeExit.Should().Be(ExitCodes.SUCCESS);
        using var consumedDocument = JsonDocument.Parse(harness.Output.Substring(secondOutputOffset));
        JsonElement consumedRoot = consumedDocument.RootElement;
        consumedRoot.GetProperty("result").GetString().Should().Be("Warned");
        JsonElement consumedBaseline = consumedRoot.GetProperty("baseline");
        consumedBaseline.GetProperty("generated").GetBoolean().Should().BeFalse();
        consumedBaseline.GetProperty("entries").GetInt32().Should().Be(1);
        consumedBaseline.GetProperty("suppressed_findings").GetInt32().Should().Be(1);
        consumedRoot.GetProperty("summary").GetProperty("ignored_by_baseline").GetInt32().Should().Be(1);
        consumedRoot.GetProperty("findings").EnumerateArray().Single()
            .GetProperty("action").GetString().Should().Be("AllowedByBaseline");
        harness.Error.Should().NotContain(canary);
    }

    [Fact]
    public async Task ScanAll_Baseline_ChangedSecretBlocksAgain()
    {
        using var harness = new CliHarness();
        string originalCanary = CliHarness.CreateCanaryToken();
        harness.WriteFile("config/app.env", $"aws_key={originalCanary}\n");
        harness.Stage("config/app.env");
        harness.Commit("feat: add config");
        string baselinePath = harness.ResolvePath(".gitguard-baseline.json");

        int generateExit = await harness.RunAsync(
            "scan", "--all", "--baseline", baselinePath, "--format", "json");
        generateExit.Should().Be(ExitCodes.BLOCKED);

        string changedCanary = CliHarness.CreateCanaryToken();
        harness.WriteFile("config/app.env", $"aws_key={changedCanary}\n");
        harness.Stage("config/app.env");
        harness.Commit("feat: rotate key");

        int secondOutputOffset = harness.Output.Length;
        int scanExit = await harness.RunAsync("scan", "--all", "--format", "json");

        scanExit.Should().Be(ExitCodes.BLOCKED);
        using var document = JsonDocument.Parse(harness.Output.Substring(secondOutputOffset));
        JsonElement root = document.RootElement;
        root.GetProperty("result").GetString().Should().Be("Blocked");
        root.GetProperty("baseline").GetProperty("suppressed_findings").GetInt32().Should().Be(0);
        root.GetProperty("findings").EnumerateArray().Single()
            .GetProperty("action").GetString().Should().Be("Block");
    }

    [Fact]
    public async Task ScanAll_MalformedBaseline_ReturnsConfigInvalid()
    {
        using var harness = new CliHarness();
        harness.WriteFile("app.txt", "clean\n");
        harness.Stage("app.txt");
        harness.Commit("init");
        harness.WriteFile(".gitguard-baseline.json", "{ not-json");

        int exitCode = await harness.RunAsync("scan", "--all", "--format", "json");

        exitCode.Should().Be(ExitCodes.CONFIG_INVALID);
        using var document = JsonDocument.Parse(harness.Output);
        document.RootElement.GetProperty("error_code").GetString()
            .Should().Be(BaselineException.BASELINE_INVALID);
    }

    [Fact]
    public async Task ScanAll_TextReport_ShowsBaselineStatus()
    {
        using var harness = new CliHarness();
        string canary = CliHarness.CreateCanaryToken();
        harness.WriteFile("config/app.env", $"aws_key={canary}\n");
        harness.Stage("config/app.env");
        harness.Commit("feat: add config");
        string baselinePath = harness.ResolvePath(".gitguard-baseline.json");

        int generateExit = await harness.RunAsync(
            "scan", "--all", "--baseline", baselinePath);
        generateExit.Should().Be(ExitCodes.BLOCKED);
        harness.Error.Should().Contain("基线");

        int secondOutputOffset = harness.Error.Length;
        int consumeExit = await harness.RunAsync("scan", "--all");

        consumeExit.Should().Be(ExitCodes.SUCCESS);
        harness.Error.Substring(secondOutputOffset).Should().Contain("历史噪声 1 条");
    }

    [Theory]
    [InlineData("scan", "--staged", "--baseline", "baseline.json")]
    [InlineData("scan", "--push", "--baseline", "baseline.json")]
    public async Task Scan_BaselineWithoutAll_ReturnsUsageError(params string[] args)
    {
        using var harness = new CliHarness();

        int exitCode = await harness.RunAsync(args);

        exitCode.Should().Be(ExitCodes.USAGE_ERROR);
        harness.Error.Should().Contain("--baseline");
    }

    [Fact]
    public async Task ScanAll_EmptyBaselinePath_ReturnsUsageError()
    {
        using var harness = new CliHarness();

        int exitCode = await harness.RunAsync("scan", "--all", "--baseline", string.Empty);

        exitCode.Should().Be(ExitCodes.USAGE_ERROR);
        harness.Error.Should().Contain("--baseline");
    }
}
