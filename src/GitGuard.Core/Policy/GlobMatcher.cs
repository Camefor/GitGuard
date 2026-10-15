namespace GitGuard.Core.Policy;

/// <summary>
/// glob 路径匹配器，供白名单 <c>PathPattern</c>（后续规则 <c>FilePathGlobs</c> 与 exclude 列表共用语义）：
/// <list type="bullet">
///   <item><description><c>*</c>：匹配路径段内任意字符（可为空），不跨越 <c>/</c>。</description></item>
///   <item><description><c>?</c>：匹配路径段内单个字符，不匹配 <c>/</c>。</description></item>
///   <item><description><c>**</c>（独立段）：匹配零个或多个完整路径段，如 <c>**/test/**</c>。</description></item>
///   <item><description>其余字符按字面量精确匹配。</description></item>
/// </list>
/// 分隔符统一为 <c>/</c>；为兼容 Windows 书写习惯，模式与路径中的 <c>\</c> 也按分隔符归一。
/// 匹配大小写敏感（Ordinal），保证三平台行为一致。
/// </summary>
public static class GlobMatcher
{
    /// <summary>判断仓库相对路径是否匹配 glob 模式。</summary>
    /// <param name="pattern">glob 模式。</param>
    /// <param name="path">待匹配的路径（仓库相对路径，允许 <c>/</c> 或 <c>\</c> 分隔）。</param>
    /// <returns>匹配返回 <see langword="true"/>。</returns>
    /// <exception cref="ArgumentException"><paramref name="pattern"/> 为 <see langword="null"/>、空或仅空白。</exception>
    /// <exception cref="ArgumentNullException"><paramref name="path"/> 为 <see langword="null"/>。</exception>
    public static bool IsMatch(string pattern, string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pattern);
        ArgumentNullException.ThrowIfNull(path);

        if (path.Length == 0)
        {
            return false;
        }

        string[] patternSegments = SplitSegments(pattern);
        string[] pathSegments = SplitSegments(path);

        // 空模式段（如 "///"）等价于空模式，视为无匹配条件，直接拒绝
        if (patternSegments.Length == 0)
        {
            return false;
        }

        return MatchSegments(patternSegments, 0, pathSegments, 0);
    }

    private static string[] SplitSegments(string value) =>
        value.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);

    private static bool MatchSegments(string[] pattern, int patternIndex, string[] path, int pathIndex)
    {
        int patternCount = pattern.Length;
        int pathCount = path.Length;

        while (patternIndex < patternCount)
        {
            if (pattern[patternIndex] == "**")
            {
                patternIndex++;

                // 末尾的 "**" 吞掉剩余全部路径段（含零段）
                if (patternIndex == patternCount)
                {
                    return true;
                }

                for (int skip = pathIndex; skip <= pathCount; skip++)
                {
                    if (MatchSegments(pattern, patternIndex, path, skip))
                    {
                        return true;
                    }
                }

                return false;
            }

            if (pathIndex >= pathCount || !MatchSegment(pattern[patternIndex], path[pathIndex]))
            {
                return false;
            }

            patternIndex++;
            pathIndex++;
        }

        return pathIndex == pathCount;
    }

    private static bool MatchSegment(string pattern, string text)
    {
        int patternIndex = 0;
        int textIndex = 0;
        int starIndex = -1;
        int starTextIndex = 0;

        while (textIndex < text.Length)
        {
            if (patternIndex < pattern.Length
                && (pattern[patternIndex] == '?' || pattern[patternIndex] == text[textIndex]))
            {
                patternIndex++;
                textIndex++;
            }
            else if (patternIndex < pattern.Length && pattern[patternIndex] == '*')
            {
                starIndex = patternIndex++;
                starTextIndex = textIndex;
            }
            else if (starIndex >= 0)
            {
                patternIndex = starIndex + 1;
                textIndex = ++starTextIndex;
            }
            else
            {
                return false;
            }
        }

        while (patternIndex < pattern.Length && pattern[patternIndex] == '*')
        {
            patternIndex++;
        }

        return patternIndex == pattern.Length;
    }
}
