using YamlDotNet.Serialization;

namespace GitGuard.Application.Configuration;

/// <summary>
/// <c>.gitguard.yml</c> / 用户级 <c>config.yml</c> 的根文档 DTO（模块设计/Policy.md §7）。
/// 所有字段可空以区分「未填写」与「显式空值」：合并（项目级覆盖用户级）与默认值填充都在加载器中完成。
/// 属性名与 YAML 键的映射由 <c>UnderscoredNamingConvention</c>（PascalCase → snake_case）完成。
/// </summary>
internal sealed class GitGuardConfigDocument
{
    /// <summary>Schema 版本；必填且必须等于 <see cref="GitGuardConfig.SUPPORTED_VERSION"/>（FR-070）。</summary>
    public int? Version { get; set; }

    /// <summary><c>scan</c> 段落。</summary>
    public ScanSectionDto? Scan { get; set; }

    /// <summary><c>logging</c> 段落。</summary>
    public LoggingSectionDto? Logging { get; set; }

    /// <summary><c>rules</c> 段落。</summary>
    public RulesSectionDto? Rules { get; set; }

    /// <summary><c>policy</c> 段落。</summary>
    public PolicySectionDto? Policy { get; set; }

    /// <summary><c>exclude</c> 段落（GGD-0009 仅解析不消费，见 <see cref="GitGuardConfigLoader"/> 文档）。</summary>
    public ExcludeSectionDto? Exclude { get; set; }

    /// <summary><c>allowlist</c> 段落。</summary>
    public AllowlistSectionDto? Allowlist { get; set; }
}

/// <summary><c>scan</c> 段落 DTO。</summary>
internal sealed class ScanSectionDto
{
    /// <summary>是否启用 pre-commit 扫描（当前仅解析，语义接线顺延）。</summary>
    public bool? PreCommit { get; set; }

    /// <summary>是否启用 pre-push 扫描（当前仅解析，语义接线顺延）。</summary>
    public bool? PrePush { get; set; }

    /// <summary>FailClosed 开关；置 <see langword="false"/> 仅产生警告（DR-03 行为不变）。</summary>
    public bool? FailClosed { get; set; }

    /// <summary>单文件大小上限（字节）。</summary>
    public int? MaxFileBytes { get; set; }

    /// <summary>扫描级超时（毫秒）。</summary>
    public int? TimeoutMs { get; set; }
}

/// <summary><c>logging</c> 段落 DTO。</summary>
internal sealed class LoggingSectionDto
{
    /// <summary>是否默认输出 Debug 级日志（等价于每次携带 <c>--verbose</c>）。</summary>
    public bool? Verbose { get; set; }

    /// <summary>文件日志设置。</summary>
    public LogFileSectionDto? File { get; set; }
}

/// <summary><c>logging.file</c> 段落 DTO（09-日志设计 §7）。</summary>
internal sealed class LogFileSectionDto
{
    /// <summary>是否启用文件日志。</summary>
    public bool? Enabled { get; set; }

    /// <summary>日志文件路径（支持 <c>~</c> 前缀）。</summary>
    public string? Path { get; set; }

    /// <summary>单文件滚动阈值（字节）。</summary>
    public long? MaxBytes { get; set; }

    /// <summary>滚动保留份数。</summary>
    public int? MaxFiles { get; set; }
}

/// <summary><c>rules</c> 段落 DTO。</summary>
internal sealed class RulesSectionDto
{
    /// <summary>秘密检测类别。</summary>
    public SecretRulesSectionDto? Secrets { get; set; }

    /// <summary>隐私检测类别。</summary>
    public PrivacyRulesSectionDto? Privacy { get; set; }

    /// <summary>本地路径检测类别。</summary>
    public RuleCategorySectionDto? LocalPath { get; set; }

    /// <summary>URL 检测类别。</summary>
    public RuleCategorySectionDto? Url { get; set; }
}

/// <summary>规则类别通用 DTO（<c>enabled</c> + <c>disabled_rules</c>）；<c>rules.local_path</c> / <c>rules.url</c> 直接使用。</summary>
internal sealed class RuleCategorySectionDto
{
    /// <summary>是否启用该类别（<see langword="false"/> 时不装配其检测器）。</summary>
    public bool? Enabled { get; set; }

    /// <summary>禁用的规则 ID 列表。</summary>
    public List<string>? DisabledRules { get; set; }
}

/// <summary>
/// <c>rules.secrets</c> 段落 DTO。
/// 不继承 <see cref="RuleCategorySectionDto"/>：YamlDotNet 静态上下文对派生 DTO 的类型映射支持有限（AOT 优先），
/// 字段重复声明换取确定性生成代码。
/// </summary>
internal sealed class SecretRulesSectionDto
{
    /// <summary>是否启用该类别。</summary>
    public bool? Enabled { get; set; }

    /// <summary>禁用的规则 ID 列表。</summary>
    public List<string>? DisabledRules { get; set; }

    /// <summary>是否启用 Gitleaks 适配器（MVP 不支持，置 <see langword="true"/> → 退出码 13）。</summary>
    public bool? UseGitleaks { get; set; }

    /// <summary>熵阈值（用于高熵字符串候选过滤）。</summary>
    public double? MinEntropy { get; set; }
}

/// <summary><c>rules.privacy</c> 段落 DTO；不继承理由同 <see cref="SecretRulesSectionDto"/>。</summary>
internal sealed class PrivacyRulesSectionDto
{
    /// <summary>是否启用该类别。</summary>
    public bool? Enabled { get; set; }

    /// <summary>禁用的规则 ID 列表。</summary>
    public List<string>? DisabledRules { get; set; }

    /// <summary>是否要求上下文关键字门禁。</summary>
    public bool? RequireContext { get; set; }

    /// <summary>测试文件 glob 列表（命中即降级或跳过）。</summary>
    public List<string>? TestFilePatterns { get; set; }
}

/// <summary><c>policy</c> 段落 DTO（严重度 → 动作名）。</summary>
internal sealed class PolicySectionDto
{
    /// <summary>Critical 的映射动作名（<c>block</c> / <c>warn</c> / <c>allow</c>）。</summary>
    public string? Critical { get; set; }

    /// <summary>High 的映射动作名。</summary>
    public string? High { get; set; }

    /// <summary>Medium 的映射动作名。</summary>
    public string? Medium { get; set; }

    /// <summary>Low 的映射动作名。</summary>
    public string? Low { get; set; }
}

/// <summary><c>exclude</c> 段落 DTO。</summary>
internal sealed class ExcludeSectionDto
{
    /// <summary>路径 glob 列表。</summary>
    public List<string>? Paths { get; set; }

    /// <summary>文件名 glob 列表。</summary>
    public List<string>? Files { get; set; }
}

/// <summary><c>allowlist</c> 段落 DTO。</summary>
internal sealed class AllowlistSectionDto
{
    /// <summary>白名单条目列表。</summary>
    public List<AllowlistEntryDto>? Entries { get; set; }
}

/// <summary>单条白名单条目 DTO（Policy.md §7 <c>allowlist.entries</c>）。</summary>
internal sealed class AllowlistEntryDto
{
    /// <summary>规则 ID 匹配条件。</summary>
    public string? RuleId { get; set; }

    /// <summary>文件路径 glob 匹配条件。</summary>
    public string? PathPattern { get; set; }

    /// <summary>指纹匹配条件（64 字符十六进制）。</summary>
    public string? Fingerprint { get; set; }

    /// <summary>登记原因（必填，≥ 5 字符）。</summary>
    public string? Reason { get; set; }
}

/// <summary>
/// 配置文件 YAML 静态上下文（ADR-0002 / 13-开发规范 §4.2）：源生成器为配置 DTO 生成确定性反序列化代码，
/// 替代 AOT 下不可用的反射式类型解析（IL2026 / IL3050）。
/// 新增 DTO 类型时必须在此登记 <c>[YamlSerializable]</c>，否则运行时反序列化失败。
/// </summary>
[YamlStaticContext]
[YamlSerializable(typeof(GitGuardConfigDocument))]
[YamlSerializable(typeof(ScanSectionDto))]
[YamlSerializable(typeof(LoggingSectionDto))]
[YamlSerializable(typeof(LogFileSectionDto))]
[YamlSerializable(typeof(RulesSectionDto))]
[YamlSerializable(typeof(RuleCategorySectionDto))]
[YamlSerializable(typeof(SecretRulesSectionDto))]
[YamlSerializable(typeof(PrivacyRulesSectionDto))]
[YamlSerializable(typeof(PolicySectionDto))]
[YamlSerializable(typeof(ExcludeSectionDto))]
[YamlSerializable(typeof(AllowlistSectionDto))]
[YamlSerializable(typeof(AllowlistEntryDto))]
public partial class GitGuardConfigYamlContext : StaticContext
{
}
