using FluentAssertions;
using GitGuard.Core.Domain;
using GitGuard.Core.Model;

namespace GitGuard.Core.Tests;

public sealed class FingerprintTests
{
    [Fact]
    public void FromHex_ValidLowercaseHex_ReturnsSameHex()
    {
        string hex = new string('a', 64);

        Fingerprint.FromHex(hex).Hex.Should().Be(hex);
    }

    [Fact]
    public void FromHex_UppercaseHex_NormalizesToLowercase()
    {
        string hex = new string('A', 64);

        Fingerprint.FromHex(hex).Hex.Should().Be(new string('a', 64));
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    public void FromHex_LengthNot64_Throws(string hex)
    {
        Action act = () => Fingerprint.FromHex(hex);

        act.Should().Throw<InvariantViolationException>().WithMessage("*DR-02*");
    }

    [Fact]
    public void FromHex_NonHexCharacter_Throws()
    {
        string hex = "g" + new string('a', 63);

        Action act = () => Fingerprint.FromHex(hex);

        act.Should().Throw<InvariantViolationException>().WithMessage("*DR-02*");
    }

    [Fact]
    public void Equality_SameHexDifferentCase_AreEqual()
    {
        var lower = Fingerprint.FromHex(new string('b', 64));
        var upper = Fingerprint.FromHex(new string('B', 64));

        lower.Should().Be(upper);
    }

    [Fact]
    public void ToString_ReturnsNormalizedHex()
    {
        var fingerprint = Fingerprint.FromHex(new string('C', 64));

        fingerprint.ToString().Should().Be(new string('c', 64));
    }

    [Fact]
    public void Default_UninitializedValue_HasEmptyHex()
    {
        default(Fingerprint).Hex.Should().BeEmpty();
    }
}
