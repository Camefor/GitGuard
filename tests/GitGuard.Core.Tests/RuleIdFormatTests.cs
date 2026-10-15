using FluentAssertions;
using GitGuard.Core.Domain;

namespace GitGuard.Core.Tests;

public sealed class RuleIdFormatTests
{
    [Theory]
    [InlineData("GGD/SECRET/AWS-AKID")]
    [InlineData("USR/PATTERN/MY_RULE-1")]
    [InlineData("GLK/GENERIC/API_KEY")]
    public void IsValid_ContractConformingId_ReturnsTrue(string ruleId)
    {
        RuleIdFormat.IsValid(ruleId).Should().BeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("ggd/SECRET/AWS")]
    [InlineData("SECRET/AWS")]
    [InlineData("GGD/SECRET")]
    [InlineData("GGD/secret/AWS")]
    [InlineData("GGD/SECRET/AWS/AKID")]
    [InlineData("GGD/SECRET/aws-akid")]
    [InlineData("GGD/SECRET/")]
    public void IsValid_ContractViolatingId_ReturnsFalse(string? ruleId)
    {
        RuleIdFormat.IsValid(ruleId).Should().BeFalse();
    }
}
