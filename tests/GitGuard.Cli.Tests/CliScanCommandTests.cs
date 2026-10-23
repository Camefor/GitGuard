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

    // GGD-0038：scan.pre_commit / scan.pre_push 仅控制 Hook 模式（--hook-mode），手动扫描不受影响
    [Fact]
    public async Task PreCommitHookMode_WithScanDisabled_SkipsAndRecordsNonBypassMarker()
    {
        using var harness = new CliHarness();
        harness.WriteProjectConfig("""
            version: 1
            scan:
              pre_commit: false
            """);
        string canary = CliHarness.CreateCanaryToken();
        harness.WriteFile("config/app.env", $"aws_key={canary}\n");
        harness.Stage("config/app.env");

        int exitCode = await harness.RunAsync(
            "scan", "--staged", "--record-pending", "--hook-mode", "pre-commit");

        exitCode.Should().Be(ExitCodes.SUCCESS);
        harness.Error.Should().Contain("scan.pre_commit=false");
        harness.Error.Should().NotContain(canary);
        File.Exists(harness.PendingScanPath).Should().BeTrue("配置关闭扫描仍需记录待提交预测");
        using var marker = JsonDocument.Parse(File.ReadAllText(harness.PendingScanPath));
        marker.RootElement.GetProperty("skipped").GetBoolean()
            .Should().BeFalse("配置关闭扫描不是绕过，避免 post-commit 误登记 BypassDetected");
    }

    [Fact]
    public async Task PrePushHookMode_WithScanDisabled_SkipsWithoutScanning()
    {
        using var harness = new CliHarness();
        harness.WriteProjectConfig("""
            version: 1
            scan:
              pre_push: false
            """);
        string canary = CliHarness.CreateCanaryToken();
        harness.WriteFile("config/app.env", $"aws_key={canary}\n");
        harness.Stage("config/app.env");
        string sha = harness.Commit("feat: add config");
        string pushLine = $"refs/heads/master {sha} refs/heads/master {ZERO_SHA}";

        int exitCode = await harness.RunAsync(
            ["scan", "--push", "--hook-mode", "pre-push"], pushLine);

        exitCode.Should().Be(ExitCodes.SUCCESS);
        harness.Error.Should().Contain("scan.pre_push=false");
        harness.Error.Should().NotContain(canary);
    }

    [Fact]
    public async Task ManualStagedScan_WithScanDisabled_StillScans()
    {
        using var harness = new CliHarness();
        harness.WriteProjectConfig("""
            version: 1
            scan:
              pre_commit: false
            """);
        string canary = CliHarness.CreateCanaryToken();
        harness.WriteFile("config/app.env", $"aws_key={canary}\n");
        harness.Stage("config/app.env");

        int exitCode = await harness.RunAsync("scan", "--staged");

        exitCode.Should().Be(ExitCodes.BLOCKED, "scan.pre_commit 只控制 Hook 模式，手动扫描始终执行");
        harness.Error.Should().Contain("GGD/SECRET/AWS-AKID");
    }

    [Theory]
    [InlineData("--push", "pre-commit")]
    [InlineData("--staged", "pre-push")]
    public async Task HookMode_MismatchedScope_ReturnsUsageError(string scope, string hookMode)
    {
        using var harness = new CliHarness();
        harness.WriteFile("notes.txt", "clean\n");
        harness.Stage("notes.txt");

        int exitCode = await harness.RunAsync("scan", scope, "--hook-mode", hookMode);

        exitCode.Should().Be(ExitCodes.USAGE_ERROR);
    }

    // GGD-0039：rules.url.public_hostname_whitelist 按主机抑制 URL 规则命中
    [Fact]
    public async Task UrlTokenInQueryOnWhitelistedHost_IsClean()
    {
        using var harness = new CliHarness();
        harness.WriteFile("app/settings.txt", "endpoint = \"https://docs.example.com/callback?token=abc123\"\n");
        harness.Stage("app/settings.txt");
        harness.WriteProjectConfig("""
            version: 1
            rules:
              url:
                public_hostname_whitelist:
                  - docs.example.com
            """);

        int exitCode = await harness.RunAsync("scan", "--staged", "--format", "json");

        exitCode.Should().Be(ExitCodes.SUCCESS);
        using var document = JsonDocument.Parse(harness.Output);
        JsonElement root = document.RootElement;
        root.GetProperty("result").GetString().Should().Be("Clean");
        root.GetProperty("findings").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task UrlTokenInQueryOnNonWhitelistedHost_Blocks()
    {
        using var harness = new CliHarness();
        harness.WriteFile("app/settings.txt", "endpoint = \"https://internal.example.net/callback?token=abc123\"\n");
        harness.Stage("app/settings.txt");
        harness.WriteProjectConfig("""
            version: 1
            rules:
              url:
                public_hostname_whitelist:
                  - docs.example.com
            """);

        int exitCode = await harness.RunAsync("scan", "--staged");

        exitCode.Should().Be(ExitCodes.BLOCKED);
        harness.Error.Should().Contain("GGD/URL/TOKEN-IN-QUERY");
    }

    // GGD-0040：Markdown 围栏代码块内的 LocalPath 命中不产出 Finding
    [Fact]
    public async Task MarkdownFencedLocalPath_IsCleanEvenWhenLowSeverityBlocks()
    {
        using var harness = new CliHarness();
        const string content = "```bash\n"
            + "cd /home/alice/projects/app\n"
            + "```\n";
        harness.WriteFile("notes.markdown", content);
        harness.Stage("notes.markdown");
        harness.WriteProjectConfig("""
            version: 1
            policy:
              low: block
            """);

        int exitCode = await harness.RunAsync("scan", "--staged", "--format", "json");

        exitCode.Should().Be(ExitCodes.SUCCESS);
        using var document = JsonDocument.Parse(harness.Output);
        JsonElement root = document.RootElement;
        root.GetProperty("result").GetString().Should().Be("Clean");
        root.GetProperty("findings").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task MarkdownPathOutsideFence_StillBlocks()
    {
        using var harness = new CliHarness();
        harness.WriteFile("notes.md", "cd /home/bob/projects/app\n");
        harness.Stage("notes.md");
        harness.WriteProjectConfig("""
            version: 1
            policy:
              low: block
            """);

        int exitCode = await harness.RunAsync("scan", "--staged");

        exitCode.Should().Be(ExitCodes.BLOCKED);
        harness.Error.Should().Contain("GGD/LOCALPATH/UNIX-HOME");
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
