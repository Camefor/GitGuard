using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;

namespace GitGuard.Benchmarks;

/// <summary>单次子进程运行结果：退出码、墙钟耗时（毫秒）与输出。</summary>
internal sealed record ProcessRunResult
{
    /// <summary>初始化运行结果。</summary>
    /// <param name="exitCode">进程退出码。</param>
    /// <param name="elapsedMilliseconds">墙钟耗时（毫秒）。</param>
    /// <param name="standardOutput">标准输出。</param>
    /// <param name="standardError">标准错误。</param>
    internal ProcessRunResult(int exitCode, double elapsedMilliseconds, string standardOutput, string standardError)
    {
        ExitCode = exitCode;
        ElapsedMilliseconds = elapsedMilliseconds;
        StandardOutput = standardOutput;
        StandardError = standardError;
    }

    /// <summary>进程退出码。</summary>
    public int ExitCode { get; }

    /// <summary>墙钟耗时（毫秒）。</summary>
    public double ElapsedMilliseconds { get; }

    /// <summary>标准输出。</summary>
    public string StandardOutput { get; }

    /// <summary>标准错误。</summary>
    public string StandardError { get; }
}

/// <summary>子进程执行器：注入工作目录 / 环境变量 / 标准输入，并测量进程墙钟耗时。</summary>
internal static class ProcessRunner
{
    /// <summary>启动进程并等待退出，返回墙钟耗时。</summary>
    /// <param name="executable">可执行文件路径。</param>
    /// <param name="arguments">参数列表。</param>
    /// <param name="workingDirectory">工作目录。</param>
    /// <param name="standardInput">标准输入；<see langword="null"/> 表示不写入。</param>
    /// <param name="environment">环境变量增量（值为 <see langword="null"/> 表示移除）。</param>
    /// <param name="timeout">单次运行超时。</param>
    /// <param name="cancellationToken">调用方取消令牌。</param>
    /// <returns>运行结果。</returns>
    /// <exception cref="InvalidOperationException">进程无法启动。</exception>
    /// <exception cref="TimeoutException">进程超过 <paramref name="timeout"/> 仍未退出。</exception>
    internal static async Task<ProcessRunResult> RunAsync(
        string executable,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        string? standardInput,
        IReadOnlyDictionary<string, string?> environment,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo(executable)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = standardInput is not null,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        foreach ((string name, string? value) in environment)
        {
            if (value is null)
            {
                startInfo.Environment.Remove(name);
            }
            else
            {
                startInfo.Environment[name] = value;
            }
        }

        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);

        long startedTimestamp = Stopwatch.GetTimestamp();
        using Process process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"无法启动进程：{executable}");
        try
        {
            if (standardInput is not null)
            {
                await process.StandardInput.WriteAsync(standardInput.AsMemory(), timeoutSource.Token).ConfigureAwait(false);
                process.StandardInput.Close();
            }

            Task<string> outputTask = process.StandardOutput.ReadToEndAsync(timeoutSource.Token);
            Task<string> errorTask = process.StandardError.ReadToEndAsync(timeoutSource.Token);
            await process.WaitForExitAsync(timeoutSource.Token).ConfigureAwait(false);
            string standardOutput = await outputTask.ConfigureAwait(false);
            string standardError = await errorTask.ConfigureAwait(false);

            double elapsedMilliseconds = Stopwatch.GetElapsedTime(startedTimestamp).TotalMilliseconds;
            return new ProcessRunResult(process.ExitCode, elapsedMilliseconds, standardOutput, standardError);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            TryKill(process);
            string seconds = timeout.TotalSeconds.ToString("F0", CultureInfo.InvariantCulture);
            throw new TimeoutException($"进程超过 {seconds}s 未结束：{executable}");
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            throw;
        }
    }

    private static void TryKill(Process process)
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
            // 进程已退出：无需处理
        }
        catch (Win32Exception)
        {
            // 终止失败：保持原始取消 / 超时异常
        }
    }
}
