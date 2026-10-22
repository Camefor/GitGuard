namespace GitGuard.Detection.Adapters;

/// <summary>
/// Gitleaks 子进程执行抽象：生产实现为 <see cref="GitleaksProcessRunner"/>，
/// 测试可注入替身以验证探测与 JSON 映射，无需真实安装 Gitleaks。
/// </summary>
public interface IGitleaksProcessRunner
{
    /// <summary>启动 Gitleaks 并等待其结束（超时终止进程树，不重试）。</summary>
    /// <param name="executablePath">可执行文件绝对路径。</param>
    /// <param name="arguments">命令行参数（逐项传递，不经过 shell）。</param>
    /// <param name="timeout">单次调用超时（模块设计 Secret §4：10s）。</param>
    /// <param name="cancellationToken">调用方取消令牌。</param>
    /// <returns>退出码与输出文本。</returns>
    /// <exception cref="GitGuard.Core.DetectionException">无法启动进程或执行超时（FailClosed）。</exception>
    Task<GitleaksProcessResult> RunAsync(
        string executablePath,
        IReadOnlyList<string> arguments,
        TimeSpan timeout,
        CancellationToken cancellationToken = default);
}
