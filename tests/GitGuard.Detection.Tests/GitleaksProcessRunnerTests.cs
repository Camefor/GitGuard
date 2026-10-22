using FluentAssertions;
using GitGuard.Core;
using GitGuard.Detection.Adapters;

namespace GitGuard.Detection.Tests;

/// <summary>
/// Gitleaks 子进程执行器测试：输出 / 退出码采集、启动失败与超时终止（FailClosed，不重试）。
/// </summary>
public sealed class GitleaksProcessRunnerTests
{
    [Fact]
    public async Task RunAsync_CapturesOutputAndExitCode()
    {
        (string fileName, string[] arguments) = CreatePlatformCommand("echo hello", exitCode: 3);
        var runner = new GitleaksProcessRunner();

        GitleaksProcessResult result = await runner.RunAsync(
            fileName,
            arguments,
            TimeSpan.FromSeconds(30),
            TestContext.Current.CancellationToken);

        result.ExitCode.Should().Be(3);
        result.StandardOutput.Should().Contain("hello");
    }

    [Fact]
    public async Task RunAsync_MissingExecutable_ThrowsDetectionException()
    {
        string missing = Path.Combine(Path.GetTempPath(), $"gitguard-missing-{Guid.NewGuid():N}.exe");
        var runner = new GitleaksProcessRunner();

        Func<Task> act = () => runner.RunAsync(missing, Array.Empty<string>(), TimeSpan.FromSeconds(5));

        await act.Should().ThrowAsync<DetectionException>().WithMessage("*无法启动 gitleaks 进程*");
    }

    [Fact]
    public async Task RunAsync_Timeout_KillsProcessAndThrowsDetectionException()
    {
        (string fileName, string[] arguments) = CreatePlatformCommand("sleep", exitCode: 0, sleepSeconds: 5);
        var runner = new GitleaksProcessRunner();

        Func<Task> act = () => runner.RunAsync(
            fileName,
            arguments,
            TimeSpan.FromMilliseconds(200),
            TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<DetectionException>().WithMessage("*执行超时*");
    }

    [Fact]
    public async Task RunAsync_ExternalCancellation_PropagatesOperationCanceledException()
    {
        (string fileName, string[] arguments) = CreatePlatformCommand("sleep", exitCode: 0, sleepSeconds: 5);
        var runner = new GitleaksProcessRunner();
        using var cancellationSource = new CancellationTokenSource();
        cancellationSource.Cancel();

        Func<Task> act = () => runner.RunAsync(
            fileName,
            arguments,
            TimeSpan.FromSeconds(30),
            cancellationSource.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    /// <summary>构造平台相关的短命令：Windows 用 cmd.exe，类 Unix 用 /bin/sh。</summary>
    /// <param name="command">shell 命令；<c>echo hello</c> 或 <c>sleep</c> 两个受支持取值。</param>
    /// <param name="exitCode">期望退出码。</param>
    /// <param name="sleepSeconds">sleep 命令的时长（仅 <c>sleep</c> 使用）。</param>
    /// <returns>可执行文件与参数。</returns>
    private static (string FileName, string[] Arguments) CreatePlatformCommand(
        string command,
        int exitCode,
        int sleepSeconds = 0)
    {
        if (OperatingSystem.IsWindows())
        {
            string body = command == "sleep"
                ? $"ping -n {sleepSeconds + 1} 127.0.0.1 >nul"
                : $"echo hello & exit /b {exitCode}";
            return ("cmd.exe", new[] { "/d", "/c", body });
        }

        string shellBody = command == "sleep"
            ? $"sleep {sleepSeconds}"
            : $"echo hello; exit {exitCode}";
        return ("/bin/sh", new[] { "-c", shellBody });
    }
}
