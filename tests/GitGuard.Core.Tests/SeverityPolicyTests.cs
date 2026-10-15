using FluentAssertions;
using GitGuard.Core.Domain;
using GitGuard.Core.Model;
using GitGuard.Core.Policy;

namespace GitGuard.Core.Tests;

public sealed class SeverityPolicyTests
{
    [Theory]
    [InlineData(Severity.Critical, PolicyAction.Block)]
    [InlineData(Severity.High, PolicyAction.Block)]
    [InlineData(Severity.Medium, PolicyAction.Warn)]
    [InlineData(Severity.Low, PolicyAction.Warn)]
    public void Default_ActionFor_MapsAsSpecified(Severity severity, PolicyAction expected)
    {
        SeverityPolicy.Default.ActionFor(severity).Should().Be(expected);
    }

    [Fact]
    public void Default_Mapping_CoversAllSeverities()
    {
        SeverityPolicy.Default.Mapping.Should().HaveCount(Enum.GetValues<Severity>().Length);
    }

    [Fact]
    public void ActionFor_UndefinedSeverity_Throws()
    {
        Action act = () => SeverityPolicy.Default.ActionFor((Severity)99);

        act.Should().Throw<InvariantViolationException>().WithMessage("*DDD §4.2*");
    }

    [Fact]
    public void Ctor_MissingSeverity_Throws()
    {
        var mapping = new Dictionary<Severity, PolicyAction>
        {
            [Severity.Critical] = PolicyAction.Block,
            [Severity.High] = PolicyAction.Block,
            [Severity.Medium] = PolicyAction.Warn,
        };

        Action act = () => CreatePolicy(mapping);

        act.Should().Throw<InvariantViolationException>().WithMessage("*FR-022*");
    }

    [Fact]
    public void Ctor_CriticalMappedToAllow_Throws()
    {
        var mapping = new Dictionary<Severity, PolicyAction>
        {
            [Severity.Critical] = PolicyAction.Allow,
            [Severity.High] = PolicyAction.Block,
            [Severity.Medium] = PolicyAction.Warn,
            [Severity.Low] = PolicyAction.Allow,
        };

        Action act = () => CreatePolicy(mapping);

        act.Should().Throw<InvariantViolationException>().WithMessage("*硬下限*");
    }

    [Fact]
    public void Ctor_CustomMapping_IsHonored()
    {
        var mapping = new Dictionary<Severity, PolicyAction>
        {
            [Severity.Critical] = PolicyAction.Block,
            [Severity.High] = PolicyAction.Warn,
            [Severity.Medium] = PolicyAction.Allow,
            [Severity.Low] = PolicyAction.Allow,
        };

        var policy = CreatePolicy(mapping);

        policy.ActionFor(Severity.High).Should().Be(PolicyAction.Warn);
        policy.ActionFor(Severity.Medium).Should().Be(PolicyAction.Allow);
    }

    [Fact]
    public void Ctor_UndefinedActionValue_Throws()
    {
        var mapping = new Dictionary<Severity, PolicyAction>
        {
            [Severity.Critical] = PolicyAction.Block,
            [Severity.High] = PolicyAction.Block,
            [Severity.Medium] = PolicyAction.Warn,
            [Severity.Low] = (PolicyAction)99,
        };

        Action act = () => CreatePolicy(mapping);

        act.Should().Throw<InvariantViolationException>().WithMessage("*FR-022*");
    }

    [Fact]
    public void Ctor_NullMapping_ThrowsArgumentNullException()
    {
        Action act = () => CreatePolicy(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    // 经工厂方法包装构造，规避 CA1806 对 lambda 内被丢弃 new 结果的告警
    private static SeverityPolicy CreatePolicy(IReadOnlyDictionary<Severity, PolicyAction> mapping) =>
        new SeverityPolicy(mapping);
}
