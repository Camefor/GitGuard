namespace GitGuard.Core.Services;

/// <summary>
/// 日志脱敏流水线（09-日志设计 §9.2 / DR-02）：敏感内容进入终端、日志文件与 SQLite 审计前
/// 必须经本类处理 —— 敏感字段掩码（<see cref="Masker"/>）、日志文本去控制字符（<see cref="TerminalSanitizer"/>）、
/// 仓库路径匿名化（<see cref="PathAnonymizer"/>）。
/// 本类为纯函数组件；Infrastructure 的脱敏日志 Sink（SAD §5.1）在写入前调用本类。
/// </summary>
public static class SanitizingLogger
{
    /// <summary>清洗日志消息文本（流水线第 2 步）：去除 ANSI 转义序列与控制字符。</summary>
    /// <param name="message">待写入日志的文本。</param>
    /// <returns>可安全写入终端的文本。</returns>
    /// <exception cref="ArgumentNullException"><paramref name="message"/> 为 <see langword="null"/>。</exception>
    public static string SanitizeMessage(string message) => TerminalSanitizer.Strip(message);

    /// <summary>把已识别为敏感的原文转为可入日志的掩码文本（流水线第 1 步）。</summary>
    /// <param name="rawSensitive">检测器命中的敏感原文。</param>
    /// <returns>满足 DR-02 的掩码文本（保留前后 2 字符，中间 <c>*</c>，≤ 32 字符）。</returns>
    /// <exception cref="ArgumentNullException"><paramref name="rawSensitive"/> 为 <see langword="null"/>。</exception>
    /// <exception cref="GitGuard.Core.Domain.InvariantViolationException">原文为空时抛出（DR-02）。</exception>
    public static string MaskField(string rawSensitive) => Masker.Mask(rawSensitive).Value;

    /// <summary>把 <c>repo_path</c> 转为可入日志的文本（流水线第 3 步）。</summary>
    /// <param name="repoPath">仓库绝对路径。</param>
    /// <param name="isDebugLevel">是否为 Debug/Trace 级日志；仅此时允许出现原始路径（仍需终端清洗）。</param>
    /// <returns>非 Debug 级返回 16 字符的 <c>repo_path_hash</c>；Debug 级返回清洗后的原始路径。</returns>
    /// <exception cref="ArgumentNullException"><paramref name="repoPath"/> 为 <see langword="null"/>。</exception>
    /// <exception cref="GitGuard.Core.Domain.InvariantViolationException">路径为空或全为空白时抛出（DR-02）。</exception>
    public static string RepoPathForLog(string repoPath, bool isDebugLevel) =>
        isDebugLevel ? TerminalSanitizer.Strip(repoPath) : PathAnonymizer.Anonymize(repoPath);
}
