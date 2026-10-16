namespace GitGuard.Application.Scanning;

/// <summary>
/// 文本行枚举（差异文本与 pre-push stdin 共用）：按 LF 切分，CRLF 行终止的尾部 CR 归一剥离。
/// 剥离的原因：CRLF 工作区文件的差异内容行会带裸 CR，若进入检测文本会破坏 <c>$</c> 锚定正则
/// （如手机号 / 身份证行尾）并使掩码片段携带控制字符。
/// </summary>
internal static class LineReader
{
    /// <summary>枚举文本行；文本以换行符结尾时最后不产生空行。</summary>
    /// <param name="text">待枚举文本。</param>
    /// <returns>行序列（不含行终止符）。</returns>
    internal static IEnumerable<string> Enumerate(string text)
    {
        int lineStart = 0;
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] != '\n')
            {
                continue;
            }

            int lineEnd = i > lineStart && text[i - 1] == '\r' ? i - 1 : i;
            yield return text[lineStart..lineEnd];
            lineStart = i + 1;
        }

        if (lineStart < text.Length)
        {
            yield return text[lineStart..];
        }
    }
}
