namespace GitGuard.Core.Domain;

/// <summary>
/// 领域不变式守卫。
/// 依据 <c>docs/04-领域模型设计(DDD).md</c> §1：DR-NN 的校验必须集中在 Core 领域层，禁止散落到上层实现。
/// </summary>
public static class DomainGuard
{
    /// <summary>
    /// 断言条件成立；不成立时抛出携带领域规则编号的 <see cref="InvariantViolationException"/>。
    /// </summary>
    /// <param name="condition">必须成立的条件。</param>
    /// <param name="ruleId">领域规则或条款编号（如 <c>DR-03</c>、<c>DDD §3.1</c>）。</param>
    /// <param name="message">违反说明；由本方法拼接 <c>[编号]</c> 前缀。</param>
    /// <exception cref="InvariantViolationException">条件不成立时抛出。</exception>
    public static void Require(bool condition, string ruleId, string message)
    {
        if (!condition)
        {
            throw new InvariantViolationException($"[{ruleId}] {message}");
        }
    }
}
