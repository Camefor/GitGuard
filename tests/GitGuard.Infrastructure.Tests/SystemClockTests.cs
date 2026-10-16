using FluentAssertions;
using GitGuard.Infrastructure.Time;

namespace GitGuard.Infrastructure.Tests;

/// <summary>SystemClock 单元测试：UTC 偏移恒为零，取值贴近系统墙钟。</summary>
public sealed class SystemClockTests
{
    [Fact]
    public void UtcNow_ReturnsZeroOffset()
    {
        var clock = new SystemClock();

        clock.UtcNow.Offset.Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public void UtcNow_IsCloseToSystemWallClock()
    {
        DateTimeOffset expected = DateTimeOffset.UtcNow;
        var clock = new SystemClock();

        clock.UtcNow.Should().BeCloseTo(expected, TimeSpan.FromSeconds(2));
    }
}
