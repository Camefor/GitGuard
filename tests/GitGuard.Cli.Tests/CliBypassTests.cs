using System.Text.Json;
using FluentAssertions;

namespace GitGuard.Cli.Tests;

/// <summary>
/// 绕过检测 CLI 链路测试（TC-FR-050-01 / TC-DR-06-01，SRS FR-050 / DR-06）：
/// pre-commit 隐藏选项 <c>--record-pending</c> 的预测写入 / 清理、<c>GITGUARD_SKIP=1</c> 的 Hook 模式语义，
/// 以及隐藏命令 <c>hook post-commit</c> 的 <c>gitguard.bypass.v1</c> 输出契约。
/// </summary>
public sealed class CliBypassTests
{
    [Fact]
    public async Task RecordPending_UnbornHead_WritesMarkerWithNullParent()
    {
        using var harness = new CliHarness();
        harness.WriteFile("README.md", "# Sample\n");
        harness.Stage("README.md");

        int exitCode = await harness.RunAsync("scan", "--staged", "--record-pending");

        exitCode.Should().Be(ExitCodes.SUCCESS);
        using var marker = ReadMarker(harness);
        marker.RootElement.GetProperty("parent_sha").ValueKind.Should().Be(JsonValueKind.Null);
        marker.RootElement.GetProperty("tree_sha").GetString().Should().Be(harness.RunGit("write-tree"));
        marker.RootElement.GetProperty("skipped").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task RecordPending_AfterBaselineCommit_RecordsHeadAsParent()
    {
        using var harness = new CliHarness();
        harness.WriteFile("README.md", "# Sample\n");
        harness.Stage("README.md");
        string head = harness.Commit("baseline");
        harness.WriteFile("src/app.txt", "hello\n");
        harness.Stage("src/app.txt");

        int exitCode = await harness.RunAsync("scan", "--staged", "--record-pending");

        exitCode.Should().Be(ExitCodes.SUCCESS);
        using var marker = ReadMarker(harness);
        marker.RootElement.GetProperty("parent_sha").GetString().Should().Be(head);
        marker.RootElement.GetProperty("tree_sha").GetString().Should().Be(harness.RunGit("write-tree"));
    }

    [Fact]
    public async Task ManualStagedScan_DoesNotWriteMarker()
    {
        using var harness = new CliHarness();
        harness.WriteFile("README.md", "# Sample\n");
        harness.Stage("README.md");

        int exitCode = await harness.RunAsync("scan", "--staged");

        exitCode.Should().Be(ExitCodes.SUCCESS);
        File.Exists(harness.PendingScanPath).Should().BeFalse("手动扫描不属于 Hook 链路，不得留下待提交标记");
    }

    [Fact]
    public async Task RecordPending_BlockedScan_ClearsPreviousMarker()
    {
        using var harness = new CliHarness();
        harness.WriteFile("README.md", "# Sample\n");
        harness.Stage("README.md");
        (await harness.RunAsync("scan", "--staged", "--record-pending")).Should().Be(ExitCodes.SUCCESS);
        File.Exists(harness.PendingScanPath).Should().BeTrue();

        harness.WriteFile("config/leak.env", $"AWS_ACCESS_KEY_ID={CliHarness.CreateCanaryToken()}\n");
        harness.Stage("config/leak.env");
        int exitCode = await harness.RunAsync("scan", "--staged", "--record-pending");

        exitCode.Should().Be(ExitCodes.BLOCKED);
        File.Exists(harness.PendingScanPath).Should().BeFalse("扫描未通过时必须清理旧标记，避免掩盖后续真实绕过");
    }

    [Fact]
    public async Task RecordPending_WithGitGuardSkip_WritesSkippedMarkerWithoutAudit()
    {
        using var harness = new CliHarness();
        harness.WriteFile("config/leak.env", $"AWS_ACCESS_KEY_ID={CliHarness.CreateCanaryToken()}\n");
        harness.Stage("config/leak.env");

        int exitCode = await harness.RunAsync(["scan", "--staged", "--record-pending"], string.Empty, skipEnvironment: "1");

        exitCode.Should().Be(ExitCodes.SUCCESS, "GITGUARD_SKIP=1 时不得执行扫描");
        harness.Error.Should().Contain("GITGUARD_SKIP=1");
        using var marker = ReadMarker(harness);
        marker.RootElement.GetProperty("skipped").GetBoolean().Should().BeTrue();
        File.Exists(Path.Combine(harness.DataDirectory, "audit.db")).Should().BeFalse("跳过扫描不得写入扫描审计");
    }

    [Fact]
    public async Task GitGuardSkipEnvironment_ManualScan_IsIgnored()
    {
        using var harness = new CliHarness();
        harness.WriteFile("config/leak.env", $"AWS_ACCESS_KEY_ID={CliHarness.CreateCanaryToken()}\n");
        harness.Stage("config/leak.env");

        int exitCode = await harness.RunAsync(["scan", "--staged"], string.Empty, skipEnvironment: "1");

        exitCode.Should().Be(ExitCodes.BLOCKED, "GITGUARD_SKIP 仅在 Hook 模式（--record-pending）生效");
        File.Exists(harness.PendingScanPath).Should().BeFalse();
    }

    [Fact]
    public async Task RecordPending_WithoutStaged_ReturnsUsageError()
    {
        using var harness = new CliHarness();

        int exitCode = await harness.RunAsync("scan", "--push", "--record-pending");

        exitCode.Should().Be(ExitCodes.USAGE_ERROR);
        harness.Error.Should().Contain("--record-pending 只能与 --staged 一起使用");
    }

    [Fact]
    public async Task HookPostCommit_NoMarker_ReportsNoVerifyJson()
    {
        using var harness = new CliHarness();
        harness.WriteFile("README.md", "# Sample\n");
        harness.Stage("README.md");
        string head = harness.Commit("baseline");

        int exitCode = await harness.RunAsync("hook", "post-commit", "--format", "json");

        exitCode.Should().Be(ExitCodes.SUCCESS, "post-commit 命令始终以 0 返回（Git 忽略其退出码）");
        using var document = JsonDocument.Parse(harness.Output);
        JsonElement root = document.RootElement;
        root.GetProperty("schema").GetString().Should().Be("gitguard.bypass.v1");
        root.GetProperty("detected").GetBoolean().Should().BeTrue();
        root.GetProperty("reason").GetString().Should().Be("NoVerify");
        root.GetProperty("commit_sha").GetString().Should().Be(head);
        root.GetProperty("run_id").GetString().Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task HookPostCommit_MatchingMarker_ReportsNotDetectedAndConsumesMarker()
    {
        using var harness = new CliHarness();
        harness.WriteFile("README.md", "# Sample\n");
        harness.Stage("README.md");
        string baseline = harness.Commit("baseline");
        harness.WriteFile("src/app.txt", "hello\n");
        harness.Stage("src/app.txt");
        string head = harness.Commit("second");
        WriteMarker(harness, baseline, harness.RunGit("rev-parse", "HEAD^{tree}"), skipped: false);

        int exitCode = await harness.RunAsync("hook", "post-commit", "--format", "json");

        exitCode.Should().Be(ExitCodes.SUCCESS);
        using var document = JsonDocument.Parse(harness.Output);
        JsonElement root = document.RootElement;
        root.GetProperty("detected").GetBoolean().Should().BeFalse();
        root.GetProperty("reason").ValueKind.Should().Be(JsonValueKind.Null);
        root.GetProperty("commit_sha").GetString().Should().Be(head);
        root.GetProperty("run_id").ValueKind.Should().Be(JsonValueKind.Null);
        File.Exists(harness.PendingScanPath).Should().BeFalse("正常提交必须消费预测标记");
    }

    [Fact]
    public async Task HookPostCommit_SkippedMarker_ReportsGitGuardSkip()
    {
        using var harness = new CliHarness();
        harness.WriteFile("README.md", "# Sample\n");
        harness.Stage("README.md");
        string baseline = harness.Commit("baseline");
        harness.WriteFile("src/app.txt", "hello\n");
        harness.Stage("src/app.txt");
        string head = harness.Commit("second");
        WriteMarker(harness, baseline, harness.RunGit("rev-parse", "HEAD^{tree}"), skipped: true);

        int exitCode = await harness.RunAsync("hook", "post-commit", "--format", "json");

        exitCode.Should().Be(ExitCodes.SUCCESS);
        using var document = JsonDocument.Parse(harness.Output);
        JsonElement root = document.RootElement;
        root.GetProperty("detected").GetBoolean().Should().BeTrue();
        root.GetProperty("reason").GetString().Should().Be("GitGuardSkip");
        root.GetProperty("commit_sha").GetString().Should().Be(head);
        root.GetProperty("run_id").GetString().Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task HookPostCommit_NoMarker_TextModeWarnsWithNoVerifyReason()
    {
        using var harness = new CliHarness();
        harness.WriteFile("README.md", "# Sample\n");
        harness.Stage("README.md");
        harness.Commit("baseline");

        int exitCode = await harness.RunAsync("hook", "post-commit");

        exitCode.Should().Be(ExitCodes.SUCCESS);
        harness.Output.Should().BeEmpty();
        harness.Error.Should().Contain("检测到绕过扫描的提交").And.Contain("--no-verify（未经过 pre-commit 扫描）");
    }

    private static JsonDocument ReadMarker(CliHarness harness)
    {
        File.Exists(harness.PendingScanPath).Should().BeTrue("Hook 模式应写入待提交预测");
        return JsonDocument.Parse(File.ReadAllBytes(harness.PendingScanPath));
    }

    private static void WriteMarker(CliHarness harness, string? parentSha, string treeSha, bool skipped)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(harness.PendingScanPath)
            ?? throw new InvalidOperationException("标记文件路径缺少父目录。"));
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            if (parentSha is null)
            {
                writer.WriteNull("parent_sha");
            }
            else
            {
                writer.WriteString("parent_sha", parentSha);
            }

            writer.WriteString("tree_sha", treeSha);
            writer.WriteBoolean("skipped", skipped);
            writer.WriteNumber("recorded_at", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            writer.WriteEndObject();
        }

        File.WriteAllBytes(harness.PendingScanPath, stream.ToArray());
    }
}
