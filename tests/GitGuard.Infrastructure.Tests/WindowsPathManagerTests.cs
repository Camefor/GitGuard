using System.Runtime.Versioning;
using FluentAssertions;
using GitGuard.Core;
using GitGuard.Core.Model;
using GitGuard.Infrastructure.FileSystem;
using Microsoft.Win32;

namespace GitGuard.Infrastructure.Tests;

/// <summary>
/// Windows 用户 PATH 管理器测试（GGD-0061）：使用临时 HKCU 子键与临时状态文件，
/// 绝不读写真实 <c>HKCU\Environment\Path</c>；安装去重 / 超长保护 / 卸载可逆 / 状态查询。
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsPathManagerTests
{
    [Fact]
    public void Install_AppendsDirectoryAndSavesState()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Skip("Windows 注册表 PATH 管理仅在 Windows 运行");
            return;
        }

        using var directory = new TempDirectory();
        using var context = new RegistryContext(directory.Root);
        string executable = Path.Combine(directory.Root, "bin", "gitguard.exe");

        PathInstallReport report = context.Manager.Install(executable, force: false, updateProfile: false);

        report.Platform.Should().Be("windows");
        report.TargetDirectory.Should().Be(Path.Combine(directory.Root, "bin"));
        report.PathUpdated.Should().BeTrue();
        report.AlreadyPresent.Should().BeFalse();
        report.ProfilePath.Should().BeNull();
        report.Effective.Should().BeFalse("当前进程 PATH 由测试显式提供");

        string? stored = context.Store.Read();
        stored.Should().NotBeNull();
        PathEnvironmentVariable.Split(stored, ';').Should().Contain(report.TargetDirectory);
        File.Exists(context.StatePath).Should().BeTrue();
    }

    [Fact]
    public void Install_AlreadyPresent_DoesNotDuplicateEntry()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Skip("Windows 注册表 PATH 管理仅在 Windows 运行");
            return;
        }

        using var directory = new TempDirectory();
        using var context = new RegistryContext(directory.Root);
        string executable = Path.Combine(directory.Root, "bin", "gitguard.exe");

        context.Manager.Install(executable, force: false, updateProfile: false);
        PathInstallReport second = context.Manager.Install(executable, force: false, updateProfile: false);

        second.AlreadyPresent.Should().BeTrue();
        second.PathUpdated.Should().BeFalse();
        string[] entries = PathEnvironmentVariable.Split(context.Store.Read(), ';').ToArray();
        entries.Count(entry => string.Equals(entry, second.TargetDirectory, StringComparison.OrdinalIgnoreCase))
            .Should().Be(1);
    }

    [Fact]
    public void Install_PathTooLong_ThrowsTooLongWithoutWriting()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Skip("Windows 注册表 PATH 管理仅在 Windows 运行");
            return;
        }

        using var directory = new TempDirectory();
        using var context = new RegistryContext(directory.Root);
        context.Store.Write(new string('a', PathEnvironmentVariable.WINDOWS_MAX_LENGTH - 5));

        Action act = () => context.Manager.Install(
            Path.Combine(directory.Root, "bin", "gitguard.exe"),
            force: false,
            updateProfile: false);

        act.Should().Throw<PathException>()
            .Which.ErrorCode.Should().Be(PathException.TOO_LONG);
        context.Store.Read().Should().Be(new string('a', PathEnvironmentVariable.WINDOWS_MAX_LENGTH - 5));
    }

    [Fact]
    public void Uninstall_RemovesManagedEntryAndState_PreservesOtherEntries()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Skip("Windows 注册表 PATH 管理仅在 Windows 运行");
            return;
        }

        using var directory = new TempDirectory();
        using var context = new RegistryContext(directory.Root);
        string executable = Path.Combine(directory.Root, "bin", "gitguard.exe");
        context.Store.Write("C:\\Windows;C:\\Tools");
        context.Manager.Install(executable, force: false, updateProfile: false);

        PathUninstallReport report = context.Manager.Uninstall(executable);

        report.PathUpdated.Should().BeTrue();
        report.NotInstalled.Should().BeFalse();
        context.Store.Read().Should().Be("C:\\Windows;C:\\Tools");
        File.Exists(context.StatePath).Should().BeFalse();
    }

    [Fact]
    public void Uninstall_NothingManaged_ReturnsNotInstalled()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Skip("Windows 注册表 PATH 管理仅在 Windows 运行");
            return;
        }

        using var directory = new TempDirectory();
        using var context = new RegistryContext(directory.Root);

        PathUninstallReport report = context.Manager.Uninstall(Path.Combine(directory.Root, "bin", "gitguard.exe"));

        report.NotInstalled.Should().BeTrue();
        report.PathUpdated.Should().BeFalse();
    }

    [Fact]
    public void GetStatus_ReportsPersistedEntryAndEffectiveState()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Skip("Windows 注册表 PATH 管理仅在 Windows 运行");
            return;
        }

        using var directory = new TempDirectory();
        using var context = new RegistryContext(directory.Root);
        string executable = Path.Combine(directory.Root, "bin", "gitguard.exe");

        PathStatusReport before = context.Manager.GetStatus(executable);
        before.Installed.Should().BeFalse();
        before.PathEntryPresent.Should().BeFalse();

        context.Manager.Install(executable, force: false, updateProfile: false);
        context.CurrentPath = context.Manager.GetStatus(executable).TargetDirectory;
        PathStatusReport after = context.Manager.GetStatus(executable);

        after.Installed.Should().BeTrue();
        after.PathEntryPresent.Should().BeTrue();
        after.Effective.Should().BeTrue();
    }

    /// <summary>临时注册表键与状态文件上下文，析构时删除测试子键。</summary>
    [SupportedOSPlatform("windows")]
    private sealed class RegistryContext : IDisposable
    {
        private readonly string _keyPath;

        internal RegistryContext(string root)
        {
            _keyPath = $@"Software\GitGuardTests\PathManager\{Guid.NewGuid():N}";
            Store = new WindowsRegistryPathStore(_keyPath);
            StatePath = Path.Combine(root, "data", "path-install.json");
            CurrentPath = "C:\\Windows";
            Manager = new WindowsPathManager(Store, StatePath, () => CurrentPath);
        }

        internal WindowsRegistryPathStore Store { get; }

        internal string StatePath { get; }

        internal string? CurrentPath { get; set; }

        internal WindowsPathManager Manager { get; }

        public void Dispose()
        {
            try
            {
                Registry.CurrentUser.DeleteSubKeyTree(_keyPath, throwOnMissingSubKey: false);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException)
            {
                // 测试清理失败不影响断言结论
            }
        }
    }
}
