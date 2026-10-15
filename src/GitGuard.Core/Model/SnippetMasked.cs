using GitGuard.Core.Domain;

namespace GitGuard.Core.Model;

/// <summary>
/// 脱敏片段值对象：持久化与输出中唯一允许出现的敏感内容形态（DR-02 / DDD §4.2）。
/// 长度不超过 32 字符，且至少 50% 的字符为掩码 <c>*</c>，防止原文泄漏进审计与日志。
/// </summary>
public readonly record struct SnippetMasked
{
    /// <summary>脱敏片段的最大长度（DDD §4.2 / <c>findings.snippet_masked</c> 的 CHECK 约束）。</summary>
    public const int MAX_LENGTH = 32;

    private readonly string? _value;

    private SnippetMasked(string value) => _value = value;

    /// <summary>掩码文本；未初始化的 <c>default</c> 值为空字符串。</summary>
    public string Value => _value ?? string.Empty;

    /// <summary>
    /// 从掩码文本构造脱敏片段；由 <c>Masker</c>（GGD-0003）在裁剪原文后调用。
    /// </summary>
    /// <param name="value">掩码后的片段文本。</param>
    /// <returns>通过校验的脱敏片段。</returns>
    /// <exception cref="InvariantViolationException">为空、超过 32 字符或掩码占比不足 50% 时抛出。</exception>
    public static SnippetMasked Create(string value)
    {
        DomainGuard.Require(!string.IsNullOrEmpty(value), "DR-02", "SnippetMasked 不能为空");
        DomainGuard.Require(value.Length <= MAX_LENGTH, "DR-02", $"SnippetMasked 不能超过 {MAX_LENGTH} 字符（当前 {value.Length} 字符）");

        int maskedCount = value.Count(c => c == '*');
        DomainGuard.Require(maskedCount * 2 >= value.Length, "DR-02", "SnippetMasked 的掩码 '*' 必须覆盖至少 50% 的字符，疑似包含未脱敏原文");

        return new SnippetMasked(value);
    }

    /// <summary>返回掩码文本。</summary>
    /// <returns>脱敏后的片段。</returns>
    public override string ToString() => Value;
}
