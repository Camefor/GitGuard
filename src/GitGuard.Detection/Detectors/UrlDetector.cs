using System.Text.RegularExpressions;
using GitGuard.Core.Model;
using GitGuard.Core.Rules;

namespace GitGuard.Detection.Detectors;

/// <summary>
/// Url 检测器（模块设计 LocalPathUrl §3.2）：扫描 URL 中的凭据 / Token 与内网地址泄漏，
/// 承载 <c>GGD/URL/*</c> 13 条规则。类别特有语义：示例文件（example / sample / demo 开头）中严重度降一级；
/// 命中公共主机名白名单的发现项按配置抑制。
/// </summary>
public sealed class UrlDetector : RegexRuleDetector
{
    /// <summary>检测器名称（IDetector.Name）。</summary>
    public const string DETECTOR_NAME = "url";

    /// <summary>规则类别段（规则 ID 第 2 段）。</summary>
    public const string RULE_CATEGORY = "URL";

    private readonly UrlDetectorOptions _options;

    /// <summary>初始化 Url 检测器。</summary>
    /// <param name="rules">全量规则集；基类按 <c>URL</c> 类别过滤。</param>
    /// <param name="findingFactory">发现项工厂。</param>
    /// <param name="options">配置；<see langword="null"/> 使用默认值。</param>
    public UrlDetector(
        IReadOnlyList<DetectionRule> rules,
        FindingFactory findingFactory,
        UrlDetectorOptions? options = null)
        : base(DETECTOR_NAME, RULE_CATEGORY, rules, findingFactory) =>
        _options = options ?? new UrlDetectorOptions();

    /// <summary>示例文件名中降一级（Low 保持 Low，命中记录保留，模块设计 LocalPathUrl §3.2）。</summary>
    /// <param name="rule">当前规则。</param>
    /// <param name="change">文件变更。</param>
    /// <returns>最终严重度。</returns>
    protected override Severity? AdjustSeverity(DetectionRule rule, FileChange change) =>
        IsSampleFile(change.Path) ? SeverityDowngrade.StepOrFloor(rule.Severity) : rule.Severity;

    /// <summary>
    /// 公共主机名白名单抑制（模块设计 LocalPathUrl §3.2）：仅当命中行可解析出所属 URL 主机且命中白名单时抑制；
    /// 无法解析主机（如裸 IP / 内网域名）时保持原判定，避免误抑制。
    /// </summary>
    /// <param name="rule">当前规则。</param>
    /// <param name="line">命中行。</param>
    /// <param name="match">正则命中（提供定位以关联 URL）。</param>
    /// <returns>抑制返回 <see langword="true"/>。</returns>
    protected override bool ShouldSuppressMatch(DetectionRule rule, ChangedLine line, Match match)
    {
        if (_options.PublicHostnameWhitelist.Count == 0)
        {
            return false;
        }

        string? host = ExtractHost(line.Text, match.Index, match.Length);
        if (host is null)
        {
            return false;
        }

        foreach (string entry in _options.PublicHostnameWhitelist)
        {
            if (HostMatches(entry, host))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 从命中位置向前关联最近的 URL 并提取主机：解析 <c>scheme://authority</c>，跳过用户信息（<c>user:pass@</c>）
    /// 与端口 / 方括号 IPv6 形式；无 URL 上下文时返回 <see langword="null"/>。
    /// </summary>
    /// <param name="text">命中行文本。</param>
    /// <param name="matchIndex">命中起点（0-based）。</param>
    /// <param name="matchLength">命中长度。</param>
    /// <returns>小写无关的主机名；无法解析时为 <see langword="null"/>。</returns>
    private static string? ExtractHost(string text, int matchIndex, int matchLength)
    {
        int searchEnd = Math.Min(text.Length - 1, matchIndex + matchLength);
        if (searchEnd < 0)
        {
            return null;
        }

        int scheme = text.LastIndexOf("://", searchEnd, StringComparison.Ordinal);
        if (scheme < 0)
        {
            return null;
        }

        int authorityStart = scheme + 3;
        int authorityEnd = authorityStart;
        while (authorityEnd < text.Length && !IsAuthorityTerminator(text[authorityEnd]))
        {
            authorityEnd++;
        }

        if (authorityEnd <= authorityStart)
        {
            return null;
        }

        string authority = text[authorityStart..authorityEnd];
        int userInfo = authority.LastIndexOf('@');
        string hostPort = userInfo >= 0 ? authority.Substring(userInfo + 1) : authority;
        if (hostPort.Length == 0)
        {
            return null;
        }

        string host;
        if (hostPort[0] == '[')
        {
            int closingBracket = hostPort.IndexOf(']');
            if (closingBracket <= 1)
            {
                return null;
            }

            host = hostPort[1..closingBracket];
        }
        else
        {
            int portSeparator = hostPort.IndexOf(':');
            host = portSeparator >= 0 ? hostPort[..portSeparator] : hostPort;
        }

        return host.Length == 0 ? null : host;
    }

    private static bool IsAuthorityTerminator(char c) =>
        c is '/' or '?' or '#' or '"' or '\'' or '<' or '>' or ')' or ',' or ';' || char.IsWhiteSpace(c);

    private static bool HostMatches(string entry, string host)
    {
        if (entry.StartsWith("*.", StringComparison.Ordinal))
        {
            // entry[1..] = ".example.com"：只匹配子域，不匹配裸域
            string suffix = entry[1..];
            return host.Length > suffix.Length && host.EndsWith(suffix, StringComparison.OrdinalIgnoreCase);
        }

        return string.Equals(entry, host, StringComparison.OrdinalIgnoreCase);
    }

    private bool IsSampleFile(string path)
    {
        int lastSlash = path.LastIndexOf('/');
        string fileName = lastSlash >= 0 ? path.Substring(lastSlash + 1) : path;
        foreach (string prefix in _options.SampleFilePrefixes)
        {
            if (fileName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
