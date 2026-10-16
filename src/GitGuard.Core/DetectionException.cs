namespace GitGuard.Core;

/// <summary>
/// 检测层失败的统一异常类型（<c>10-异常处理设计.md</c> §2）：检测器（<c>IDetector</c> 实现）在规则匹配失败、
/// 正则超时等场景必须以本类型向上传播，由 Application 封装为 <c>ScanResult(Failed)</c>、CLI 映射退出码 11。
/// 置于 Core 根命名空间，与 <see cref="GitException"/> / <see cref="SqliteException"/> 同置位理由：
/// Cli 层需要 catch 本类型映射退出码，而 Cli 不得引用 Detection。
/// </summary>
public sealed class DetectionException : GitGuardException
{
    /// <summary>错误码：检测器失败（正则匹配超时、规则执行异常），<c>10-异常处理设计.md</c> §4 登记值，CLI 映射退出码 11。</summary>
    public const string DETECTOR_FAILED = "DETECTOR_FAILED";

    /// <summary>初始化 <see cref="DetectionException"/> 的实例。</summary>
    /// <param name="message">异常消息（面向用户的简述，不得包含被扫描内容）。</param>
    /// <param name="ruleId">触发失败的规则 ID；非规则级失败（如加载期解析错误）为 <see langword="null"/>。</param>
    public DetectionException(string message, string? ruleId = null)
        : base(message)
    {
        ErrorCode = DETECTOR_FAILED;
        RuleId = ruleId;
    }

    /// <summary>使用内部异常初始化实例（如 <c>RegexMatchTimeoutException</c> / <c>YamlException</c>）。</summary>
    /// <param name="message">异常消息。</param>
    /// <param name="ruleId">触发失败的规则 ID；非规则级失败为 <see langword="null"/>。</param>
    /// <param name="innerException">内部异常。</param>
    public DetectionException(string message, string? ruleId, Exception innerException)
        : base(message, innerException)
    {
        ErrorCode = DETECTOR_FAILED;
        RuleId = ruleId;
    }

    /// <summary>错误码（<c>10-异常处理设计.md</c> §4 登记值）。</summary>
    public string ErrorCode { get; }

    /// <summary>触发失败的规则 ID；非规则级失败（如加载期解析错误）为 <see langword="null"/>。</summary>
    public string? RuleId { get; }
}
