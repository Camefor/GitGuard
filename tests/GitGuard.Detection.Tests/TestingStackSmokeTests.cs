using FluentAssertions;

namespace GitGuard.Detection.Tests;

public sealed class TestingStackSmokeTests
{
    [Fact]
    public void TestingStack_XunitAndFluentAssertions_ShouldBeWired()
    {
        (1 + 1).Should().Be(2);
    }
}
