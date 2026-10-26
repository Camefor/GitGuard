using FluentAssertions;
using GitGuard.Core;
using GitGuard.Core.Model;
using GitGuard.Infrastructure.FileSystem;

namespace GitGuard.Infrastructure.Tests;

/// <summary>
/// Unix（Linux / macOS）用户 PATH 管理器测试（GGD-0061）：使用临时 HOME / SHELL / 状态文件，
/// 覆盖符号链接创建与冲突接管、profile 标记块写入 / 移除、状态查询与卸载可逆性。
/// </summary>
public sealed class UnixPathManagerTests
{
    [Fact]
    public void Install_CreatesSymlinkAndState()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Skip("Unix 符号链接 PATH 管理不在 Windows 运行");
            return;
        }

        using var directory = new TempDirectory();
        var context = new UnixContext(directory.Root);
        string executable = context.CreateExecutable();

        PathInstallReport report = context.Manager.Install(executable, force: false, updateProfile: false);

        report.Platform.Should().Be(UnixPathManager.PlatformName);
        report.TargetDirectory.Should().Be(Path.Combine(directory.Root, ".local", "bin"));
        report.PathUpdated.Should().BeTrue();
        report.AlreadyPresent.Should().BeFalse();
        report.ProfilePath.Should().BeNull();

        string linkPath = Path.Combine(report.TargetDirectory, "gitguard");
        FileSystemInfo? linkTarget = File.ResolveLinkTarget(linkPath, returnFinalTarget: false);
        linkTarget.Should().NotBeNull();
        linkTarget!.FullName.Should().Be(executable);
        File.Exists(context.StatePath).Should().BeTrue();
    }

    [Fact]
    public void Install_AlreadyPresent_DoesNotRecreateLink()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Skip("Unix 符号链接 PATH 管理不在 Windows 运行");
            return;
        }

        using var directory = new TempDirectory();
        var context = new UnixContext(directory.Root);
        string executable = context.CreateExecutable();

        context.Manager.Install(executable, force: false, updateProfile: false);
        PathInstallReport second = context.Manager.Install(executable, force: false, updateProfile: false);

        second.AlreadyPresent.Should().BeTrue();
        second.PathUpdated.Should().BeFalse();
    }

    [Fact]
    public void Install_UpdateProfile_WritesMarkedBlock()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Skip("Unix 符号链接 PATH 管理不在 Windows 运行");
            return;
        }

        using var directory = new TempDirectory();
        var context = new UnixContext(directory.Root, shellPath: "/bin/zsh");
        string executable = context.CreateExecutable();

        PathInstallReport report = context.Manager.Install(executable, force: false, updateProfile: true);

        report.ProfilePath.Should().Be(Path.Combine(directory.Root, ".zshrc"));
        report.ProfileUpdated.Should().BeTrue();
        File.ReadAllText(report.ProfilePath!).Should().Contain(ShellProfileEditor.BEGIN_MARKER);
    }

    [Fact]
    public void Install_ForeignLinkWithoutForce_ThrowsEntryConflict()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Skip("Unix 符号链接 PATH 管理不在 Windows 运行");
            return;
        }

        using var directory = new TempDirectory();
        var context = new UnixContext(directory.Root);
        string executable = context.CreateExecutable();
        string foreign = Path.Combine(directory.Root, "other-tool");
        File.WriteAllText(foreign, "other");
        string linkPath = Path.Combine(directory.Root, ".local", "bin", "gitguard");
        string? linkDirectory = Path.GetDirectoryName(linkPath);
        Directory.CreateDirectory(linkDirectory!);
        File.CreateSymbolicLink(linkPath, foreign);

        Action act = () => context.Manager.Install(executable, force: false, updateProfile: false);

        act.Should().Throw<PathException>()
            .Which.ErrorCode.Should().Be(PathException.ENTRY_CONFLICT);
    }

    [Fact]
    public void Install_ForeignLinkWithForce_BacksUpAndTakesOver()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Skip("Unix 符号链接 PATH 管理不在 Windows 运行");
            return;
        }

        using var directory = new TempDirectory();
        var context = new UnixContext(directory.Root);
        string executable = context.CreateExecutable();
        string foreign = Path.Combine(directory.Root, "other-tool");
        File.WriteAllText(foreign, "other");
        string linkPath = Path.Combine(directory.Root, ".local", "bin", "gitguard");
        string? linkDirectory = Path.GetDirectoryName(linkPath);
        Directory.CreateDirectory(linkDirectory!);
        File.CreateSymbolicLink(linkPath, foreign);

        PathInstallReport report = context.Manager.Install(executable, force: true, updateProfile: false);

        report.BackupPath.Should().Be(linkPath + ".local.bak");
        File.Exists(report.BackupPath).Should().BeTrue();
        FileSystemInfo? takenOver = File.ResolveLinkTarget(linkPath, returnFinalTarget: false);
        takenOver.Should().NotBeNull();
        takenOver!.FullName.Should().Be(executable);
    }

    [Fact]
    public void Uninstall_RemovesManagedLinkAndProfileBlock()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Skip("Unix 符号链接 PATH 管理不在 Windows 运行");
            return;
        }

        using var directory = new TempDirectory();
        var context = new UnixContext(directory.Root, shellPath: "/bin/bash");
        string executable = context.CreateExecutable();
        File.WriteAllText(Path.Combine(directory.Root, ".bashrc"), "export EDITOR=vim\n");
        PathInstallReport install = context.Manager.Install(executable, force: false, updateProfile: true);

        PathUninstallReport report = context.Manager.Uninstall(executable);

        report.NotInstalled.Should().BeFalse();
        report.LinkRemoved.Should().BeTrue();
        report.ProfileBlockRemoved.Should().BeTrue();
        File.Exists(Path.Combine(install.TargetDirectory, "gitguard")).Should().BeFalse();
        string profile = File.ReadAllText(install.ProfilePath!);
        profile.Should().Contain("export EDITOR=vim").And.NotContain(ShellProfileEditor.BEGIN_MARKER);
    }

    [Fact]
    public void GetStatus_ReportsManagedStateAndEffectiveness()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Skip("Unix 符号链接 PATH 管理不在 Windows 运行");
            return;
        }

        using var directory = new TempDirectory();
        var context = new UnixContext(directory.Root, shellPath: "/bin/zsh");
        string executable = context.CreateExecutable();
        PathInstallReport install = context.Manager.Install(executable, force: false, updateProfile: true);
        context.CurrentPath = install.TargetDirectory + ":/usr/bin";

        PathStatusReport report = context.Manager.GetStatus(executable);

        report.Installed.Should().BeTrue();
        report.PathEntryPresent.Should().BeTrue();
        report.Effective.Should().BeTrue();
        report.ProfileConfigured.Should().BeTrue();
    }

    [Fact]
    public void Uninstall_NothingManaged_ReturnsNotInstalled()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Skip("Unix 符号链接 PATH 管理不在 Windows 运行");
            return;
        }

        using var directory = new TempDirectory();
        var context = new UnixContext(directory.Root);

        PathUninstallReport report = context.Manager.Uninstall(context.CreateExecutable());

        report.NotInstalled.Should().BeTrue();
        report.LinkRemoved.Should().BeFalse();
        report.ProfileBlockRemoved.Should().BeFalse();
    }

    private sealed class UnixContext
    {
        internal UnixContext(string home, string? shellPath = null)
        {
            StatePath = Path.Combine(home, "data", "path-install.json");
            CurrentPath = "/usr/bin";
            Manager = new UnixPathManager(home, shellPath, StatePath, () => CurrentPath);
        }

        internal string StatePath { get; }

        internal string? CurrentPath { get; set; }

        internal UnixPathManager Manager { get; }

        internal string CreateExecutable()
        {
            string? dataDirectory = Path.GetDirectoryName(StatePath);
            string path = Path.Combine(dataDirectory!, "gitguard");
            Directory.CreateDirectory(dataDirectory!);
            File.WriteAllText(path, "stub");
            return path;
        }
    }
}
