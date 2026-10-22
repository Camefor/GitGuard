using System.Text.Json;
using FluentAssertions;
using GitGuard.Core;

namespace GitGuard.Cli.Tests;

/// <summary>
/// <c>gitguard allowlist</c> 端到端测试（<c>06-API接口设计</c> §5.5 / FR-040 / FR-041）：
/// <c>add</c> / <c>remove</c> / <c>list</c> 的 JSON 与文本渲染、退出码 20 / 21 / 7，
/// 以及 SQLite 白名单条目参与扫描放行（扫描策略合并）。
/// </summary>
public sealed class CliAllowlistCommandTests
{
    private const string RULE_ID = "GGD/SECRET/AWS-AKID";

    [Fact]
    public async Task Add_Json_RendersPersistedEntry()
    {
        using var harness = new CliHarness(isRepository: false);
        string fingerprint = new string('A', 64);

        int exitCode = await harness.RunAsync(
            "allowlist",
            "add",
            "--rule",
            RULE_ID,
            "--path",
            "config/**",
            "--fingerprint",
            fingerprint,
            "--reason",
            "  已知误报登记  ",
            "--format",
            "json");

        exitCode.Should().Be(ExitCodes.SUCCESS);
        using var document = JsonDocument.Parse(harness.Output);
        JsonElement root = document.RootElement;
        root.GetProperty("schema").GetString().Should().Be("gitguard.allowlist.add.v1");
        JsonElement entry = root.GetProperty("entry");
        Guid.TryParse(entry.GetProperty("id").GetString(), out Guid id).Should().BeTrue();
        id.Should().NotBe(Guid.Empty);
        entry.GetProperty("rule_id").GetString().Should().Be(RULE_ID);
        entry.GetProperty("path_pattern").GetString().Should().Be("config/**");
        entry.GetProperty("fingerprint").GetString().Should().Be(new string('a', 64));
        entry.GetProperty("reason").GetString().Should().Be("已知误报登记");
        entry.GetProperty("created_at").GetString().Should().EndWith("Z");
        entry.GetProperty("created_by").GetString().Should().Be(Environment.UserName);
        entry.GetProperty("expires_at").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task Add_Text_ThenListJson_RoundTripsEntryWithoutRepository()
    {
        using var harness = new CliHarness(isRepository: false);

        int addExit = await harness.RunAsync(
            "allowlist",
            "add",
            "--rule",
            RULE_ID,
            "--reason",
            "测试白名单条目");

        addExit.Should().Be(ExitCodes.SUCCESS, "allowlist 管理本机审计库，不要求当前目录是 Git 仓库");
        harness.Output.Should().BeEmpty();
        harness.Error.Should()
            .Contain("GitGuard 白名单条目已新增")
            .And.Contain("ID")
            .And.Contain(RULE_ID)
            .And.Contain("测试白名单条目");

        int listExit = await harness.RunAsync("allowlist", "list", "--format", "json");

        listExit.Should().Be(ExitCodes.SUCCESS);
        using var document = JsonDocument.Parse(harness.Output);
        JsonElement root = document.RootElement;
        root.GetProperty("schema").GetString().Should().Be("gitguard.allowlist.list.v1");
        root.GetProperty("total").GetInt32().Should().Be(1);
        root.GetProperty("limit").GetInt32().Should().Be(20);
        root.GetProperty("offset").GetInt32().Should().Be(0);
        root.GetProperty("count").GetInt32().Should().Be(1);
        JsonElement entry = root.GetProperty("entries").EnumerateArray().Single();
        entry.GetProperty("rule_id").GetString().Should().Be(RULE_ID);
        entry.GetProperty("path_pattern").ValueKind.Should().Be(JsonValueKind.Null);
        entry.GetProperty("fingerprint").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task Add_ValidationFailures_ReturnExitCode20()
    {
        using var harness = new CliHarness(isRepository: false);

        (await harness.RunAsync("allowlist", "add", "--reason", "缺少匹配条件"))
            .Should().Be(ExitCodes.ALLOWLIST_VALIDATION_FAILED);
        (await harness.RunAsync("allowlist", "add", "--rule", RULE_ID, "--reason", "abcd"))
            .Should().Be(ExitCodes.ALLOWLIST_VALIDATION_FAILED);
        (await harness.RunAsync("allowlist", "add", "--fingerprint", "not-a-fingerprint", "--reason", "指纹格式非法"))
            .Should().Be(ExitCodes.ALLOWLIST_VALIDATION_FAILED);

        harness.Error.Should().Contain(AllowlistException.ALLOWLIST_VALIDATION_FAILED);
        harness.Output.Should().BeEmpty();
    }

    [Fact]
    public async Task Remove_ExistingEntry_DeletesIt()
    {
        using var harness = new CliHarness(isRepository: false);
        (await harness.RunAsync(
            "allowlist",
            "add",
            "--rule",
            RULE_ID,
            "--reason",
            "待删除条目",
            "--format",
            "json")).Should().Be(ExitCodes.SUCCESS);
        using var addDocument = JsonDocument.Parse(harness.Output);
        string id = addDocument.RootElement.GetProperty("entry").GetProperty("id").GetString()
            ?? throw new InvalidOperationException("新增结果缺少条目 ID。");

        int removeExit = await harness.RunAsync("allowlist", "remove", "--id", id);

        removeExit.Should().Be(ExitCodes.SUCCESS);
        harness.Error.Should().Contain("GitGuard 白名单条目已删除").And.Contain(id);

        int listExit = await harness.RunAsync("allowlist", "list");
        listExit.Should().Be(ExitCodes.SUCCESS);
        harness.Error.Should().Contain("白名单为空。");
    }

    [Fact]
    public async Task Remove_MissingEntry_ReturnsExitCode21()
    {
        using var harness = new CliHarness(isRepository: false);

        int exitCode = await harness.RunAsync("allowlist", "remove", "--id", Guid.CreateVersion7().ToString());

        exitCode.Should().Be(ExitCodes.ALLOWLIST_NOT_FOUND);
        harness.Error.Should().Contain(AllowlistException.ALLOWLIST_NOT_FOUND);
        harness.Output.Should().BeEmpty();
    }

    [Fact]
    public async Task Remove_InvalidOrMissingId_ReturnsUsageError()
    {
        using var harness = new CliHarness(isRepository: false);

        (await harness.RunAsync("allowlist", "remove")).Should().Be(ExitCodes.USAGE_ERROR);
        harness.Error.Should().Contain("--id");
        (await harness.RunAsync("allowlist", "remove", "--id", "not-a-guid"))
            .Should().Be(ExitCodes.USAGE_ERROR);
        harness.Error.Should().Contain("GUID");
        harness.Output.Should().BeEmpty();
    }

    [Fact]
    public async Task List_Text_PaginatesAndRendersConditions()
    {
        using var harness = new CliHarness(isRepository: false);
        (await harness.RunAsync("allowlist", "add", "--rule", RULE_ID, "--reason", "第一条白名单")).Should().Be(ExitCodes.SUCCESS);
        (await harness.RunAsync("allowlist", "add", "--path", "src/**", "--reason", "第二条白名单")).Should().Be(ExitCodes.SUCCESS);

        int exitCode = await harness.RunAsync("allowlist", "list", "--limit", "1", "--offset", "1");

        exitCode.Should().Be(ExitCodes.SUCCESS);
        harness.Output.Should().BeEmpty();
        harness.Error.Should()
            .Contain("GitGuard 白名单")
            .And.Contain("共 2 条，显示 2-2 条")
            .And.Contain("第一条白名单")
            .And.Contain("规则=" + RULE_ID);
    }

    [Fact]
    public async Task List_InvalidPageOptions_ReturnUsageError()
    {
        using var harness = new CliHarness(isRepository: false);

        (await harness.RunAsync("allowlist", "list", "--limit", "0")).Should().Be(ExitCodes.USAGE_ERROR);
        harness.Error.Should().Contain("--limit");
        (await harness.RunAsync("allowlist", "list", "--offset=-1")).Should().Be(ExitCodes.USAGE_ERROR);
        harness.Error.Should().Contain("--offset");
        harness.Output.Should().BeEmpty();
    }

    [Fact]
    public async Task Scan_AllowlistedEntry_SuppressesFindingUntilRemoved()
    {
        using var harness = new CliHarness();
        (await harness.RunAsync(
            "allowlist",
            "add",
            "--rule",
            RULE_ID,
            "--reason",
            "扫描放行集成测试",
            "--format",
            "json")).Should().Be(ExitCodes.SUCCESS);
        using var addDocument = JsonDocument.Parse(harness.Output);
        string id = addDocument.RootElement.GetProperty("entry").GetProperty("id").GetString()
            ?? throw new InvalidOperationException("新增结果缺少条目 ID。");

        harness.WriteFile("config/app.env", $"aws_key={CliHarness.CreateCanaryToken()}\n");
        harness.Stage("config/app.env");

        int allowlistedScan = await harness.RunAsync("scan", "--staged");
        allowlistedScan.Should().Be(ExitCodes.SUCCESS, "SQLite 白名单条目必须参与扫描策略放行");
        harness.Error.Should().Contain("白名单忽略 1");

        (await harness.RunAsync("allowlist", "remove", "--id", id)).Should().Be(ExitCodes.SUCCESS);
        int blockedScan = await harness.RunAsync("scan", "--staged");
        blockedScan.Should().Be(ExitCodes.BLOCKED, "删除白名单后同一发现项必须重新阻断");
    }
}
