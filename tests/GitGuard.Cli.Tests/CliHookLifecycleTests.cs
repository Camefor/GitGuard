using System.Text.Json;
using FluentAssertions;

namespace GitGuard.Cli.Tests;

/// <summary>
/// <c>install</c> / <c>uninstall</c> / <c>status</c> 端到端测试（TC-FR-010 / TC-FR-011 / TC-FR-012，
/// <c>06-API接口设计</c> §5.1~§5.3）：退出码 0 / 5 / 6 与 <c>gitguard.install.v1</c> /
/// <c>gitguard.uninstall.v1</c> / <c>gitguard.status.v1</c> JSON 契约。
/// </summary>
public sealed class CliHookLifecycleTests
{
    [Fact]
    public async Task Install_WritesManagedDispatchScriptsForBothHooks()
    {
        using var harness = new CliHarness();

        int exitCode = await harness.RunAsync("install");

        exitCode.Should().Be(ExitCodes.SUCCESS);
        harness.Output.Should().BeEmpty();
        harness.Error.Should().Contain("GitGuard Hook 安装完成");

        foreach (string hook in new[] { "pre-commit", "pre-push" })
        {
            string script = File.ReadAllText(harness.ResolvePath($".git/hooks/{hook}"));
            script.Should().Contain($"# gitguard-managed v{CliHarness.TEST_VERSION}");
            script.Should().Contain(CliHarness.TEST_EXECUTABLE);
        }
    }

    [Fact]
    public async Task Install_JsonOutput_MatchesInstallSchema()
    {
        using var harness = new CliHarness();

        int exitCode = await harness.RunAsync("install", "--hook", "pre-commit", "--format", "json");

        exitCode.Should().Be(ExitCodes.SUCCESS);
        using var document = JsonDocument.Parse(harness.Output);
        JsonElement root = document.RootElement;
        root.GetProperty("schema").GetString().Should().Be("gitguard.install.v1");
        root.GetProperty("version").GetString().Should().Be(CliHarness.TEST_VERSION);
        root.GetProperty("repo_path").GetString().Should().Be(harness.Root);

        JsonElement hook = root.GetProperty("hooks").EnumerateArray().Single();
        hook.GetProperty("hook").GetString().Should().Be("pre-commit");
        hook.GetProperty("disposition").GetString().Should().Be("Installed");
        hook.GetProperty("hook_path").GetString().Should().NotBeNullOrWhiteSpace();
        hook.GetProperty("backup_path").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task Uninstall_WithoutInstallation_ReturnsHookNotInstalled()
    {
        using var harness = new CliHarness();

        int exitCode = await harness.RunAsync("uninstall", "--format", "json");

        exitCode.Should().Be(ExitCodes.HOOK_NOT_INSTALLED);
        using var document = JsonDocument.Parse(harness.Output);
        JsonElement root = document.RootElement;
        root.GetProperty("schema").GetString().Should().Be("gitguard.uninstall.v1");
        root.GetProperty("not_installed").GetBoolean().Should().BeTrue();
        root.GetProperty("hooks").EnumerateArray()
            .Should().OnlyContain(hook => hook.GetProperty("disposition").GetString() == "NotInstalled");
    }

    [Fact]
    public async Task InstallThenUninstall_RemovesDispatchScript()
    {
        using var harness = new CliHarness();
        (await harness.RunAsync("install")).Should().Be(ExitCodes.SUCCESS);

        int exitCode = await harness.RunAsync("uninstall", "--format", "json");

        exitCode.Should().Be(ExitCodes.SUCCESS);
        using var document = JsonDocument.Parse(harness.Output);
        document.RootElement.GetProperty("hooks").EnumerateArray()
            .Should().OnlyContain(hook => hook.GetProperty("disposition").GetString() == "Removed");
        File.Exists(harness.ResolvePath(".git/hooks/pre-commit")).Should().BeFalse();
        File.Exists(harness.ResolvePath(".git/hooks/pre-push")).Should().BeFalse();
    }

    [Fact]
    public async Task Status_BeforeInstall_ReportsMissingHooksWithoutAttention()
    {
        using var harness = new CliHarness();

        int exitCode = await harness.RunAsync("status", "--format", "json");

        exitCode.Should().Be(ExitCodes.SUCCESS);
        using var document = JsonDocument.Parse(harness.Output);
        JsonElement root = document.RootElement;
        root.GetProperty("schema").GetString().Should().Be("gitguard.status.v1");
        root.GetProperty("gitguard_version").GetString().Should().Be(CliHarness.TEST_VERSION);
        root.GetProperty("needs_attention").GetBoolean().Should().BeFalse();
        root.GetProperty("hooks").EnumerateArray()
            .Should().OnlyContain(hook => hook.GetProperty("state").GetString() == "Missing");
    }

    [Fact]
    public async Task Status_AfterInstall_ReportsManagedHooks()
    {
        using var harness = new CliHarness();
        (await harness.RunAsync("install")).Should().Be(ExitCodes.SUCCESS);

        int exitCode = await harness.RunAsync("status", "--format", "json");

        exitCode.Should().Be(ExitCodes.SUCCESS);
        using var document = JsonDocument.Parse(harness.Output);
        JsonElement hook = document.RootElement.GetProperty("hooks").EnumerateArray().First();
        hook.GetProperty("state").GetString().Should().Be("Managed");
        hook.GetProperty("managed_version").GetString().Should().Be(CliHarness.TEST_VERSION);
    }

    [Fact]
    public async Task Status_WithUnmanagedHook_ReturnsHookNotManaged()
    {
        using var harness = new CliHarness();
        harness.WriteFile(".git/hooks/pre-commit", "#!/bin/sh\necho legacy\n");

        int exitCode = await harness.RunAsync("status");

        exitCode.Should().Be(ExitCodes.HOOK_NOT_MANAGED);
        harness.Error.Should().Contain("Legacy（非托管脚本）").And.Contain("注意");
    }

    [Fact]
    public async Task NonRepositoryDirectory_ReturnsNotAGitRepository()
    {
        using var harness = new CliHarness(isRepository: false);

        (await harness.RunAsync("install")).Should().Be(ExitCodes.NOT_A_GIT_REPOSITORY);
        (await harness.RunAsync("uninstall")).Should().Be(ExitCodes.NOT_A_GIT_REPOSITORY);
        (await harness.RunAsync("status")).Should().Be(ExitCodes.NOT_A_GIT_REPOSITORY);
    }
}
