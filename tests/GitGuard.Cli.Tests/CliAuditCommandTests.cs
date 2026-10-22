using System.Text;
using System.Text.Json;
using FluentAssertions;
using GitGuard.Core;

namespace GitGuard.Cli.Tests;

/// <summary>
/// <c>gitguard audit</c> 端到端测试（<c>06-API接口设计</c> §5.6 / FR-023）：真实扫描落库后验证
/// <c>list</c> 的 JSON / 文本渲染、筛选与用法错误（退出码 7），以及 <c>export</c> 的脱敏文件、
/// 导出摘要与失败退出码 31。
/// </summary>
public sealed class CliAuditCommandTests
{
    [Fact]
    public async Task List_Json_ReportsScannedRunWithoutSensitiveContent()
    {
        using var harness = new CliHarness();
        string canary = CliHarness.CreateCanaryToken();
        harness.WriteFile("config/app.env", $"aws_key={canary}\n");
        harness.Stage("config/app.env");
        (await harness.RunAsync("scan", "--staged")).Should().Be(ExitCodes.BLOCKED);

        int exitCode = await harness.RunAsync("audit", "list", "--format", "json");

        exitCode.Should().Be(ExitCodes.SUCCESS);
        using var document = JsonDocument.Parse(harness.Output);
        JsonElement root = document.RootElement;
        root.GetProperty("schema").GetString().Should().Be("gitguard.audit.list.v1");
        root.GetProperty("total").GetInt32().Should().Be(1);
        root.GetProperty("limit").GetInt32().Should().Be(20);
        root.GetProperty("offset").GetInt32().Should().Be(0);
        root.GetProperty("count").GetInt32().Should().Be(1);

        JsonElement run = root.GetProperty("runs").EnumerateArray().Single();
        run.GetProperty("repo_path").GetString().Should().Be(Path.TrimEndingDirectorySeparator(harness.Root));
        run.GetProperty("scan_type").GetString().Should().Be("PreCommit");
        run.GetProperty("result").GetString().Should().Be("Blocked");
        run.GetProperty("exit_code").GetInt32().Should().Be(ExitCodes.BLOCKED);
        run.GetProperty("findings_count").GetInt32().Should().Be(1);
        run.GetProperty("started_at").GetString().Should().EndWith("Z");
        run.GetProperty("duration_ms").ValueKind.Should().Be(JsonValueKind.Number);
        harness.Output.Should().NotContain(canary, "审计列表不得回显密钥原文");
    }

    [Fact]
    public async Task List_Text_RendersHeaderSummaryAndRunLine()
    {
        using var harness = new CliHarness();
        harness.WriteFile("config/app.env", $"aws_key={CliHarness.CreateCanaryToken()}\n");
        harness.Stage("config/app.env");
        (await harness.RunAsync("scan", "--staged")).Should().Be(ExitCodes.BLOCKED);

        int exitCode = await harness.RunAsync("audit", "list");

        exitCode.Should().Be(ExitCodes.SUCCESS);
        harness.Output.Should().BeEmpty();
        harness.Error.Should()
            .Contain("GitGuard 审计记录")
            .And.Contain("共 1 条，显示 1-1 条")
            .And.Contain("pre-commit")
            .And.Contain("Blocked")
            .And.Contain(harness.Root);
    }

    [Fact]
    public async Task List_WithoutRepository_SucceedsWithEmptyDatabase()
    {
        using var harness = new CliHarness(isRepository: false);

        int exitCode = await harness.RunAsync("audit", "list", "--format", "json");

        exitCode.Should().Be(ExitCodes.SUCCESS, "audit 命令查询本机审计库，不要求当前目录是 Git 仓库");
        using var document = JsonDocument.Parse(harness.Output);
        JsonElement root = document.RootElement;
        root.GetProperty("total").GetInt32().Should().Be(0);
        root.GetProperty("count").GetInt32().Should().Be(0);
        root.GetProperty("runs").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task List_ResultFilter_ReturnsOnlyMatchingRuns()
    {
        using var harness = new CliHarness();
        harness.WriteFile("notes.txt", "clean\n");
        harness.Stage("notes.txt");
        (await harness.RunAsync("scan", "--staged")).Should().Be(ExitCodes.SUCCESS);
        harness.WriteFile("config/app.env", $"aws_key={CliHarness.CreateCanaryToken()}\n");
        harness.Stage("config/app.env");
        (await harness.RunAsync("scan", "--staged")).Should().Be(ExitCodes.BLOCKED);

        int exitCode = await harness.RunAsync("audit", "list", "--result", "Blocked", "--format", "json");

        exitCode.Should().Be(ExitCodes.SUCCESS);
        using var document = JsonDocument.Parse(harness.Output);
        JsonElement root = document.RootElement;
        root.GetProperty("total").GetInt32().Should().Be(1);
        root.GetProperty("runs").EnumerateArray().Single()
            .GetProperty("result").GetString().Should().Be("Blocked");
    }

    [Fact]
    public async Task View_Json_ReportsRunAndFindingsWithoutSensitiveContent()
    {
        using var harness = new CliHarness();
        string canary = CliHarness.CreateCanaryToken();
        harness.WriteFile("config/app.env", $"aws_key={canary}\n");
        harness.Stage("config/app.env");
        (await harness.RunAsync("scan", "--staged")).Should().Be(ExitCodes.BLOCKED);
        string runId = await GetLatestRunIdAsync(harness);

        int outputOffset = harness.Output.Length;
        int exitCode = await harness.RunAsync("audit", "view", runId, "--format", "json");

        exitCode.Should().Be(ExitCodes.SUCCESS);
        using var document = JsonDocument.Parse(harness.Output[outputOffset..]);
        JsonElement root = document.RootElement;
        root.GetProperty("schema").GetString().Should().Be("gitguard.audit.view.v1");

        JsonElement run = root.GetProperty("run");
        run.GetProperty("run_id").GetString().Should().Be(runId);
        run.GetProperty("repo_path").GetString().Should().Be(Path.TrimEndingDirectorySeparator(harness.Root));
        run.GetProperty("scan_type").GetString().Should().Be("PreCommit");
        run.GetProperty("started_at").GetString().Should().EndWith("Z");
        run.GetProperty("duration_ms").ValueKind.Should().Be(JsonValueKind.Number);
        run.GetProperty("result").GetString().Should().Be("Blocked");
        run.GetProperty("exit_code").GetInt32().Should().Be(ExitCodes.BLOCKED);
        run.GetProperty("findings_count").GetInt32().Should().Be(1);

        JsonElement finding = root.GetProperty("findings").EnumerateArray().Single();
        finding.EnumerateObject().Select(static property => property.Name).Should().BeEquivalentTo(
            "finding_id",
            "rule_id",
            "file_path",
            "line_number",
            "severity",
            "fingerprint",
            "snippet_masked",
            "commit_sha");
        finding.GetProperty("rule_id").GetString().Should().Be("GGD/SECRET/AWS-AKID");
        finding.GetProperty("file_path").GetString().Should().Be("config/app.env");
        finding.GetProperty("line_number").GetInt32().Should().Be(1);
        finding.GetProperty("severity").GetString().Should().Be("Critical");
        finding.GetProperty("fingerprint").GetString().Should().HaveLength(64);
        finding.GetProperty("snippet_masked").GetString().Should().NotBeNullOrWhiteSpace().And.NotContain(canary);
        finding.GetProperty("commit_sha").ValueKind.Should().Be(JsonValueKind.Null);
        harness.Output.Should().NotContain(canary, "审计详情不得回显密钥原文");
    }

    [Fact]
    public async Task View_Text_RendersRunSummaryAndFindingLines()
    {
        using var harness = new CliHarness();
        harness.WriteFile("config/app.env", $"aws_key={CliHarness.CreateCanaryToken()}\n");
        harness.Stage("config/app.env");
        (await harness.RunAsync("scan", "--staged")).Should().Be(ExitCodes.BLOCKED);
        string runId = await GetLatestRunIdAsync(harness);

        int outputOffset = harness.Output.Length;
        int exitCode = await harness.RunAsync("audit", "view", runId);

        exitCode.Should().Be(ExitCodes.SUCCESS);
        harness.Output[outputOffset..].Should().BeEmpty();
        harness.Error.Should()
            .Contain("GitGuard 审计运行详情")
            .And.Contain(runId)
            .And.Contain("pre-commit")
            .And.Contain("Blocked")
            .And.Contain("config/app.env:1")
            .And.Contain("GGD/SECRET/AWS-AKID");
    }

    [Fact]
    public async Task View_CleanRun_ReportsNoFindings()
    {
        using var harness = new CliHarness();
        harness.WriteFile("notes.txt", "clean\n");
        harness.Stage("notes.txt");
        (await harness.RunAsync("scan", "--staged")).Should().Be(ExitCodes.SUCCESS);
        string runId = await GetLatestRunIdAsync(harness);

        (await harness.RunAsync("audit", "view", runId)).Should().Be(ExitCodes.SUCCESS);

        harness.Error.Should().Contain("无（本次运行未记录发现项）");
    }

    [Fact]
    public async Task View_UnknownRun_ReturnsAuditRunNotFound()
    {
        using var harness = new CliHarness(isRepository: false);

        int exitCode = await harness.RunAsync("audit", "view", Guid.NewGuid().ToString());

        exitCode.Should().Be(ExitCodes.AUDIT_RUN_NOT_FOUND);
        harness.Output.Should().BeEmpty();
        harness.Error.Should()
            .Contain("审计运行不存在")
            .And.Contain("AUDIT_RUN_NOT_FOUND");
    }

    [Fact]
    public async Task View_InvalidRunId_ReturnsUsageError()
    {
        using var harness = new CliHarness(isRepository: false);

        (await harness.RunAsync("audit", "view", "not-a-guid")).Should().Be(ExitCodes.USAGE_ERROR);
        harness.Error.Should().Contain("GUID");
        (await harness.RunAsync("audit", "view", "00000000-0000-0000-0000-000000000000"))
            .Should().Be(ExitCodes.USAGE_ERROR);

        harness.Output.Should().BeEmpty();
    }

    [Fact]
    public async Task InvalidAuditArguments_ReturnUsageError()
    {
        using var harness = new CliHarness(isRepository: false);

        (await harness.RunAsync("audit", "list", "--limit", "0")).Should().Be(ExitCodes.USAGE_ERROR);
        harness.Error.Should().Contain("--limit");
        (await harness.RunAsync("audit", "list", "--offset=-1")).Should().Be(ExitCodes.USAGE_ERROR);
        harness.Error.Should().Contain("--offset");
        (await harness.RunAsync("audit", "list", "--since", "not-a-time")).Should().Be(ExitCodes.USAGE_ERROR);
        harness.Error.Should().Contain("--since");
        (await harness.RunAsync("audit", "list", "--result", "Secret")).Should().Be(ExitCodes.USAGE_ERROR);
        harness.Error.Should().Contain("--result");
        (await harness.RunAsync("audit", "export")).Should().Be(ExitCodes.USAGE_ERROR);
        harness.Error.Should().Contain("--output");
        (await harness.RunAsync("audit", "export", "--output", "audit.txt", "--format", "yaml"))
            .Should().Be(ExitCodes.USAGE_ERROR);

        harness.Output.Should().BeEmpty();
    }

    [Fact]
    public async Task Export_Csv_WritesMaskedFileAndReportsSummary()
    {
        using var harness = new CliHarness();
        string canary = CliHarness.CreateCanaryToken();
        harness.WriteFile("config/app.env", $"aws_key={canary}\n");
        harness.Stage("config/app.env");
        (await harness.RunAsync("scan", "--staged")).Should().Be(ExitCodes.BLOCKED);
        string exportDirectory = Path.Combine(harness.Root, "exports");
        Directory.CreateDirectory(exportDirectory);
        string outputPath = Path.Combine(exportDirectory, "audit.csv");

        int exitCode = await harness.RunAsync("audit", "export", "--output", outputPath, "--format", "csv");

        exitCode.Should().Be(ExitCodes.SUCCESS);
        harness.Output.Should().BeEmpty();
        harness.Error.Should().Contain("GitGuard 审计导出完成").And.Contain(outputPath);

        byte[] bytes = File.ReadAllBytes(outputPath);
        bytes.Take(3).Should().Equal(0xEF, 0xBB, 0xBF);
        string content = Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        content.Should()
            .StartWith("run_id,repo_path,scan_type,started_at,duration_ms,result,exit_code,")
            .And.Contain("GGD/SECRET/AWS-AKID")
            .And.NotContain(canary)
            .And.NotContain("aws_key=");
    }

    [Fact]
    public async Task Export_Json_WritesFileAndJsonSummary()
    {
        using var harness = new CliHarness();
        string canary = CliHarness.CreateCanaryToken();
        harness.WriteFile("config/app.env", $"aws_key={canary}\n");
        harness.Stage("config/app.env");
        (await harness.RunAsync("scan", "--staged")).Should().Be(ExitCodes.BLOCKED);
        string outputPath = Path.Combine(harness.Root, "audit.json");

        // 全局 --format 控制摘要输出，子命令后的 --format 控制导出文件格式
        int exitCode = await harness.RunAsync(
            "--format",
            "json",
            "audit",
            "export",
            "--output",
            outputPath,
            "--format",
            "json");

        exitCode.Should().Be(ExitCodes.SUCCESS);
        harness.Error.Should().NotContain("GitGuard 审计导出完成", "JSON 模式下摘要走 stdout");
        using var summary = JsonDocument.Parse(harness.Output);
        JsonElement root = summary.RootElement;
        root.GetProperty("schema").GetString().Should().Be("gitguard.audit.export.summary.v1");
        root.GetProperty("output_path").GetString().Should().Be(Path.GetFullPath(outputPath));
        root.GetProperty("format").GetString().Should().Be("json");
        root.GetProperty("run_count").GetInt32().Should().Be(1);
        root.GetProperty("finding_count").GetInt32().Should().Be(1);

        using var exported = JsonDocument.Parse(File.ReadAllText(outputPath));
        exported.RootElement.GetProperty("schema").GetString().Should().Be("gitguard.audit.export.v1");
        string? snippetMasked = exported.RootElement
            .GetProperty("runs")[0]
            .GetProperty("findings")[0]
            .GetProperty("snippet_masked")
            .GetString();
        snippetMasked.Should().NotBeNullOrWhiteSpace().And.NotContain(canary);
    }

    [Fact]
    public async Task Export_UnwritableDestination_ReturnsAuditExportFailed()
    {
        using var harness = new CliHarness();

        int exitCode = await harness.RunAsync("audit", "export", "--output", harness.Root);

        exitCode.Should().Be(ExitCodes.AUDIT_EXPORT_FAILED);
        harness.Output.Should().BeEmpty();
        harness.Error.Should().Contain(AuditExportException.AUDIT_EXPORT_FAILED);
    }

    /// <summary>经 <c>audit list --format json</c> 读取最近一次运行的 ID（列表按开始时间倒序）。</summary>
    private static async Task<string> GetLatestRunIdAsync(CliHarness harness)
    {
        (await harness.RunAsync("audit", "list", "--format", "json")).Should().Be(ExitCodes.SUCCESS);
        using var document = JsonDocument.Parse(harness.Output);
        return document.RootElement.GetProperty("runs")[0].GetProperty("run_id").GetString()
            ?? throw new InvalidOperationException("审计列表缺少 run_id。");
    }
}
