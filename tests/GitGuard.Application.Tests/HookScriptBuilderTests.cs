using FluentAssertions;
using GitGuard.Application.Hooks;
using GitGuard.Core.Model;

namespace GitGuard.Application.Tests;

/// <summary>
/// 分发脚本构建器单元测试（ADR-0004 / 模块设计/Hook.md §7）：
/// 渲染输出必须为无 BOM、LF 行尾的 POSIX sh 脚本，且托管标记行位于首两行（安装识别依赖该标记）。
/// </summary>
public sealed class HookScriptBuilderTests
{
    private const string SampleVersion = "1.2.3";
    private const string SampleExecutable = "/opt/gitguard/gitguard";

    [Fact]
    public void Render_PreCommit_ProducesManagedPosixScript()
    {
        string script = HookScriptBuilder.Render(HookType.PreCommit, SampleExecutable, SampleVersion);

        // 首两行即托管标记（HookFileOperations.Inspect 的识别窗口）
        script.Should().StartWith("#!/bin/sh\n# gitguard-managed v1.2.3\n");
        script.Should().Contain("GITGUARD_BIN=\"/opt/gitguard/gitguard\"");
        script.Should().Contain("scan --staged --record-pending");
        script.Should().Contain("pre-commit.local.bak");
        script.Should().NotContain("{{");
        script[0].Should().Be('#');
        script.Should().NotContain("\r");
        script.Should().EndWith("\n");
    }

    [Fact]
    public void Render_PrePush_UsesPushScanAndOwnLegacyBackup()
    {
        string script = HookScriptBuilder.Render(HookType.PrePush, SampleExecutable, SampleVersion);

        script.Should().Contain("scan --push");
        script.Should().NotContain("scan --staged");
        script.Should().Contain("pre-push.local.bak");
        script.Should().Contain("# gitguard-managed v1.2.3");
    }

    [Fact]
    public void Render_GitGuardFailure_StillInvokesLegacyHookAndKeepsGuardExitCode()
    {
        string script = HookScriptBuilder.Render(HookType.PreCommit, SampleExecutable, SampleVersion);

        // set -e 会在 GitGuard 非零退出时提前终止、跳过原 Hook（模块设计/Hook.md §7 模板缺陷）：不得出现
        script.Should().NotContain("set -e");
        script.Should().Contain("GUARD_EXIT=$?");
        script.Should().Contain("LEGACY_EXIT=$?");
        script.Should().Contain("exit \"$GUARD_EXIT\"");
        script.Should().Contain("exit \"$LEGACY_EXIT\"");
        script.Should().Contain("\"$LEGACY_HOOK\" \"$@\"");
    }

    [Fact]
    public void Render_WindowsStyleExecutablePath_NormalizesToForwardSlashes()
    {
        string script = HookScriptBuilder.Render(HookType.PreCommit, @"C:\tools\gitguard.exe", SampleVersion);

        // MSYS sh 把反斜杠当转义字符：Windows 上必须固化为正斜杠路径
        string expected = OperatingSystem.IsWindows() ? "C:/tools/gitguard.exe" : @"C:\tools\gitguard.exe";
        script.Should().Contain($"GITGUARD_BIN=\"{expected}\"");
    }

    [Fact]
    public void Render_MissingExecutable_ExitsWithDistinctCodeBeforeScan()
    {
        string script = HookScriptBuilder.Render(HookType.PreCommit, SampleExecutable, SampleVersion);

        // GITGUARD_BIN 失效（如卸载残留、路径迁移）时给出可辨识退出码，而不是让 hook 静默通过
        script.Should().Contain("if [ ! -x \"$GITGUARD_BIN\" ]");
        script.Should().Contain("exit 10");
    }

    [Fact]
    public void Render_PostCommit_CallsBypassDetectionWithOwnLegacyBackup()
    {
        string script = HookScriptBuilder.Render(HookType.PostCommit, SampleExecutable, SampleVersion);

        // FR-050：post-commit 只做绕过检测，不再触发暂存区扫描
        script.Should().Contain("hook post-commit");
        script.Should().NotContain("scan --staged");
        script.Should().Contain("post-commit.local.bak");
        script.Should().Contain("# gitguard-managed v1.2.3");
        script.Should().NotContain("\r");
    }

    [Fact]
    public void Render_UndefinedHookType_ThrowsArgumentOutOfRange()
    {
        Action act = () => HookScriptBuilder.Render((HookType)99, SampleExecutable, SampleVersion);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Render_NullExecutablePath_ThrowsArgumentNullException()
    {
        Action act = () => HookScriptBuilder.Render(HookType.PreCommit, null!, SampleVersion);

        act.Should().Throw<ArgumentNullException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Render_BlankExecutablePath_ThrowsArgumentException(string executablePath)
    {
        Action act = () => HookScriptBuilder.Render(HookType.PreCommit, executablePath, SampleVersion);

        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Render_BlankVersion_ThrowsArgumentException(string version)
    {
        Action act = () => HookScriptBuilder.Render(HookType.PreCommit, SampleExecutable, version);

        act.Should().Throw<ArgumentException>();
    }
}
