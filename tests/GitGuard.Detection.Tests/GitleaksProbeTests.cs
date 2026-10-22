using FluentAssertions;
using GitGuard.Core;
using GitGuard.Detection.Adapters;

namespace GitGuard.Detection.Tests;

/// <summary>
/// Gitleaks 启动期探测测试（FR-090 / ADR-0003）：缺失可执行文件 → 禁用且不阻断；
/// 8.x 版本 → 无警告启用；其他版本 / 无法解析 → 警告但继续启用。
/// </summary>
public sealed class GitleaksProbeTests
{
    [Fact]
    public void Probe_MissingConfiguredPath_ReturnsUnavailableWithoutRunningProcess()
    {
        string missingPath = Path.Combine(Path.GetTempPath(), $"gitguard-missing-{Guid.NewGuid():N}.exe");
        var runner = new StubProcessRunner(() => new GitleaksProcessResult(0, "v8.18.4", string.Empty));

        GitleaksProbeResult result = GitleaksProbe.Probe(missingPath, runner);

        result.IsAvailable.Should().BeFalse();
        result.Warning.Should().Contain("Gitleaks 适配器已禁用").And.Contain("gitleaks_path");
        runner.CallCount.Should().Be(0);
    }

    [Fact]
    public void Probe_SupportedMajorVersion_ReturnsAvailableWithoutWarning()
    {
        string executable = CreateTemporaryExecutable();
        try
        {
            var runner = new StubProcessRunner(() => new GitleaksProcessResult(0, "v8.18.4\n", string.Empty));

            GitleaksProbeResult result = GitleaksProbe.Probe(executable, runner);

            result.IsAvailable.Should().BeTrue();
            result.ExecutablePath.Should().Be(Path.GetFullPath(executable));
            result.VersionLine.Should().Be("v8.18.4");
            result.Warning.Should().BeNull();
            runner.LastArguments.Should().ContainSingle().Which.Should().Be("version");
        }
        finally
        {
            File.Delete(executable);
        }
    }

    [Fact]
    public void Probe_UnsupportedMajorVersion_ReturnsAvailableWithWarning()
    {
        string executable = CreateTemporaryExecutable();
        try
        {
            var runner = new StubProcessRunner(() => new GitleaksProcessResult(0, "v7.6.1", string.Empty));

            GitleaksProbeResult result = GitleaksProbe.Probe(executable, runner);

            result.IsAvailable.Should().BeTrue("版本不在验证区间时仍启用，仅告警");
            result.Warning.Should().Contain("8.x");
        }
        finally
        {
            File.Delete(executable);
        }
    }

    [Fact]
    public void Probe_UnparseableVersion_ReturnsAvailableWithWarning()
    {
        string executable = CreateTemporaryExecutable();
        try
        {
            var runner = new StubProcessRunner(() => new GitleaksProcessResult(0, "gitleaks dev build", string.Empty));

            GitleaksProbeResult result = GitleaksProbe.Probe(executable, runner);

            result.IsAvailable.Should().BeTrue();
            result.Warning.Should().Contain("无法识别").And.Contain("gitleaks dev build");
        }
        finally
        {
            File.Delete(executable);
        }
    }

    [Fact]
    public void Probe_VersionCommandFails_ReturnsUnavailable()
    {
        string executable = CreateTemporaryExecutable();
        try
        {
            var runner = new StubProcessRunner(() => new GitleaksProcessResult(1, string.Empty, "unknown flag: version"));

            GitleaksProbeResult result = GitleaksProbe.Probe(executable, runner);

            result.IsAvailable.Should().BeFalse();
            result.Warning.Should().Contain("退出码 1");
        }
        finally
        {
            File.Delete(executable);
        }
    }

    [Fact]
    public void Probe_ProcessRunnerThrows_ReturnsUnavailableInsteadOfPropagating()
    {
        string executable = CreateTemporaryExecutable();
        try
        {
            var runner = new StubProcessRunner(() => throw new DetectionException("启动失败（测试注入）。"));

            GitleaksProbeResult result = GitleaksProbe.Probe(executable, runner);

            result.IsAvailable.Should().BeFalse();
            result.Warning.Should().Contain("版本探测失败");
        }
        finally
        {
            File.Delete(executable);
        }
    }

    private static string CreateTemporaryExecutable()
    {
        string path = Path.Combine(Path.GetTempPath(), $"gitguard-probe-{Guid.NewGuid():N}.exe");
        File.WriteAllText(path, string.Empty);
        return path;
    }

    /// <summary>固定返回结果并记录参数的子进程替身。</summary>
    private sealed class StubProcessRunner : IGitleaksProcessRunner
    {
        private readonly Func<GitleaksProcessResult> _result;

        internal StubProcessRunner(Func<GitleaksProcessResult> result) => _result = result;

        internal int CallCount { get; private set; }

        internal IReadOnlyList<string> LastArguments { get; private set; } = Array.Empty<string>();

        public Task<GitleaksProcessResult> RunAsync(
            string executablePath,
            IReadOnlyList<string> arguments,
            TimeSpan timeout,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            LastArguments = arguments;
            return Task.FromResult(_result());
        }
    }
}
