namespace GitGuard.Core.Domain;

/// <summary>
/// 领域不变式（DR-NN / <c>DDD §3-4</c>）被违反时抛出。
/// 依据 <c>docs/10-异常处理设计.md</c> §2：仅允许从 Core 领域层抛出，上层不得把底层异常包装成本类型。
/// </summary>
public sealed class InvariantViolationException : GitGuardException
{
    /// <summary>初始化 <see cref="InvariantViolationException"/> 的实例。</summary>
    public InvariantViolationException()
    {
    }

    /// <summary>使用指定消息初始化实例。</summary>
    /// <param name="message">异常消息，按约定以 <c>[规则编号]</c> 前缀标注被违反的领域规则。</param>
    public InvariantViolationException(string message)
        : base(message)
    {
    }

    /// <summary>使用指定消息与内部异常初始化实例。</summary>
    /// <param name="message">异常消息。</param>
    /// <param name="innerException">内部异常。</param>
    public InvariantViolationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
