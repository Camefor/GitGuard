using System.Text.Json;
using FluentAssertions;
using GitGuard.Core.Model;
using GitGuard.Core.Policy;
using GitGuard.Desktop.Smoke;
using GitGuard.Infrastructure.Sqlite;

namespace GitGuard.Desktop.Tests;

/// <summary>桌面端无界面冒烟模式测试（GGD-0047）：参数解析、审计读取、导出与报告契约。</summary>
public sealed class DesktopSmokeTestTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "gitguard-desktop-smoke-tests",
        Guid.NewGuid().ToString("N"));

    /// <summary>为每个用例创建独立临时目录。</summary>
    public DesktopSmokeTestTests() => Directory.CreateDirectory(_root);

    /// <inheritdoc />
    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // 临时目录清理失败不影响断言结果
        }
    }

    [Theory]
    [InlineData("--smoke-test", true)]
    [InlineData("--data-directory", false)]
    [InlineData("--smoke-testing", false)]
    public void IsRequested_OnlyMatchesExactSwitch(string argument, bool expected)
    {
        DesktopSmokeTest.IsRequested(new[] { argument }).Should().Be(expected);
    }

    [Fact]
    public void Run_EmptyDataDirectory_WritesSuccessfulReport()
    {
        string dataDirectory = Path.Combine(_root, "data");
        string reportPath = Path.Combine(_root, "reports", "smoke.json");

        int exitCode = DesktopSmokeTest.Run(
            new[] { "--smoke-test", "--data-directory", dataDirectory, "--report", reportPath });

        exitCode.Should().Be(0);
        using var report = JsonDocument.Parse(File.ReadAllText(reportPath));
        report.RootElement.GetProperty("ok").GetBoolean().Should().BeTrue();
        report.RootElement.GetProperty("schema").GetString().Should().Be("gitguard.desktop.smoke.v1");
        report.RootElement.GetProperty("auditRunCount").GetInt32().Should().Be(0);
        report.RootElement.GetProperty("latestRunId").ValueKind.Should().Be(JsonValueKind.Null);
        report.RootElement.GetProperty("error").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public void Run_SeededAuditRun_ReportsLatestDetailAndExport()
    {
        string dataDirectory = Path.Combine(_root, "data");
        Directory.CreateDirectory(dataDirectory);
        var store = new SqliteAuditStore(Path.Combine(dataDirectory, "audit.db"), "0.3.0-test");
        var run = ScanRun.Create(
            _root,
            ScanType.Manual,
            new DateTimeOffset(2026, 10, 23, 8, 30, 0, TimeSpan.Zero),
            15,
            ScanResult.Clean,
            0);
        store.SaveScanRun(run, new Dictionary<Finding, PolicyDecision>());
        string reportPath = Path.Combine(_root, "smoke.json");
        string exportPath = Path.Combine(_root, "audit-export.json");

        int exitCode = DesktopSmokeTest.Run(
            new[]
            {
                "--smoke-test",
                "--data-directory",
                dataDirectory,
                "--expected-runs",
                "1",
                "--report",
                reportPath,
                "--export",
                exportPath,
            });

        exitCode.Should().Be(0);
        File.Exists(exportPath).Should().BeTrue();
        using var report = JsonDocument.Parse(File.ReadAllText(reportPath));
        report.RootElement.GetProperty("ok").GetBoolean().Should().BeTrue();
        report.RootElement.GetProperty("auditRunCount").GetInt32().Should().Be(1);
        report.RootElement.GetProperty("latestRunId").GetGuid().Should().Be(run.Id);
        report.RootElement.GetProperty("latestRunResult").GetString().Should().Be("Clean");
        report.RootElement.GetProperty("latestRunRepoPath").GetString().Should().Be(run.RepoPath);
        report.RootElement.GetProperty("exportedRunCount").GetInt32().Should().Be(1);
    }

    [Fact]
    public void Run_ExpectedRunsNotMet_ReturnsFailureAndWritesReport()
    {
        string dataDirectory = Path.Combine(_root, "data");
        string reportPath = Path.Combine(_root, "smoke.json");

        int exitCode = DesktopSmokeTest.Run(
            new[]
            {
                "--smoke-test",
                "--data-directory",
                dataDirectory,
                "--expected-runs",
                "2",
                "--report",
                reportPath,
            });

        exitCode.Should().Be(1);
        using var report = JsonDocument.Parse(File.ReadAllText(reportPath));
        report.RootElement.GetProperty("ok").GetBoolean().Should().BeFalse();
        report.RootElement.GetProperty("auditRunCount").GetInt32().Should().Be(0);
        report.RootElement.GetProperty("expectedRuns").GetInt32().Should().Be(2);
        report.RootElement.GetProperty("error").GetString().Should().Contain("expected at least 2");
    }

    [Theory]
    [InlineData("--report")]
    [InlineData("--expected-runs")]
    [InlineData("--export")]
    public void Run_MissingOptionValue_ReturnsFailure(string option)
    {
        string[] args = { "--smoke-test", option };
        DesktopSmokeTest.Run(args).Should().Be(1);
    }

    [Fact]
    public void Run_UnknownArgument_ReturnsFailure()
    {
        string[] args = { "--smoke-test", "--unexpected" };
        DesktopSmokeTest.Run(args).Should().Be(1);
    }
}
