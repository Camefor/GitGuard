using GitGuard.Core.Model;

namespace GitGuard.Core.Policy;

/// <summary>
/// 白名单匹配器（FR-040 / DDD §8）：对单条 Finding 按「规则 ID + 路径 glob + 指纹」三条件取交集匹配，
/// 条目中缺省的条件视为通配；已过期条目不参与匹配。纯函数实现，无 IO（SAD §3）。
/// </summary>
public static class AllowlistMatcher
{
    /// <summary>判断 Finding 是否命中白名单条目。</summary>
    /// <param name="entry">白名单条目。</param>
    /// <param name="finding">待判定的发现项。</param>
    /// <param name="now">当前时间（UTC），用于过期判定。</param>
    /// <returns>条目有效且全部已声明的条件均匹配时返回 <see langword="true"/>。</returns>
    /// <exception cref="ArgumentNullException"><paramref name="entry"/> 或 <paramref name="finding"/> 为 <see langword="null"/>。</exception>
    public static bool Matches(AllowlistEntry entry, Finding finding, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(finding);

        if (entry.ExpiresAt is { } expiresAt && now >= expiresAt)
        {
            return false;
        }

        if (entry.RuleId is { } ruleId && !string.Equals(ruleId, finding.RuleId, StringComparison.Ordinal))
        {
            return false;
        }

        if (entry.PathPattern is { } pathPattern && !GlobMatcher.IsMatch(pathPattern, finding.FilePath))
        {
            return false;
        }

        if (entry.Fingerprint is { } fingerprint && !fingerprint.Equals(finding.Fingerprint))
        {
            return false;
        }

        return true;
    }
}
