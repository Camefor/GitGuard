namespace GitGuard.E2E.Tests;

/// <summary>子进程执行结果快照（退出码 + 已捕获的完整标准流）。</summary>
internal sealed class ProcessResult
{
    /// <summary>创建结果快照。</summary>
    /// <param name="exitCode">进程退出码。</param>
    /// <param name="standardOutput">标准输出全文。</param>
    /// <param name="standardError">标准错误全文。</param>
    internal ProcessResult(int exitCode, string standardOutput, string standardError)
    {
        ExitCode = exitCode;
        StandardOutput = standardOutput;
        StandardError = standardError;
    }

    /// <summary>进程退出码（GitGuard CLI 契约见 06-API接口设计 §6）。</summary>
    internal int ExitCode { get; }

    /// <summary>标准输出全文（<c>--format json</c> 的机器可读输出）。</summary>
    internal string StandardOutput { get; }

    /// <summary>标准错误全文（文本报告、日志与错误提示）。</summary>
    internal string StandardError { get; }
}
