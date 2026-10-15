using FluentAssertions;
using GitGuard.Core.Domain;
using GitGuard.Core.Model;
using GitGuard.Core.Policy;

namespace GitGuard.Core.Tests;

public sealed class PolicyDecisionTests
{
    [Fact]
    public void Ctor_BlockNotIgnored_PopulatesFields()
    {
        var decision = new PolicyDecision(PolicyAction.Block, false, Severity.Critical);

        decision.Action.Should().Be(PolicyAction.Block);
        decision.IgnoredByAllowlist.Should().BeFalse();
        decision.Severity.Should().Be(Severity.Critical);
    }

    [Fact]
    public void Ctor_AllowedByAllowlist_Accepted()
    {
        var decision = new PolicyDecision(PolicyAction.Allow, true, Severity.High);

        decision.Action.Should().Be(PolicyAction.Allow);
        decision.IgnoredByAllowlist.Should().BeTrue();
    }

    [Theory]
    [InlineData(PolicyAction.Warn)]
    [InlineData(PolicyAction.Block)]
    public void Ctor_IgnoredWithNonAllowAction_Throws(PolicyAction action)
    {
        Action act = () => CreateDecision(action, true, Severity.Medium);

        act.Should().Throw<InvariantViolationException>().WithMessage("*FR-040*");
    }

    [Fact]
    public void Ctor_UndefinedAction_Throws()
    {
        Action act = () => CreateDecision((PolicyAction)99, false, Severity.Low);

        act.Should().Throw<InvariantViolationException>().WithMessage("*FR-022*");
    }

    [Fact]
    public void Ctor_UndefinedSeverity_Throws()
    {
        Action act = () => CreateDecision(PolicyAction.Allow, false, (Severity)99);

        act.Should().Throw<InvariantViolationException>().WithMessage("*DDD §4.2*");
    }

    // 经工厂方法包装构造，规避 CA1806 对 lambda 内被丢弃 new 结果的告警
    private static PolicyDecision CreateDecision(PolicyAction action, bool ignoredByAllowlist, Severity severity) =>
        new PolicyDecision(action, ignoredByAllowlist, severity);
}
