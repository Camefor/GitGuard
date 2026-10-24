using FluentAssertions;
using GitGuard.Detection.Validation;

namespace GitGuard.Detection.Tests;

/// <summary>IBAN 模 97 校验测试（GGD/PRIVACY/IBAN 判定器）：公开示例 IBAN 与构造的错误校验位。</summary>
public sealed class IbanChecksumTests
{
    [Theory]
    [InlineData("DE89370400440532013000")]
    [InlineData("GB82WEST12345698765432")]
    [InlineData("FR1420041010050500013M02606")]
    [InlineData("NL91ABNA0417164300")]
    public void IsValid_ValidIban_ShouldReturnTrue(string iban)
    {
        IbanChecksum.IsValid(iban).Should().BeTrue();
    }

    [Theory]
    [InlineData("DE89370400440532013001")]
    [InlineData("GB82WEST12345698765433")]
    [InlineData("FR1420041010050500013M02607")]
    public void IsValid_InvalidChecksum_ShouldReturnFalse(string iban)
    {
        IbanChecksum.IsValid(iban).Should().BeFalse();
    }

    [Theory]
    [InlineData("")]
    [InlineData("DE893704004405")]
    [InlineData("de89370400440532013000")]
    [InlineData("DE89 3704 0044 0532 0130 00")]
    [InlineData("1E89370400440532013000")]
    public void IsValid_MalformedInput_ShouldReturnFalse(string iban)
    {
        IbanChecksum.IsValid(iban).Should().BeFalse();
    }
}
