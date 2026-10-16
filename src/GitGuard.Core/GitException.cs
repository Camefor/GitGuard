namespace GitGuard.Core;

/// <summary>
/// Git 访问（<c>IGitAccess</c> 实现）失败的统一异常类型。
/// 依据 <c>docs/10-异常处理设计.md</c> §2：Infrastructure 层不得抛出纯粹的 <see cref="Exception"/>，
/// git 子进程失败一律以本类型向上传播；<see cref="ErrorCode"/> 取自该文档 §4 的错误码登记表，
/// 供 CLI 映射退出码（<c>NOT_A_GIT_REPOSITORY</c> → 2，<c>GIT_COMMAND_FAILED</c> → 10）。
/// </summary>
public sealed class GitException : GitGuardException
{
    /// <summary>错误码：当前路径不是 Git 仓库（CLI 映射退出码 2）。</summary>
    public const string NOT_A_GIT_REPOSITORY = "NOT_A_GIT_REPOSITORY";

    /// <summary>错误码：Git 子进程非零退出、超时或无法启动（CLI 映射退出码 10）。</summary>
    public const string GIT_COMMAND_FAILED = "GIT_COMMAND_FAILED";

    /// <summary>初始化 <see cref="GitException"/> 的实例。</summary>
    /// <param name="errorCode">错误码；取 <see cref="NOT_A_GIT_REPOSITORY"/> 或 <see cref="GIT_COMMAND_FAILED"/>。</param>
    /// <param name="message">异常消息（面向用户的简述，不得包含被扫描内容）。</param>
    public GitException(string errorCode, string message)
        : base(message) => ErrorCode = errorCode;

    /// <summary>使用 git 子进程的退出码与 stderr 初始化实例。</summary>
    /// <param name="errorCode">错误码。</param>
    /// <param name="message">异常消息。</param>
    /// <param name="exitCode">git 子进程退出码；超时为 <see langword="null"/>。</param>
    /// <param name="standardError">git 子进程 stderr，供诊断排错；无输出时为 <see langword="null"/>。</param>
    public GitException(string errorCode, string message, int? exitCode, string? standardError)
        : base(message)
    {
        ErrorCode = errorCode;
        ExitCode = exitCode;
        StandardError = standardError;
    }

    /// <summary>使用内部异常初始化实例（如 git 进程无法启动）。</summary>
    /// <param name="errorCode">错误码。</param>
    /// <param name="message">异常消息。</param>
    /// <param name="innerException">内部异常。</param>
    public GitException(string errorCode, string message, Exception innerException)
        : base(message, innerException) => ErrorCode = errorCode;

    /// <summary>错误码（<c>10-异常处理设计.md</c> §4 登记值）。</summary>
    public string ErrorCode { get; }

    /// <summary>git 子进程退出码；进程未能启动或超时为 <see langword="null"/>。</summary>
    public int? ExitCode { get; }

    /// <summary>git 子进程 stderr（仅供诊断；写入日志前必须经脱敏流水线）。</summary>
    public string? StandardError { get; }
}
