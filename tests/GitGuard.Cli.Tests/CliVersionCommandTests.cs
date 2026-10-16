using System.Text.Json;
using FluentAssertions;

namespace GitGuard.Cli.Tests;

/// <summary>
/// <c>gitguard version</c> 测试（<c>docs/06-API接口设计.md</c> §5.8）：不触碰仓库与配置，文本输出到 stderr、
/// JSON 输出到 stdout，字段含 GitGuard 版本、规则库版本与运行时信息。
/// </summary>
public sealed class CliVersionCommandTests
{
    [Fact]
    public async Task Version_TextOutput_ReportsVersionsOnStandardError()
    {
        using var harness = new CliHarness();

        int exitCode = await harness.RunAsync("version");

        exitCode.Should().Be(ExitCodes.SUCCESS);
        harness.Output.Should().BeEmpty();
        harness.Error.Should().Contain($"gitguard {CliHarness.TEST_VERSION}");
        harness.Error.Should().Contain("规则库版本").And.Contain("本机 git").And.Contain("运行时");
    }

    [Fact]
    public async Task Version_JsonOutput_MatchesVersionSchema()
    {
        using var harness = new CliHarness();

        int exitCode = await harness.RunAsync("version", "--format", "json");

        exitCode.Should().Be(ExitCodes.SUCCESS);
        harness.Error.Should().BeEmpty();

        using var document = JsonDocument.Parse(harness.Output);
        JsonElement root = document.RootElement;
        root.GetProperty("schema").GetString().Should().Be("gitguard.version.v1");
        root.GetProperty("gitguard_version").GetString().Should().Be(CliHarness.TEST_VERSION);
        root.GetProperty("rules_version").GetInt32().Should().Be(1);
        root.GetProperty("runtime").GetProperty("framework").GetString().Should().NotBeNullOrWhiteSpace();
        root.GetProperty("runtime").GetProperty("runtime_identifier").GetString().Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Version_OutsideRepository_StillSucceeds()
    {
        using var harness = new CliHarness(isRepository: false);

        int exitCode = await harness.RunAsync("version");

        exitCode.Should().Be(ExitCodes.SUCCESS, "version 不依赖仓库上下文");
    }
}
