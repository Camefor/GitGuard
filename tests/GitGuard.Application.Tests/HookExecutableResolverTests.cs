using FluentAssertions;
using GitGuard.Application.Hooks;
using GitGuard.Core;

namespace GitGuard.Application.Tests;

/// <summary>
/// GitGuard CLI 路径解析测试（GGD-0054）：配置 → 宿主进程 → PATH → 已知安装位置 → 未找到；
/// 配置路径缺失必须 FailClosed，桌面端不得回退自身 GUI 进程。
/// </summary>
public sealed class HookExecutableResolverTests
{
    private static string ExecutableName => OperatingSystem.IsWindows() ? "gitguard.exe" : "gitguard";

    [Fact]
    public void Resolve_ConfiguredPath_WinsOverPreferredAndPath()
    {
        using var temp = new TempDirectory();
        string configured = CreateFile(temp.Root, "configured", "gitguard.exe");
        string preferred = CreateFile(temp.Root, "preferred", "gitguard.exe");
        string pathDirectory = Path.Combine(temp.Root, "path");
        CreateFile(pathDirectory, ExecutableName, "gitguard");
        var resolver = new HookExecutableResolver(
            temp.Root,
            pathEnvironment: pathDirectory,
            knownInstallLocations: Array.Empty<string>());

        string? result = resolver.Resolve(configured, preferred);

        result.Should().Be(Path.GetFullPath(configured));
    }

    [Fact]
    public void Resolve_RelativeConfiguredPath_ResolvesAgainstWorkingDirectory()
    {
        using var temp = new TempDirectory();
        CreateFile(temp.Root, "tools/gitguard", "gitguard");
        var resolver = new HookExecutableResolver(
            temp.Root,
            pathEnvironment: string.Empty,
            knownInstallLocations: Array.Empty<string>());

        string? result = resolver.Resolve("tools/gitguard");

        result.Should().Be(Path.GetFullPath(Path.Combine(temp.Root, "tools", "gitguard")));
    }

    [Fact]
    public void Resolve_ConfiguredPathMissing_ThrowsConfigurationException()
    {
        using var temp = new TempDirectory();
        var resolver = new HookExecutableResolver(
            temp.Root,
            pathEnvironment: string.Empty,
            knownInstallLocations: Array.Empty<string>());
        string missing = Path.Combine(temp.Root, "missing", "gitguard.exe");

        Action act = () => _ = resolver.Resolve(missing);

        act.Should().Throw<ConfigurationException>()
            .Which.Message.Should().Contain("hooks.gitguard_executable_path")
            .And.Contain("不存在");
    }

    [Fact]
    public void Resolve_WithoutConfigured_UsesPreferredProcessPath()
    {
        using var temp = new TempDirectory();
        string preferred = CreateFile(temp.Root, "bin", "gitguard.exe");
        var resolver = new HookExecutableResolver(
            temp.Root,
            pathEnvironment: string.Empty,
            knownInstallLocations: Array.Empty<string>());

        string? result = resolver.Resolve(configuredPath: null, preferred);

        result.Should().Be(Path.GetFullPath(preferred));
    }

    [Fact]
    public void Resolve_WithoutConfiguredOrPreferred_FindsOnPath()
    {
        using var temp = new TempDirectory();
        string pathDirectory = Path.Combine(temp.Root, "path");
        string executable = CreateFile(pathDirectory, ExecutableName, "gitguard");
        var resolver = new HookExecutableResolver(
            temp.Root,
            pathEnvironment: pathDirectory,
            knownInstallLocations: Array.Empty<string>());

        string? result = resolver.Resolve(configuredPath: null);

        result.Should().Be(Path.GetFullPath(executable));
    }

    [Fact]
    public void Resolve_PathMiss_FallsBackToKnownInstallLocation()
    {
        using var temp = new TempDirectory();
        string known = CreateFile(temp.Root, "known", "gitguard.exe");
        var resolver = new HookExecutableResolver(
            temp.Root,
            pathEnvironment: string.Empty,
            knownInstallLocations: new[] { known });

        string? result = resolver.Resolve(configuredPath: null);

        result.Should().Be(Path.GetFullPath(known));
    }

    [Fact]
    public void Resolve_NoCandidate_ReturnsNullForDesktopPicker()
    {
        using var temp = new TempDirectory();
        var resolver = new HookExecutableResolver(
            temp.Root,
            pathEnvironment: string.Empty,
            knownInstallLocations: Array.Empty<string>());

        string? result = resolver.Resolve(configuredPath: null);

        result.Should().BeNull();
    }

    private static string CreateFile(string directory, string relativePath, string content)
    {
        string path = Path.Combine(directory, relativePath);
        string? parent = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(parent))
        {
            Directory.CreateDirectory(parent);
        }

        File.WriteAllText(path, content);
        return path;
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Root = Path.Combine(Path.GetTempPath(), "gitguard-hook-resolver-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
        }

        public string Root { get; }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Root, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // 临时目录清理失败不影响测试结论
            }
        }
    }
}
