namespace GitGuard.Infrastructure.FileSystem;

/// <summary>
/// PATH 环境变量字符串的纯函数操作（GGD-0061）：拆分 / 去重 / 追加 / 移除。
/// 保留原始条目的书写形式（例如 Windows 的 <c>%USERPROFILE%</c> 变量引用），仅在比较时展开，
/// 避免重写用户 PATH 时把变量引用固化为绝对路径。
/// </summary>
internal static class PathEnvironmentVariable
{
    /// <summary>Windows 用户 PATH 的建议安全长度上限（环境变量硬上限 32767，预留余量）。</summary>
    internal const int WINDOWS_MAX_LENGTH = 30000;

    /// <summary>拆分 PATH 值：按分隔符切分、去除首尾空白并丢弃空条目。</summary>
    /// <param name="value">PATH 原始值；<see langword="null"/> / 空串返回空列表。</param>
    /// <param name="separator">平台分隔符（Windows <c>;</c>，Unix <c>:</c>）。</param>
    /// <returns>按原顺序排列的非空条目。</returns>
    internal static IReadOnlyList<string> Split(string? value, char separator)
    {
        if (string.IsNullOrEmpty(value))
        {
            return Array.Empty<string>();
        }

        var entries = new List<string>();
        foreach (string part in value.Split(separator))
        {
            string trimmed = part.Trim();
            if (trimmed.Length > 0)
            {
                entries.Add(trimmed);
            }
        }

        return entries;
    }

    /// <summary>判断 PATH 条目列表中是否已包含指定目录（展开环境变量并忽略末尾分隔符后比较）。</summary>
    /// <param name="entries">PATH 条目列表。</param>
    /// <param name="directory">目标目录。</param>
    /// <param name="comparison">路径比较规则（Windows 不区分大小写）。</param>
    /// <returns>包含返回 <see langword="true"/>。</returns>
    internal static bool Contains(IReadOnlyList<string> entries, string directory, StringComparison comparison)
    {
        foreach (string entry in entries)
        {
            if (EntryMatches(entry, directory, comparison))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>追加目录并返回新的 PATH 值；已存在时原样返回。</summary>
    /// <param name="value">PATH 原始值。</param>
    /// <param name="separator">平台分隔符。</param>
    /// <param name="directory">目标目录。</param>
    /// <param name="comparison">路径比较规则。</param>
    /// <returns>新的 PATH 值（不含空条目）。</returns>
    internal static string Append(string? value, char separator, string directory, StringComparison comparison)
    {
        IReadOnlyList<string> entries = Split(value, separator);
        if (Contains(entries, directory, comparison))
        {
            return value ?? string.Empty;
        }

        return entries.Count == 0
            ? directory
            : string.Join(separator, entries) + separator + directory;
    }

    /// <summary>移除与目录匹配的条目并返回新的 PATH 值；保留其他条目的原始书写形式。</summary>
    /// <param name="value">PATH 原始值。</param>
    /// <param name="separator">平台分隔符。</param>
    /// <param name="directory">目标目录。</param>
    /// <param name="comparison">路径比较规则。</param>
    /// <param name="removed">是否至少移除了一条。</param>
    /// <returns>新的 PATH 值（不含空条目）。</returns>
    internal static string Remove(
        string? value,
        char separator,
        string directory,
        StringComparison comparison,
        out bool removed)
    {
        IReadOnlyList<string> entries = Split(value, separator);
        var remaining = new List<string>(entries.Count);
        removed = false;
        foreach (string entry in entries)
        {
            if (EntryMatches(entry, directory, comparison))
            {
                removed = true;
                continue;
            }

            remaining.Add(entry);
        }

        return remaining.Count == 0 ? string.Empty : string.Join(separator, remaining);
    }

    /// <summary>判断原始 PATH 条目是否指向指定目录。</summary>
    /// <param name="entry">原始条目（可能包含环境变量引用）。</param>
    /// <param name="directory">目标目录。</param>
    /// <param name="comparison">路径比较规则。</param>
    /// <returns>匹配返回 <see langword="true"/>。</returns>
    private static bool EntryMatches(string entry, string directory, StringComparison comparison)
    {
        string expanded = Normalize(Environment.ExpandEnvironmentVariables(entry));
        string expected = Normalize(directory);
        return string.Equals(expanded, expected, comparison);
    }

    /// <summary>规范化路径用于比较：展开 <c>~</c> 之外的变量引用已在上游完成；去掉末尾分隔符并尽力转为绝对路径。</summary>
    /// <param name="path">原始路径。</param>
    /// <returns>规范化后的路径。</returns>
    private static string Normalize(string path)
    {
        string result = path.Trim();
        try
        {
            result = Path.GetFullPath(result);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            // 非法路径按原样比较：宁可重复一条无害条目，也不抛异常打断安装
        }

        return Path.TrimEndingDirectorySeparator(result);
    }
}
