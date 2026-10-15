namespace GitGuard.Core;

/// <summary>
/// GitGuard 全部自定义异常的基类。
/// 依据 <c>docs/10-异常处理设计.md</c> §2：各层（Detection / Infrastructure / Application）的自定义异常均派生自本类型。
/// </summary>
public abstract class GitGuardException : Exception
{
    /// <summary>初始化 <see cref="GitGuardException"/> 的实例。</summary>
    protected GitGuardException()
    {
    }

    /// <summary>使用指定消息初始化实例。</summary>
    /// <param name="message">异常消息。</param>
    protected GitGuardException(string message)
        : base(message)
    {
    }

    /// <summary>使用指定消息与内部异常初始化实例。</summary>
    /// <param name="message">异常消息。</param>
    /// <param name="innerException">内部异常。</param>
    protected GitGuardException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
