namespace GitGuard.Core;

/// <summary>
/// Hook 安装 / 卸载失败的统一异常类型（<c>10-异常处理设计.md</c> §2）。
/// <see cref="ErrorCode"/> 取自该文档 §4 的错误码登记表，供 CLI 映射退出码
/// （<c>WRITE_PERMISSION_DENIED</c> → 3，<c>UNKNOWN_HOOK_PRESENT</c> → 4，<c>INSTALL_IN_PROGRESS</c> → 50）。
/// 置于 Core 根命名空间，与 <see cref="GitException"/> / <see cref="SqliteException"/> / <see cref="DetectionException"/> 同置位理由：
/// Cli 层需要 catch 本类型映射退出码，而 Cli 不得引用 Application / Infrastructure。
/// </summary>
public sealed class HookException : GitGuardException
{
    /// <summary>错误码：hooks 目录 / Hook 文件不可写（<c>10-异常处理设计.md</c> §4 登记值，CLI 映射退出码 3）。</summary>
    public const string WRITE_PERMISSION_DENIED = "WRITE_PERMISSION_DENIED";

    /// <summary>
    /// 错误码：目标 Hook 存在但无法安全包裹 —— 二进制文件、符号链接、或备份文件已存在而目标未托管；
    /// 需 <c>--force</c> 显式接管（<c>10-异常处理设计.md</c> §4 登记值，CLI 映射退出码 4）。
    /// </summary>
    public const string UNKNOWN_HOOK_PRESENT = "UNKNOWN_HOOK_PRESENT";

    /// <summary>错误码：已有并发的 install / uninstall 持锁（<c>10-异常处理设计.md</c> §4 登记值，CLI 映射退出码 50）。</summary>
    public const string INSTALL_IN_PROGRESS = "INSTALL_IN_PROGRESS";

    /// <summary>初始化 <see cref="HookException"/> 的实例。</summary>
    /// <param name="errorCode">错误码；取本类型登记的三个常量之一。</param>
    /// <param name="message">异常消息（面向用户的简述，不得包含文件内容原文）。</param>
    public HookException(string errorCode, string message)
        : base(message) => ErrorCode = errorCode;

    /// <summary>使用内部异常初始化实例（如目标路径的 IO 失败）。</summary>
    /// <param name="errorCode">错误码。</param>
    /// <param name="message">异常消息。</param>
    /// <param name="innerException">内部异常。</param>
    public HookException(string errorCode, string message, Exception innerException)
        : base(message, innerException) => ErrorCode = errorCode;

    /// <summary>错误码（<c>10-异常处理设计.md</c> §4 登记值）。</summary>
    public string ErrorCode { get; }
}
