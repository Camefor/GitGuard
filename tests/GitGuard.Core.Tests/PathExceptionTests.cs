using FluentAssertions;

namespace GitGuard.Core.Tests;

/// <summary>PathException 单元测试（10-异常处理设计 §4：PATH 错误码注册表与 CLI 退出码映射契约）。</summary>
public sealed class PathExceptionTests
{
    [Fact]
    public void Ctor_ErrorCodeAndMessage_SetsPropertiesWithoutInnerException()
    {
        var exception = new PathException(PathException.ENTRY_CONFLICT, "目标符号链接已存在");

        exception.ErrorCode.Should().Be(PathException.ENTRY_CONFLICT);
        exception.Message.Should().Be("目标符号链接已存在");
        exception.InnerException.Should().BeNull();
    }

    [Fact]
    public void Ctor_InnerException_SetsInnerExceptionAndErrorCode()
    {
        var inner = new UnauthorizedAccessException("拒绝访问");

        var exception = new PathException(PathException.WRITE_PERMISSION_DENIED, "无法写入用户 PATH", inner);

        exception.ErrorCode.Should().Be(PathException.WRITE_PERMISSION_DENIED);
        exception.InnerException.Should().BeSameAs(inner);
    }

    [Fact]
    public void ErrorCodes_PinLiteralValuesFromRegistry()
    {
        // 字面值即 CLI 退出码映射依据（10-异常处理设计 §4）：改名会破坏契约
        PathException.WRITE_PERMISSION_DENIED.Should().Be("PATH_WRITE_PERMISSION_DENIED");
        PathException.ENTRY_CONFLICT.Should().Be("PATH_ENTRY_CONFLICT");
        PathException.UNSUPPORTED.Should().Be("PATH_UNSUPPORTED");
        PathException.TOO_LONG.Should().Be("PATH_TOO_LONG");
    }

    [Fact]
    public void Inherits_GitGuardException()
    {
        new PathException(PathException.UNSUPPORTED, "平台不支持").Should().BeAssignableTo<GitGuardException>();
    }
}
