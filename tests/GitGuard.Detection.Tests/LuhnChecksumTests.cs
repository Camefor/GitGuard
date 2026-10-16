using FluentAssertions;
using GitGuard.Detection.Validation;

namespace GitGuard.Detection.Tests;

/// <summary>Luhn 校验测试（GGD/PRIVACY/CN-BANKCARD 判定器）：合成卡号样本均为算法生成。</summary>
public sealed class LuhnChecksumTests
{
    [Theory]
    [InlineData("6225880137712345679")]
    [InlineData("6222021234567894")]
    [InlineData("4111111111111111")]
    [InlineData("5500005555555559")]
    public void IsValid_LuhnValidCardNumber_ShouldReturnTrue(string digits)
    {
        LuhnChecksum.IsValid(digits).Should().BeTrue();
    }

    [Theory]
    [InlineData("6222021234567895")]
    [InlineData("1234567890123456")]
    public void IsValid_LuhnInvalidCardNumber_ShouldReturnFalse(string digits)
    {
        LuhnChecksum.IsValid(digits).Should().BeFalse();
    }

    [Theory]
    [InlineData("")]
    [InlineData("4111-1111-1111-1111")]
    [InlineData("４１１１１１１１１１１１１１１１")]
    public void IsValid_NonDigitOrEmptyInput_ShouldReturnFalse(string digits)
    {
        LuhnChecksum.IsValid(digits).Should().BeFalse();
    }
}
