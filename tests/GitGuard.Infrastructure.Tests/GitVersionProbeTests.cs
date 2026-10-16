using FluentAssertions;
using GitGuard.Infrastructure.Git;

namespace GitGuard.Infrastructure.Tests;

/// <summary>
/// git 版本探测测试（FR-012 状态展示 / <c>gitguard version</c>）：不要求当前目录是仓库，
/// 失败一律以结果对象表达（信息不可得 ≠ 阻断），不抛出异常。
/// </summary>
public sealed class GitVersionProbeTests
{
    [Fact]
    public void Probe_RealGit_ReturnsVersionLine()
    {
        GitVersionProbeResult result = GitVersionProbe.Probe();

        result.Available.Should().BeTrue();
        result.VersionLine.Should().StartWith("git version");
        result.FailureMessage.Should().BeNull();
    }

    [Fact]
    public void Probe_MissingExecutable_ReturnsFailureInsteadOfThrowing()
    {
        GitVersionProbeResult result = GitVersionProbe.Probe("gitguard-nonexistent-executable");

        result.Available.Should().BeFalse();
        result.VersionLine.Should().BeNull();
        result.FailureMessage.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Probe_RejectsBlankExecutable()
    {
        FluentActions.Invoking(() => GitVersionProbe.Probe("  ")).Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ProbeResult_EnforcesAvailabilityInvariant()
    {
        FluentActions.Invoking(() => new GitVersionProbeResult(true, null, null))
            .Should().Throw<ArgumentException>("可用时必须携带版本行");
        FluentActions.Invoking(() => new GitVersionProbeResult(false, "git version 2.43.0", null))
            .Should().Throw<ArgumentException>("不可用时必须携带失败原因");
    }
}
