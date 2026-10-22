using System.Text.Json;
using FluentAssertions;
using GitGuard.Core;

namespace GitGuard.Cli.Tests;

/// <summary>
/// <c>gitguard scan --all</c> 端到端测试（TC-FR-060-01~04）：HEAD 全量树扫描、
/// 历史提交扫描、<c>--max-files</c> 截断与 <c>--since</c> 续扫，以及选项组合校验。
/// </summary>
public sealed class CliScanAllTests
{
    // TC-FR-060-01：已提交文件由 HEAD 全量树扫描覆盖，commit_sha 为 null
    [Fact]
    public async Task ScanAll_CommittedCanary_BlocksWithHeadTreeFinding()
    {
        using var harness = new CliHarness();
        string canary = CliHarness.CreateCanaryToken();
        harness.WriteFile("config/app.env", $"aws_key={canary}\n");
        harness.Stage("config/app.env");
        harness.Commit("feat: add config");

        int exitCode = await harness.RunAsync("scan", "--all", "--format", "json");

        exitCode.Should().Be(ExitCodes.BLOCKED);
        using var document = JsonDocument.Parse(harness.Output);
        JsonElement root = document.RootElement;
        root.GetProperty("scan_type").GetString().Should().Be("Manual");
        root.GetProperty("result").GetString().Should().Be("Blocked");

        JsonElement fullScan = root.GetProperty("full_scan");
        fullScan.GetProperty("include_history").GetBoolean().Should().BeFalse();
        fullScan.GetProperty("commits_scanned").GetInt32().Should().Be(0);
        fullScan.GetProperty("remaining_commits").GetInt32().Should().Be(0);
        fullScan.GetProperty("truncated").GetBoolean().Should().BeFalse();
        fullScan.GetProperty("next_since_sha").ValueKind.Should().Be(JsonValueKind.Null);

        root.GetProperty("findings").EnumerateArray().Single()
            .GetProperty("commit_sha").ValueKind.Should().Be(JsonValueKind.Null);
        harness.Error.Should().NotContain(canary);
    }

    // TC-FR-060-02：密钥仅在历史提交中出现（HEAD 已删除）→ 历史模式必须发现并阻断
    [Fact]
    public async Task ScanAll_IncludeHistory_FindsSecretRemovedBeforeHead()
    {
        using var harness = new CliHarness();
        string canary = CliHarness.CreateCanaryToken();
        harness.WriteFile("old.env", $"aws_key={canary}\n");
        harness.Stage("old.env");
        string secretCommit = harness.Commit("feat: add then remove secret");
        File.Delete(harness.ResolvePath("old.env"));
        harness.Stage("old.env");
        harness.Commit("chore: remove secret");

        int exitCode = await harness.RunAsync("scan", "--all", "--include-history", "--format", "json");

        exitCode.Should().Be(ExitCodes.BLOCKED);
        using var document = JsonDocument.Parse(harness.Output);
        JsonElement root = document.RootElement;
        JsonElement fullScan = root.GetProperty("full_scan");
        fullScan.GetProperty("include_history").GetBoolean().Should().BeTrue();
        fullScan.GetProperty("commits_scanned").GetInt32().Should().BeGreaterThanOrEqualTo(1);
        fullScan.GetProperty("truncated").GetBoolean().Should().BeFalse();
        fullScan.GetProperty("next_since_sha").ValueKind.Should().Be(JsonValueKind.Null);

        root.GetProperty("findings").EnumerateArray().Single()
            .GetProperty("commit_sha").GetString().Should().Be(secretCommit);
        harness.Error.Should().NotContain(canary);
    }

    // TC-FR-060-03：--max-files 在提交边界截断，并通过 next_since_sha / --since 续扫
    [Fact]
    public async Task ScanAll_MaxFiles_TruncatesAndSinceResumesOlderHistory()
    {
        using var harness = new CliHarness();
        string canary = CliHarness.CreateCanaryToken();
        harness.WriteFile("old.env", $"aws_key={canary}\n");
        harness.Stage("old.env");
        string secretCommit = harness.Commit("feat: add secret");
        File.Delete(harness.ResolvePath("old.env"));
        harness.Stage("old.env");
        string deleteCommit = harness.Commit("chore: remove secret");
        harness.WriteFile("notes.txt", "clean\n");
        harness.Stage("notes.txt");
        harness.Commit("docs: add notes");

        int firstExit = await harness.RunAsync(
            "scan", "--all", "--include-history", "--max-files", "1", "--format", "json");

        firstExit.Should().Be(ExitCodes.SUCCESS);
        using var firstDocument = JsonDocument.Parse(harness.Output);
        JsonElement firstRoot = firstDocument.RootElement;
        firstRoot.GetProperty("result").GetString().Should().Be("Clean");
        JsonElement firstFullScan = firstRoot.GetProperty("full_scan");
        firstFullScan.GetProperty("truncated").GetBoolean().Should().BeTrue();
        firstFullScan.GetProperty("commits_scanned").GetInt32().Should().Be(1);
        firstFullScan.GetProperty("remaining_commits").GetInt32().Should().Be(1);
        string nextSinceSha = firstFullScan.GetProperty("next_since_sha").GetString()
            ?? throw new InvalidOperationException("截断时必须提供 next_since_sha。");
        nextSinceSha.Should().Be(deleteCommit);

        // CliHarness 的输出流在多次 RunAsync 之间累加：只解析第二次运行的 JSON
        int secondOutputOffset = harness.Output.Length;
        int secondExit = await harness.RunAsync("scan", "--all", "--since", nextSinceSha, "--format", "json");

        secondExit.Should().Be(ExitCodes.BLOCKED);
        using var secondDocument = JsonDocument.Parse(harness.Output.Substring(secondOutputOffset));
        JsonElement secondRoot = secondDocument.RootElement;
        JsonElement secondFullScan = secondRoot.GetProperty("full_scan");
        secondFullScan.GetProperty("include_history").GetBoolean().Should().BeTrue();
        secondFullScan.GetProperty("commits_scanned").GetInt32().Should().Be(1);
        secondFullScan.GetProperty("remaining_commits").GetInt32().Should().Be(0);
        secondFullScan.GetProperty("truncated").GetBoolean().Should().BeFalse();
        secondRoot.GetProperty("findings").EnumerateArray().Single()
            .GetProperty("commit_sha").GetString().Should().Be(secretCommit);
    }

    [Fact]
    public async Task ScanAll_CleanRepository_ReportsHistoryDisabledInText()
    {
        using var harness = new CliHarness();
        harness.WriteFile("app.txt", "clean\n");
        harness.Stage("app.txt");
        harness.Commit("init");

        int exitCode = await harness.RunAsync("scan", "--all");

        exitCode.Should().Be(ExitCodes.SUCCESS);
        harness.Error.Should().Contain("扫描完成");
        harness.Error.Should().Contain("历史提交");
        harness.Error.Should().Contain("未扫描");
    }

    [Fact]
    public async Task ScanAll_EmptyRepository_ReturnsClean()
    {
        using var harness = new CliHarness();

        int exitCode = await harness.RunAsync("scan", "--all", "--format", "json");

        exitCode.Should().Be(ExitCodes.SUCCESS);
        using var document = JsonDocument.Parse(harness.Output);
        JsonElement root = document.RootElement;
        root.GetProperty("result").GetString().Should().Be("Clean");
        root.GetProperty("summary").GetProperty("files_scanned").GetInt32().Should().Be(0);
        root.GetProperty("full_scan").GetProperty("include_history").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task StagedScan_JsonDoesNotContainFullScanSection()
    {
        using var harness = new CliHarness();
        harness.WriteFile("app.txt", "clean\n");
        harness.Stage("app.txt");

        int exitCode = await harness.RunAsync("scan", "--staged", "--format", "json");

        exitCode.Should().Be(ExitCodes.SUCCESS);
        using var document = JsonDocument.Parse(harness.Output);
        document.RootElement.TryGetProperty("full_scan", out _).Should().BeFalse();
    }

    [Theory]
    [InlineData("scan", "--all", "--staged")]
    [InlineData("scan", "--all", "--push")]
    [InlineData("scan", "--staged", "--include-history")]
    [InlineData("scan", "--staged", "--since", "HEAD")]
    [InlineData("scan", "--all", "--max-files", "0")]
    [InlineData("scan", "--all", "--since=-abc")]
    public async Task ScanAll_InvalidOptionCombination_ReturnsUsageError(params string[] args)
    {
        using var harness = new CliHarness();

        int exitCode = await harness.RunAsync(args);

        exitCode.Should().Be(ExitCodes.USAGE_ERROR);
        harness.Error.Should().Contain("用法错误");
    }
}
