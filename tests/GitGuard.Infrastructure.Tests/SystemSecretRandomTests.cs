using FluentAssertions;
using GitGuard.Infrastructure.Security;

namespace GitGuard.Infrastructure.Tests;

/// <summary>
/// 系统随机源测试（DR-02 / 07-缓存设计 §3）：包装操作系统 CSPRNG，供指纹盐生成使用。
/// </summary>
public sealed class SystemSecretRandomTests
{
    [Fact]
    public void GetBytes_ReturnsRequestedLengthAndNonConstantPayload()
    {
        byte[] bytes = SystemSecretRandom.Instance.GetBytes(32);

        bytes.Should().HaveCount(32);
        bytes.Should().Contain(value => value != 0, "32 字节 CSPRNG 输出不可能全为 0");
    }

    [Fact]
    public void GetBytes_TwoCalls_ProduceDifferentBytes()
    {
        byte[] first = SystemSecretRandom.Instance.GetBytes(32);
        byte[] second = SystemSecretRandom.Instance.GetBytes(32);

        second.Should().NotEqual(first);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void GetBytes_NonPositiveCount_Throws(int count)
    {
        FluentActions.Invoking(() => SystemSecretRandom.Instance.GetBytes(count))
            .Should().Throw<ArgumentOutOfRangeException>();
    }
}
