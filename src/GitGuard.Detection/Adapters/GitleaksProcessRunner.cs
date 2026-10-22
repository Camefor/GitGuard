using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using GitGuard.Core;

namespace GitGuard.Detection.Adapters;

/// <summary>
/// Gitleaks 子进程执行器：<c>ProcessStartInfo.ArgumentList</c> 逐项传参（不经 shell、不做字符串拼接），
/// UTF-8 读取输出；超时或调用方取消时终止整个进程树且绝不重试（10-异常处理设计 §2 / §5）。
/// </summary>
public sealed class GitleaksProcessRunner : IGitleaksProcessRunner
{
    /// <summary>启动 Gitleaks 并等待其结束。</summary>
    /// <param name="executablePath">可执行文件绝对路径。</param>
    /// <param name="arguments">命令行参数。</param>
    /// <param name="timeout">单次调用超时。</param>
    /// <param name="cancellationToken">调用方取消令牌。</param>
    /// <returns>退出码与输出文本。</returns>
    /// <exception cref="ArgumentException"><paramref name="executablePath"/> 为空。</exception>
    /// <exception cref="ArgumentNullException"><paramref name="arguments"/> 为 <see langword="null"/>。</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="timeout"/> 非正。</exception>
    /// <exception cref="DetectionException">进程无法启动或执行超时（FailClosed）。</exception>
    public async Task<GitleaksProcessResult> RunAsync(
        string executablePath,
        IReadOnlyList<string> arguments,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(timeout, TimeSpan.Zero);

        string? executableDirectory = Path.GetDirectoryName(executablePath);
        var startInfo = new ProcessStartInfo(executablePath)
        {
            WorkingDirectory = string.IsNullOrEmpty(executableDirectory) ? Environment.CurrentDirectory : executableDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        Process process;
        try
        {
            process = Process.Start(startInfo)
                ?? throw new DetectionException($"启动 gitleaks 进程（{executablePath}）失败：未返回进程句柄。");
        }
        catch (Win32Exception ex)
        {
            throw new DetectionException(
                $"无法启动 gitleaks 进程（{executablePath}）：请确认可执行文件存在且具有执行权限。",
                ruleId: null,
                ex);
        }

        using (process)
        {
            Task<string> standardOutput = process.StandardOutput.ReadToEndAsync(cancellationToken);
            Task<string> standardError = process.StandardError.ReadToEndAsync(cancellationToken);

            // 链接调用方令牌与单次调用超时：扫描级取消原样上抛，适配器超时转为 DETECTOR_FAILED
            using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutSource.CancelAfter(timeout);
            try
            {
                await process.WaitForExitAsync(timeoutSource.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                KillProcess(process);
                throw new DetectionException(
                    $"gitleaks 执行超时（{timeout.TotalSeconds:0.#}s），已终止子进程；按 FailClosed 直接失败且不重试。");
            }
            catch (OperationCanceledException)
            {
                KillProcess(process);
                throw;
            }

            return new GitleaksProcessResult(
                process.ExitCode,
                await standardOutput.ConfigureAwait(false),
                await standardError.ConfigureAwait(false));
        }
    }

    /// <summary>终止整个进程树；进程已退出或句柄失效时忽略。</summary>
    /// <param name="process">目标进程。</param>
    private static void KillProcess(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
            // 进程在检查与终止之间自然退出
        }
        catch (Win32Exception)
        {
            // 句柄已失效：无可终止内容
        }
    }
}
