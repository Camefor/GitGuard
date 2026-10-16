using FluentAssertions;

namespace GitGuard.Core.Tests;

/// <summary>GitException 单元测试（10-异常处理设计 §2 / §4：错误码注册表与退出码映射契约）。</summary>
public sealed class GitExceptionTests
{
    [Fact]
    public void Ctor_ErrorCodeAndMessage_SetsPropertiesWithoutDiagnostics()
    {
        var exception = new GitException(GitException.NOT_A_GIT_REPOSITORY, "当前路径不是 Git 仓库");

        exception.ErrorCode.Should().Be(GitException.NOT_A_GIT_REPOSITORY);
        exception.Message.Should().Be("当前路径不是 Git 仓库");
        exception.ExitCode.Should().BeNull();
        exception.StandardError.Should().BeNull();
    }

    [Fact]
    public void Ctor_ExitCodeAndStandardError_CarriesGitDiagnostics()
    {
        var exception = new GitException(GitException.GIT_COMMAND_FAILED, "git diff --cached 失败", 128, "fatal: bad object");

        exception.ErrorCode.Should().Be(GitException.GIT_COMMAND_FAILED);
        exception.ExitCode.Should().Be(128);
        exception.StandardError.Should().Be("fatal: bad object");
    }

    [Fact]
    public void Ctor_NullExitCodeAndStandardError_AllowedForTimeoutOrStartFailure()
    {
        // 超时终止与进程无法启动均无退出码 / stderr（10-异常处理设计 §6）
        var exception = new GitException(GitException.GIT_COMMAND_FAILED, "git 命令超时", null, null);

        exception.ExitCode.Should().BeNull();
        exception.StandardError.Should().BeNull();
    }

    [Fact]
    public void Ctor_InnerException_SetsInnerExceptionAndErrorCode()
    {
        var inner = new InvalidOperationException("无法启动 git 进程");

        var exception = new GitException(GitException.GIT_COMMAND_FAILED, "启动 git 子进程失败", inner);

        exception.ErrorCode.Should().Be(GitException.GIT_COMMAND_FAILED);
        exception.InnerException.Should().BeSameAs(inner);
    }

    [Fact]
    public void ErrorCodes_PinLiteralValuesFromRegistry()
    {
        // 字面值即 CLI 退出码映射依据（10-异常处理设计 §4）：改名会破坏契约
        GitException.NOT_A_GIT_REPOSITORY.Should().Be("NOT_A_GIT_REPOSITORY");
        GitException.GIT_COMMAND_FAILED.Should().Be("GIT_COMMAND_FAILED");
    }
}
