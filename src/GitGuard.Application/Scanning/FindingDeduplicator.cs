using GitGuard.Core.Model;

namespace GitGuard.Application.Scanning;

/// <summary>
/// 跨引擎发现项去重（ADR-0003 配套机制）：同一提交、同一文件、同一行、同一指纹视为同一次泄漏，
/// 保留来源优先级最高者（<c>USR</c> &gt; <c>GLK</c> &gt; <c>GGD</c>）；严重度不一致时取最高，
/// 避免 Gitleaks 的 High 默认值掩盖内置 Critical 规则的阻断语义。
/// 指纹相同时不同文件 / 行 / 提交仍各自保留（同一密钥在多处出现是多个独立事实）。
/// </summary>
internal static class FindingDeduplicator
{
    /// <summary>按（提交 SHA、文件路径、行号、指纹）去重并合并严重度。</summary>
    /// <param name="findings">原始发现项（保持检测器产出顺序）。</param>
    /// <returns>去重后的发现项列表（保持首次出现顺序）。</returns>
    internal static List<Finding> Deduplicate(List<Finding> findings)
    {
        if (findings.Count <= 1)
        {
            return findings;
        }

        var order = new List<(string? CommitSha, string FilePath, int LineNumber, string Fingerprint)>(findings.Count);
        var winners = new Dictionary<(string? CommitSha, string FilePath, int LineNumber, string Fingerprint), Finding>();
        foreach (Finding finding in findings)
        {
            var key = (finding.CommitSha, finding.FilePath, finding.LineNumber, finding.Fingerprint.Hex);
            if (!winners.TryGetValue(key, out Finding? existing))
            {
                winners.Add(key, finding);
                order.Add(key);
                continue;
            }

            Finding preferred = SourcePriority(finding.RuleId) < SourcePriority(existing.RuleId)
                ? finding
                : existing;
            Severity severity = finding.Severity > existing.Severity ? finding.Severity : existing.Severity;
            winners[key] = preferred.Severity == severity
                ? preferred
                : Finding.Restore(
                    preferred.Id,
                    preferred.RuleId,
                    preferred.FilePath,
                    preferred.LineNumber,
                    severity,
                    preferred.Fingerprint,
                    preferred.SnippetMasked,
                    preferred.CommitSha);
        }

        var result = new List<Finding>(order.Count);
        foreach (var key in order)
        {
            result.Add(winners[key]);
        }

        return result;
    }

    /// <summary>规则来源优先级（数值越小越优先；ADR-0003：USR &gt; GLK &gt; GGD）。</summary>
    /// <param name="ruleId">规则 ID。</param>
    /// <returns>优先级序号。</returns>
    private static int SourcePriority(string ruleId)
    {
        if (ruleId.StartsWith("USR/", StringComparison.Ordinal))
        {
            return 0;
        }

        if (ruleId.StartsWith("GLK/", StringComparison.Ordinal))
        {
            return 1;
        }

        return ruleId.StartsWith("GGD/", StringComparison.Ordinal) ? 2 : 3;
    }
}
