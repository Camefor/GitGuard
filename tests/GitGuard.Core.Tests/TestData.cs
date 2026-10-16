using GitGuard.Core.Model;

namespace GitGuard.Core.Tests;

/// <summary>测试共享数据：保持各测试类的样例值一致，减少魔法字符串。</summary>
internal static class TestData
{
    /// <summary>TC-DR-02-01 金丝雀 Token（09-日志设计 §9.3）；任何输出中出现其全文即视为 DR-02 违规。</summary>
    internal const string CanaryToken = "AKIA_SENTINEL_0192b3c45d6e7f809abc1234567890ab";

    internal static Fingerprint SampleFingerprint { get; } = Fingerprint.FromHex(new string('a', 64));

    internal static DateTimeOffset SampleUtcNow { get; } = new DateTimeOffset(2026, 10, 15, 12, 0, 0, TimeSpan.Zero);

    internal static string SampleRepoPath { get; } = Path.GetFullPath("repo");

    internal static Finding CreateFinding(
        string ruleId = "GGD/SECRET/AWS-AKID",
        string filePath = "src/appsettings.json",
        int lineNumber = 18,
        Severity severity = Severity.Critical,
        Fingerprint? fingerprint = null,
        string snippet = "AKIA****************",
        string? commitSha = null) =>
        Finding.Create(
            ruleId,
            filePath,
            lineNumber,
            severity,
            fingerprint ?? SampleFingerprint,
            SnippetMasked.Create(snippet),
            commitSha);
}
