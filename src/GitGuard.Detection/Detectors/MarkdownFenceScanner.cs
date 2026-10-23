using GitGuard.Core.Model;

namespace GitGuard.Detection.Detectors;

/// <summary>
/// Markdown 围栏代码块扫描器（模块设计 LocalPathUrl §3.1）：从差异新增行中识别围栏边界，
/// 返回位于围栏代码块内的行号集合，供 LocalPath 检测器抑制示例路径。
/// 差异为 unified-0 且只含变更行，围栏开标记与目标行之间出现行号缺口时无法确认块状态，
/// 按保守策略重置为围栏外——宁可多报，不可漏报。
/// </summary>
internal static class MarkdownFenceScanner
{
    private const int MIN_FENCE_LENGTH = 3;
    private const int MAX_FENCE_INDENT = 3;

    /// <summary>返回围栏代码块内的变更行号集合。</summary>
    /// <param name="change">文件变更（保持差异行顺序与行号）。</param>
    /// <returns>围栏内的行号集合；无围栏或无内容行时为 <see langword="null"/>。</returns>
    public static IReadOnlySet<int>? FindFencedLineNumbers(FileChange change)
    {
        HashSet<int>? fenced = null;
        char fenceChar = '\0';
        int fenceLength = 0;
        int previousNumber = -1;

        foreach (ChangedLine line in change.Lines)
        {
            if (previousNumber >= 0 && line.Number != previousNumber + 1)
            {
                fenceChar = '\0';
                fenceLength = 0;
            }

            previousNumber = line.Number;

            if (fenceChar == '\0')
            {
                if (TryReadOpeningFence(line.Text, out char openingChar, out int openingLength))
                {
                    fenceChar = openingChar;
                    fenceLength = openingLength;
                }

                continue;
            }

            if (IsClosingFence(line.Text, fenceChar, fenceLength))
            {
                fenceChar = '\0';
                fenceLength = 0;
                continue;
            }

            (fenced ??= new HashSet<int>()).Add(line.Number);
        }

        return fenced;
    }

    private static bool TryReadOpeningFence(string text, out char fenceChar, out int fenceLength)
    {
        fenceChar = '\0';
        fenceLength = 0;
        int start = IndexAfterIndent(text);
        if (start < 0 || start >= text.Length || (text[start] != '`' && text[start] != '~'))
        {
            return false;
        }

        char candidate = text[start];
        int end = start;
        while (end < text.Length && text[end] == candidate)
        {
            end++;
        }

        if (end - start < MIN_FENCE_LENGTH)
        {
            return false;
        }

        // CommonMark：反引号围栏的信息串不得包含反引号，含反引号的行不是围栏开标记
        if (candidate == '`' && text.IndexOf('`', end) >= 0)
        {
            return false;
        }

        fenceChar = candidate;
        fenceLength = end - start;
        return true;
    }

    private static bool IsClosingFence(string text, char fenceChar, int openingLength)
    {
        int start = IndexAfterIndent(text);
        if (start < 0 || start >= text.Length || text[start] != fenceChar)
        {
            return false;
        }

        int end = start;
        while (end < text.Length && text[end] == fenceChar)
        {
            end++;
        }

        if (end - start < openingLength)
        {
            return false;
        }

        // 收尾围栏只允许尾随空白；其他字符说明这是围栏内容而非边界
        for (int i = end; i < text.Length; i++)
        {
            if (text[i] != ' ' && text[i] != '\t')
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>跳过最多 3 个前导空格；缩进超过 3 个空格的行不构成围栏边界。</summary>
    /// <param name="text">行文本。</param>
    /// <returns>围栏字符起始下标；缩进超限时返回 -1。</returns>
    private static int IndexAfterIndent(string text)
    {
        int spaces = 0;
        while (spaces < text.Length && text[spaces] == ' ')
        {
            spaces++;
            if (spaces > MAX_FENCE_INDENT)
            {
                return -1;
            }
        }

        return spaces;
    }
}
