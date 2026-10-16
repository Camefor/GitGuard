using FluentAssertions;
using GitGuard.Core.Model;
using GitGuard.Core.Policy;

namespace GitGuard.Core.Tests;

/// <summary>策略评估器单元测试（FR-022 / 模块设计 Policy.md §6.1）：严重度映射与白名单放行。</summary>
public sealed class PolicyEvaluatorTests
{
    [Theory]
    [InlineData(Severity.Critical, PolicyAction.Block)]
    [InlineData(Severity.High, PolicyAction.Block)]
    [InlineData(Severity.Medium, PolicyAction.Warn)]
    [InlineData(Severity.Low, PolicyAction.Warn)]
    public void Evaluate_WithoutAllowlist_MapsSeverityToDefaultAction(Severity severity, PolicyAction expected)
    {
        Finding finding = TestData.CreateFinding(severity: severity);

        var decisions = PolicyEvaluator.Evaluate(
            new List<Finding> { finding },
            SeverityPolicy.Default,
            TestData.SampleUtcNow);

        PolicyDecision decision = decisions[finding];
        decision.Action.Should().Be(expected);
        decision.IgnoredByAllowlist.Should().BeFalse();
        decision.Severity.Should().Be(severity);
    }

    [Fact]
    public void Evaluate_EmptyFindings_ReturnsEmptyMapping()
    {
        var decisions = PolicyEvaluator.Evaluate(
            Array.Empty<Finding>(),
            SeverityPolicy.Default,
            TestData.SampleUtcNow);

        decisions.Should().BeEmpty();
    }

    [Fact]
    public void Evaluate_AllowlistMatch_ReturnsAllowWithIgnoredFlag()
    {
        Finding finding = TestData.CreateFinding();
        AllowlistEntry entry = CreateEntry(ruleId: "GGD/SECRET/AWS-AKID");

        var decisions = PolicyEvaluator.Evaluate(
            new List<Finding> { finding },
            SeverityPolicy.Default,
            TestData.SampleUtcNow,
            new List<AllowlistEntry> { entry });

        PolicyDecision decision = decisions[finding];
        decision.Action.Should().Be(PolicyAction.Allow);
        decision.IgnoredByAllowlist.Should().BeTrue();
    }

    [Fact]
    public void Evaluate_AllowlistDoesNotMatch_FallsBackToSeverityMapping()
    {
        Finding finding = TestData.CreateFinding();
        AllowlistEntry entry = CreateEntry(ruleId: "USR/PATTERN/OTHER");

        var decisions = PolicyEvaluator.Evaluate(
            new List<Finding> { finding },
            SeverityPolicy.Default,
            TestData.SampleUtcNow,
            new List<AllowlistEntry> { entry });

        PolicyDecision decision = decisions[finding];
        decision.Action.Should().Be(PolicyAction.Block);
        decision.IgnoredByAllowlist.Should().BeFalse();
    }

    [Fact]
    public void Evaluate_ExpiredAllowlistEntry_IsNotApplied()
    {
        Finding finding = TestData.CreateFinding();
        AllowlistEntry entry = CreateEntry(
            ruleId: "GGD/SECRET/AWS-AKID",
            expiresAt: TestData.SampleUtcNow.AddHours(-1),
            createdAt: TestData.SampleUtcNow.AddHours(-2));

        var decisions = PolicyEvaluator.Evaluate(
            new List<Finding> { finding },
            SeverityPolicy.Default,
            TestData.SampleUtcNow,
            new List<AllowlistEntry> { entry });

        decisions[finding].Action.Should().Be(PolicyAction.Block);
    }

    [Fact]
    public void Evaluate_ReturnsDecisionForEveryFinding()
    {
        var findings = new List<Finding>
        {
            TestData.CreateFinding(ruleId: "GGD/SECRET/AWS-AKID", severity: Severity.Critical),
            TestData.CreateFinding(ruleId: "GGD/SECRET/JWT", severity: Severity.Medium),
            TestData.CreateFinding(ruleId: "GGD/PRIVACY/EMAIL", severity: Severity.Low),
        };

        var decisions = PolicyEvaluator.Evaluate(findings, SeverityPolicy.Default, TestData.SampleUtcNow);

        // ImmutableDictionary 的枚举顺序不构成契约（键哈希取自随机 UUIDv7 身份），只断言集合成员
        decisions.Should().HaveCount(findings.Count);
        foreach (Finding finding in findings)
        {
            decisions.Should().ContainKey(finding);
        }
    }

    [Fact]
    public void Evaluate_NullFindings_ThrowsArgumentNullException()
    {
        Action act = () => PolicyEvaluator.Evaluate(null!, SeverityPolicy.Default, TestData.SampleUtcNow);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Evaluate_NullPolicy_ThrowsArgumentNullException()
    {
        Action act = () => PolicyEvaluator.Evaluate(Array.Empty<Finding>(), null!, TestData.SampleUtcNow);

        act.Should().Throw<ArgumentNullException>();
    }

    private static AllowlistEntry CreateEntry(
        string? ruleId = null,
        DateTimeOffset? expiresAt = null,
        DateTimeOffset? createdAt = null) =>
        AllowlistEntry.Create(
            ruleId,
            null,
            null,
            "测试白名单条目",
            createdAt ?? TestData.SampleUtcNow,
            "tester",
            expiresAt);
}
