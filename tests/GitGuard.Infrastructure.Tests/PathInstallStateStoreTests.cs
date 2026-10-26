using FluentAssertions;
using GitGuard.Infrastructure.FileSystem;

namespace GitGuard.Infrastructure.Tests;

/// <summary>
/// PATH 托管状态文件测试（GGD-0061）：原子写入 / 读取失败容错 / 删除；
/// 状态文件仅用于精确卸载，损坏时必须按「未安装」处理而不是阻断命令。
/// </summary>
public sealed class PathInstallStateStoreTests
{
    [Fact]
    public void SaveAndLoad_RoundTripsAllFields()
    {
        using var directory = new TempDirectory();
        string statePath = Path.Combine(directory.Root, "data", "path-install.json");
        var store = new PathInstallStateStore(statePath);
        DateTimeOffset installedAt = DateTimeOffset.UtcNow;

        store.Save(new PathInstallState("linux", "/home/user/.local/bin", "/opt/gitguard/gitguard", "/home/user/.zshrc", installedAt));

        PathInstallState? state = store.Load();
        state.Should().NotBeNull();
        state!.Platform.Should().Be("linux");
        state.TargetDirectory.Should().Be("/home/user/.local/bin");
        state.ExecutablePath.Should().Be("/opt/gitguard/gitguard");
        state.ProfilePath.Should().Be("/home/user/.zshrc");
        state.InstalledAt.Should().BeCloseTo(installedAt, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void Save_NullProfile_RoundTripsAsNull()
    {
        using var directory = new TempDirectory();
        string statePath = Path.Combine(directory.Root, "path-install.json");
        var store = new PathInstallStateStore(statePath);

        store.Save(new PathInstallState("windows", "C:\\Tools", "C:\\Tools\\gitguard.exe", null, DateTimeOffset.UtcNow));

        PathInstallState? state = store.Load();
        state.Should().NotBeNull();
        state!.ProfilePath.Should().BeNull();
    }

    [Fact]
    public void Load_MissingOrCorruptFile_ReturnsNull()
    {
        using var directory = new TempDirectory();
        string statePath = Path.Combine(directory.Root, "path-install.json");
        var store = new PathInstallStateStore(statePath);

        store.Load().Should().BeNull();

        File.WriteAllText(statePath, "{ not json");
        store.Load().Should().BeNull();

        File.WriteAllText(statePath, """{"schema":"other.v1","platform":"linux"}""");
        store.Load().Should().BeNull();
    }

    [Fact]
    public void Delete_RemovesStateFile()
    {
        using var directory = new TempDirectory();
        string statePath = Path.Combine(directory.Root, "path-install.json");
        var store = new PathInstallStateStore(statePath);
        store.Save(new PathInstallState("linux", "/tmp/bin", "/tmp/bin/gitguard", null, DateTimeOffset.UtcNow));

        store.Delete();

        File.Exists(statePath).Should().BeFalse();
        store.Load().Should().BeNull();
    }
}
