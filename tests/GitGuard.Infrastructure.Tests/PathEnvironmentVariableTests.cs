using FluentAssertions;
using GitGuard.Infrastructure.FileSystem;

namespace GitGuard.Infrastructure.Tests;

/// <summary>
/// PATH 环境变量纯函数测试（GGD-0061）：拆分 / 去重 / 追加 / 移除必须保留既有条目书写形式，
/// 仅比较时规范化，避免把 Windows <c>%VAR%</c> 引用重写为绝对路径。
/// </summary>
public sealed class PathEnvironmentVariableTests
{
    [Fact]
    public void Split_DropsEmptyAndWhitespaceEntries()
    {
        PathEnvironmentVariable.Split("a;; b ; ", ';')
            .Should().Equal("a", "b");
        PathEnvironmentVariable.Split(null, ';').Should().BeEmpty();
        PathEnvironmentVariable.Split("   ", ';').Should().BeEmpty();
    }

    [Fact]
    public void Append_NewDirectory_AppendsToEnd()
    {
        using var directory = new TempDirectory();
        string target = Path.Combine(directory.Root, "bin");

        string result = PathEnvironmentVariable.Append("C:\\Windows;C:\\Tools", ';', target, StringComparison.OrdinalIgnoreCase);

        result.Should().EndWith(target);
        PathEnvironmentVariable.Split(result, ';').Should().Equal("C:\\Windows", "C:\\Tools", target);
    }

    [Fact]
    public void Append_EmptyValue_ReturnsDirectoryOnly()
    {
        using var directory = new TempDirectory();
        string target = Path.Combine(directory.Root, "bin");

        PathEnvironmentVariable.Append(null, ';', target, StringComparison.OrdinalIgnoreCase).Should().Be(target);
    }

    [Fact]
    public void Append_DirectoryAlreadyPresent_ReturnsOriginalValue()
    {
        using var directory = new TempDirectory();
        string target = Path.Combine(directory.Root, "bin");

        string value = "C:\\Windows;" + target + Path.DirectorySeparatorChar;

        string result = PathEnvironmentVariable.Append(value, ';', target, StringComparison.OrdinalIgnoreCase);

        result.Should().Be(value);
        PathEnvironmentVariable.Split(result, ';').Should().HaveCount(2);
    }

    [Fact]
    public void Contains_ComparerControlsCaseSensitivity()
    {
        using var directory = new TempDirectory();
        string target = Path.Combine(directory.Root, "Bin");
        string upper = target.ToUpperInvariant();
        IReadOnlyList<string> entries = new[] { upper };

        PathEnvironmentVariable.Contains(entries, target, StringComparison.OrdinalIgnoreCase).Should().BeTrue();
        PathEnvironmentVariable.Contains(entries, target, StringComparison.Ordinal).Should().BeFalse();
    }

    [Fact]
    public void Remove_ManagedEntry_RemovesOnlyMatchingEntry()
    {
        using var directory = new TempDirectory();
        string target = Path.Combine(directory.Root, "bin");

        string updated = PathEnvironmentVariable.Remove(
            $"C:\\Windows;{target};C:\\Tools",
            ';',
            target,
            StringComparison.OrdinalIgnoreCase,
            out bool removed);

        removed.Should().BeTrue();
        updated.Should().Be("C:\\Windows;C:\\Tools");
    }

    [Fact]
    public void Remove_MissingEntry_LeavesValueUnchanged()
    {
        const string VALUE = "C:\\Windows;C:\\Tools";

        string updated = PathEnvironmentVariable.Remove(
            VALUE,
            ';',
            "D:\\Missing",
            StringComparison.OrdinalIgnoreCase,
            out bool removed);

        removed.Should().BeFalse();
        updated.Should().Be(VALUE);
    }

    [Fact]
    public void Append_PreservesVariableReferencesInExistingEntries()
    {
        using var directory = new TempDirectory();
        string target = Path.Combine(directory.Root, "bin");

        string result = PathEnvironmentVariable.Append("%USERPROFILE%\\bin;C:\\Tools", ';', target, StringComparison.Ordinal);

        result.Should().StartWith("%USERPROFILE%\\bin;C:\\Tools;");
        result.Should().Contain("%USERPROFILE%\\bin", "既有条目不得被展开重写");
    }
}
