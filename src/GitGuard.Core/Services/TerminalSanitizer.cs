using System.Text.RegularExpressions;

namespace GitGuard.Core.Services;

/// <summary>
/// 终端输出清洗器（09-日志设计 §9.2 第 2 步）：移除 ANSI 转义序列与 ASCII 控制字符
/// （0x00~0x1F、0x7F，仅保留 <c>\n</c> 与 <c>\t</c>），防止恶意路径或内容篡改终端显示
/// （08-权限设计「日志注入」）。使用源生成正则以保证 Native AOT 兼容（ADR-0002）。
/// </summary>
public static partial class TerminalSanitizer
{
    /// <summary>清洗文本，使其可安全写入终端。</summary>
    /// <param name="text">待清洗文本（日志消息、文件路径等）。</param>
    /// <returns>不含 ANSI 转义序列与控制字符的文本。</returns>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> 为 <see langword="null"/>。</exception>
    public static string Strip(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        string withoutAnsi = AnsiSequenceRegex().Replace(text, string.Empty);
        return ControlCharacterRegex().Replace(withoutAnsi, string.Empty);
    }

    /// <summary>匹配 ANSI CSI 转义序列（如 <c>ESC[31m</c> 颜色、<c>ESC[2J</c> 清屏）。</summary>
    [GeneratedRegex(@"\x1B\[[0-?]*[ -/]*[@-~]", RegexOptions.CultureInvariant)]
    private static partial Regex AnsiSequenceRegex();

    /// <summary>匹配 ASCII 控制字符（0x00~0x1F、0x7F），保留 <c>\t</c>（0x09）与 <c>\n</c>（0x0A）。</summary>
    [GeneratedRegex(@"[\x00-\x08\x0B-\x1F\x7F]", RegexOptions.CultureInvariant)]
    private static partial Regex ControlCharacterRegex();
}
