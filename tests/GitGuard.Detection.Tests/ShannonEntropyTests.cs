using FluentAssertions;
using GitGuard.Detection.Validation;

namespace GitGuard.Detection.Tests;

/// <summary>Shannon 熵计算测试：阈值判定边界（模块设计 Secret §6 默认阈值 4.0）。</summary>
public sealed class ShannonEntropyTests
{
    [Fact]
    public void Calculate_SingleRepeatedCharacter_ShouldBeZero()
    {
        ShannonEntropy.Calculate(new string('a', 40)).Should().Be(0.0);
    }

    [Fact]
    public void Calculate_EmptyString_ShouldBeZero()
    {
        ShannonEntropy.Calculate(string.Empty).Should().Be(0.0);
    }

    [Fact]
    public void Calculate_TwoBalancedSymbols_ShouldBeOneBitPerCharacter()
    {
        ShannonEntropy.Calculate("abab").Should().BeApproximately(1.0, 1e-9);
    }

    [Fact]
    public void Calculate_FourBalancedSymbols_ShouldBeTwoBitsPerCharacter()
    {
        ShannonEntropy.Calculate("abcd").Should().BeApproximately(2.0, 1e-9);
    }

    [Fact]
    public void Calculate_Base64LikeSecret_ShouldExceedDefaultThreshold()
    {
        // AWS 官方文档示例密钥形态（合成样例）：约 4.66 bit/char，高于默认阈值 4.0
        ShannonEntropy.Calculate("wJalrXUtnFEMI/K7MDENG/bPxRfiCYEXAMPLEKEY").Should().BeGreaterThan(4.0);
    }

    [Fact]
    public void Calculate_RepeatedCharacterSecret_ShouldStayBelowDefaultThreshold()
    {
        ShannonEntropy.Calculate(new string('a', 40)).Should().BeLessThan(4.0);
    }
}
