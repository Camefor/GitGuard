using GitGuard.Core.Services;

namespace GitGuard.Cli;

/// <summary>
/// 用户可见文本的最后一道脱敏（<c>docs/09-日志设计.md</c> §9 / <c>10-异常处理设计</c> §7）：
/// 去除 ANSI 转义与控制字符（<see cref="SanitizingLogger.SanitizeMessage"/>），
/// 并把用户主目录前缀折叠为 <c>~</c>，避免错误消息暴露路径中的用户名。
/// </summary>
internal static class MessageSanitizer
{
    /// <summary>清洗待输出到终端 / 日志 / JSON 的文本。</summary>
    /// <param name="text">原始文本；<see langword="null"/> 时返回空串。</param>
    /// <returns>可安全输出的文本。</returns>
    internal static string Sanitize(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        string cleaned = SanitizingLogger.SanitizeMessage(text);
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (home.Length > 0 && cleaned.Contains(home, StringComparison.OrdinalIgnoreCase))
        {
            cleaned = cleaned.Replace(home, "~", StringComparison.OrdinalIgnoreCase);
        }

        return cleaned;
    }
}
