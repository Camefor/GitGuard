using GitGuard.Core;

namespace GitGuard.Cli;

/// <summary>
/// 异常 → 退出码 / 错误码的纯函数映射（<c>docs/10-异常处理设计.md</c> §3 / §4）：
/// GitGuard 各层自定义异常均定义于 <c>GitGuard.Core</c> 根命名空间，CLI 在此完成唯一一次分类，
/// 未登记类型一律按 <see cref="ExitCodes.UNKNOWN_ERROR"/>（99）FailClosed 处理。
/// </summary>
public static class ExitCodeMapper
{
    /// <summary>错误码：参数 / CLI 用法错误（退出码 7，GGD-0009 登记）。</summary>
    public const string CLI_USAGE_ERROR = "CLI_USAGE_ERROR";

    /// <summary>错误码：扫描超时或外部取消（退出码 12，<c>10-异常处理设计</c> §4 登记值）。</summary>
    public const string SCAN_TIMEOUT = "SCAN_TIMEOUT";

    /// <summary>错误码：规则 ID 不存在（退出码 40，<c>10-异常处理设计</c> §4 登记值）。</summary>
    public const string RULE_NOT_FOUND = "RULE_NOT_FOUND";

    /// <summary>错误码：未分类异常（退出码 99，<c>10-异常处理设计</c> §4 登记值）。</summary>
    public const string UNKNOWN_ERROR = "UNKNOWN_ERROR";

    /// <summary>映射异常对应的 CLI 退出码。</summary>
    /// <param name="exception">捕获到的异常。</param>
    /// <returns>退出码（<see cref="ExitCodes"/> 常量之一）。</returns>
    /// <exception cref="ArgumentNullException"><paramref name="exception"/> 为 <see langword="null"/>。</exception>
    public static int Map(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        return exception switch
        {
            GitException git when git.ErrorCode == GitException.NOT_A_GIT_REPOSITORY => ExitCodes.NOT_A_GIT_REPOSITORY,
            GitException => ExitCodes.GIT_COMMAND_FAILED,
            HookException hook when hook.ErrorCode == HookException.WRITE_PERMISSION_DENIED => ExitCodes.WRITE_PERMISSION_DENIED,
            HookException hook when hook.ErrorCode == HookException.UNKNOWN_HOOK_PRESENT => ExitCodes.UNKNOWN_HOOK_PRESENT,
            HookException hook when hook.ErrorCode == HookException.INSTALL_IN_PROGRESS => ExitCodes.INSTALL_IN_PROGRESS,
            HookException => ExitCodes.UNKNOWN_ERROR,
            AllowlistException allowlist when allowlist.ErrorCode == AllowlistException.ALLOWLIST_VALIDATION_FAILED
                => ExitCodes.ALLOWLIST_VALIDATION_FAILED,
            AllowlistException => ExitCodes.ALLOWLIST_NOT_FOUND,
            BaselineException baseline when baseline.ErrorCode == BaselineException.BASELINE_WRITE_FAILED
                => ExitCodes.AUDIT_EXPORT_FAILED,
            BaselineException => ExitCodes.CONFIG_INVALID,
            ConfigurationException => ExitCodes.CONFIG_INVALID,
            DetectionException => ExitCodes.DETECTOR_FAILED,
            SqliteException => ExitCodes.AUDIT_STORE_FAILED,
            AuditExportException => ExitCodes.AUDIT_EXPORT_FAILED,
            OperationCanceledException => ExitCodes.SCAN_TIMEOUT,
            _ => ExitCodes.UNKNOWN_ERROR,
        };
    }

    /// <summary>映射异常对应的错误码（<c>docs/10-异常处理设计.md</c> §4 登记表；错误 JSON 的 <c>error_code</c> 字段）。</summary>
    /// <param name="exception">捕获到的异常。</param>
    /// <returns>错误码字符串。</returns>
    /// <exception cref="ArgumentNullException"><paramref name="exception"/> 为 <see langword="null"/>。</exception>
    public static string ErrorCodeOf(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        return exception switch
        {
            GitException git => git.ErrorCode,
            HookException hook => hook.ErrorCode,
            AllowlistException allowlist => allowlist.ErrorCode,
            BaselineException baseline => baseline.ErrorCode,
            ConfigurationException configuration => configuration.ErrorCode,
            DetectionException detection => detection.ErrorCode,
            SqliteException => SqliteException.AUDIT_STORE_FAILED,
            AuditExportException export => export.ErrorCode,
            OperationCanceledException => SCAN_TIMEOUT,
            _ => UNKNOWN_ERROR,
        };
    }
}
