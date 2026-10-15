using FluentAssertions;
using GitGuard.Core.Domain;
using GitGuard.Core.Model;
using GitGuard.Core.Policy;

namespace GitGuard.Core.Tests;

public sealed class AllowlistEntryTests
{
    [Fact]
    public void Create_RuleIdOnly_Accepted()
    {
        var entry = AllowlistEntry.Create("GGD/SECRET/AWS-AKID", null, null, "已知误报登记", TestData.SampleUtcNow, "tester");

        entry.Id.Should().NotBe(Guid.Empty);
        entry.Id.Version.Should().Be(7);
        entry.RuleId.Should().Be("GGD/SECRET/AWS-AKID");
        entry.PathPattern.Should().BeNull();
        entry.Fingerprint.Should().BeNull();
        entry.Reason.Should().Be("已知误报登记");
        entry.CreatedAt.Should().Be(TestData.SampleUtcNow);
        entry.CreatedBy.Should().Be("tester");
        entry.ExpiresAt.Should().BeNull();
    }

    [Fact]
    public void Create_PathPatternOnly_Accepted()
    {
        var entry = AllowlistEntry.Create(null, "test/**", null, "测试样例目录忽略", TestData.SampleUtcNow, "tester");

        entry.PathPattern.Should().Be("test/**");
        entry.RuleId.Should().BeNull();
    }

    [Fact]
    public void Create_FingerprintOnly_Accepted()
    {
        var entry = AllowlistEntry.Create(null, null, TestData.SampleFingerprint, "已知样例密钥登记", TestData.SampleUtcNow, "tester");

        entry.Fingerprint.Should().Be(TestData.SampleFingerprint);
    }

    [Fact]
    public void Create_AllConditions_Accepted()
    {
        var entry = AllowlistEntry.Create("GGD/SECRET/AWS-AKID", "src/**/*.json", TestData.SampleFingerprint, "全条件组合登记", TestData.SampleUtcNow, "tester");

        entry.RuleId.Should().NotBeNull();
        entry.PathPattern.Should().NotBeNull();
        entry.Fingerprint.Should().NotBeNull();
    }

    [Fact]
    public void Create_NoCondition_Throws()
    {
        Action act = () => AllowlistEntry.Create(null, null, null, "缺少匹配条件", TestData.SampleUtcNow, "tester");

        act.Should().Throw<InvariantViolationException>().WithMessage("*DDD §3.3*");
    }

    [Fact]
    public void Create_InvalidRuleId_Throws()
    {
        Action act = () => AllowlistEntry.Create("not-a-rule", null, null, "非法规则标识", TestData.SampleUtcNow, "tester");

        act.Should().Throw<InvariantViolationException>().WithMessage("*DDD §3.2*");
    }

    [Fact]
    public void Create_WhitespacePathPattern_Throws()
    {
        Action act = () => AllowlistEntry.Create(null, "   ", null, "空白路径模式", TestData.SampleUtcNow, "tester");

        act.Should().Throw<InvariantViolationException>().WithMessage("*DDD §3.3*");
    }

    [Fact]
    public void Create_UninitializedFingerprint_Throws()
    {
        Action act = () => AllowlistEntry.Create(null, null, default(Fingerprint), "未初始化指纹", TestData.SampleUtcNow, "tester");

        act.Should().Throw<InvariantViolationException>().WithMessage("*DDD §3.3*");
    }

    [Theory]
    [InlineData("abcd")]
    [InlineData("   ")]
    public void Create_ReasonTooShort_Throws(string reason)
    {
        Action act = () => AllowlistEntry.Create("GGD/SECRET/AWS-AKID", null, null, reason, TestData.SampleUtcNow, "tester");

        act.Should().Throw<InvariantViolationException>().WithMessage("*至少 5 字符*");
    }

    [Fact]
    public void Create_ReasonExactly5_Accepted()
    {
        var entry = AllowlistEntry.Create("GGD/SECRET/AWS-AKID", null, null, "abcde", TestData.SampleUtcNow, "tester");

        entry.Reason.Should().Be("abcde");
    }

    [Fact]
    public void Create_ReasonTooLong_Throws()
    {
        string reason = new string('a', 257);

        Action act = () => AllowlistEntry.Create("GGD/SECRET/AWS-AKID", null, null, reason, TestData.SampleUtcNow, "tester");

        act.Should().Throw<InvariantViolationException>().WithMessage("*256*");
    }

    [Fact]
    public void Create_ReasonWithSurroundingWhitespace_Trimmed()
    {
        var entry = AllowlistEntry.Create("GGD/SECRET/AWS-AKID", null, null, "  abcde  ", TestData.SampleUtcNow, "tester");

        entry.Reason.Should().Be("abcde");
    }

    [Fact]
    public void Create_NonUtcCreatedAt_Throws()
    {
        var local = new DateTimeOffset(2026, 10, 15, 20, 0, 0, TimeSpan.FromHours(8));

        Action act = () => AllowlistEntry.Create("GGD/SECRET/AWS-AKID", null, null, "非 UTC 时间", local, "tester");

        act.Should().Throw<InvariantViolationException>().WithMessage("*UTC*");
    }

    [Fact]
    public void Create_WhitespaceCreatedBy_Throws()
    {
        Action act = () => AllowlistEntry.Create("GGD/SECRET/AWS-AKID", null, null, "空白登记人", TestData.SampleUtcNow, "   ");

        act.Should().Throw<InvariantViolationException>().WithMessage("*DDD §3.3*");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Create_ExpiresAtNotAfterCreatedAt_Throws(int hoursOffset)
    {
        var createdAt = TestData.SampleUtcNow;
        var expiresAt = createdAt.AddHours(hoursOffset);

        Action act = () => AllowlistEntry.Create("GGD/SECRET/AWS-AKID", null, null, "过期时间非法", createdAt, "tester", expiresAt);

        act.Should().Throw<InvariantViolationException>().WithMessage("*ExpiresAt*");
    }

    [Fact]
    public void Create_ExpiresAtAfterCreatedAt_Accepted()
    {
        var expiresAt = TestData.SampleUtcNow.AddHours(24);

        var entry = AllowlistEntry.Create("GGD/SECRET/AWS-AKID", null, null, "带过期时间登记", TestData.SampleUtcNow, "tester", expiresAt);

        entry.ExpiresAt.Should().Be(expiresAt);
    }

    [Fact]
    public void Create_NullReason_ThrowsArgumentNullException()
    {
        Action act = () => AllowlistEntry.Create("GGD/SECRET/AWS-AKID", null, null, null!, TestData.SampleUtcNow, "tester");

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Create_NullCreatedBy_ThrowsArgumentNullException()
    {
        Action act = () => AllowlistEntry.Create("GGD/SECRET/AWS-AKID", null, null, "登记人为空", TestData.SampleUtcNow, null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Equality_SameConditionsDifferentInstances_DistinctIdentity()
    {
        var first = AllowlistEntry.Create("GGD/SECRET/AWS-AKID", null, null, "相同条件登记", TestData.SampleUtcNow, "tester");
        var second = AllowlistEntry.Create("GGD/SECRET/AWS-AKID", null, null, "相同条件登记", TestData.SampleUtcNow, "tester");

        first.Equals(first).Should().BeTrue();
        first.Should().NotBe(second);
    }
}
