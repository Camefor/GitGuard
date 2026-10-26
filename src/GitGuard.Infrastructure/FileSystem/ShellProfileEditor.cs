using GitGuard.Core;

namespace GitGuard.Infrastructure.FileSystem;

/// <summary>Shell profile 语法族：POSIX（bash / zsh / sh）或 fish。</summary>
internal enum ShellKind
{
    /// <summary>POSIX 兼容语法（<c>export PATH</c>）。</summary>
    Posix = 0,

    /// <summary>fish 语法（<c>set -gx PATH</c>）。</summary>
    Fish = 1,
}

/// <summary>
/// Shell profile 标记块编辑器（GGD-0061）：以 <c># &gt;&gt;&gt; gitguard &gt;&gt;&gt;</c> / <c># &lt;&lt;&lt; gitguard &lt;&lt;&lt;</c>
/// 包裹幂等块，卸载只删除该块、保留用户其他内容。纯文本操作，不执行 Shell。
/// </summary>
internal static class ShellProfileEditor
{
    /// <summary>标记块起始行。</summary>
    internal const string BEGIN_MARKER = "# >>> gitguard >>>";

    /// <summary>标记块结束行。</summary>
    internal const string END_MARKER = "# <<< gitguard <<<";

    /// <summary>解析 Shell 语法族；未知 Shell 抛 <see cref="PathException"/>（退出码 61）。</summary>
    /// <param name="shellPath"><c>SHELL</c> 环境变量值（如 <c>/bin/zsh</c>）；空值按 POSIX <c>sh</c> 处理。</param>
    /// <returns>Shell 语法族。</returns>
    /// <exception cref="PathException">Shell 不支持自动写入 profile。</exception>
    internal static ShellKind ResolveShellKind(string? shellPath)
    {
        string name = Path.GetFileName(shellPath ?? string.Empty).ToLowerInvariant();
        if (name.Length == 0)
        {
            return ShellKind.Posix;
        }

        return name switch
        {
            "sh" or "bash" or "zsh" or "dash" or "ksh" => ShellKind.Posix,
            "fish" => ShellKind.Fish,
            _ => throw new PathException(
                PathException.UNSUPPORTED,
                $"不支持自动修改 Shell profile（SHELL={shellPath}）；请手动把 ~/.local/bin 加入 PATH，或改用 --update-profile 指定受支持的 Shell。"),
        };
    }

    /// <summary>解析当前用户应写入的 profile 文件路径。</summary>
    /// <param name="homeDirectory">用户主目录。</param>
    /// <param name="shellPath"><c>SHELL</c> 环境变量值。</param>
    /// <returns>profile 绝对路径（文件可能尚不存在）。</returns>
    /// <exception cref="PathException">Shell 不支持。</exception>
    internal static string ResolveProfilePath(string homeDirectory, string? shellPath)
    {
        if (ResolveShellKind(shellPath) == ShellKind.Fish)
        {
            return Path.Combine(homeDirectory, ".config", "fish", "config.fish");
        }

        string name = Path.GetFileName(shellPath ?? string.Empty).ToLowerInvariant();
        if (name == "zsh")
        {
            return Path.Combine(homeDirectory, ".zshrc");
        }

        if (name == "bash")
        {
            return ResolveBashProfile(homeDirectory);
        }

        return Path.Combine(homeDirectory, ".profile");
    }

    /// <summary>判断 profile 内容是否已包含 GitGuard 标记块（起止标记均存在且顺序正确）。</summary>
    /// <param name="content">profile 内容；<see langword="null"/> 视为空。</param>
    /// <returns>包含返回 <see langword="true"/>。</returns>
    internal static bool ContainsBlock(string? content)
    {
        if (string.IsNullOrEmpty(content))
        {
            return false;
        }

        int begin = content.IndexOf(BEGIN_MARKER, StringComparison.Ordinal);
        return begin >= 0
            && content.IndexOf(END_MARKER, begin, StringComparison.Ordinal) >= 0;
    }

    /// <summary>追加 GitGuard 标记块（已存在时原样返回）。</summary>
    /// <param name="content">原 profile 内容；<see langword="null"/> 视为空文件。</param>
    /// <param name="kind">Shell 语法族。</param>
    /// <returns>新内容与是否发生修改。</returns>
    internal static (string Content, bool Changed) AddBlock(string? content, ShellKind kind)
    {
        if (ContainsBlock(content))
        {
            return (content ?? string.Empty, false);
        }

        string newLine = DetectNewLine(content);
        string block = BuildBlock(kind, newLine);
        if (string.IsNullOrEmpty(content))
        {
            return (block + newLine, true);
        }

        string prefix = content.EndsWith('\n') ? content : content + newLine;
        return (prefix + newLine + block + newLine, true);
    }

    /// <summary>移除 GitGuard 标记块（保留其他内容；标记不完整时不修改，避免误删用户内容）。</summary>
    /// <param name="content">原 profile 内容。</param>
    /// <returns>新内容与是否发生修改。</returns>
    internal static (string Content, bool Changed) RemoveBlock(string? content)
    {
        if (string.IsNullOrEmpty(content))
        {
            return (content ?? string.Empty, false);
        }

        int begin = content.IndexOf(BEGIN_MARKER, StringComparison.Ordinal);
        if (begin < 0)
        {
            return (content, false);
        }

        int end = content.IndexOf(END_MARKER, begin, StringComparison.Ordinal);
        if (end < 0)
        {
            return (content, false);
        }

        int endOfLine = content.IndexOf('\n', end);
        int removeEnd = endOfLine < 0 ? content.Length : endOfLine + 1;
        string result = content.Remove(begin, removeEnd - begin);
        return (result, true);
    }

    private static string ResolveBashProfile(string homeDirectory)
    {
        string[] candidates = OperatingSystem.IsMacOS()
            ? new[] { ".bash_profile", ".bashrc" }
            : new[] { ".bashrc", ".bash_profile" };
        foreach (string candidate in candidates)
        {
            string path = Path.Combine(homeDirectory, candidate);
            if (File.Exists(path))
            {
                return path;
            }
        }

        return Path.Combine(homeDirectory, ".profile");
    }

    private static string BuildBlock(ShellKind kind, string newLine)
    {
        string[] lines = kind == ShellKind.Fish
            ? new[]
            {
                BEGIN_MARKER,
                "if not contains -- $HOME/.local/bin $PATH",
                "    set -gx PATH $HOME/.local/bin $PATH",
                "end",
                END_MARKER,
            }
            : new[]
            {
                BEGIN_MARKER,
                "case \":$PATH:\" in",
                "    *\":$HOME/.local/bin:\"*) ;;",
                "    *) export PATH=\"$HOME/.local/bin:$PATH\" ;;",
                "esac",
                END_MARKER,
            };
        return string.Join(newLine, lines);
    }

    private static string DetectNewLine(string? content) =>
        content is not null && content.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
}
