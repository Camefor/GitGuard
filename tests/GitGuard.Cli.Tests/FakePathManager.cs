using GitGuard.Core;
using GitGuard.Core.Abstractions;
using GitGuard.Core.Model;

namespace GitGuard.Cli.Tests;

/// <summary>CLI PATH 命令测试用假实现：记录调用参数并返回可配置报告，绝不触碰真实用户 PATH。</summary>
internal sealed class FakePathManager : IPathManager
{
    internal const string PLATFORM = "linux";
    internal const string TARGET_DIRECTORY = "/home/user/.local/bin";
    internal const string EXECUTABLE = "/opt/gitguard/gitguard";

    internal PathInstallReport InstallReport { get; set; } = new PathInstallReport(
        PLATFORM,
        TARGET_DIRECTORY,
        EXECUTABLE,
        alreadyPresent: false,
        pathUpdated: true,
        profilePath: "/home/user/.zshrc",
        profileUpdated: true,
        backupPath: null,
        effective: true);

    internal PathUninstallReport UninstallReport { get; set; } = new PathUninstallReport(
        PLATFORM,
        TARGET_DIRECTORY,
        pathUpdated: false,
        linkRemoved: true,
        profileBlockRemoved: true,
        notInstalled: false);

    internal PathStatusReport StatusReport { get; set; } = new PathStatusReport(
        PLATFORM,
        TARGET_DIRECTORY,
        EXECUTABLE,
        installed: true,
        pathEntryPresent: true,
        effective: true,
        profilePath: "/home/user/.zshrc",
        profileConfigured: true);

    internal Exception? InstallException { get; set; }

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
        if (InstallException is { } exception)
        {
            throw exception;
        }

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
