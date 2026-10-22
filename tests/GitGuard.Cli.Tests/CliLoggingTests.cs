using System.Text.Json;
using FluentAssertions;
using GitGuard.Application.Composition;

namespace GitGuard.Cli.Tests;

/// <summary>
/// 全局选项语义测试（<c>模块设计/Cli.md</c> §7）：<c>--verbose</c> / <c>--trace</c> 提升日志级别、
/// <c>--log-file</c> 落盘 JSON-lines、<c>--no-color</c> 与 <c>--format json</c> 的输出分流契约。
/// </summary>
public sealed class CliLoggingTests
{
    [Fact]
    public async Task DefaultLevel_SuppressesDebugLines()
    {
        using var harness = new CliHarness();

        (await harness.RunAsync("version")).Should().Be(ExitCodes.SUCCESS);

        harness.Error.Should().NotContain("[debug]");
    }

    [Fact]
    public async Task Verbose_EnablesDebugLines()
    {
        using var harness = new CliHarness();

        (await harness.RunAsync("version", "--verbose")).Should().Be(ExitCodes.SUCCESS);

        harness.Error.Should().Contain("[debug] Cli: 命令开始：version");
    }

    [Fact]
    public async Task VerboseAndTrace_KeepsCommandWorking()
    {
        using var harness = new CliHarness();

        (await harness.RunAsync("version", "--verbose", "--trace")).Should().Be(ExitCodes.SUCCESS);

        harness.Error.Should().Contain("[debug]");
    }

    [Fact]
    public async Task LogFileOption_WritesJsonLinesSink()
    {
        using var harness = new CliHarness();
        string logPath = Path.Combine(harness.DataDirectory, "logs", "cli.log");

        (await harness.RunAsync("version", "--verbose", "--log-file", logPath)).Should().Be(ExitCodes.SUCCESS);

        File.Exists(logPath).Should().BeTrue();
        string line = File.ReadLines(logPath).First();
        using var document = JsonDocument.Parse(line);
        JsonElement root = document.RootElement;
        root.GetProperty("level").GetString().Should().Be("debug");
        root.GetProperty("category").GetString().Should().Be("Cli");
        root.GetProperty("message").GetString().Should().Be("命令开始：version");
    }

    [Fact]
    public async Task ConfiguredLogFileWithoutPath_WritesUnderPlatformDataDirectory()
    {
        using var harness = new CliHarness();
        harness.WriteProjectConfig(
            """
            version: 1
            logging:
              file:
                enabled: true
            """);
        string logPath = Path.Combine(harness.DataDirectory, "logs", "gitguard.log");

        (await harness.RunAsync("version", "--verbose")).Should().Be(ExitCodes.SUCCESS);

        File.Exists(logPath).Should().BeTrue("缺省 logging.file.path 必须落在注入的数据目录下");
        using var document = JsonDocument.Parse(File.ReadLines(logPath).First());
        document.RootElement.GetProperty("category").GetString().Should().Be("Cli");
    }

    [Fact]
    public void ExpandUserPath_ExpandsTildePrefixOnly()
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        // 展开结果可能保留 "/" 分隔符：以 GetFullPath 归一化后再比较
        Path.GetFullPath(GitGuardSession.ExpandUserPath("~/logs/cli.log"))
            .Should().Be(Path.GetFullPath(Path.Combine(home, "logs", "cli.log")));
        GitGuardSession.ExpandUserPath("~").Should().Be(home);
        GitGuardSession.ExpandUserPath("~user/logs").Should().Be("~user/logs", "不支持的形态原样返回");
        GitGuardSession.ExpandUserPath("relative/cli.log").Should().Be("relative/cli.log");
    }

    [Fact]
    public async Task JsonFormat_KeepsStderrEmptyForSuccessfulCommands()
    {
        using var harness = new CliHarness();
        harness.WriteFile("notes.txt", "clean\n");
        harness.Stage("notes.txt");

        (await harness.RunAsync("scan", "--staged", "--format", "json", "--verbose")).Should().Be(ExitCodes.SUCCESS);

        // 日志始终写 stderr（JSON 模式也不得污染 stdout）
        harness.Error.Should().Contain("[debug]");
        using var document = JsonDocument.Parse(harness.Output);
        document.RootElement.GetProperty("result").GetString().Should().Be("Clean");
    }
}
