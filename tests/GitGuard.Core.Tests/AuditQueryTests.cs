using FluentAssertions;
using GitGuard.Core.Domain;
using GitGuard.Core.Model;

namespace GitGuard.Core.Tests;

/// <summary>审计查询模型不变式（06-API接口设计 §5.6 / §4.8）。</summary>
public sealed class AuditQueryTests
{
    [Fact]
    public void Constructor_Defaults_AreTwentyAndUnfiltered()
    {
        var query = new AuditQuery();

        query.Limit.Should().Be(AuditQuery.DEFAULT_LIMIT);
        query.Limit.Should().Be(20);
        query.Offset.Should().Be(0);
        query.Since.Should().BeNull();
        query.Result.Should().BeNull();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(AuditQuery.MAX_LIMIT + 1)]
    public void Constructor_LimitOutOfRange_Throws(int limit)
    {
        Action act = () => _ = new AuditQuery(limit);

        act.Should().Throw<InvariantViolationException>().WithMessage("*limit*");
    }

    [Fact]
    public void Constructor_MaxLimit_Accepted()
    {
        var query = new AuditQuery(AuditQuery.MAX_LIMIT);

        query.Limit.Should().Be(AuditQuery.MAX_LIMIT);
    }

    [Fact]
    public void Constructor_NegativeOffset_Throws()
    {
        Action act = () => _ = new AuditQuery(offset: -1);

        act.Should().Throw<InvariantViolationException>().WithMessage("*offset*");
    }

    [Fact]
    public void Constructor_NonUtcSince_Throws()
    {
        var withOffset = new DateTimeOffset(2026, 10, 21, 8, 0, 0, TimeSpan.FromHours(8));

        Action act = () => _ = new AuditQuery(since: withOffset);

        act.Should().Throw<InvariantViolationException>().WithMessage("*UTC*");
    }

    [Fact]
    public void Constructor_UtcSinceAndDefinedResult_Accepted()
    {
        var since = new DateTimeOffset(2026, 10, 21, 0, 0, 0, TimeSpan.Zero);

        var query = new AuditQuery(offset: 40, since: since, result: ScanResult.Blocked);

        query.Offset.Should().Be(40);
        query.Since.Should().Be(since);
        query.Result.Should().Be(ScanResult.Blocked);
    }

    [Fact]
    public void Constructor_UndefinedResult_Throws()
    {
        Action act = () => _ = new AuditQuery(result: (ScanResult)99);

        act.Should().Throw<InvariantViolationException>().WithMessage("*ScanResult*");
    }
}
