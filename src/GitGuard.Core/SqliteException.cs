namespace GitGuard.Core;

/// <summary>
/// SQLite 审计存储操作失败的统一异常类型。
/// 依据 <c>docs/10-异常处理设计.md</c> §2：Infrastructure 层不得抛出纯粹的 <see cref="Exception"/>，
/// SQLite 访问失败一律以本类型向上传播；Application 层据此按「写失败不阻断扫描」降级并登记
/// <c>AuditWriteFailed</c> 事件（10-异常处理设计 §5、模块设计/Audit.md §4）。
/// </summary>
public sealed class SqliteException : GitGuardException
{
    /// <summary>错误码：审计存储失败 —— 打开 / 迁移 / 读写失败（<c>10-异常处理设计.md</c> §4 登记值，CLI 映射退出码 14）。</summary>
    public const string AUDIT_STORE_FAILED = "AUDIT_STORE_FAILED";

    /// <summary>初始化 <see cref="SqliteException"/> 的实例。</summary>
    public SqliteException()
        : base("SQLite 审计存储操作失败。")
    {
    }

    /// <summary>使用指定消息初始化实例。</summary>
    /// <param name="message">异常消息（面向用户的简述，不得包含被扫描内容）。</param>
    public SqliteException(string message)
        : base(message)
    {
    }

    /// <summary>使用指定消息与内部异常初始化实例。</summary>
    /// <param name="message">异常消息。</param>
    /// <param name="innerException">内部异常（如 <c>Microsoft.Data.Sqlite.SqliteException</c>）。</param>
    public SqliteException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
