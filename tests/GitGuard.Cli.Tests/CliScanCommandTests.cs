using System.Text.Json;
using FluentAssertions;
using GitGuard.Core;

namespace GitGuard.Cli.Tests;

/// <summary>
/// <c>gitguard scan</c> 端到端测试（TC-FR-020-01~02 / TC-FR-030 / TC-DR-03-04，<c>06-API接口设计</c> §5.4）：
/// 真实 git 仓库 + 真实检测器 + 真实 SQLite 审计库，覆盖退出码 0 / 1 / 2 / 13 与 <c>gitguard.scan.v1</c> JSON 契约。
/// </summary>
public sealed class CliScanCommandTests
{
    private const string ZERO_SHA = "0000000000000000000000000000000000000000";

    // TC-FR-020-01：暂存区含金丝雀 Token → 退出码 1，报告命中且片段脱敏
    [Fact]
    public async Task StagedCanary_BlocksWithExitCodeOneAndMasksSnippet()
    {
        using var harness = new CliHarness();
        string canary = CliHarness.CreateCanaryToken();
        harness.WriteFile("config/app.env", $"aws_key={canary}\n");
        harness.Stage("config/app.env");

        int exitCode = await harness.RunAsync("scan", "--staged");

        exitCode.Should().Be(ExitCodes.BLOCKED);
        harness.Output.Should().BeEmpty();
        harness.Error.Should().Contain("扫描被阻断").And.Contain("GGD/SECRET/AWS-AKID");
        harness.Error.Should().NotContain(canary, "报告与日志不得回显密钥原文");
    }

    // FR-090 / ADR-0003：use_gitleaks 可执行文件缺失时降级为警告，扫描继续并保持退出码 0
    [Fact]
    public async Task UseGitleaksWithMissingExecutable_ScanSucceedsWithWarning()
    {
        using var harness = new CliHarness();
        harness.WriteFile("notes.txt", "clean\n");
        harness.Stage("notes.txt");
        string missing = Path.Combine(harness.Root, "missing", "gitleaks.exe").Replace('\\', '/');
        harness.WriteProjectConfig($"""
            version: 1
            rules:
              secrets:
                use_gitleaks: true
                gitleaks_path: "{missing}"
            """);

        int exitCode = await harness.RunAsync("scan", "--staged");

        exitCode.Should().Be(ExitCodes.SUCCESS);
        harness.Error.Should().Contain("Gitleaks 适配器已禁用");
    }

    // TC-FR-020-02：未暂存文件含密钥、暂存区干净 → 退出码 0（DR-04）
    [Fact]
    public async Task UnstagedSecret_IsNotScanned()
    {
        using var harness = new CliHarness();
        harness.WriteFile("notes.txt", "clean\n");
        harness.Stage("notes.txt");
        harness.WriteFile("local.env", $"aws_key={CliHarness.CreateCanaryToken()}\n");

        int exitCode = await harness.RunAsync("scan", "--staged");

        exitCode.Should().Be(ExitCodes.SUCCESS);
        harness.Error.Should().Contain("扫描完成");
    }

    [Fact]
    public async Task StagedCanaryInExcludedPath_IsNotScanned()
    {
        using var harness = new CliHarness();
        string canary = CliHarness.CreateCanaryToken();
        harness.WriteFile("vendor/lib/app.env", $"aws_key={canary}\n");
        harness.Stage("vendor/lib/app.env");
        harness.WriteProjectConfig("""
            version: 1
            exclude:
              paths:
                - "**/vendor/**"
            """);

        int exitCode = await harness.RunAsync("scan", "--staged", "--format", "json");

        exitCode.Should().Be(ExitCodes.SUCCESS);
        using var document = JsonDocument.Parse(harness.Output);
        JsonElement root = document.RootElement;
        root.GetProperty("result").GetString().Should().Be("Clean");
        root.GetProperty("summary").GetProperty("files_scanned").GetInt32().Should().Be(0);
        root.GetProperty("findings").GetArrayLength().Should().Be(0);
        harness.Error.Should().NotContain(canary);
    }

    [Fact]
    public async Task StagedCanaryInExcludedFile_IsNotScanned()
    {
        using var harness = new CliHarness();
        string canary = CliHarness.CreateCanaryToken();
        harness.WriteFile("dist/assets/bundle.min.js", $"aws_key={canary}\n");
        harness.Stage("dist/assets/bundle.min.js");
        harness.WriteProjectConfig("""
            version: 1
            exclude:
              files:
                - "*.min.js"
            """);

        int exitCode = await harness.RunAsync("scan", "--staged", "--format", "json");

        exitCode.Should().Be(ExitCodes.SUCCESS);
        using var document = JsonDocument.Parse(harness.Output);
        JsonElement root = document.RootElement;
        root.GetProperty("result").GetString().Should().Be("Clean");
        root.GetProperty("summary").GetProperty("files_scanned").GetInt32().Should().Be(0);
        root.GetProperty("findings").GetArrayLength().Should().Be(0);
        harness.Error.Should().NotContain(canary);
    }

    [Fact]
    public async Task StagedScan_JsonOutput_MatchesScanSchema()
    {
        using var harness = new CliHarness();
        string canary = CliHarness.CreateCanaryToken();
        harness.WriteFile("config/app.env", $"aws_key={canary}\n");
        harness.Stage("config/app.env");

        int exitCode = await harness.RunAsync("scan", "--staged", "--format", "json");

        exitCode.Should().Be(ExitCodes.BLOCKED);
        using var document = JsonDocument.Parse(harness.Output);
        JsonElement root = document.RootElement;
        root.GetProperty("schema").GetString().Should().Be("gitguard.scan.v1");
        root.GetProperty("scan_type").GetString().Should().Be("PreCommit");
        root.GetProperty("result").GetString().Should().Be("Blocked");
        root.GetProperty("exit_code").GetInt32().Should().Be(ExitCodes.BLOCKED);
        root.GetProperty("summary").GetProperty("files_scanned").GetInt32().Should().Be(1);

        JsonElement finding = root.GetProperty("findings").EnumerateArray().Single();
        finding.GetProperty("rule_id").GetString().Should().Be("GGD/SECRET/AWS-AKID");
        finding.GetProperty("file_path").GetString().Should().Be("config/app.env");
        finding.GetProperty("line_number").GetInt32().Should().Be(1);
        finding.GetProperty("action").GetString().Should().Be("Block");
        finding.GetProperty("commit_sha").ValueKind.Should().Be(JsonValueKind.Null);
        finding.GetProperty("snippet_masked").GetString().Should().NotContain(canary);
        finding.GetProperty("fingerprint").GetString().Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task NonRepositoryDirectory_ReturnsNotAGitRepository()
    {
        using var harness = new CliHarness(isRepository: false);

        int exitCode = await harness.RunAsync("scan", "--staged");

        exitCode.Should().Be(ExitCodes.NOT_A_GIT_REPOSITORY);
        harness.Error.Should().Contain("错误码").And.Contain(GitException.NOT_A_GIT_REPOSITORY);
    }

    // TC-DR-03-04：坏配置 → FailClosed 退出码 13
    [Fact]
    public async Task UnsupportedConfigVersion_ReturnsConfigInvalid()
    {
        using var harness = new CliHarness();
        harness.WriteProjectConfig("version: 99\n");

        int exitCode = await harness.RunAsync("scan", "--staged");

        exitCode.Should().Be(ExitCodes.CONFIG_INVALID);
        harness.Error.Should().Contain(ConfigurationException.CONFIG_INVALID);
    }

    [Fact]
    public async Task PushScan_ReadsStdinAndReportsCommitSha()
    {
        using var harness = new CliHarness();
        string canary = CliHarness.CreateCanaryToken();
        harness.WriteFile("config/app.env", $"aws_key={canary}\n");
        harness.Stage("config/app.env");
        string sha = harness.Commit("feat: add config");
        string pushLine = $"refs/heads/master {sha} refs/heads/master {ZERO_SHA}";

        int exitCode = await harness.RunAsync(["scan", "--push", "--format", "json"], pushLine);

        exitCode.Should().Be(ExitCodes.BLOCKED);
        using var document = JsonDocument.Parse(harness.Output);
        JsonElement root = document.RootElement;
        root.GetProperty("scan_type").GetString().Should().Be("PrePush");
        root.GetProperty("result").GetString().Should().Be("Blocked");
        root.GetProperty("findings").EnumerateArray().Single()
            .GetProperty("commit_sha").GetString().Should().Be(sha);
    }
}
