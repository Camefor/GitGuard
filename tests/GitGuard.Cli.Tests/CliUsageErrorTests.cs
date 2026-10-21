using System.Text.Json;
using System.Text.RegularExpressions;
using FluentAssertions;

namespace GitGuard.Cli.Tests;

/// <summary>
/// 用法错误（退出码 7，GGD-0009 新增）测试：未知命令 / 未知选项 / 选项冲突 / 受限取值非法 / 参数组合非法
/// 必须在解析阶段收敛为 7，且不触碰仓库与配置（<c>模块设计/Cli.md</c> §4、<c>06-API接口设计</c> §6）。
/// </summary>
public sealed partial class CliUsageErrorTests
{
    private const string USAGE_ERROR_TEXT = "用法错误";

    /// <summary>短追踪 ID 格式：<c>XXXX-XXXX</c>（十六进制）。</summary>
    [GeneratedRegex("^[0-9a-f]{4}-[0-9a-f]{4}$")]
    private static partial Regex TraceIdPattern { get; }

    [Fact]
    public async Task UnknownCommand_ReturnsUsageErrorWithHelpHint()
    {
        using var harness = new CliHarness();

        int exitCode = await harness.RunAsync("frobnicate");

        exitCode.Should().Be(ExitCodes.USAGE_ERROR);
        harness.Error.Should().Contain(USAGE_ERROR_TEXT).And.Contain("gitguard --help");
        harness.Output.Should().BeEmpty();
    }

    [Fact]
    public async Task UnknownOption_ReturnsUsageError()
    {
        using var harness = new CliHarness();

        int exitCode = await harness.RunAsync("scan", "--staged", "--bogus");

        exitCode.Should().Be(ExitCodes.USAGE_ERROR);
        harness.Error.Should().Contain(USAGE_ERROR_TEXT);
    }

    [Fact]
    public async Task ScanWithoutScopeOrWithBothScopes_ReturnsUsageError()
    {
        using var harness = new CliHarness();

        (await harness.RunAsync("scan")).Should().Be(ExitCodes.USAGE_ERROR);
        harness.Error.Should().Contain("必须且只能指定 --staged 或 --push");

        int both = await harness.RunAsync("scan", "--staged", "--push");
        both.Should().Be(ExitCodes.USAGE_ERROR);
    }

    [Fact]
    public async Task TraceWithoutVerbose_ReturnsUsageError()
    {
        using var harness = new CliHarness();

        int exitCode = await harness.RunAsync("version", "--trace");

        exitCode.Should().Be(ExitCodes.USAGE_ERROR);
        harness.Error.Should().Contain("--trace 必须与 --verbose 同用");
    }

    [Fact]
    public async Task InvalidRestrictedValue_FallsBackToTextUsageError()
    {
        using var harness = new CliHarness();

        // --format 本身非法：无法保证机器可读输出契约，回退文本报告
        int exitCode = await harness.RunAsync("version", "--format", "yaml");

        exitCode.Should().Be(ExitCodes.USAGE_ERROR);
        harness.Error.Should().Contain(USAGE_ERROR_TEXT);
        harness.Output.Should().BeEmpty();
    }

    [Fact]
    public async Task UnknownHook_FailsFastWithAvailableValues()
    {
        using var harness = new CliHarness();

        int unknown = await harness.RunAsync("install", "--hook", "pre-receive");

        unknown.Should().Be(ExitCodes.USAGE_ERROR);
        harness.Error.Should().Contain("可用：pre-commit、pre-push、post-commit");
    }

    [Fact]
    public async Task JsonFormat_RendersMachineReadableUsageError()
    {
        using var harness = new CliHarness();

        int exitCode = await harness.RunAsync("frobnicate", "--format", "json");

        exitCode.Should().Be(ExitCodes.USAGE_ERROR);
        harness.Error.Should().BeEmpty();

        using var document = JsonDocument.Parse(harness.Output);
        JsonElement root = document.RootElement;
        root.GetProperty("schema").GetString().Should().Be("gitguard.error.v1");
        root.GetProperty("exit_code").GetInt32().Should().Be(ExitCodes.USAGE_ERROR);
        root.GetProperty("error_code").GetString().Should().Be(ExitCodeMapper.CLI_USAGE_ERROR);
        root.GetProperty("message").GetString().Should().NotBeNullOrWhiteSpace();

        string? traceId = root.GetProperty("trace_id").GetString();
        traceId.Should().NotBeNullOrWhiteSpace();
        TraceIdPattern.IsMatch(traceId!).Should().BeTrue($"{traceId} 应为 XXXX-XXXX 形态");
    }

    [Fact]
    public async Task UsageError_DoesNotCreateAuditStoreOrHookFiles()
    {
        using var harness = new CliHarness();

        int exitCode = await harness.RunAsync("install", "--hook", "pre-receive");

        exitCode.Should().Be(ExitCodes.USAGE_ERROR);
        Directory.EnumerateFileSystemEntries(harness.DataDirectory).Should().BeEmpty("用法错误不得触发审计库或缓存落盘");
        File.Exists(harness.ResolvePath(".git/hooks/pre-commit")).Should().BeFalse();
    }
}
