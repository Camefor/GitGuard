using FluentAssertions;
using GitGuard.Core.Domain;
using GitGuard.Core.Model;
using GitGuard.Core.Services;

namespace GitGuard.Core.Tests;

public sealed class MaskerTests
{
    [Fact]
    public void Mask_CanaryToken_FullTokenAbsentAndPinEdges()
    {
        var masked = Masker.Mask(TestData.CanaryToken);

        masked.Value.Should().NotContain(TestData.CanaryToken);
        masked.Value.Should().StartWith("AK").And.EndWith("ab");
        masked.Value.Should().HaveLength(SnippetMasked.MAX_LENGTH);
    }

    [Theory]
    [InlineData("a", "*")]
    [InlineData("ab", "**")]
    [InlineData("abc", "***")]
    [InlineData("abcd", "a**d")]
    [InlineData("abcde", "a***e")]
    [InlineData("abcdefg", "a*****g")]
    [InlineData("abcdefgh", "ab****gh")]
    public void Mask_ShortSecrets_MatchExpectedMask(string raw, string expected)
    {
        Masker.Mask(raw).Value.Should().Be(expected);
    }

    [Fact]
    public void Mask_LongSecret_KeepsHeadAndTail()
    {
        string raw = "AKIAIOSFODNN7EXAMPLE" + new string('Z', 20);

        var masked = Masker.Mask(raw);

        masked.Value.Should().Be("AK" + new string('*', 28) + "ZZ");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(31)]
    [InlineData(32)]
    [InlineData(33)]
    [InlineData(64)]
    [InlineData(100)]
    public void Mask_AnyLength_SatisfiesDr02Invariants(int length)
    {
        string raw = new string('x', length);

        var masked = Masker.Mask(raw);

        masked.Value.Should().HaveLength(Math.Min(length, SnippetMasked.MAX_LENGTH));
        masked.Value.Count(c => c == '*').Should().BeGreaterThanOrEqualTo((masked.Value.Length + 1) / 2);
        masked.Value.Count(c => c != '*').Should().BeLessThanOrEqualTo(4);
    }

    [Fact]
    public void Mask_ExactlyMaxLength_OutputSameLength()
    {
        string raw = new string('y', SnippetMasked.MAX_LENGTH);

        var masked = Masker.Mask(raw);

        masked.Value.Should().HaveLength(SnippetMasked.MAX_LENGTH);
        masked.Value.Should().StartWith("yy").And.EndWith("yy");
    }

    [Fact]
    public void Mask_Empty_Throws()
    {
        Action act = () => Masker.Mask(string.Empty);

        act.Should().Throw<InvariantViolationException>().WithMessage("*DR-02*");
    }

    [Fact]
    public void Mask_Null_ThrowsArgumentNullException()
    {
        Action act = () => Masker.Mask(null!);

        act.Should().Throw<ArgumentNullException>();
    }
}
