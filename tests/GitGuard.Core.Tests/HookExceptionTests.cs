using FluentAssertions;

namespace GitGuard.Core.Tests;

/// <summary>HookException 单元测试（10-异常处理设计 §4：错误码注册表与 CLI 退出码映射契约）。</summary>
public sealed class HookExceptionTests
{
    [Fact]
    public void Ctor_ErrorCodeAndMessage_SetsPropertiesWithoutInnerException()
    {
        var exception = new HookException(HookException.UNKNOWN_HOOK_PRESENT, "目标 Hook 需 --force 接管");

        exception.ErrorCode.Should().Be(HookException.UNKNOWN_HOOK_PRESENT);
        exception.Message.Should().Be("目标 Hook 需 --force 接管");
        exception.InnerException.Should().BeNull();
    }

    [Fact]
    public void Ctor_InnerException_SetsInnerExceptionAndErrorCode()
    {
        var inner = new UnauthorizedAccessException("拒绝访问");

        var exception = new HookException(HookException.WRITE_PERMISSION_DENIED, "无法写入 Hook 文件", inner);

        exception.ErrorCode.Should().Be(HookException.WRITE_PERMISSION_DENIED);
        exception.InnerException.Should().BeSameAs(inner);
    }

    [Fact]
    public void ErrorCodes_PinLiteralValuesFromRegistry()
    {
        // 字面值即 CLI 退出码映射依据（10-异常处理设计 §4）：改名会破坏契约
        HookException.WRITE_PERMISSION_DENIED.Should().Be("WRITE_PERMISSION_DENIED");
        HookException.UNKNOWN_HOOK_PRESENT.Should().Be("UNKNOWN_HOOK_PRESENT");
        HookException.INSTALL_IN_PROGRESS.Should().Be("INSTALL_IN_PROGRESS");
    }

    [Fact]
    public void Inherits_GitGuardException()
    {
        // Cli 层统一以基类捕获后按 ErrorCode 映射退出码（10-异常处理设计 §2）
        new HookException(HookException.INSTALL_IN_PROGRESS, "并发安装中").Should().BeAssignableTo<GitGuardException>();
    }
}
