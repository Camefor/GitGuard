using FluentAssertions;
using GitGuard.Core;
using GitGuard.Infrastructure.FileSystem;

namespace GitGuard.Infrastructure.Tests;

/// <summary>
/// Shell profile 标记块编辑器测试（GGD-0061）：支持 bash / zsh / sh / fish，块写入幂等、卸载可逆，
/// 标记不完整时绝不删除用户内容。
/// </summary>
public sealed class ShellProfileEditorTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("/bin/sh")]
    [InlineData("/bin/bash")]
    [InlineData("/usr/bin/zsh")]
    [InlineData("/bin/dash")]
    [InlineData("/bin/ksh")]
    public void ResolveShellKind_PosixShells_ReturnPosix(string? shellPath)
    {
        ShellProfileEditor.ResolveShellKind(shellPath).Should().Be(ShellKind.Posix);
    }

    [Fact]
    public void ResolveShellKind_Fish_ReturnsFish()
    {
        ShellProfileEditor.ResolveShellKind("/usr/bin/fish").Should().Be(ShellKind.Fish);
    }

    [Fact]
    public void ResolveShellKind_UnknownShell_ThrowsUnsupported()
    {
        Action act = () => ShellProfileEditor.ResolveShellKind("/usr/bin/pwsh");

        act.Should().Throw<PathException>()
            .Which.ErrorCode.Should().Be(PathException.UNSUPPORTED);
    }

    [Fact]
    public void ResolveProfilePath_ShellConventions()
    {
        using var directory = new TempDirectory();

        ShellProfileEditor.ResolveProfilePath(directory.Root, "/usr/bin/zsh")
            .Should().Be(Path.Combine(directory.Root, ".zshrc"));
        ShellProfileEditor.ResolveProfilePath(directory.Root, "/bin/sh")
            .Should().Be(Path.Combine(directory.Root, ".profile"));
        ShellProfileEditor.ResolveProfilePath(directory.Root, "/usr/bin/fish")
            .Should().Be(Path.Combine(directory.Root, ".config", "fish", "config.fish"));
    }

    [Fact]
    public void ResolveProfilePath_Bash_PrefersExistingRcFile()
    {
        using var directory = new TempDirectory();
        File.WriteAllText(Path.Combine(directory.Root, ".bashrc"), string.Empty);

        string path = ShellProfileEditor.ResolveProfilePath(directory.Root, "/bin/bash");

        path.Should().Be(Path.Combine(directory.Root, OperatingSystem.IsMacOS() ? ".bash_profile" : ".bashrc"));
    }

    [Fact]
    public void AddBlock_EmptyContent_WritesMarkedPosixBlock()
    {
        (string content, bool changed) = ShellProfileEditor.AddBlock(null, ShellKind.Posix);

        changed.Should().BeTrue();
        content.Should().Contain(ShellProfileEditor.BEGIN_MARKER)
            .And.Contain(ShellProfileEditor.END_MARKER)
            .And.Contain("$HOME/.local/bin");
    }

    [Fact]
    public void AddBlock_IsIdempotent()
    {
        (string first, bool firstChanged) = ShellProfileEditor.AddBlock("export EDITOR=vim\n", ShellKind.Posix);
        (string second, bool secondChanged) = ShellProfileEditor.AddBlock(first, ShellKind.Posix);

        firstChanged.Should().BeTrue();
        secondChanged.Should().BeFalse();
        second.Should().Be(first);
        second.Split(ShellProfileEditor.BEGIN_MARKER).Should().HaveCount(2);
    }

    [Fact]
    public void AddBlock_Fish_WritesFishSyntax()
    {
        (string content, bool changed) = ShellProfileEditor.AddBlock(null, ShellKind.Fish);

        changed.Should().BeTrue();
        content.Should().Contain("set -gx PATH $HOME/.local/bin $PATH");
    }

    [Fact]
    public void AddBlock_PreservesCrlfStyle()
    {
        (string content, _) = ShellProfileEditor.AddBlock("export EDITOR=vim\r\n", ShellKind.Posix);

        content.Should().Contain("\r\n# >>> gitguard >>>\r\n");
        content.Replace("\r\n", string.Empty).Should().NotContain("\n", "CRLF 文件内不得混入孤立 LF");
    }

    [Fact]
    public void RemoveBlock_RemovesOnlyManagedBlock()
    {
        const string ORIGINAL = "export EDITOR=vim\n# >>> gitguard >>>\nexport PATH=\"$HOME/.local/bin:$PATH\"\n# <<< gitguard <<<\nexport LANG=en_US.UTF-8\n";

        (string content, bool changed) = ShellProfileEditor.RemoveBlock(ORIGINAL);

        changed.Should().BeTrue();
        content.Should().Contain("export EDITOR=vim")
            .And.Contain("export LANG=en_US.UTF-8")
            .And.NotContain(ShellProfileEditor.BEGIN_MARKER)
            .And.NotContain(ShellProfileEditor.END_MARKER);
    }

    [Fact]
    public void RemoveBlock_IncompleteMarkers_LeavesContentUntouched()
    {
        const string ORIGINAL = "# >>> gitguard >>>\nexport PATH=\"$HOME/.local/bin:$PATH\"\n";

        (string content, bool changed) = ShellProfileEditor.RemoveBlock(ORIGINAL);

        changed.Should().BeFalse();
        content.Should().Be(ORIGINAL);
    }

    [Fact]
    public void ContainsBlock_RequiresBothMarkersInOrder()
    {
        ShellProfileEditor.ContainsBlock($"{ShellProfileEditor.END_MARKER}\n{ShellProfileEditor.BEGIN_MARKER}").Should().BeFalse();
        ShellProfileEditor.ContainsBlock($"{ShellProfileEditor.BEGIN_MARKER}\n{ShellProfileEditor.END_MARKER}").Should().BeTrue();
    }
}
