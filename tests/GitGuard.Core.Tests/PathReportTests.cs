using FluentAssertions;
using GitGuard.Core.Model;

namespace GitGuard.Core.Tests;

/// <summary>PATH 管理报告模型单元测试（GGD-0061：字段完整性与参数校验）。</summary>
public sealed class PathReportTests
{
    private const string PLATFORM = "linux";
    private const string TARGET_DIRECTORY = "/home/user/.local/bin";
    private const string EXECUTABLE = "/opt/gitguard/gitguard";

    [Fact]
    public void PathInstallReport_PopulatesAllProperties()
    {
        var report = new PathInstallReport(
            PLATFORM,
            TARGET_DIRECTORY,
            EXECUTABLE,
            alreadyPresent: false,
            pathUpdated: true,
            profilePath: "/home/user/.zshrc",
            profileUpdated: true,
            backupPath: "/home/user/.local/bin/gitguard.local.bak",
            effective: true);

        report.Platform.Should().Be(PLATFORM);
        report.TargetDirectory.Should().Be(TARGET_DIRECTORY);
        report.ExecutablePath.Should().Be(EXECUTABLE);
        report.AlreadyPresent.Should().BeFalse();
        report.PathUpdated.Should().BeTrue();
        report.ProfilePath.Should().Be("/home/user/.zshrc");
        report.ProfileUpdated.Should().BeTrue();
        report.BackupPath.Should().Be("/home/user/.local/bin/gitguard.local.bak");
        report.Effective.Should().BeTrue();
    }

    [Fact]
    public void PathUninstallReport_PopulatesAllProperties()
    {
        var report = new PathUninstallReport(
            PLATFORM,
            TARGET_DIRECTORY,
            pathUpdated: false,
            linkRemoved: true,
            profileBlockRemoved: true,
            notInstalled: false);

        report.Platform.Should().Be(PLATFORM);
        report.TargetDirectory.Should().Be(TARGET_DIRECTORY);
        report.PathUpdated.Should().BeFalse();
        report.LinkRemoved.Should().BeTrue();
        report.ProfileBlockRemoved.Should().BeTrue();
        report.NotInstalled.Should().BeFalse();
    }

    [Fact]
    public void PathStatusReport_PopulatesAllProperties()
    {
        var report = new PathStatusReport(
            PLATFORM,
            TARGET_DIRECTORY,
            EXECUTABLE,
            installed: true,
            pathEntryPresent: true,
            effective: false,
            profilePath: "/home/user/.profile",
            profileConfigured: true);

        report.Platform.Should().Be(PLATFORM);
        report.TargetDirectory.Should().Be(TARGET_DIRECTORY);
        report.ExecutablePath.Should().Be(EXECUTABLE);
        report.Installed.Should().BeTrue();
        report.PathEntryPresent.Should().BeTrue();
        report.Effective.Should().BeFalse();
        report.ProfilePath.Should().Be("/home/user/.profile");
        report.ProfileConfigured.Should().BeTrue();
    }

    [Fact]
    public void Reports_BlankRequiredFields_ThrowArgumentException()
    {
        FluentActions.Invoking(() => new PathInstallReport(" ", TARGET_DIRECTORY, EXECUTABLE, false, false, null, false, null, false))
            .Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => new PathInstallReport(PLATFORM, " ", EXECUTABLE, false, false, null, false, null, false))
            .Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => new PathUninstallReport(PLATFORM, " ", false, false, false, false))
            .Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => new PathStatusReport(PLATFORM, TARGET_DIRECTORY, " ", false, false, false, null, false))
            .Should().Throw<ArgumentException>();
    }
}
