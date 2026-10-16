using YamlDotNet.Serialization;

namespace GitGuard.Detection.Rules;

/// <summary>
/// <c>BuiltinRules.yaml</c> 根文档 DTO（版本号 + 规则列表）。
/// 属性名与 YAML 键的映射由 <c>UnderscoredNamingConvention</c>（PascalCase → snake_case）完成。
/// </summary>
internal sealed class BuiltinRulesDocument
{
    /// <summary>文档版本；必须等于 <see cref="BuiltinRuleLoader.SUPPORTED_VERSION"/>，否则拒绝加载。</summary>
    public int Version { get; set; }

    /// <summary>规则条目。</summary>
    public List<RuleDto>? Rules { get; set; }
}

/// <summary>
/// 单条规则的 YAML DTO。字段均为可空以区分「未填写」与「显式空值」，由加载器统一做必填校验与默认值填充。
/// </summary>
internal sealed class RuleDto
{
    /// <summary>规则 ID（必填）。</summary>
    public string? Id { get; set; }

    /// <summary>规则名称（必填）。</summary>
    public string? Name { get; set; }

    /// <summary>严重度名（<c>Low</c> / <c>Medium</c> / <c>High</c> / <c>Critical</c>，必填）。</summary>
    public string? Severity { get; set; }

    /// <summary>正则文本（必填）。</summary>
    public string? Pattern { get; set; }

    /// <summary>上下文关键字；省略或空列表视为无关键字门禁。</summary>
    public List<string>? ContextKeywords { get; set; }

    /// <summary>文件 glob 白名单；省略或空列表视为不限文件。</summary>
    public List<string>? FilePathGlobs { get; set; }

    /// <summary>声明式校验器名；省略或空列表视为无校验器。</summary>
    public List<string>? Validators { get; set; }

    /// <summary>是否启用；省略视为启用（与 YAML 简洁书写习惯一致）。</summary>
    public bool? Enabled { get; set; }
}

/// <summary>
/// YamlDotNet 静态上下文（ADR-0002 / 13-开发规范 §4.2）：源生成器为 <see cref="BuiltinRulesDocument"/> 与
/// <see cref="RuleDto"/> 生成确定性反序列化代码，替代 AOT 下不可用的反射式类型解析（IL2026 / IL3050）。
/// 新增 DTO 类型时必须在此登记 <c>[YamlSerializable]</c>，否则运行时反序列化失败。
/// </summary>
[YamlStaticContext]
[YamlSerializable(typeof(BuiltinRulesDocument))]
[YamlSerializable(typeof(RuleDto))]
public partial class BuiltinRulesYamlContext : StaticContext
{
}
