using GitGuard.Cli.Localization;
using GitGuard.Cli.Logging;

namespace GitGuard.Cli.Commands;

/// <summary>命令动作内的用法错误出口（参数组合非法等，统一退出码 7 + 提示 <c>--help</c>）。</summary>
internal static class UsageError
{
    /// <summary>输出用法错误报告并返回退出码 7。</summary>
    /// <param name="runtime">运行时上下文（渲染器与日志器须已配置）。</param>
    /// <param name="details">错误描述（逐条输出）。</param>
    /// <returns><see cref="ExitCodes.USAGE_ERROR"/>。</returns>
    /// <exception cref="ArgumentNullException"><paramref name="runtime"/> 为 <see langword="null"/>。</exception>
    /// <exception cref="ArgumentException"><paramref name="details"/> 为空集合。</exception>
    internal static int Report(CliRuntime runtime, params string[] details)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        if (details.Length == 0)
        {
            throw new ArgumentException("至少需要一条错误描述。", nameof(details));
        }

        runtime.Writer.WriteUsageError(details, runtime.TraceId);
        runtime.Logger.Log(
            CliLogLevel.Warning,
            "Cli",
            string.Join(runtime.Text.Get(CliTextKey.ErrorJoinSeparator), details),
            new LogFields { ErrorCode = ExitCodeMapper.CLI_USAGE_ERROR, ExitCode = ExitCodes.USAGE_ERROR, TraceId = runtime.TraceId });
        return ExitCodes.USAGE_ERROR;
    }
}
