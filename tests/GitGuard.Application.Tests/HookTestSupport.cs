using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace GitGuard.Application.Tests;

/// <summary>
/// Hook 分发脚本真实执行辅助（16-测试方案：TC-DR-05-01 必须由真实 POSIX shell 执行分发脚本，
/// 文本断言不能替代执行结果）。
/// 平台实测结论（Windows + Git for Windows）：MSYS sh 的 <c>[ -x ]</c> 以文件头 shebang 作为可执行判据，
/// 因此替身可执行文件必须是无扩展名的 shebang 脚本（<c>.cmd</c> 判定为不可执行，注入会走 exit 10 分支）。
/// </summary>
internal static class HookTestSupport
{
    /// <summary>
    /// 解析可用的 POSIX shell；Windows 优先返回 Git for Windows 自带的 MSYS sh
    /// （与 git 执行 hook 的解释器一致），其次回退 PATH 上的 <c>sh</c>；均不可用时返回 <see langword="null"/>。
    /// </summary>
    internal static string? ResolveShPath()
    {
        if (OperatingSystem.IsWindows())
        {
            string? gitRoot = TryResolveGitRoot();
            if (gitRoot is not null)
            {
                string candidate = Path.Combine(gitRoot, "bin", "sh.exe");
                if (File.Exists(candidate))
                {
                    return candidate;
                }

                candidate = Path.Combine(gitRoot, "usr", "bin", "sh.exe");
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }

        return CanStartShell("sh") ? "sh" : null;
    }

    /// <summary>以 POSIX shell 运行脚本（工作目录与 Git 执行 hook 时一致为仓库根），返回进程退出码。</summary>
    /// <param name="shPath">shell 可执行文件路径（<see cref="ResolveShPath"/> 的返回值）。</param>
    /// <param name="scriptPath">待执行脚本路径。</param>
    /// <param name="workingDirectory">工作目录（仓库根）。</param>
    /// <param name="arguments">传给脚本的位置参数（对应 hook 的 <c>$@</c>）。</param>
    /// <returns>脚本进程退出码。</returns>
    /// <exception cref="InvalidOperationException">shell 进程无法启动。</exception>
    internal static int RunScript(string shPath, string scriptPath, string workingDirectory, params string[] arguments)
    {
        var startInfo = new ProcessStartInfo(shPath)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add(ToUnixPath(scriptPath));
        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using Process process = Process.Start(startInfo) ?? throw new InvalidOperationException("无法启动 sh 进程。");
        Task<string> standardOutput = process.StandardOutput.ReadToEndAsync();
        Task<string> standardError = process.StandardError.ReadToEndAsync();
        process.WaitForExit();

        _ = standardOutput.GetAwaiter().GetResult();
        _ = standardError.GetAwaiter().GetResult();
        return process.ExitCode;
    }

    /// <summary>
    /// 生成 GitGuard 替身可执行文件：把收到的参数追加到 <paramref name="argumentLogPath"/>，
    /// 退出码取 <paramref name="exitCodeFilePath"/> 内容（缺省 0），便于在用例中改写期望退出码。
    /// </summary>
    internal static string CreateStubExecutable(string filePath, string argumentLogPath, string exitCodeFilePath)
    {
        string body =
            $"printf '%s\\n' \"$*\" >> \"{ToUnixPath(argumentLogPath)}\"\n" +
            $"exit \"$(cat \"{ToUnixPath(exitCodeFilePath)}\" 2>/dev/null || echo 0)\"\n";
        WritePosixScript(filePath, body);
        return filePath;
    }

    /// <summary>写入 POSIX sh 脚本（无 BOM、LF 行尾，保证 shebang 生效）。</summary>
    internal static void WritePosixScript(string filePath, string body) =>
        File.WriteAllText(filePath, "#!/bin/sh\n" + body, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

    /// <summary>把路径转为正斜杠形态（MSYS sh 把反斜杠当转义字符）。</summary>
    internal static string ToUnixPath(string path) =>
        OperatingSystem.IsWindows() ? path.Replace('\\', '/') : path;

    private static string? TryResolveGitRoot()
    {
        try
        {
            var startInfo = new ProcessStartInfo("git")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            startInfo.ArgumentList.Add("--exec-path");

            using Process process = Process.Start(startInfo) ?? throw new InvalidOperationException("无法启动 git 进程。");
            string output = process.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit();
            if (process.ExitCode != 0 || output.Length == 0)
            {
                return null;
            }

            // <git-root>/mingw64/libexec/git-core：上溯三级即 Git 安装根
            return Path.GetFullPath(Path.Combine(output, "..", "..", ".."));
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            return null;
        }
    }

    private static bool CanStartShell(string executable)
    {
        try
        {
            var startInfo = new ProcessStartInfo(executable)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            startInfo.ArgumentList.Add("-c");
            startInfo.ArgumentList.Add("exit 0");

            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return false;
            }

            process.WaitForExit();
            return process.ExitCode == 0;
        }
        catch (Win32Exception)
        {
            return false;
        }
    }
}
