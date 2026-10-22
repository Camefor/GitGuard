using System.Text;
using GitGuard.Core;
using GitGuard.Core.Domain;
using GitGuard.Core.Model;
using GitGuard.Core.Rules;
using YamlDotNet.Core;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace GitGuard.Detection.Rules;

/// <summary>
/// 内置规则加载器（模块设计 Secret §3；DDD §9 反模式：检测器代码中禁止写死正则，规则一律经本加载器载入）。
/// 规则随程序集嵌入发布（<c>EmbeddedResource</c>，ADR-0002 单文件发行），加载失败一律抛
/// <see cref="DetectionException"/>（FailClosed，10-异常处理设计 §5）。
/// </summary>
public static class BuiltinRuleLoader
{
    /// <summary>本加载器支持的规则文档版本；<c>BuiltinRules.yaml</c> 的 <c>version</c> 必须与其一致。</summary>
    public const int SUPPORTED_VERSION = 1;

    /// <summary>嵌入资源名（与 <c>GitGuard.Detection.csproj</c> 的 <c>LogicalName</c> 一一对应）。</summary>
    public const string RESOURCE_NAME = "GitGuard.Detection.Rules.BuiltinRules.yaml";

    /// <summary>内置规则 ID 前缀：加载器只接受 <c>GGD/</c> 前缀，防止来源标注与 ID 前缀错配。</summary>
    private const string BUILTIN_PREFIX = "GGD/";

    private static readonly IDeserializer _deserializer =
        new StaticDeserializerBuilder(new BuiltinRulesYamlContext())
            .WithNamingConvention(UnderscoredNamingConvention.Instance)
            .Build();

    /// <summary>加载程序集内嵌的 <c>BuiltinRules.yaml</c>。</summary>
    /// <returns>按 YAML 书写顺序排列的规则列表（扫描顺序即 FR-021 要求的稳定顺序）。</returns>
    /// <exception cref="DetectionException">嵌入资源缺失、YAML 解析失败或规则校验失败。</exception>
    public static IReadOnlyList<DetectionRule> LoadEmbedded()
    {
        using Stream stream = typeof(BuiltinRuleLoader).Assembly.GetManifestResourceStream(RESOURCE_NAME)
            ?? throw new DetectionException($"内置规则嵌入资源缺失：{RESOURCE_NAME}（构建配置错误）");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return Load(reader.ReadToEnd());
    }

    /// <summary>
    /// 从 YAML 文本加载规则集；供测试与后续用户规则（P3 <c>UserRegexDetector</c>，USR/ 前缀）复用。
    /// </summary>
    /// <param name="yaml">规则文档 YAML 文本。</param>
    /// <returns>通过全部不变式校验的规则列表。</returns>
    /// <exception cref="ArgumentNullException"><paramref name="yaml"/> 为 <see langword="null"/>。</exception>
    /// <exception cref="DetectionException">版本不匹配、必填字段缺失、严重度非法、正则不可编译或规则 ID 重复。</exception>
    public static IReadOnlyList<DetectionRule> Load(string yaml)
    {
        ArgumentNullException.ThrowIfNull(yaml);

        BuiltinRulesDocument document;
        try
        {
            document = _deserializer.Deserialize<BuiltinRulesDocument>(yaml);
        }
        catch (YamlException ex)
        {
            throw new DetectionException($"内置规则 YAML 解析失败：{ex.Message}", null, ex);
        }

        if (document.Version != SUPPORTED_VERSION)
        {
            throw new DetectionException($"内置规则文档版本不受支持：{document.Version}（本版本仅支持 {SUPPORTED_VERSION}）");
        }

        if (document.Rules is null || document.Rules.Count == 0)
        {
            throw new DetectionException("内置规则集为空：rules 不得省略");
        }

        var rules = new List<DetectionRule>(document.Rules.Count);
        var seenIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (RuleDto dto in document.Rules)
        {
            DetectionRule rule = MapRule(dto);
            if (!seenIds.Add(rule.RuleId))
            {
                throw new DetectionException($"内置规则 ID 重复：{rule.RuleId}");
            }

            rules.Add(rule);
        }

        return rules;
    }

    private static DetectionRule MapRule(RuleDto dto)
    {
        string? ruleId = dto.Id;
        if (string.IsNullOrWhiteSpace(ruleId))
        {
            throw new DetectionException("内置规则缺少 id 字段");
        }

        if (!ruleId.StartsWith(BUILTIN_PREFIX, StringComparison.Ordinal))
        {
            throw new DetectionException($"内置规则 ID 必须以 {BUILTIN_PREFIX} 开头：{ruleId}");
        }

        if (string.IsNullOrWhiteSpace(dto.Name))
        {
            throw new DetectionException($"规则 {ruleId} 缺少 name 字段");
        }

        if (!Enum.TryParse(dto.Severity, ignoreCase: true, out Severity severity) || !Enum.IsDefined(severity))
        {
            throw new DetectionException($"规则 {ruleId} 的 severity 非法：{dto.Severity ?? "(缺失)"}（应为 Low/Medium/High/Critical）");
        }

        if (string.IsNullOrWhiteSpace(dto.Pattern))
        {
            throw new DetectionException($"规则 {ruleId} 缺少 pattern 字段");
        }

        try
        {
            var pattern = RulePattern.Create(
                dto.Pattern,
                dto.ContextKeywords,
                dto.FilePathGlobs,
                dto.Validators);

            return DetectionRule.Create(
                ruleId,
                dto.Name,
                severity,
                pattern,
                RuleSource.Builtin,
                dto.Enabled ?? true);
        }
        catch (InvariantViolationException ex)
        {
            throw new DetectionException($"规则 {ruleId} 校验失败：{ex.Message}", ruleId, ex);
        }
    }
}
