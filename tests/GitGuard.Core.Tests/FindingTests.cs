using FluentAssertions;
using GitGuard.Core.Domain;
using GitGuard.Core.Model;

namespace GitGuard.Core.Tests;

public sealed class FindingTests
{
    [Fact]
    public void Create_ValidArguments_PopulatesFieldsAndGeneratesId()
    {
        var snippet = SnippetMasked.Create("AKIA****************");

        var finding = Finding.Create(
            "GGD/SECRET/AWS-AKID",
            "src/appsettings.json",
            18,
            Severity.Critical,
            TestData.SampleFingerprint,
            snippet,
            "0123456789abcdef0123456789abcdef01234567");

        finding.Id.Should().NotBe(Guid.Empty);
        finding.Id.Version.Should().Be(7);
        finding.RuleId.Should().Be("GGD/SECRET/AWS-AKID");
        finding.FilePath.Should().Be("src/appsettings.json");
        finding.LineNumber.Should().Be(18);
        finding.Severity.Should().Be(Severity.Critical);
        finding.Fingerprint.Should().Be(TestData.SampleFingerprint);
        finding.SnippetMasked.Should().Be(snippet);
        finding.CommitSha.Should().Be("0123456789abcdef0123456789abcdef01234567");
    }

    [Fact]
    public void Create_WithoutCommitSha_CommitShaIsNull()
    {
        var finding = TestData.CreateFinding();

        finding.CommitSha.Should().BeNull();
    }

    [Theory]
    [InlineData("0123456789abcdef0123456789abcdef01234567")]
    [InlineData("0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef")]
    public void Create_CommitShaOf40Or64Hex_Accepted(string commitSha)
    {
        var finding = TestData.CreateFinding(commitSha: commitSha);

        finding.CommitSha.Should().Be(commitSha);
    }

    [Theory]
    [InlineData("secret/AWS")]
    [InlineData("GGD/secret/AWS")]
    [InlineData("GGD/SECRET")]
    public void Create_InvalidRuleIdFormat_Throws(string ruleId)
    {
        Action act = () => TestData.CreateFinding(ruleId: ruleId);

        act.Should().Throw<InvariantViolationException>().WithMessage("*DDD §3.2*");
    }

    [Fact]
    public void Create_WhitespaceFilePath_Throws()
    {
        Action act = () => TestData.CreateFinding(filePath: "   ");

        act.Should().Throw<InvariantViolationException>().WithMessage("*DDD §4.1*");
    }

    [Fact]
    public void Create_RootedFilePath_Throws()
    {
        Action act = () => TestData.CreateFinding(filePath: Path.GetFullPath("appsettings.json"));

        act.Should().Throw<InvariantViolationException>().WithMessage("*DDD §4.1*");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Create_LineNumberBelowOne_Throws(int lineNumber)
    {
        Action act = () => TestData.CreateFinding(lineNumber: lineNumber);

        act.Should().Throw<InvariantViolationException>().WithMessage("*DDD §4.1*");
    }

    [Fact]
    public void Create_UndefinedSeverity_Throws()
    {
        Action act = () => TestData.CreateFinding(severity: (Severity)99);

        act.Should().Throw<InvariantViolationException>().WithMessage("*DDD §4.2*");
    }

    [Fact]
    public void Create_UninitializedFingerprint_Throws()
    {
        Action act = () => TestData.CreateFinding(fingerprint: default(Fingerprint));

        act.Should().Throw<InvariantViolationException>().WithMessage("*DR-02*");
    }

    [Fact]
    public void Create_UninitializedSnippetMasked_Throws()
    {
        Action act = () => Finding.Create(
            "GGD/SECRET/AWS-AKID",
            "src/appsettings.json",
            18,
            Severity.Critical,
            TestData.SampleFingerprint,
            default(SnippetMasked));

        act.Should().Throw<InvariantViolationException>().WithMessage("*DR-02*");
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("0123456789abcdef0123456789abcdef0123456")]
    public void Create_CommitShaOfInvalidLength_Throws(string commitSha)
    {
        Action act = () => TestData.CreateFinding(commitSha: commitSha);

        act.Should().Throw<InvariantViolationException>().WithMessage("*DDD §4.1*");
    }

    [Fact]
    public void Create_CommitShaWithNonHexCharacter_Throws()
    {
        Action act = () => TestData.CreateFinding(commitSha: new string('z', 40));

        act.Should().Throw<InvariantViolationException>().WithMessage("*DDD §4.1*");
    }

    [Fact]
    public void Create_NullRuleId_ThrowsArgumentNullException()
    {
        Action act = () => TestData.CreateFinding(ruleId: null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Create_NullFilePath_ThrowsArgumentNullException()
    {
        Action act = () => TestData.CreateFinding(filePath: null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Equality_SameContentDifferentInstances_DistinctIdentity()
    {
        var first = TestData.CreateFinding();
        var second = TestData.CreateFinding();

        first.Equals(first).Should().BeTrue();
        first.Should().NotBe(second);
    }
}
