using System.Collections.Immutable;
using GitGuard.Application.Scanning;
using GitGuard.Core.Policy;
using GitGuard.Core.Rules;
using GitGuard.Infrastructure.FileSystem;

namespace GitGuard.Application.Configuration;

/// <summary>
/// 合并并校验后的有效配置（模块设计/Policy.md §5、§6.2「配置加载」流程的终点）：
/// 由 <see cref="GitGuardConfigLoader"/> 从「内置默认 ← 用户级 config.yml ← 项目级 .gitguard.yml」三层合并产出，
/// 供组合根映射为用例参数（<see cref="Scan"/> → 扫描设置、<see cref="Policy"/> → 策略判定、
/// 各 <c>Rules</c> 设置 → 检测器装配）。
/// 本记录为不可变快照：CLI 进程生命周期内配置改动需重启进程（07-缓存设计 §4）。
/// </summary>
public sealed record GitGuardConfig
{
    /// <summary>本版本支持的配置 Schema 版本；<c>version</c> 字段必填且必须等于此值（FR-070 / Policy.md §7）。</summary>
    public const int SUPPORTED_VERSION = 1;

    /// <summary>内置默认配置（无任何配置文件时的行为，Policy.md §7 默认值列）。</summary>
    public static GitGuardConfig Default { get; } = new GitGuardConfig(
        SUPPORTED_VERSION,
        ScanSettings.Default,
        SeverityPolicy.Default,
        SecretRulesSettings.Default,
        PrivacyRulesSettings.Default,
        CategoryRulesSettings.Default,
        CategoryRulesSettings.Default,
        LogFileSettings.Default,
        logVerbose: false,
        allowlist: ImmutableArray<AllowlistEntry>.Empty,
        loadedConfigFiles: ImmutableArray<string>.Empty,
        warnings: ImmutableArray<string>.Empty);

    /// <summary>初始化有效配置。</summary>
    /// <param name="version">Schema 版本（恒为 <see cref="SUPPORTED_VERSION"/>）。</param>
    /// <param name="scan">扫描设置（<c>scan</c> 段落）。</param>
    /// <param name="policy">严重度 → 动作映射（<c>policy</c> 段落，已过硬下限校验）。</param>
    /// <param name="secrets">秘密检测类别设置。</param>
    /// <param name="privacy">隐私检测类别设置。</param>
    /// <param name="localPath">本地路径检测类别设置。</param>
    /// <param name="url">URL 检测类别设置。</param>
    /// <param name="logFile">文件日志设置（<c>logging.file</c> 段落）。</param>
    /// <param name="logVerbose"><c>logging.verbose</c>：默认以 Debug 级输出（等价于常带 <c>--verbose</c>）。</param>
    /// <param name="allowlist">配置文件声明的白名单条目（<c>allowlist.entries</c>）。</param>
    /// <param name="loadedConfigFiles">实际参与合并的配置文件路径（按从低到高优先级排列），供 <c>status</c> 展示加载链。</param>
    /// <param name="warnings">宽松解析产生的警告（未知字段、暂未接线的字段等），不阻断执行。</param>
    /// <exception cref="ArgumentNullException">任一引用参数为 <see langword="null"/>。</exception>
    public GitGuardConfig(
        int version,
        ScanSettings scan,
        SeverityPolicy policy,
        SecretRulesSettings secrets,
        PrivacyRulesSettings privacy,
        CategoryRulesSettings localPath,
        CategoryRulesSettings url,
        LogFileSettings logFile,
        bool logVerbose,
        IReadOnlyList<AllowlistEntry> allowlist,
        IReadOnlyList<string> loadedConfigFiles,
        IReadOnlyList<string> warnings)
    {
        ArgumentNullException.ThrowIfNull(scan);
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(secrets);
        ArgumentNullException.ThrowIfNull(privacy);
        ArgumentNullException.ThrowIfNull(localPath);
        ArgumentNullException.ThrowIfNull(url);
        ArgumentNullException.ThrowIfNull(logFile);
        ArgumentNullException.ThrowIfNull(allowlist);
        ArgumentNullException.ThrowIfNull(loadedConfigFiles);
        ArgumentNullException.ThrowIfNull(warnings);

        Version = version;
        Scan = scan;
        Policy = policy;
        Secrets = secrets;
        Privacy = privacy;
        LocalPath = localPath;
        Url = url;
        LogFile = logFile;
        LogVerbose = logVerbose;
        Allowlist = allowlist.ToImmutableArray();
        LoadedConfigFiles = loadedConfigFiles.ToImmutableArray();
        Warnings = warnings.ToImmutableArray();
    }

    /// <summary>Schema 版本（加载成功时恒为 <see cref="SUPPORTED_VERSION"/>）。</summary>
    public int Version { get; }

    /// <summary>扫描设置：<c>scan.max_file_bytes</c> / <c>scan.timeout_ms</c>。</summary>
    public ScanSettings Scan { get; }

    /// <summary>严重度 → 动作映射（已通过 Critical ≠ Allow 硬下限校验）。</summary>
    public SeverityPolicy Policy { get; }

    /// <summary>秘密检测类别设置（<c>rules.secrets</c>）。</summary>
    public SecretRulesSettings Secrets { get; }

    /// <summary>隐私检测类别设置（<c>rules.privacy</c>）。</summary>
    public PrivacyRulesSettings Privacy { get; }

    /// <summary>本地路径检测类别设置（<c>rules.local_path</c>）。</summary>
    public CategoryRulesSettings LocalPath { get; }

    /// <summary>URL 检测类别设置（<c>rules.url</c>）。</summary>
    public CategoryRulesSettings Url { get; }

    /// <summary>文件日志设置（<c>logging.file</c>）。</summary>
    public LogFileSettings LogFile { get; }

    /// <summary><c>logging.verbose</c>：默认输出 Debug 级日志。</summary>
    public bool LogVerbose { get; }

    /// <summary>配置文件声明的白名单条目；参与策略判定的「ignored_by_allowlist」匹配。</summary>
    public ImmutableArray<AllowlistEntry> Allowlist { get; }

    /// <summary>排除的路径 glob（<c>exclude.paths</c>，匹配仓库相对路径）；命中文件不进入扫描。</summary>
    public ImmutableArray<string> ExcludePaths { get; init; } = ImmutableArray<string>.Empty;

    /// <summary>排除的文件名 glob（<c>exclude.files</c>，匹配任意目录下的文件名）；命中文件不进入扫描。</summary>
    public ImmutableArray<string> ExcludeFiles { get; init; } = ImmutableArray<string>.Empty;

    /// <summary>
    /// 项目配置声明的自定义规则（<c>rules.custom</c>，FR-080）：已编译并以 <see cref="RuleSource.User"/> 标注，
    /// 与内置规则一同交给四类检测器；正则编译失败或 ID / 严重度非法在加载期 FailClosed（退出码 13）。
    /// </summary>
    public ImmutableArray<DetectionRule> CustomRules { get; init; } = ImmutableArray<DetectionRule>.Empty;

    /// <summary>实际参与合并的配置文件路径（低优先级在前）；空表示全部使用内置默认。</summary>
    public ImmutableArray<string> LoadedConfigFiles { get; }

    /// <summary>宽松解析警告（未知字段、暂未接线的字段）；不阻断执行。</summary>
    public ImmutableArray<string> Warnings { get; }
}

/// <summary>规则类别通用设置（<c>enabled</c> + <c>disabled_rules</c>）。</summary>
public sealed record CategoryRulesSettings
{
    /// <summary>默认设置：启用、无禁用规则。</summary>
    public static CategoryRulesSettings Default { get; } = new CategoryRulesSettings();

    /// <summary>是否启用该类别；<see langword="false"/> 时组合根不装配其检测器。</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>禁用的规则 ID 列表（内置规则 ID，如 <c>GGD/SECRET/AWS-AKID</c>）。</summary>
    public IReadOnlyList<string> DisabledRules { get; init; } = ImmutableArray<string>.Empty;
}

/// <summary>秘密检测类别设置（<c>rules.secrets</c>）。</summary>
public sealed record SecretRulesSettings
{
    /// <summary>默认设置：启用、无禁用规则、熵阈值 4.0（模块设计/Secret.md §7）。</summary>
    public static SecretRulesSettings Default { get; } = new SecretRulesSettings();

    /// <summary>是否启用秘密检测。</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>禁用的规则 ID 列表。</summary>
    public IReadOnlyList<string> DisabledRules { get; init; } = ImmutableArray<string>.Empty;

    /// <summary>高熵判定阈值（<c>min_entropy</c>）。</summary>
    public double MinEntropy { get; init; } = 4.0;

    /// <summary>
    /// 是否启用 GitleaksAdapter（<c>use_gitleaks</c>，FR-090）：默认关闭；
    /// 启用后启动期探测可执行文件，缺失时仅告警降级，不阻断扫描。
    /// </summary>
    public bool UseGitleaks { get; init; }

    /// <summary>
    /// Gitleaks 可执行文件路径（<c>gitleaks_path</c>）：<see langword="null"/> 表示在 <c>PATH</c> 中查找；
    /// 支持 <c>~</c> 前缀。仅在 <see cref="UseGitleaks"/> 为 <see langword="true"/> 时消费。
    /// </summary>
    public string? GitleaksPath { get; init; }
}

/// <summary>隐私检测类别设置（<c>rules.privacy</c>）。</summary>
public sealed record PrivacyRulesSettings
{
    /// <summary>默认设置：启用、要求上下文关键字、默认测试路径子串（模块设计/Privacy.md §7）。</summary>
    public static PrivacyRulesSettings Default { get; } = new PrivacyRulesSettings();

    /// <summary>是否启用隐私检测。</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>禁用的规则 ID 列表。</summary>
    public IReadOnlyList<string> DisabledRules { get; init; } = ImmutableArray<string>.Empty;

    /// <summary>是否要求上下文关键字门禁（<c>require_context</c>）。</summary>
    public bool RequireContext { get; init; } = true;

    /// <summary>测试文件路径子串（<c>test_file_patterns</c>，大小写不敏感，命中即严重度降一级）。</summary>
    public IReadOnlyList<string> TestFilePatterns { get; init; } =
        ImmutableArray.Create("test", "spec", "fixture", "mock", "sample", "example");
}

/// <summary>文件日志设置（<c>logging.file</c>，09-日志设计 §7）。</summary>
public sealed record LogFileSettings
{
    /// <summary>默认设置：关闭；路径取平台数据目录（<see cref="UserDataPaths.LogFilePath"/>），滚动阈值取 Policy.md §7 默认值。</summary>
    public static LogFileSettings Default { get; } = new LogFileSettings();

    /// <summary>是否启用文件日志（09-日志设计 §7：默认不写日志文件）。</summary>
    public bool Enabled { get; init; }

    /// <summary>日志文件路径；默认 <c>&lt;data&gt;/logs/gitguard.log</c>，<c>~</c> 前缀由日志 Sink 展开为用户主目录。</summary>
    public string Path { get; init; } = UserDataPaths.Default.LogFilePath;

    /// <summary>单文件滚动阈值（字节），默认 10 MiB。</summary>
    public long MaxBytes { get; init; } = 10L * 1024 * 1024;

    /// <summary>滚动保留份数，默认 5（09-日志设计 §8：50 MiB 封顶）。</summary>
    public int MaxFiles { get; init; } = 5;
}
