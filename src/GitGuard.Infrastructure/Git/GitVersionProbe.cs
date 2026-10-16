using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace GitGuard.Infrastructure.Git;

/// <summary>
/// 轻量 git 版本探测（<c>git --version</c>）：与 <see cref="GitAccess"/> 不同，本探测器不要求当前目录是 Git 仓库，
/// 供 <c>gitguard version</c> 在任意位置展示 Git 环境信息（FR-012 状态展示的降级形态）。
/// 失败不抛异常：git 缺失或超时是「信息不可得」而非阻断条件，由调用方决定提示文案。
/// </summary>
public static class GitVersionProbe
{
    /// <summary>探测超时；比扫描级 Git 命令（30s）更短，避免版本查询拖慢冷启动。</summary>
    public static readonly TimeSpan DEFAULT_TIMEOUT = TimeSpan.FromSeconds(10);

    /// <summary>探测本机 git 版本。</summary>
    /// <param name="gitExecutable">git 可执行文件；默认从 PATH 解析 <c>git</c>。</param>
    /// <param name="timeout">探测超时；默认 10s。</param>
    /// <returns>探测结果（不可用时携带失败原因，不抛异常）。</returns>
    /// <exception cref="ArgumentException"><paramref name="gitExecutable"/> 为空或全空白。</exception>
    public static GitVersionProbeResult Probe(string gitExecutable = "git", TimeSpan? timeout = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gitExecutable);
        TimeSpan effectiveTimeout = timeout ?? DEFAULT_TIMEOUT;

        var startInfo = new ProcessStartInfo(gitExecutable)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add("--version");

        try
        {
            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return new GitVersionProbeResult(false, null, $"启动 {gitExecutable} 进程失败：未返回进程句柄。");
            }

            Task<string> standardOutput = process.StandardOutput.ReadToEndAsync();
            Task<string> standardError = process.StandardError.ReadToEndAsync();

            if (!process.WaitForExit((int)effectiveTimeout.TotalMilliseconds))
            {
                KillProcess(process);
                return new GitVersionProbeResult(false, null, $"git --version 超时（{effectiveTimeout.TotalSeconds:0}s）。");
            }

            string output = standardOutput.GetAwaiter().GetResult().Trim();
            if (process.ExitCode != 0 || output.Length == 0)
            {
                string error = standardError.GetAwaiter().GetResult().Trim();
                return new GitVersionProbeResult(false, null, $"git --version 失败（退出码 {process.ExitCode}）：{error}");
            }

            return new GitVersionProbeResult(true, output, null);
        }
        catch (Win32Exception ex)
        {
            return new GitVersionProbeResult(false, null, $"无法启动 git（{gitExecutable}）：{ex.Message}");
        }
    }

    private static void KillProcess(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
            process.WaitForExit();
        }
        catch (InvalidOperationException)
        {
            // 进程在 Kill 前已自行退出
        }
        catch (Win32Exception)
        {
            // 无权终止子进程（罕见）：放弃等待，返回超时结果
        }
    }
}

/// <summary>
/// <see cref="GitVersionProbe.Probe"/> 的结果。
/// </summary>
public sealed record GitVersionProbeResult
{
    /// <summary>初始化探测结果。</summary>
    /// <param name="available">git 是否可用（可执行文件存在且 <c>--version</c> 正常返回）。</param>
    /// <param name="versionLine">git 原始版本行（如 <c>git version 2.43.0</c>）；不可用时为 <see langword="null"/>。</param>
    /// <param name="failureMessage">不可用原因；可用时为 <see langword="null"/>。</param>
    /// <exception cref="ArgumentException"><paramref name="available"/> 为 <see langword="true"/> 但 <paramref name="versionLine"/> 缺失，或为 <see langword="false"/> 但 <paramref name="failureMessage"/> 缺失。</exception>
    public GitVersionProbeResult(bool available, string? versionLine, string? failureMessage)
    {
        if (available)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(versionLine);
        }
        else
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(failureMessage);
        }

        Available = available;
        VersionLine = versionLine;
        FailureMessage = failureMessage;
    }

    /// <summary>git 是否可用（可执行文件存在且 <c>--version</c> 正常返回）。</summary>
    public bool Available { get; }

    /// <summary>git 原始版本行（如 <c>git version 2.43.0</c>）；不可用时为 <see langword="null"/>。</summary>
    public string? VersionLine { get; }

    /// <summary>不可用原因；可用时为 <see langword="null"/>。</summary>
    public string? FailureMessage { get; }
}
