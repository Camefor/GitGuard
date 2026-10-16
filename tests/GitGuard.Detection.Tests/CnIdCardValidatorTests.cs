using FluentAssertions;
using GitGuard.Detection.Validation;

namespace GitGuard.Detection.Tests;

/// <summary>身份证校验位测试（GB 11643-1999，GGD/PRIVACY/CN-IDCARD-18 判定器）：样本均为合成号码。</summary>
public sealed class CnIdCardValidatorTests
{
    [Theory]
    [InlineData("11010519491231002X")]
    [InlineData("32031119770101001X")]
    [InlineData("440524198802150120")]
    [InlineData("330106199001020136")]
    public void IsValid_ChecksumValidIdCard_ShouldReturnTrue(string idCard)
    {
        CnIdCardValidator.IsValid(idCard).Should().BeTrue();
    }

    [Theory]
    [InlineData("110105194912310021")] // 校验位错误（应为 X）
    [InlineData("11010519491231002")] // 长度不足
    [InlineData("1101051949123100XX")] // 主体含非数字
    [InlineData("110105194913310021")] // 校验位正确但月份 13 非法
    [InlineData("110105194912000021")] // 校验位正确但日 00 非法
    [InlineData("110105194912320025")] // 校验位正确但日 32 非法
    public void IsValid_InvalidIdCard_ShouldReturnFalse(string idCard)
    {
        CnIdCardValidator.IsValid(idCard).Should().BeFalse();
    }

    [Fact]
    public void IsValid_LowerCaseCheckCode_ShouldReturnTrue()
    {
        CnIdCardValidator.IsValid("11010519491231002x").Should().BeTrue();
    }
}
