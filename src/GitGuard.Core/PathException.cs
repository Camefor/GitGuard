namespace GitGuard.Core;

/// <summary>
/// CLI 用户级 PATH 管理失败的统一异常类型（<c>10-异常处理设计.md</c> §4）。
/// <see cref="ErrorCode"/> 供 CLI 映射退出码：写入权限 → 3，条目冲突 → 60，不支持平台 / Shell → 61，超长 → 62。
/// 置于 Core 根命名空间，与 <see cref="HookException"/> / <see cref="GitException"/> 同置位理由：
/// CLI 需要 catch 本类型映射退出码，而 CLI 不得引用 Application / Infrastructure。
/// </summary>
public sealed class PathException : GitGuardException
{
    /// <summary>错误码：用户 PATH / 注册表 / 符号链接 / Shell profile 不可写（CLI 映射退出码 3）。</summary>
    public const string WRITE_PERMISSION_DENIED = "PATH_WRITE_PERMISSION_DENIED";

    /// <summary>错误码：目标路径已存在非 GitGuard 托管的同名文件 / 符号链接，需 <c>--force</c> 显式接管（CLI 映射退出码 60）。</summary>
    public const string ENTRY_CONFLICT = "PATH_ENTRY_CONFLICT";

    /// <summary>错误码：当前平台或 Shell 不支持自动 PATH 管理（CLI 映射退出码 61）。</summary>
    public const string UNSUPPORTED = "PATH_UNSUPPORTED";

    /// <summary>错误码：写入后的 PATH 超过平台长度上限（CLI 映射退出码 62）。</summary>
    public const string TOO_LONG = "PATH_TOO_LONG";

    /// <summary>初始化 <see cref="PathException"/> 的实例。</summary>
    /// <param name="errorCode">错误码；取本类型登记的四个常量之一。</param>
    /// <param name="message">异常消息（面向用户的简述）。</param>
    public PathException(string errorCode, string message)
        : base(message) => ErrorCode = errorCode;

    /// <summary>使用内部异常初始化实例（如注册表 / 文件系统 IO 失败）。</summary>
    /// <param name="errorCode">错误码。</param>
    /// <param name="message">异常消息。</param>
    /// <param name="innerException">内部异常。</param>
    public PathException(string errorCode, string message, Exception innerException)
        : base(message, innerException) => ErrorCode = errorCode;

    /// <summary>错误码（<c>10-异常处理设计.md</c> §4 登记值）。</summary>
    public string ErrorCode { get; }
}
