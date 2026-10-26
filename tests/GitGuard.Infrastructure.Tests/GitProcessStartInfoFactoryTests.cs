using System.Diagnostics;
using FluentAssertions;
using GitGuard.Infrastructure.Git;

namespace GitGuard.Infrastructure.Tests;

/// <summary>
/// git 子进程启动配置测试：桌面端为 WinExe，若未禁用控制台窗口，状态刷新与扫描会反复闪现窗口。
/// </summary>
public sealed class GitProcessStartInfoFactoryTests
{
    [Fact]
    public void Create_ConfiguresHiddenBackgroundProcess()
    {
        ProcessStartInfo startInfo = GitProcessStartInfoFactory.Create("git");

        startInfo.FileName.Should().Be("git");
        startInfo.UseShellExecute.Should().BeFalse("git 子进程必须直接启动，不经过 shell");
        startInfo.CreateNoWindow.Should().BeTrue("WinExe 桌面端不得为 git 子进程创建控制台窗口");
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void Create_RejectsBlankExecutable(string executable)
    {
        FluentActions.Invoking(() => GitProcessStartInfoFactory.Create(executable))
            .Should().Throw<ArgumentException>();
    }
}
