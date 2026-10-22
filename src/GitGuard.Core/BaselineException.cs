namespace GitGuard.Core;

/// <summary>
/// 基线文件读写失败的统一异常类型（<c>10-异常处理设计.md</c> §4）：
/// 基线文件缺失结构 / 字段非法 → 退出码 13（FailClosed，不得静默忽略基线）；
/// 基线写入失败 → 退出码 31（与导出写入失败同属文件产物写入失败）。
/// 异常消息不得包含被扫描原文（DR-02）。
/// </summary>
public sealed class BaselineException : GitGuardException
{
    /// <summary>错误码：基线文件内容非法（CLI 映射退出码 13）。</summary>
    public const string BASELINE_INVALID = "BASELINE_INVALID";

    /// <summary>错误码：基线文件写入失败（CLI 映射退出码 31）。</summary>
    public const string BASELINE_WRITE_FAILED = "BASELINE_WRITE_FAILED";

    /// <summary>初始化 <see cref="BaselineException"/> 的实例。</summary>
    /// <param name="errorCode">错误码（<see cref="BASELINE_INVALID"/> / <see cref="BASELINE_WRITE_FAILED"/>）。</param>
    /// <param name="message">异常消息（面向用户的简述，不得包含被扫描内容）。</param>
    public BaselineException(string errorCode, string message)
        : base(message) => ErrorCode = errorCode;

    /// <summary>使用内部异常初始化实例。</summary>
    /// <param name="errorCode">错误码（<see cref="BASELINE_INVALID"/> / <see cref="BASELINE_WRITE_FAILED"/>）。</param>
    /// <param name="message">异常消息（不得包含被扫描内容）。</param>
    /// <param name="innerException">内部异常（如 <see cref="IOException"/>）。</param>
    public BaselineException(string errorCode, string message, Exception innerException)
        : base(message, innerException) => ErrorCode = errorCode;

    /// <summary>错误码（<c>10-异常处理设计.md</c> §4 登记值）。</summary>
    public string ErrorCode { get; }
}
