using GitGuard.Core.Model;
using GitGuard.Core.Policy;
using GitGuard.Core.Rules;

namespace GitGuard.Detection.Detectors;

/// <summary>
/// LocalPath 检测器（模块设计 LocalPathUrl §3.1）：扫描本机绝对路径泄漏，承载 <c>GGD/LOCALPATH/*</c> 12 条规则。
/// 类别特有语义：文档文件（<c>.gitignore</c> / <c>README.md</c> / <c>docs/**</c>）中严重度降一级，Low 规则不命中；
/// Markdown 围栏代码块内的路径视为示例内容，不产出 Finding。
/// </summary>
public sealed class LocalPathDetector : RegexRuleDetector
{
    /// <summary>检测器名称（IDetector.Name）。</summary>
    public const string DETECTOR_NAME = "local_path";

    /// <summary>规则类别段（规则 ID 第 2 段）。</summary>
    public const string RULE_CATEGORY = "LOCALPATH";

    private readonly LocalPathDetectorOptions _options;

    /// <summary>初始化 LocalPath 检测器。</summary>
    /// <param name="rules">全量规则集；基类按 <c>LOCALPATH</c> 类别过滤。</param>
    /// <param name="findingFactory">发现项工厂。</param>
    /// <param name="options">配置；<see langword="null"/> 使用默认值。</param>
    public LocalPathDetector(
        IReadOnlyList<DetectionRule> rules,
        FindingFactory findingFactory,
        LocalPathDetectorOptions? options = null)
        : base(DETECTOR_NAME, RULE_CATEGORY, rules, findingFactory) =>
        _options = options ?? new LocalPathDetectorOptions();

    /// <summary>文档文件中降一级；Low 无可降级（返回 <see langword="null"/>）即不命中（模块设计 LocalPathUrl §3.1）。</summary>
    /// <param name="rule">当前规则。</param>
    /// <param name="change">文件变更。</param>
    /// <returns>最终严重度；不命中时为 <see langword="null"/>。</returns>
    protected override Severity? AdjustSeverity(DetectionRule rule, FileChange change) =>
        IsDocFile(change.Path) ? SeverityDowngrade.Step(rule.Severity) : rule.Severity;

    /// <summary>
    /// Markdown 文档中的围栏代码块内容不参与路径判定（模块设计 LocalPathUrl §3.1 P2）：
    /// 仅处理 <c>.md</c> / <c>.markdown</c>；围栏与目标行跨差异缺口时由扫描器保守回落为正常检测。
    /// </summary>
    /// <param name="change">文件变更。</param>
    /// <returns>围栏内行号集合；非 Markdown 或无围栏时为 <see langword="null"/>。</returns>
    protected override IReadOnlySet<int>? GetSuppressedLineNumbers(FileChange change) =>
        IsMarkdownFile(change.Path) ? MarkdownFenceScanner.FindFencedLineNumbers(change) : null;

    private static bool IsMarkdownFile(string path) =>
        path.EndsWith(".md", StringComparison.OrdinalIgnoreCase)
        || path.EndsWith(".markdown", StringComparison.OrdinalIgnoreCase);

    private bool IsDocFile(string path)
    {
        foreach (string glob in _options.DocFileGlobs)
        {
            if (GlobMatcher.IsMatch(glob, path))
            {
                return true;
            }
        }

        return false;
    }
}
