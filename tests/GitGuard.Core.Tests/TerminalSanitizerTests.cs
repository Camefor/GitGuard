using FluentAssertions;
using GitGuard.Core.Services;

namespace GitGuard.Core.Tests;

public sealed class TerminalSanitizerTests
{
    [Fact]
    public void Strip_PlainText_Unchanged()
    {
        TerminalSanitizer.Strip("scan finished: 3 findings").Should().Be("scan finished: 3 findings");
    }

    [Theory]
    [InlineData("\u001B[31mERROR\u001B[0m", "ERROR")]
    [InlineData("\u001B[2Jcleared", "cleared")]
    [InlineData("\u001B[1;33mwarning\u001B[22;39m", "warning")]
    public void Strip_AnsiEscapeSequences_Removed(string input, string expected)
    {
        TerminalSanitizer.Strip(input).Should().Be(expected);
    }

    [Fact]
    public void Strip_ControlCharacters_Removed()
    {
        TerminalSanitizer.Strip("a\u0000b\u0007c\u001Fd\u007Fe").Should().Be("abcde");
    }

    [Fact]
    public void Strip_CarriageReturn_Removed()
    {
        TerminalSanitizer.Strip("a\r\nb").Should().Be("a\nb");
    }

    [Fact]
    public void Strip_NewlineAndTab_Preserved()
    {
        TerminalSanitizer.Strip("a\nb\tc").Should().Be("a\nb\tc");
    }

    [Fact]
    public void Strip_MaliciousPathWithEscapeSequence_ProducesPlainText()
    {
        TerminalSanitizer.Strip("..\u001B[2Kfake\u001B[31m/evil").Should().Be("..fake/evil");
    }

    [Fact]
    public void Strip_Empty_ReturnsEmpty()
    {
        TerminalSanitizer.Strip(string.Empty).Should().BeEmpty();
    }

    [Fact]
    public void Strip_Null_ThrowsArgumentNullException()
    {
        Action act = () => TerminalSanitizer.Strip(null!);

        act.Should().Throw<ArgumentNullException>();
    }
}
