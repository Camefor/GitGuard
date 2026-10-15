using FluentAssertions;

namespace GitGuard.E2E.Tests;

public sealed class TestingStackSmokeTests
{
    [Fact]
    public void TestingStack_XunitAndFluentAssertions_ShouldBeWired()
    {
        (1 + 1).Should().Be(2);
    }
}
