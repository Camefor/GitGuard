using System.ComponentModel;
using System.Diagnostics;

namespace GitGuard.Desktop.Services;

/// <summary>
/// 基于用户环境变量的编辑器启动器（GGD-0043）：优先 <c>GITGUARD_EDITOR</c>，其次 <c>VISUAL</c> / <c>EDITOR</c>；
/// 常见编辑器按各自语法携带行号，其余编辑器只打开文件；均未配置时回退到操作系统文件关联。
/// 不阻塞 UI 等待编辑器退出，也不读取文件内容。
/// </summary>
public sealed class ShellEditorLauncher : IEditorLauncher
{
    private static readonly string[] _vscodeFamily = new[] { "code", "code-insiders", "cursor", "codium", "windsurf" };
    private static readonly string[] _lineSuffixEditors = new[] { "subl", "sublime_text", "zed" };
    private static readonly string[] _vimFamily = new[] { "vim", "nvim", "vi" };
    private static readonly string[] _notepadPlusPlus = new[] { "notepad++", "notepad++portable" };
    private static readonly string[] _editorEnvironmentVariables = new[] { "GITGUARD_EDITOR", "VISUAL", "EDITOR" };

    /// <inheritdoc />
    public Task<EditorLaunchResult> OpenAsync(
        string repositoryPath,
        string relativeFilePath,
        int lineNumber,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(relativeFilePath);
        ArgumentOutOfRangeException.ThrowIfLessThan(lineNumber, 1);
        cancellationToken.ThrowIfCancellationRequested();

        if (!Directory.Exists(repositoryPath))
        {
            return Task.FromResult(new EditorLaunchResult(EditorLaunchStatus.RepositoryMissing));
        }

        string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(repositoryPath));
        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(relativeFilePath, root);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return Task.FromResult(new EditorLaunchResult(EditorLaunchStatus.PathOutsideRepository, ex.Message));
        }

        string relative = Path.GetRelativePath(root, fullPath);
        if (relative == ".." || relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
        {
            return Task.FromResult(new EditorLaunchResult(EditorLaunchStatus.PathOutsideRepository));
        }

        if (!File.Exists(fullPath))
        {
            return Task.FromResult(new EditorLaunchResult(EditorLaunchStatus.FileMissing));
        }

        string? editor = ResolveEditor();
        return Task.FromResult(editor is null
            ? Launch(fullPath, new List<string>())
            : Launch(editor, BuildEditorArguments(editor, fullPath, lineNumber)));
    }

    private static string? ResolveEditor()
    {
        foreach (string variable in _editorEnvironmentVariables)
        {
            string? value = Environment.GetEnvironmentVariable(variable);
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return null;
    }

    private static List<string> BuildEditorArguments(string editor, string fullPath, int lineNumber)
    {
        string name = Path.GetFileNameWithoutExtension(editor).ToLowerInvariant();
        var arguments = new List<string>(2);

        if (_vscodeFamily.Contains(name, StringComparer.Ordinal))
        {
            arguments.Add("--goto");
            arguments.Add($"{fullPath}:{lineNumber}");
        }
        else if (_lineSuffixEditors.Contains(name, StringComparer.Ordinal))
        {
            arguments.Add($"{fullPath}:{lineNumber}");
        }
        else if (_vimFamily.Contains(name, StringComparer.Ordinal))
        {
            arguments.Add($"+{lineNumber}");
            arguments.Add(fullPath);
        }
        else if (_notepadPlusPlus.Contains(name, StringComparer.Ordinal))
        {
            arguments.Add($"-n{lineNumber}");
            arguments.Add(fullPath);
        }
        else
        {
            arguments.Add(fullPath);
        }

        return arguments;
    }

    private static EditorLaunchResult Launch(string executable, List<string> arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            UseShellExecute = true,
        };

        if (arguments.Count > 0)
        {
            startInfo.Arguments = string.Join(" ", arguments.Select(QuoteArgument));
        }

        try
        {
            using var process = Process.Start(startInfo);
            return process is null
                ? new EditorLaunchResult(EditorLaunchStatus.LaunchFailed, "Process.Start 返回 null。")
                : new EditorLaunchResult(EditorLaunchStatus.Opened);
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            return new EditorLaunchResult(EditorLaunchStatus.LaunchFailed, ex.Message);
        }
    }

    private static string QuoteArgument(string argument) =>
        argument.Contains('"')
            ? $"\"{argument.Replace("\"", "\\\"", StringComparison.Ordinal)}\""
            : argument.Any(char.IsWhiteSpace) ? $"\"{argument}\"" : argument;
}
