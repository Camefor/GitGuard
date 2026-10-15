using FluentAssertions;
using GitGuard.Core.Domain;
using GitGuard.Core.Model;

namespace GitGuard.Core.Tests;

public sealed class SnippetMaskedTests
{
    [Fact]
    public void Create_FullyMaskedSnippet_ReturnsValue()
    {
        var snippet = SnippetMasked.Create("AKIA****************");

        snippet.Value.Should().Be("AKIA****************");
        snippet.ToString().Should().Be("AKIA****************");
    }

    [Theory]
    [InlineData("abc***")]
    [InlineData("a*****")]
    [InlineData("****")]
    public void Create_AsteriskRatioAtLeastHalf_Accepted(string value)
    {
        SnippetMasked.Create(value).Value.Should().Be(value);
    }

    [Theory]
    [InlineData("ab***cd")]
    [InlineData("abcdefg*")]
    public void Create_AsteriskRatioBelowHalf_Throws(string value)
    {
        Action act = () => SnippetMasked.Create(value);

        act.Should().Throw<InvariantViolationException>().WithMessage("*DR-02*");
    }

    [Fact]
    public void Create_ExactlyMaxLength_Accepted()
    {
        string value = new string('*', SnippetMasked.MAX_LENGTH);

        SnippetMasked.Create(value).Value.Should().HaveLength(SnippetMasked.MAX_LENGTH);
    }

    [Fact]
    public void Create_TooLong_Throws()
    {
        string value = new string('*', SnippetMasked.MAX_LENGTH + 1);

        Action act = () => SnippetMasked.Create(value);

        act.Should().Throw<InvariantViolationException>().WithMessage("*DR-02*");
    }

    [Fact]
    public void Create_Empty_Throws()
    {
        Action act = () => SnippetMasked.Create(string.Empty);

        act.Should().Throw<InvariantViolationException>().WithMessage("*DR-02*");
    }

    [Fact]
    public void Default_UninitializedValue_HasEmptyValue()
    {
        default(SnippetMasked).Value.Should().BeEmpty();
    }
}
