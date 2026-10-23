namespace GitGuard.Core;

/// <summary>
/// 仓库登记失败的统一异常类型（<c>10-异常处理设计.md</c> §4 / GGD-0042）：路径校验、Git 工作区校验、
/// 重复登记与条目不存在均以本类型向桌面端传播；桌面端按 <see cref="ErrorCode"/> 映射双语提示。
/// 本异常不参与 CLI 退出码契约（桌面端专用能力，无对应 CLI 命令）。
/// </summary>
public sealed class RepositoryException : GitGuardException
{
    /// <summary>错误码：路径为空、不存在或不是目录（参数校验失败）。</summary>
    public const string REPOSITORY_VALIDATION_FAILED = "REPOSITORY_VALIDATION_FAILED";

    /// <summary>错误码：路径不构成 Git 工作区（<c>git rev-parse --show-toplevel</c> 失败）。</summary>
    public const string REPOSITORY_NOT_A_REPOSITORY = "REPOSITORY_NOT_A_REPOSITORY";

    /// <summary>错误码：同一仓库路径已登记。</summary>
    public const string REPOSITORY_ALREADY_REGISTERED = "REPOSITORY_ALREADY_REGISTERED";

    /// <summary>错误码：待移除的仓库登记不存在。</summary>
    public const string REPOSITORY_NOT_FOUND = "REPOSITORY_NOT_FOUND";

    /// <summary>初始化 <see cref="RepositoryException"/> 的实例。</summary>
    /// <param name="errorCode">错误码；取本类型登记的常量之一。</param>
    /// <param name="message">异常消息（面向用户的简述）。</param>
    public RepositoryException(string errorCode, string message)
        : base(message) => ErrorCode = errorCode;

    /// <summary>使用内部异常初始化实例（如领域不变式或 Git 校验失败）。</summary>
    /// <param name="errorCode">错误码。</param>
    /// <param name="message">异常消息。</param>
    /// <param name="innerException">内部异常。</param>
    public RepositoryException(string errorCode, string message, Exception innerException)
        : base(message, innerException) => ErrorCode = errorCode;

    /// <summary>错误码（<c>10-异常处理设计.md</c> §4 登记值）。</summary>
    public string ErrorCode { get; }
}
