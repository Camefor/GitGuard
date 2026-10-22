namespace GitGuard.Core;

/// <summary>
/// 白名单管理失败的统一异常类型（<c>10-异常处理设计.md</c> §4）：参数校验失败映射退出码 20，
/// 待删除条目不存在映射退出码 21。置于 Core 根命名空间，供 CLI 层直接映射。
/// </summary>
public sealed class AllowlistException : GitGuardException
{
    /// <summary>错误码：白名单 add 参数不合法（<c>10-异常处理设计.md</c> §4 登记值，CLI 映射退出码 20）。</summary>
    public const string ALLOWLIST_VALIDATION_FAILED = "ALLOWLIST_VALIDATION_FAILED";

    /// <summary>错误码：要删除的白名单条目不存在（<c>10-异常处理设计.md</c> §4 登记值，CLI 映射退出码 21）。</summary>
    public const string ALLOWLIST_NOT_FOUND = "ALLOWLIST_NOT_FOUND";

    /// <summary>初始化 <see cref="AllowlistException"/> 的实例。</summary>
    /// <param name="errorCode">错误码；取本类型登记的两个常量之一。</param>
    /// <param name="message">异常消息（面向用户的简述）。</param>
    public AllowlistException(string errorCode, string message)
        : base(message) => ErrorCode = errorCode;

    /// <summary>使用内部异常初始化实例（如指纹解析失败）。</summary>
    /// <param name="errorCode">错误码。</param>
    /// <param name="message">异常消息。</param>
    /// <param name="innerException">内部异常。</param>
    public AllowlistException(string errorCode, string message, Exception innerException)
        : base(message, innerException) => ErrorCode = errorCode;

    /// <summary>错误码（<c>10-异常处理设计.md</c> §4 登记值）。</summary>
    public string ErrorCode { get; }
}
