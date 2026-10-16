using System.Text.RegularExpressions;
using GitGuard.Core.Domain;

namespace GitGuard.Core.Rules;

/// <summary>
/// 规则模式值对象（DDD §3.2 / §4.2）：正则 + 上下文关键字 + 文件 glob + 声明式校验器。
/// 正则一律以 <see cref="MATCH_TIMEOUT_MS"/> 超时编译（13-开发规范 §4.2），
/// 灾难性回溯在匹配期抛 <c>RegexMatchTimeoutException</c>，由检测器转换为 <c>DetectionException</c>（FailClosed）。
/// </summary>
public sealed class RulePattern
{
    /// <summary>单规则正则匹配超时（毫秒；13-开发规范 §4.2 与模块设计 Secret §4）。</summary>
    public const int MATCH_TIMEOUT_MS = 200;

    private RulePattern(
        Regex regex,
        IReadOnlyList<string> contextKeywords,
        IReadOnlyList<string> filePathGlobs,
        IReadOnlyList<string> validators)
    {
        Regex = regex;
        ContextKeywords = contextKeywords;
        FilePathGlobs = filePathGlobs;
        Validators = validators;
    }

    /// <summary>编译后的正则（<c>Compiled | CultureInvariant</c>，含 200ms 匹配超时）。</summary>
    public Regex Regex { get; }

    /// <summary>上下文关键字：非空时要求命中行包含任一关键字（大小写不敏感，Privacy 模块可经 <c>require_context</c> 关闭）。</summary>
    public IReadOnlyList<string> ContextKeywords { get; }

    /// <summary>文件 glob 白名单：非空时仅扫描匹配任一模（<c>GlobMatcher</c> 语义）的文件。</summary>
    public IReadOnlyList<string> FilePathGlobs { get; }

    /// <summary>声明式校验器名（如 <c>shannon-entropy</c> / <c>luhn</c> / <c>cn-id-checksum</c>）；
    /// 由检测器在构造期校验其受支持集合，未知校验器拒绝加载。</summary>
    public IReadOnlyList<string> Validators { get; }

    /// <summary>
    /// 创建规则模式并编译正则。
    /// </summary>
    /// <param name="pattern">正则表达式文本。</param>
    /// <param name="contextKeywords">上下文关键字；<see langword="null"/> 视为空。</param>
    /// <param name="filePathGlobs">文件 glob 白名单；<see langword="null"/> 视为空。</param>
    /// <param name="validators">声明式校验器名；<see langword="null"/> 视为空。</param>
    /// <returns>通过编译与校验的模式。</returns>
    /// <exception cref="ArgumentNullException"><paramref name="pattern"/> 为 <see langword="null"/>。</exception>
    /// <exception cref="InvariantViolationException">正则为空、不可编译，或列表项含空值时抛出（DDD §3.2：编译失败拒绝加载）。</exception>
    public static RulePattern Create(
        string pattern,
        IReadOnlyList<string>? contextKeywords = null,
        IReadOnlyList<string>? filePathGlobs = null,
        IReadOnlyList<string>? validators = null)
    {
        ArgumentNullException.ThrowIfNull(pattern);
        DomainGuard.Require(!string.IsNullOrWhiteSpace(pattern), "DDD §3.2", "规则正则不能为空");

        Regex regex;
        try
        {
            regex = new Regex(
                pattern,
                RegexOptions.Compiled | RegexOptions.CultureInvariant,
                TimeSpan.FromMilliseconds(MATCH_TIMEOUT_MS));
        }
        catch (ArgumentException ex)
        {
            throw new InvariantViolationException($"[DDD §3.2] 规则正则不可编译，拒绝加载：{ex.Message}", ex);
        }

        return new RulePattern(
            regex,
            CopyList(contextKeywords, "上下文关键字"),
            CopyList(filePathGlobs, "文件 glob"),
            CopyList(validators, "校验器名"));
    }

    private static string[] CopyList(IReadOnlyList<string>? source, string fieldName)
    {
        if (source is null || source.Count == 0)
        {
            return Array.Empty<string>();
        }

        string[] copy = new string[source.Count];
        for (int i = 0; i < source.Count; i++)
        {
            string item = source[i];
            DomainGuard.Require(!string.IsNullOrWhiteSpace(item), "DDD §3.2", $"{fieldName}不能包含空值（第 {i + 1} 项）");
            copy[i] = item;
        }

        return copy;
    }
}
