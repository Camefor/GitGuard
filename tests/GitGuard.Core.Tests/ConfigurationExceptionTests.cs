using FluentAssertions;

namespace GitGuard.Core.Tests;

/// <summary>
/// 配置异常契约测试（<c>10-异常处理设计</c> §2 / §4）：错误码固定为 <c>CONFIG_INVALID</c>（CLI 映射退出码 13），
/// 并携带出错文件路径与行号供 <c>gitguard scan</c> 报告定位。
/// </summary>
public sealed class ConfigurationExceptionTests
{
    [Fact]
    public void Constructor_CarriesErrorCodeAndLocation()
    {
        var exception = new ConfigurationException("配置文件版本不受支持：2", "/repo/.gitguard.yml", 3);

        exception.ErrorCode.Should().Be(ConfigurationException.CONFIG_INVALID);
        exception.Message.Should().Be("配置文件版本不受支持：2");
        exception.SourcePath.Should().Be("/repo/.gitguard.yml");
        exception.Line.Should().Be(3);
        exception.Should().BeAssignableTo<GitGuardException>();
    }

    [Fact]
    public void Constructor_WithoutLocation_LeavesLocationNull()
    {
        var exception = new ConfigurationException("policy 段落校验失败");

        exception.ErrorCode.Should().Be(ConfigurationException.CONFIG_INVALID);
        exception.SourcePath.Should().BeNull();
        exception.Line.Should().BeNull();
    }

    [Fact]
    public void Constructor_WithInnerException_PreservesCause()
    {
        var cause = new InvalidOperationException("YAML 解析失败");

        var exception = new ConfigurationException("配置文件 YAML 语法错误", cause, "/repo/.gitguard.yml", 7);

        exception.InnerException.Should().BeSameAs(cause);
        exception.Line.Should().Be(7);
    }

    [Fact]
    public void ErrorCode_MatchesDocumentedRegistration()
    {
        ConfigurationException.CONFIG_INVALID.Should().Be("CONFIG_INVALID");
    }
}
