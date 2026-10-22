using FluentAssertions;
using GitGuard.Core.Domain;
using GitGuard.Core.Services;

namespace GitGuard.Core.Tests;

public sealed class PathAnonymizerTests
{
    [Fact]
    public void Anonymize_Returns16LowercaseHex()
    {
        string hash = PathAnonymizer.Anonymize(TestData.SampleRepoPath);

        hash.Should().MatchRegex("^[0-9a-f]{16}$");
        hash.Should().HaveLength(PathAnonymizer.HASH_LENGTH);
    }

    [Fact]
    public void Anonymize_SamePath_IsStable()
    {
        PathAnonymizer.Anonymize(TestData.SampleRepoPath).Should().Be(PathAnonymizer.Anonymize(TestData.SampleRepoPath));
    }

    [Fact]
    public void Anonymize_DifferentPaths_Differ()
    {
        PathAnonymizer.Anonymize("C:/repo-a").Should().NotBe(PathAnonymizer.Anonymize("C:/repo-b"));
    }

    [Fact]
    public void Anonymize_CanaryTokenInPath_NotExposed()
    {
        string path = Path.Combine(TestData.SampleRepoPath, TestData.CANARY_TOKEN, "src");

        string hash = PathAnonymizer.Anonymize(path);

        hash.Should().NotContain(TestData.CANARY_TOKEN);
        hash.Should().NotContain("repo");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Anonymize_EmptyOrWhitespace_Throws(string path)
    {
        Action act = () => PathAnonymizer.Anonymize(path);

        act.Should().Throw<InvariantViolationException>().WithMessage("*DR-02*");
    }

    [Fact]
    public void Anonymize_Null_ThrowsArgumentNullException()
    {
        Action act = () => PathAnonymizer.Anonymize(null!);

        act.Should().Throw<ArgumentNullException>();
    }
}
