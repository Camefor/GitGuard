using System.Text.Json;
using FluentAssertions;
using GitGuard.Core;

namespace GitGuard.Cli.Tests;

/// <summary>
/// <c>path install / uninstall / status</c> 端到端测试（GGD-0061 / <c>06-API接口设计</c> §5.8）：
/// 全部使用注入的假 <see cref="IPathManager"/>，不触碰真实用户 PATH；覆盖退出码 0 / 5 / 61 / 60、
/// 三种 JSON Schema 与中英文文本报告。
/// </summary>
public sealed class PathCommandTests
{
    private static readonly string[] _installCommand = new[] { "path", "install" };

    [Fact]
    public async Task Install_TextReport_RendersManagedFieldsAndForwardsFlags()
    {
        var manager = new FakePathManager();
        using var harness = new CliHarness(pathManager: manager);

        int exitCode = await harness.RunAsync("path", "install", "--force", "--update-profile");

        exitCode.Should().Be(ExitCodes.SUCCESS);
        harness.Output.Should().BeEmpty();
        harness.Error.Should().Contain("GitGuard CLI 已加入用户 PATH")
            .And.Contain(FakePathManager.TARGET_DIRECTORY)
            .And.Contain("/home/user/.zshrc");
        manager.InstallCalls.Should().Be(1);
        manager.LastForce.Should().BeTrue();
        manager.LastUpdateProfile.Should().BeTrue();
        manager.LastExecutablePath.Should().Be(Path.GetFullPath(CliHarness.TEST_EXECUTABLE));
    }

    [Fact]
    public async Task Install_AlreadyPresentAndNotEffective_RendersHints()
    {
        var manager = new FakePathManager
        {
            InstallReport = new GitGuard.Core.Model.PathInstallReport(
                FakePathManager.PLATFORM,
                FakePathManager.TARGET_DIRECTORY,
                FakePathManager.EXECUTABLE,
                alreadyPresent: true,
                pathUpdated: false,
                profilePath: null,
                profileUpdated: false,
                backupPath: null,
                effective: false),
        };
        using var harness = new CliHarness(pathManager: manager);

        int exitCode = await harness.RunAsync("path", "install");

        exitCode.Should().Be(ExitCodes.SUCCESS);
        harness.Error.Should().Contain("目标目录已在用户 PATH 中")
            .And.Contain("PATH 更新将在新终端生效");
    }

    [Fact]
    public async Task Install_Json_MatchesSchema()
    {
        var manager = new FakePathManager();
        using var harness = new CliHarness(pathManager: manager);

        int exitCode = await harness.RunAsync("path", "install", "--format", "json");

        exitCode.Should().Be(ExitCodes.SUCCESS);
        using var document = JsonDocument.Parse(harness.Output);
        JsonElement root = document.RootElement;
        root.GetProperty("schema").GetString().Should().Be("gitguard.path.install.v1");
        root.GetProperty("platform").GetString().Should().Be(FakePathManager.PLATFORM);
        root.GetProperty("target_directory").GetString().Should().Be(FakePathManager.TARGET_DIRECTORY);
        root.GetProperty("executable").GetString().Should().Be(FakePathManager.EXECUTABLE);
        root.GetProperty("already_present").GetBoolean().Should().BeFalse();
        root.GetProperty("path_updated").GetBoolean().Should().BeTrue();
        root.GetProperty("profile_path").GetString().Should().Be("/home/user/.zshrc");
        root.GetProperty("profile_updated").GetBoolean().Should().BeTrue();
        root.GetProperty("effective").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task Install_NonGitguardProcess_ReturnsPathUnsupported()
    {
        var manager = new FakePathManager();
        using var harness = new CliHarness(pathManager: manager);

        int exitCode = await harness.RunAsync(
            _installCommand,
            string.Empty,
            executablePath: "/usr/bin/dotnet");

        exitCode.Should().Be(ExitCodes.PATH_UNSUPPORTED);
        harness.Error.Should().Contain(PathException.UNSUPPORTED);
        manager.InstallCalls.Should().Be(0, "非 gitguard 进程不得触碰用户 PATH");
    }

    [Fact]
    public async Task Install_EntryConflict_ReturnsPathEntryConflict()
    {
        var manager = new FakePathManager
        {
            InstallException = new PathException(PathException.ENTRY_CONFLICT, "目标符号链接已存在"),
        };
        using var harness = new CliHarness(pathManager: manager);

        int exitCode = await harness.RunAsync("path", "install");

        exitCode.Should().Be(ExitCodes.PATH_ENTRY_CONFLICT);
        harness.Error.Should().Contain(PathException.ENTRY_CONFLICT)
            .And.Contain("目标符号链接已存在");
    }

    [Fact]
    public async Task Uninstall_TextReport_RendersRemovalSummary()
    {
        var manager = new FakePathManager();
        using var harness = new CliHarness(pathManager: manager);

        int exitCode = await harness.RunAsync("path", "uninstall");

        exitCode.Should().Be(ExitCodes.SUCCESS);
        harness.Error.Should().Contain("GitGuard CLI 已从用户 PATH 移除")
            .And.Contain("符号链接已删除")
            .And.Contain("profile 标记块已移除");
        manager.UninstallCalls.Should().Be(1);
    }

    [Fact]
    public async Task Uninstall_NotInstalled_ReturnsHookNotInstalled()
    {
        var manager = new FakePathManager
        {
            UninstallReport = new GitGuard.Core.Model.PathUninstallReport(
                FakePathManager.PLATFORM,
                FakePathManager.TARGET_DIRECTORY,
                pathUpdated: false,
                linkRemoved: false,
                profileBlockRemoved: false,
                notInstalled: true),
        };
        using var harness = new CliHarness(pathManager: manager);

        int exitCode = await harness.RunAsync("path", "uninstall");

        exitCode.Should().Be(ExitCodes.HOOK_NOT_INSTALLED);
        harness.Error.Should().Contain("未发现 GitGuard PATH 管理痕迹");
    }

    [Fact]
    public async Task Uninstall_Json_MatchesSchema()
    {
        var manager = new FakePathManager();
        using var harness = new CliHarness(pathManager: manager);

        int exitCode = await harness.RunAsync("path", "uninstall", "--format", "json");

        exitCode.Should().Be(ExitCodes.SUCCESS);
        using var document = JsonDocument.Parse(harness.Output);
        JsonElement root = document.RootElement;
        root.GetProperty("schema").GetString().Should().Be("gitguard.path.uninstall.v1");
        root.GetProperty("platform").GetString().Should().Be(FakePathManager.PLATFORM);
        root.GetProperty("target_directory").GetString().Should().Be(FakePathManager.TARGET_DIRECTORY);
        root.GetProperty("path_updated").GetBoolean().Should().BeFalse();
        root.GetProperty("link_removed").GetBoolean().Should().BeTrue();
        root.GetProperty("profile_block_removed").GetBoolean().Should().BeTrue();
        root.GetProperty("not_installed").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task Status_TextReport_RendersInstallationState()
    {
        var manager = new FakePathManager();
        using var harness = new CliHarness(pathManager: manager);

        int exitCode = await harness.RunAsync("path", "status");

        exitCode.Should().Be(ExitCodes.SUCCESS);
        harness.Error.Should().Contain("GitGuard PATH 状态")
            .And.Contain("已安装（GitGuard 托管）")
            .And.Contain("/home/user/.zshrc");
        manager.StatusCalls.Should().Be(1);
    }

    [Fact]
    public async Task Status_Json_MatchesSchema()
    {
        var manager = new FakePathManager();
        using var harness = new CliHarness(pathManager: manager);

        int exitCode = await harness.RunAsync("path", "status", "--format", "json");

        exitCode.Should().Be(ExitCodes.SUCCESS);
        using var document = JsonDocument.Parse(harness.Output);
        JsonElement root = document.RootElement;
        root.GetProperty("schema").GetString().Should().Be("gitguard.path.status.v1");
        root.GetProperty("platform").GetString().Should().Be(FakePathManager.PLATFORM);
        root.GetProperty("target_directory").GetString().Should().Be(FakePathManager.TARGET_DIRECTORY);
        root.GetProperty("executable").GetString().Should().Be(FakePathManager.EXECUTABLE);
        root.GetProperty("installed").GetBoolean().Should().BeTrue();
        root.GetProperty("path_entry_present").GetBoolean().Should().BeTrue();
        root.GetProperty("effective").GetBoolean().Should().BeTrue();
        root.GetProperty("profile_path").GetString().Should().Be("/home/user/.zshrc");
        root.GetProperty("profile_configured").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task Install_EnglishLocalization_RendersEnglishHeader()
    {
        var manager = new FakePathManager();
        using var harness = new CliHarness(pathManager: manager);

        int exitCode = await harness.RunAsync(
            _installCommand,
            string.Empty,
            languageEnvironment: "en");

        exitCode.Should().Be(ExitCodes.SUCCESS);
        harness.Error.Should().Contain("GitGuard CLI added to the user PATH")
            .And.Contain("Executable");
    }
}
