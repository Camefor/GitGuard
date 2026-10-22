using FluentAssertions;
using GitGuard.Infrastructure.FileSystem;

namespace GitGuard.Infrastructure.Tests;

/// <summary>
/// 用户数据目录解析测试（05-数据库设计 §1、07-缓存设计 §3、09-日志设计 §7、10-异常处理设计 §8）：
/// 路径均为纯计算，派生路径必须严格落在显式注入的数据目录之下（测试隔离依赖此契约）。
/// </summary>
public sealed class UserDataPathsTests
{
    [Fact]
    public void ExplicitDirectories_DeriveFixedFilePaths()
    {
        using var directory = new TempDirectory();
        string data = directory.Root;
        string config = Path.Combine(directory.Root, "config");
        var paths = new UserDataPaths(data, config);

        paths.DataDirectory.Should().Be(Path.GetFullPath(data));
        paths.AuditDatabasePath.Should().Be(Path.Combine(data, "audit.db"));
        paths.CacheDirectory.Should().Be(Path.Combine(data, "cache"));
        paths.FingerprintSaltPath.Should().Be(Path.Combine(data, "cache", "fingerprint.salt"));
        paths.LogDirectory.Should().Be(Path.Combine(data, "logs"));
        paths.LogFilePath.Should().Be(Path.Combine(data, "logs", "gitguard.log"));
        paths.CrashDirectory.Should().Be(Path.Combine(data, "crash"));
        paths.UserConfigDirectory.Should().Be(Path.GetFullPath(config));
        paths.UserConfigPath.Should().Be(Path.Combine(config, "config.yml"));
    }

    [Fact]
    public void TrailingSeparators_AreTrimmed()
    {
        using var directory = new TempDirectory();

        var paths = new UserDataPaths(directory.Root + Path.DirectorySeparatorChar, directory.Root + Path.DirectorySeparatorChar);

        paths.DataDirectory.Should().Be(Path.GetFullPath(directory.Root));
        paths.UserConfigDirectory.Should().Be(Path.GetFullPath(directory.Root));
        paths.UserConfigPath.Should().Be(Path.Combine(Path.GetFullPath(directory.Root), "config.yml"));
    }

    [Fact]
    public void Default_ResolvesPlatformConventionPaths()
    {
        UserDataPaths paths = UserDataPaths.Default;

        paths.DataDirectory.Should().NotBeNullOrWhiteSpace();
        Path.IsPathFullyQualified(paths.DataDirectory).Should().BeTrue();
        paths.AuditDatabasePath.Should().StartWith(paths.DataDirectory);
        paths.UserConfigPath.Should().EndWith("config.yml");
    }

    [Fact]
    public void Default_OnWindows_PlacesLogUnderLocalAppDataGitGuard()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Skip("Windows 默认路径断言仅在 Windows 运行（09-日志设计 §7）");
        }

        string? localAppData = Environment.GetEnvironmentVariable("LOCALAPPDATA");
        string root = string.IsNullOrWhiteSpace(localAppData)
            ? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
            : localAppData;

        UserDataPaths.Default.LogFilePath.Should().Be(
            Path.Combine(root, "GitGuard", "logs", "gitguard.log"));
    }

    [Fact]
    public void Default_OnMacOS_PlacesLogUnderApplicationSupport()
    {
        if (!OperatingSystem.IsMacOS())
        {
            Assert.Skip("macOS 默认路径断言仅在 macOS 运行（09-日志设计 §7）");
        }

        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        UserDataPaths.Default.LogFilePath.Should().Be(
            Path.Combine(home, "Library", "Application Support", "GitGuard", "logs", "gitguard.log"));
    }

    [Fact]
    public void Default_OnLinux_PlacesLogUnderXdgDataHome()
    {
        if (OperatingSystem.IsWindows() || OperatingSystem.IsMacOS())
        {
            Assert.Skip("Linux 默认路径断言仅在 Linux 运行（09-日志设计 §7）");
        }

        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string? xdgDataHome = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        string dataRoot = string.IsNullOrWhiteSpace(xdgDataHome)
            ? Path.Combine(home, ".local", "share")
            : Path.GetFullPath(xdgDataHome);

        UserDataPaths.Default.LogFilePath.Should().Be(
            Path.Combine(dataRoot, "gitguard", "logs", "gitguard.log"));
    }

    [Fact]
    public void ExplicitArguments_MustNotBeBlank()
    {
        FluentActions.Invoking(() => new UserDataPaths("  ", null)).Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => new UserDataPaths(null, "  ")).Should().Throw<ArgumentException>();
    }
}
