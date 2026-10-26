using FluentAssertions;
using GitGuard.Application.Paths;
using GitGuard.Core.Abstractions;
using GitGuard.Core.Model;

namespace GitGuard.Application.Tests;

/// <summary>PATH 管理服务测试（GGD-0061）：Application 层薄封装必须原样转发参数并返回平台报告。</summary>
public sealed class PathManagementServiceTests
{
    private const string EXECUTABLE = "/opt/gitguard/gitguard";

    [Fact]
    public void Install_ForwardsArgumentsAndReturnsReport()
    {
        var manager = new RecordingPathManager();
        var service = new PathManagementService(manager);

        PathInstallReport report = service.Install(EXECUTABLE, force: true, updateProfile: true);

        report.Should().BeSameAs(manager.InstallReport);
        manager.InstallCalls.Should().Be(1);
        manager.LastExecutablePath.Should().Be(EXECUTABLE);
        manager.LastForce.Should().BeTrue();
        manager.LastUpdateProfile.Should().BeTrue();
    }

    [Fact]
    public void Uninstall_ForwardsExecutableAndReturnsReport()
    {
        var manager = new RecordingPathManager();
        var service = new PathManagementService(manager);

        PathUninstallReport report = service.Uninstall(EXECUTABLE);

        report.Should().BeSameAs(manager.UninstallReport);
        manager.UninstallCalls.Should().Be(1);
        manager.LastExecutablePath.Should().Be(EXECUTABLE);
    }

    [Fact]
    public void GetStatus_ForwardsExecutableAndReturnsReport()
    {
        var manager = new RecordingPathManager();
        var service = new PathManagementService(manager);

        PathStatusReport report = service.GetStatus(EXECUTABLE);

        report.Should().BeSameAs(manager.StatusReport);
        manager.StatusCalls.Should().Be(1);
        manager.LastExecutablePath.Should().Be(EXECUTABLE);
    }

    [Fact]
    public void Ctor_NullManager_Throws()
    {
        FluentActions.Invoking(() => new PathManagementService(null!)).Should().Throw<ArgumentNullException>();
    }

    private sealed class RecordingPathManager : IPathManager
    {
        internal PathInstallReport InstallReport { get; } = new PathInstallReport(
            "linux",
            "/home/user/.local/bin",
            EXECUTABLE,
            false,
            true,
            null,
            false,
            null,
            false);

        internal PathUninstallReport UninstallReport { get; } = new PathUninstallReport(
            "linux",
            "/home/user/.local/bin",
            true,
            true,
            false,
            false);

        internal PathStatusReport StatusReport { get; } = new PathStatusReport(
            "linux",
            "/home/user/.local/bin",
            EXECUTABLE,
            true,
            true,
            false,
            null,
            false);

        internal int InstallCalls { get; private set; }

        internal int UninstallCalls { get; private set; }

        internal int StatusCalls { get; private set; }

        internal string? LastExecutablePath { get; private set; }

        internal bool LastForce { get; private set; }

        internal bool LastUpdateProfile { get; private set; }

        public PathInstallReport Install(string executablePath, bool force, bool updateProfile)
        {
            InstallCalls++;
            LastExecutablePath = executablePath;
            LastForce = force;
            LastUpdateProfile = updateProfile;
            return InstallReport;
        }

        public PathUninstallReport Uninstall(string executablePath)
        {
            UninstallCalls++;
            LastExecutablePath = executablePath;
            return UninstallReport;
        }

        public PathStatusReport GetStatus(string executablePath)
        {
            StatusCalls++;
            LastExecutablePath = executablePath;
            return StatusReport;
        }
    }
}
