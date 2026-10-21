namespace GitGuard.Core;

/// <summary>
/// 审计导出失败的统一异常类型（<c>10-异常处理设计.md</c> §4）：输出路径不可写、IO 失败等场景以本类型向上传播，
/// CLI 映射退出码 31。导出内容始终保持脱敏，异常消息不得包含被扫描原文。
/// </summary>
public sealed class AuditExportException : GitGuardException
{
    /// <summary>错误码：审计导出 IO 失败（<c>10-异常处理设计.md</c> §4 登记值，CLI 映射退出码 31）。</summary>
    public const string AUDIT_EXPORT_FAILED = "AUDIT_EXPORT_FAILED";

    /// <summary>初始化 <see cref="AuditExportException"/> 的实例。</summary>
    public AuditExportException()
        : base("审计导出失败。") => ErrorCode = AUDIT_EXPORT_FAILED;

    /// <summary>初始化 <see cref="AuditExportException"/> 的实例。</summary>
    /// <param name="message">异常消息（面向用户的简述，不得包含被扫描内容）。</param>
    public AuditExportException(string message)
        : base(message) => ErrorCode = AUDIT_EXPORT_FAILED;

    /// <summary>使用内部异常初始化实例。</summary>
    /// <param name="message">异常消息（不得包含被扫描内容）。</param>
    /// <param name="innerException">内部异常（如 <see cref="IOException"/>）。</param>
    public AuditExportException(string message, Exception innerException)
        : base(message, innerException) => ErrorCode = AUDIT_EXPORT_FAILED;

    /// <summary>错误码（<c>10-异常处理设计.md</c> §4 登记值）。</summary>
    public string ErrorCode { get; }
}
