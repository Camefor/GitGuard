using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using GitGuard.Core;
using GitGuard.Core.Abstractions;
using GitGuard.Core.Model;
using GitGuard.Core.Policy;
using GitGuard.Core.Rules;

namespace GitGuard.Detection.Detectors;

/// <summary>
/// 正则规则检测器基类（ADR-0003）：承载四类内置检测器的统一扫描管线——
/// 按类别挑选规则 → 文件 glob / 长行 / 上下文门禁 → 正则匹配（200ms 超时 FailClosed）→
/// 声明式校验器 → 严重度调整 → <see cref="FindingFactory"/> 构造发现项。
/// 子类通过受保护钩子定制类别特有语义，不重复实现管线。
/// </summary>
public abstract class RegexRuleDetector : IRuleTimingAwareDetector
{
    private readonly FindingFactory _findingFactory;
    private readonly Dictionary<string, Func<string, bool>> _validators;
    private readonly DetectionRule[] _rules;

    /// <summary>初始化检测器基类。</summary>
    /// <param name="name">检测器名称（<c>secret</c> / <c>privacy</c> / <c>local_path</c> / <c>url</c>）。</param>
    /// <param name="category">规则类别段（规则 ID 的第 2 段，如 <c>SECRET</c>）。</param>
    /// <param name="rules">全量规则集；基类按 <paramref name="category"/> 过滤并剔除禁用规则。</param>
    /// <param name="findingFactory">发现项工厂（注入指纹器）。</param>
    /// <param name="supportedValidators">本检测器支持的校验器表（名 → 断言）；<see langword="null"/> 视为空表。</param>
    /// <exception cref="ArgumentException">名称或类别为空。</exception>
    /// <exception cref="ArgumentNullException"><paramref name="rules"/> 或 <paramref name="findingFactory"/> 为 <see langword="null"/>。</exception>
    /// <exception cref="DetectionException">规则引用了本检测器不支持的校验器（拒绝加载，FailClosed）。</exception>
    protected RegexRuleDetector(
        string name,
        string category,
        IReadOnlyList<DetectionRule> rules,
        FindingFactory findingFactory,
        IReadOnlyDictionary<string, Func<string, bool>>? supportedValidators = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(category);
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(findingFactory);

        Name = name;
        _findingFactory = findingFactory;
        _validators = supportedValidators is null
            ? new Dictionary<string, Func<string, bool>>(StringComparer.Ordinal)
            : new Dictionary<string, Func<string, bool>>(supportedValidators, StringComparer.Ordinal);
        _rules = SelectRules(rules, category, _validators);
    }

    /// <summary>检测器名称（<c>secret</c> / <c>privacy</c> / <c>local_path</c> / <c>url</c>）。</summary>
    public string Name { get; }

    /// <summary>
    /// 扫描单个文件的变更行并流式产出发现项；同一行同一规则至多产出一条（首个命中生效）。
    /// </summary>
    /// <param name="change">文件变更。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>发现项流。</returns>
    /// <exception cref="ArgumentNullException"><paramref name="change"/> 为 <see langword="null"/>。</exception>
    /// <exception cref="DetectionException">正则匹配超时等检测失败时抛出（FailClosed）。</exception>
    public IAsyncEnumerable<Finding> DetectAsync(
        FileChange change,
        CancellationToken cancellationToken = default) =>
        DetectCoreAsync(change, timingSink: null, cancellationToken);

    /// <summary>
    /// 扫描单个文件的变更行并上报规则级耗时（NFR-M-002）；检测语义与 <see cref="DetectAsync"/> 完全一致。
    /// </summary>
    /// <param name="change">文件变更。</param>
    /// <param name="timingSink">耗时接收器。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>发现项流。</returns>
    /// <exception cref="ArgumentNullException"><paramref name="change"/> 或 <paramref name="timingSink"/> 为 <see langword="null"/>。</exception>
    /// <exception cref="DetectionException">正则匹配超时等检测失败时抛出（FailClosed）。</exception>
    public IAsyncEnumerable<Finding> DetectWithTimingAsync(
        FileChange change,
        IRuleTimingSink timingSink,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(change);
        ArgumentNullException.ThrowIfNull(timingSink);

        return DetectCoreAsync(change, timingSink, cancellationToken);
    }

    /// <summary>文件级跳过钩子（如 Secret 跳过 <c>.lock</c> / <c>.min.js</c>）；默认不跳过。</summary>
    /// <param name="change">文件变更。</param>
    /// <returns>跳过返回 <see langword="true"/>。</returns>
    protected virtual bool ShouldSkipFile(FileChange change) => false;

    /// <summary>行级规则跳过钩子（如 Secret 对超长行仅保留高置信度规则）；默认不跳过。</summary>
    /// <param name="rule">当前规则。</param>
    /// <param name="line">当前变更行。</param>
    /// <returns>跳过返回 <see langword="true"/>。</returns>
    protected virtual bool ShouldSkipRuleForLine(DetectionRule rule, ChangedLine line) => false;

    /// <summary>
    /// 上下文关键字门禁：规则声明了关键字时要求命中行包含任一关键字（大小写不敏感）；
    /// 未声明关键字的规则始终通过。
    /// </summary>
    /// <param name="rule">当前规则。</param>
    /// <param name="lineText">命中行文本。</param>
    /// <returns>满足上下文要求返回 <see langword="true"/>。</returns>
    protected virtual bool IsContextSatisfied(DetectionRule rule, string lineText)
    {
        if (rule.Pattern.ContextKeywords.Count == 0)
        {
            return true;
        }

        foreach (string keyword in rule.Pattern.ContextKeywords)
        {
            if (lineText.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>严重度调整钩子（降级 / 丢弃）；默认返回规则声明的严重度。</summary>
    /// <param name="rule">当前规则。</param>
    /// <param name="change">文件变更（提供路径上下文）。</param>
    /// <returns>最终严重度；返回 <see langword="null"/> 表示丢弃本次命中。</returns>
    protected virtual Severity? AdjustSeverity(DetectionRule rule, FileChange change) => rule.Severity;

    private static DetectionRule[] SelectRules(
        IReadOnlyList<DetectionRule> rules,
        string category,
        Dictionary<string, Func<string, bool>> supportedValidators)
    {
        var selected = new List<DetectionRule>();
        foreach (DetectionRule rule in rules)
        {
            if (CategoryOf(rule.RuleId) != category)
            {
                continue;
            }

            foreach (string validatorName in rule.Pattern.Validators)
            {
                if (!supportedValidators.ContainsKey(validatorName))
                {
                    throw new DetectionException($"规则 {rule.RuleId} 引用了本检测器不支持的校验器：{validatorName}", rule.RuleId);
                }
            }

            if (rule.Enabled)
            {
                selected.Add(rule);
            }
        }

        return selected.ToArray();
    }

    /// <summary>取规则 ID 的类别段（第 2 段）；命名契约保证恰好 3 段。</summary>
    /// <param name="ruleId">规则 ID。</param>
    /// <returns>类别段（如 <c>SECRET</c>）。</returns>
    private static string CategoryOf(string ruleId)
    {
        int firstSlash = ruleId.IndexOf('/');
        int secondSlash = ruleId.IndexOf('/', firstSlash + 1);
        return ruleId.Substring(firstSlash + 1, secondSlash - firstSlash - 1);
    }

    private static bool MatchesFileGlobs(DetectionRule rule, string path)
    {
        if (rule.Pattern.FilePathGlobs.Count == 0)
        {
            return true;
        }

        foreach (string glob in rule.Pattern.FilePathGlobs)
        {
            if (GlobMatcher.IsMatch(glob, path))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 执行规则正则并返回首个命中的敏感原文：优先取第 1 捕获组（规则以非捕获组包裹上下文时，
    /// 捕获组即敏感值），无捕获组时取整体匹配。匹配超时抛 <see cref="DetectionException"/>（FailClosed）。
    /// </summary>
    /// <param name="rule">当前规则。</param>
    /// <param name="lineText">命中行文本。</param>
    /// <returns>敏感原文；无命中返回 <see langword="null"/>。</returns>
    /// <exception cref="DetectionException">正则匹配超时（模块设计 Secret §4：200ms / 规则）。</exception>
    private static string? MatchValue(DetectionRule rule, string lineText)
    {
        MatchCollection matches;
        try
        {
            matches = rule.Pattern.Regex.Matches(lineText);

            // MatchCollection 为惰性枚举：此处强制求值，使超时异常在 try 内抛出（10-异常处理设计 §5）
            _ = matches.Count;
        }
        catch (RegexMatchTimeoutException ex)
        {
            throw new DetectionException(
                $"规则 {rule.RuleId} 正则匹配超时（{RulePattern.MATCH_TIMEOUT_MS}ms）：FailClosed 终止扫描",
                rule.RuleId,
                ex);
        }

        foreach (Match match in matches)
        {
            string value = match.Groups.Count > 1 && match.Groups[1].Success
                ? match.Groups[1].Value
                : match.Value;

            if (value.Length > 0)
            {
                return value;
            }
        }

        return null;
    }

    /// <summary>执行规则声明的全部校验器（构造期已保证校验器存在）。</summary>
    /// <param name="validators">本检测器支持的校验器表。</param>
    /// <param name="rule">当前规则。</param>
    /// <param name="value">命中值。</param>
    /// <returns>全部校验通过返回 <see langword="true"/>。</returns>
    private static bool ValidateValue(
        Dictionary<string, Func<string, bool>> validators,
        DetectionRule rule,
        string value)
    {
        foreach (string validatorName in rule.Pattern.Validators)
        {
            if (!validators[validatorName](value))
            {
                return false;
            }
        }

        return true;
    }

    private async IAsyncEnumerable<Finding> DetectCoreAsync(
        FileChange change,
        IRuleTimingSink? timingSink,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(change);

        // 扫描管线为纯 CPU 同步逻辑（正则 + 校验器），无真实异步点；等待已完成任务仅为保持 async 迭代器形态
        await Task.CompletedTask.ConfigureAwait(false);

        foreach (Finding finding in Scan(change, timingSink))
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return finding;
        }
    }

    /// <summary>
    /// 扫描管线：逐行 × 逐规则依次经文件 glob 白名单、行级跳过、上下文关键字、正则匹配、
    /// 校验器与严重度调整，命中即产出发现项；同一行同一规则至多一条（首个命中生效）。
    /// </summary>
    /// <param name="change">文件变更。</param>
    /// <param name="timingSink">耗时接收器；<see langword="null"/> 表示不采集规则耗时。</param>
    /// <returns>发现项序列。</returns>
    private IEnumerable<Finding> Scan(FileChange change, IRuleTimingSink? timingSink)
    {
        if (ShouldSkipFile(change))
        {
            yield break;
        }

        foreach (ChangedLine line in change.Lines)
        {
            foreach (DetectionRule rule in _rules)
            {
                if (!MatchesFileGlobs(rule, change.Path) || ShouldSkipRuleForLine(rule, line) || !IsContextSatisfied(rule, line.Text))
                {
                    continue;
                }

                // 仅统计真正执行正则 / 校验器的求值；文件 glob 与上下文门禁的短路不计入规则耗时
                long startedTimestamp = timingSink is null ? 0 : Stopwatch.GetTimestamp();
                string? value = MatchValue(rule, line.Text);
                Severity? severity = value is not null && ValidateValue(_validators, rule, value)
                    ? AdjustSeverity(rule, change)
                    : null;
                timingSink?.Record(rule.RuleId, Stopwatch.GetTimestamp() - startedTimestamp, severity is not null);

                if (severity is null)
                {
                    continue;
                }

                yield return _findingFactory.Create(rule, change, line.Number, value!, severity.Value);
            }
        }
    }
}
