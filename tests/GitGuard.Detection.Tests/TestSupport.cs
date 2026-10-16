using GitGuard.Core.Abstractions;
using GitGuard.Core.Model;
using GitGuard.Core.Rules;
using GitGuard.Core.Services;
using GitGuard.Detection.Detectors;
using GitGuard.Detection.Rules;

namespace GitGuard.Detection.Tests;

/// <summary>
/// 检测器测试基础设施：以确定性指纹盐装配四类内置检测器，并提供文件变更构造与发现项收集辅助方法。
/// </summary>
internal static class TestSupport
{
    /// <summary>中性扫描路径：不命中任何降级 / 跳过条件（非测试样本文档，无示例文件名前缀）。</summary>
    public const string NEUTRAL_SCAN_PATH = "src/module/service.cs";

    private static readonly IReadOnlyList<DetectionRule> RULES = BuiltinRuleLoader.LoadEmbedded();

    private static readonly FindingFactory FACTORY = new FindingFactory(new Fingerprinter(new byte[Fingerprinter.SALT_BYTES]));

    /// <summary>创建确定性指纹盐（32 字节全零）的发现项工厂；同一原文跨扫描得到相同指纹。</summary>
    /// <returns>发现项工厂。</returns>
    public static FindingFactory CreateFactory() => new FindingFactory(new Fingerprinter(new byte[Fingerprinter.SALT_BYTES]));

    /// <summary>装配 Privacy 检测器（可注入自定义选项，用于上下文门禁等行为测试）。</summary>
    /// <param name="options">检测器选项；<see langword="null"/> 使用默认值。</param>
    /// <returns>Privacy 检测器。</returns>
    public static PrivacyDetector CreatePrivacyDetector(PrivacyDetectorOptions? options = null) => new PrivacyDetector(RULES, FACTORY, options);

    /// <summary>按规则类别段（<c>SECRET</c> / <c>PRIVACY</c> / <c>LOCALPATH</c> / <c>URL</c>）装配内置检测器。</summary>
    /// <param name="category">规则类别段（规则 ID 第 2 段）。</param>
    /// <returns>使用默认选项的检测器实例。</returns>
    /// <exception cref="ArgumentOutOfRangeException">未知类别段时抛出。</exception>
    public static IDetector CreateDetector(string category) => category switch
    {
        "SECRET" => new SecretDetector(RULES, FACTORY),
        "PRIVACY" => new PrivacyDetector(RULES, FACTORY),
        "LOCALPATH" => new LocalPathDetector(RULES, FACTORY),
        "URL" => new UrlDetector(RULES, FACTORY),
        _ => throw new ArgumentOutOfRangeException(nameof(category), category, "未知的规则类别段"),
    };

    /// <summary>以单行文本（行号 1）构造文件变更并收集全部发现项。</summary>
    /// <param name="detector">检测器。</param>
    /// <param name="path">仓库相对路径。</param>
    /// <param name="text">扫描行文本。</param>
    /// <returns>发现项列表。</returns>
    public static Task<List<Finding>> DetectSingleLineAsync(IDetector detector, string path, string text) =>
        DetectLinesAsync(detector, path, text);

    /// <summary>逐行构造文件变更（行号从 1 递增）并收集全部发现项。</summary>
    /// <param name="detector">检测器。</param>
    /// <param name="path">仓库相对路径。</param>
    /// <param name="lines">扫描行文本。</param>
    /// <returns>发现项列表。</returns>
    public static async Task<List<Finding>> DetectLinesAsync(IDetector detector, string path, params string[] lines)
    {
        var changedLines = new ChangedLine[lines.Length];
        for (int i = 0; i < lines.Length; i++)
        {
            changedLines[i] = new ChangedLine(i + 1, lines[i]);
        }

        return await CollectAsync(detector, new FileChange(path, changedLines));
    }

    /// <summary>收集检测器对单个文件变更输出的全部发现项。</summary>
    /// <param name="detector">检测器。</param>
    /// <param name="change">文件变更。</param>
    /// <returns>发现项列表（保持产出顺序）。</returns>
    public static async Task<List<Finding>> CollectAsync(IDetector detector, FileChange change)
    {
        var findings = new List<Finding>();
        await foreach (Finding finding in detector.DetectAsync(change, TestContext.Current.CancellationToken))
        {
            findings.Add(finding);
        }

        return findings;
    }
}
