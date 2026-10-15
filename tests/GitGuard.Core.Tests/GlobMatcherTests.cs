using FluentAssertions;
using GitGuard.Core.Policy;

namespace GitGuard.Core.Tests;

public sealed class GlobMatcherTests
{
    [Theory]
    [InlineData("docs/README.md", "docs/README.md", true)]
    [InlineData("docs/README.md", "docs/readme.md", false)]
    [InlineData("docs/README.md", "docs/README.md.bak", false)]
    [InlineData("src/*.cs", "src/Program.cs", true)]
    [InlineData("src/*.cs", "src/sub/Program.cs", false)]
    [InlineData("src/*.cs", "src/.cs", true)]
    [InlineData("src/?.cs", "src/a.cs", true)]
    [InlineData("src/?.cs", "src/ab.cs", false)]
    [InlineData("**/test/**", "test/a.cs", true)]
    [InlineData("**/test/**", "src/a/test/b.cs", true)]
    [InlineData("**/test/**", "src/latest/a.cs", false)]
    [InlineData("**/bin/**", "src/bin/app.exe", true)]
    [InlineData("bin/**", "bin", true)]
    [InlineData("docs/**", "docs", true)]
    [InlineData("**", "any/deep/path.txt", true)]
    public void IsMatch_SingleAndDoubleStarSemantics(string pattern, string path, bool expected)
    {
        GlobMatcher.IsMatch(pattern, path).Should().Be(expected);
    }

    [Theory]
    [InlineData("src/**/test/*.cs", "src/test/a.cs", true)]
    [InlineData("src/**/test/*.cs", "src/a/b/test/x.cs", true)]
    [InlineData("src/**/test/*.cs", "src/a/b/x.cs", false)]
    public void IsMatch_DoubleStarMatchesZeroOrMoreSegments(string pattern, string path, bool expected)
    {
        GlobMatcher.IsMatch(pattern, path).Should().Be(expected);
    }

    [Theory]
    [InlineData("src\\**\\*.cs", "src/sub/a.cs", true)]
    [InlineData("src/**/*.cs", "src\\sub\\a.cs", true)]
    public void IsMatch_BackslashNormalizedAsSeparator(string pattern, string path, bool expected)
    {
        GlobMatcher.IsMatch(pattern, path).Should().Be(expected);
    }

    [Fact]
    public void IsMatch_EmptyPath_ReturnsFalse()
    {
        GlobMatcher.IsMatch("**", string.Empty).Should().BeFalse();
    }

    [Fact]
    public void IsMatch_TrailingSlashInPath_IgnoredSeparator()
    {
        GlobMatcher.IsMatch("src", "src/").Should().BeTrue();
    }

    [Fact]
    public void IsMatch_NullPattern_Throws()
    {
        Action act = () => GlobMatcher.IsMatch(null!, "src/a.cs");

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void IsMatch_WhitespacePattern_Throws()
    {
        Action act = () => GlobMatcher.IsMatch("   ", "src/a.cs");

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void IsMatch_NullPath_ThrowsArgumentNullException()
    {
        Action act = () => GlobMatcher.IsMatch("src/*", null!);

        act.Should().Throw<ArgumentNullException>();
    }
}
