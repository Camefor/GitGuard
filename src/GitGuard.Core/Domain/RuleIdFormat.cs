using System.Text.RegularExpressions;

namespace GitGuard.Core.Domain;

/// <summary>
/// 规则 ID 命名契约（<c>13-开发规范.md</c> §4.1 / <c>DDD §3.2</c>）：<c>^(GGD|USR|GLK)/[A-Z]+/[A-Z0-9_-]+$</c>。
/// 使用源生成正则以保证 Native AOT 兼容（ADR-0002）。
/// </summary>
public static partial class RuleIdFormat
{
    /// <summary>判断规则 ID 是否符合命名契约。</summary>
    /// <param name="ruleId">待校验的规则 ID；<see langword="null"/> 视为非法。</param>
    /// <returns>符合格式返回 <see langword="true"/>。</returns>
    public static bool IsValid(string? ruleId) =>
        ruleId is not null && RuleIdRegex().IsMatch(ruleId);

    [GeneratedRegex("^(GGD|USR|GLK)/[A-Z]+/[A-Z0-9_-]+$", RegexOptions.CultureInvariant)]
    private static partial Regex RuleIdRegex();
}
