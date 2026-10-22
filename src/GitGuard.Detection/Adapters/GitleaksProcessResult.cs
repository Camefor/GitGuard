namespace GitGuard.Detection.Adapters;

/// <summary>
/// Gitleaks 子进程执行结果（<c>IGitleaksProcessRunner</c> 的返回值）：
/// 仅承载退出码与标准输出 / 错误文本，供探测器与适配器解析，不包含任何敏感内容处理逻辑。
/// </summary>
public sealed class GitleaksProcessResult
{
    /// <summary>初始化执行结果。</summary>
    /// <param name="exitCode">进程退出码。</param>
    /// <param name="standardOutput">标准输出全文。</param>
    /// <param name="standardError">标准错误全文。</param>
    /// <exception cref="ArgumentNullException"><paramref name="standardOutput"/> 或 <paramref name="standardError"/> 为 <see langword="null"/>。</exception>
    public GitleaksProcessResult(int exitCode, string standardOutput, string standardError)
    {
        ArgumentNullException.ThrowIfNull(standardOutput);
        ArgumentNullException.ThrowIfNull(standardError);

        ExitCode = exitCode;
        StandardOutput = standardOutput;
        StandardError = standardError;
    }

    /// <summary>进程退出码。</summary>
    public int ExitCode { get; }

    /// <summary>标准输出全文（UTF-8 解码）。</summary>
    public string StandardOutput { get; }

    /// <summary>标准错误全文（UTF-8 解码）。</summary>
    public string StandardError { get; }
}
