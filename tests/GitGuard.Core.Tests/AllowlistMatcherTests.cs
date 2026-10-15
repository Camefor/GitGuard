using FluentAssertions;
using GitGuard.Core.Model;
using GitGuard.Core.Policy;

namespace GitGuard.Core.Tests;

public sealed class AllowlistMatcherTests
{
    [Fact]
    public void Matches_RuleIdOnly_FindingRuleMatches_ReturnsTrue()
    {
        var entry = CreateEntry(ruleId: "GGD/SECRET/AWS-AKID");

        AllowlistMatcher.Matches(entry, TestData.CreateFinding(), TestData.SampleUtcNow).Should().BeTrue();
    }

    [Fact]
    public void Matches_RuleIdOnly_FindingRuleDiffers_ReturnsFalse()
    {
        var entry = CreateEntry(ruleId: "USR/PATTERN/OTHER");

        AllowlistMatcher.Matches(entry, TestData.CreateFinding(), TestData.SampleUtcNow).Should().BeFalse();
    }

    [Fact]
    public void Matches_PathPattern_GlobMatches_ReturnsTrue()
    {
        var entry = CreateEntry(pathPattern: "src/**/*.json");
        var finding = TestData.CreateFinding(filePath: "src/config/appsettings.json");

        AllowlistMatcher.Matches(entry, finding, TestData.SampleUtcNow).Should().BeTrue();
    }

    [Fact]
    public void Matches_PathPattern_GlobMismatch_ReturnsFalse()
    {
        var entry = CreateEntry(pathPattern: "test/**");

        AllowlistMatcher.Matches(entry, TestData.CreateFinding(), TestData.SampleUtcNow).Should().BeFalse();
    }

    [Fact]
    public void Matches_FingerprintOnly_FingerprintEquals_ReturnsTrue()
    {
        var entry = CreateEntry(fingerprint: TestData.SampleFingerprint);

        AllowlistMatcher.Matches(entry, TestData.CreateFinding(), TestData.SampleUtcNow).Should().BeTrue();
    }

    [Fact]
    public void Matches_FingerprintOnly_FingerprintDiffers_ReturnsFalse()
    {
        var entry = CreateEntry(fingerprint: TestData.SampleFingerprint);
        var finding = TestData.CreateFinding(fingerprint: Fingerprint.FromHex(new string('b', 64)));

        AllowlistMatcher.Matches(entry, finding, TestData.SampleUtcNow).Should().BeFalse();
    }

    [Fact]
    public void Matches_CombinedConditions_AllMustHold()
    {
        var entry = CreateEntry(
            ruleId: "GGD/SECRET/AWS-AKID",
            pathPattern: "**/appsettings.json",
            fingerprint: TestData.SampleFingerprint);

        var matching = TestData.CreateFinding();
        var mismatchingPath = TestData.CreateFinding(filePath: "config/prod.yaml");

        AllowlistMatcher.Matches(entry, matching, TestData.SampleUtcNow).Should().BeTrue();
        AllowlistMatcher.Matches(entry, mismatchingPath, TestData.SampleUtcNow).Should().BeFalse();
    }

    [Fact]
    public void Matches_ExpiredEntry_ReturnsFalse()
    {
        var entry = AllowlistEntry.Create(
            "GGD/SECRET/AWS-AKID",
            null,
            null,
            "已过期条目",
            TestData.SampleUtcNow.AddHours(-2),
            "tester",
            TestData.SampleUtcNow.AddHours(-1));

        AllowlistMatcher.Matches(entry, TestData.CreateFinding(), TestData.SampleUtcNow).Should().BeFalse();
    }

    [Fact]
    public void Matches_ExpiresExactlyAtNow_ReturnsFalse()
    {
        var entry = AllowlistEntry.Create(
            "GGD/SECRET/AWS-AKID",
            null,
            null,
            "恰好在当前时刻过期",
            TestData.SampleUtcNow.AddHours(-1),
            "tester",
            TestData.SampleUtcNow);

        AllowlistMatcher.Matches(entry, TestData.CreateFinding(), TestData.SampleUtcNow).Should().BeFalse();
    }

    [Fact]
    public void Matches_EntryWithoutExpiry_ReturnsTrue()
    {
        var entry = CreateEntry(ruleId: "GGD/SECRET/AWS-AKID", expiresAt: null);

        AllowlistMatcher.Matches(entry, TestData.CreateFinding(), TestData.SampleUtcNow.AddYears(1)).Should().BeTrue();
    }

    [Fact]
    public void Matches_NullEntry_ThrowsArgumentNullException()
    {
        Action act = () => AllowlistMatcher.Matches(null!, TestData.CreateFinding(), TestData.SampleUtcNow);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Matches_NullFinding_ThrowsArgumentNullException()
    {
        var entry = CreateEntry(ruleId: "GGD/SECRET/AWS-AKID");

        Action act = () => AllowlistMatcher.Matches(entry, null!, TestData.SampleUtcNow);

        act.Should().Throw<ArgumentNullException>();
    }

    private static AllowlistEntry CreateEntry(
        string? ruleId = null,
        string? pathPattern = null,
        Fingerprint? fingerprint = null,
        DateTimeOffset? expiresAt = null,
        DateTimeOffset? createdAt = null) =>
        AllowlistEntry.Create(
            ruleId,
            pathPattern,
            fingerprint,
            "测试白名单条目",
            createdAt ?? TestData.SampleUtcNow,
            "tester",
            expiresAt);
}
