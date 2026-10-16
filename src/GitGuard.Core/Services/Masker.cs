using GitGuard.Core.Domain;
using GitGuard.Core.Model;

namespace GitGuard.Core.Services;

/// <summary>
/// 脱敏器（DDD §6 / DR-02）：把敏感原文裁剪为 <see cref="SnippetMasked"/> ——
/// 保留前后各 2 字符、中间替换为 <c>*</c>，总长不超过 32 字符。
/// 对短串按 L/4 收缩保留位数，以满足 <see cref="SnippetMasked"/>「掩码占比 ≥ 50%」不变式。
/// </summary>
public static class Masker
{
    /// <summary>单侧默认保留的原文可见字符数（DDD §6：保留前后 2 字符）。</summary>
    private const int KEEP_CHARS = 2;

    /// <summary>
    /// 把敏感原文裁剪为脱敏片段：长度 ≤ 32 时输出等长，更长时只保留前后各 2 字符、中间补齐至 32 字符。
    /// </summary>
    /// <param name="rawSecret">检测器命中的敏感原文；本方法调用后不得在别处保留。</param>
    /// <returns>满足 DR-02 的脱敏片段。</returns>
    /// <exception cref="ArgumentNullException"><paramref name="rawSecret"/> 为 <see langword="null"/>。</exception>
    /// <exception cref="InvariantViolationException">原文为空时抛出（DR-02）。</exception>
    public static SnippetMasked Mask(string rawSecret)
    {
        ArgumentNullException.ThrowIfNull(rawSecret);
        DomainGuard.Require(rawSecret.Length > 0, "DR-02", "待脱敏原文不能为空");

        // 保留位数取 min(2, L/4)：L ≥ 8 保留前后各 2 字符；L < 8 收缩为 1 或 0 个，保证掩码占比始终 ≥ 50%
        int keep = Math.Min(KEEP_CHARS, rawSecret.Length / 4);
        int visibleLength = Math.Min(rawSecret.Length, SnippetMasked.MAX_LENGTH);
        int maskedCount = visibleLength - (keep * 2);

        string masked = string.Concat(
            rawSecret.AsSpan(0, keep),
            new string('*', maskedCount),
            rawSecret.AsSpan(rawSecret.Length - keep));

        return SnippetMasked.Create(masked);
    }
}
