using GitGuard.Core.Policy;

namespace GitGuard.Core.Model;

/// <summary>
/// 策略覆盖事件（08-权限设计 §7）：记录用户把默认策略动作降级的行为，供 <c>PolicyOverride</c> 审计事件使用。
/// <para>
/// <see cref="RuleId"/> 通常为被禁用的内置规则 ID；严重度级覆盖使用合成标识
/// <c>SEVERITY/&lt;Severity&gt;</c>（如 <c>SEVERITY/High</c>），因为策略配置按严重度而非单条规则生效。
/// </para>
/// </summary>
public sealed record PolicyOverride
{
    /// <summary>创建策略覆盖记录；动作必须由严格变宽松（<see cref="From"/> 的数值大于 <see cref="To"/>）。</summary>
    /// <param name="ruleId">规则 ID，或严重度级合成标识 <c>SEVERITY/&lt;Severity&gt;</c>。</param>
    /// <param name="from">原动作（默认或覆盖前的有效动作）。</param>
    /// <param name="to">覆盖后的动作；必须比 <paramref name="from"/> 更宽松。</param>
    /// <exception cref="ArgumentException"><paramref name="ruleId"/> 为空或全空白。</exception>
    /// <exception cref="ArgumentOutOfRangeException">动作取值未定义。</exception>
    /// <exception cref="ArgumentException"><paramref name="to"/> 未比 <paramref name="from"/> 更宽松。</exception>
    public PolicyOverride(string ruleId, PolicyAction from, PolicyAction to)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ruleId);
        if (!Enum.IsDefined(from))
        {
            throw new ArgumentOutOfRangeException(nameof(from), from, "未知的 PolicyAction 取值");
        }

        if (!Enum.IsDefined(to))
        {
            throw new ArgumentOutOfRangeException(nameof(to), to, "未知的 PolicyAction 取值");
        }

        if (to >= from)
        {
            throw new ArgumentException("策略覆盖必须把动作降级为更宽松的取值。", nameof(to));
        }

        RuleId = ruleId;
        From = from;
        To = to;
    }

    /// <summary>规则 ID，或严重度级合成标识 <c>SEVERITY/&lt;Severity&gt;</c>。</summary>
    public string RuleId { get; }

    /// <summary>原动作（默认或覆盖前的有效动作）。</summary>
    public PolicyAction From { get; }

    /// <summary>覆盖后的动作（必然比 <see cref="From"/> 更宽松）。</summary>
    public PolicyAction To { get; }
}
