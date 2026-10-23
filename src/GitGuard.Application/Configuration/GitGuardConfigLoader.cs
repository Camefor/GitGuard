using System.Collections.Immutable;
using GitGuard.Application.Rules;
using GitGuard.Application.Scanning;
using GitGuard.Core;
using GitGuard.Core.Abstractions;
using GitGuard.Core.Domain;
using GitGuard.Core.Model;
using GitGuard.Core.Policy;
using GitGuard.Core.Rules;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace GitGuard.Application.Configuration;

/// <summary>
/// 配置文件加载器（FR-070 / FR-071 / 模块设计/Policy.md §6.2）：定位「用户级 config.yml + 项目级 .gitguard.yml」，
/// 解析 → 深度合并（项目级覆盖用户级；map 深合并、列表替换）→ 校验 → 产出不可变
/// <see cref="GitGuardConfig"/>。始终先读用户级、后读项目级；两者均不存在时返回 <see cref="GitGuardConfig.Default"/>。
/// 校验策略（DR-03 / Policy.md §3）：
/// <list type="bullet">
///   <item><description><c>version</c> 缺失或不为 <see cref="GitGuardConfig.SUPPORTED_VERSION"/> → 抛 <see cref="ConfigurationException"/>（退出码 13）。</description></item>
///   <item><description><c>policy.critical: allow</c> 违反硬下限 → 抛 <see cref="ConfigurationException"/>（退出码 13）。</description></item>
///   <item><description>未知字段、<c>fail_closed: false</c> → 宽松处理：记入 <see cref="GitGuardConfig.Warnings"/>，不阻断。</description></item>
///   <item><description><c>hooks.gitguard_executable_path</c> 为空白 → 抛 <see cref="ConfigurationException"/>（退出码 13）；文件存在性在安装期校验。</description></item>
///   <item><description><c>exclude.paths</c> / <c>exclude.files</c> 含空项 → 抛 <see cref="ConfigurationException"/>（退出码 13）。</description></item>
///   <item><description><c>rules.secrets.gitleaks_path</c> 为空白 → 抛 <see cref="ConfigurationException"/>（退出码 13）；路径存在性与版本在启动期探测。</description></item>
/// </list>
/// </summary>
public sealed class GitGuardConfigLoader
{
    /// <summary>项目级配置文件名（FR-070）。</summary>
    public const string PROJECT_CONFIG_FILE_NAME = ".gitguard.yml";

    /// <summary>用户级配置文件的相对路径（FR-071：<c>~/.config/gitguard/config.yml</c>）。</summary>
    public const string USER_CONFIG_RELATIVE_PATH = ".config/gitguard/config.yml";

    // IgnoreUnmatchedProperties：宽松解析（Policy.md §9「未知字段：警告但不阻断」）——未知键由
    // CollectUnknownKeyWarnings 记入警告，反序列化阶段必须放行，否则会在产生警告前先抛 YAML 语法错误（退出码 13）。
    private static readonly IDeserializer _deserializer =
        new StaticDeserializerBuilder(new GitGuardConfigYamlContext())
            .WithNamingConvention(UnderscoredNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();

    /// <summary>
    /// 各层级已知键（宽松解析的「未知字段 → 警告」依据，Policy.md §9）。
    /// 未列出的键不做键名校验。
    /// </summary>
    private static readonly ImmutableDictionary<string, ImmutableHashSet<string>> _knownKeys =
        new Dictionary<string, ImmutableHashSet<string>>(StringComparer.Ordinal)
        {
            [string.Empty] = ImmutableHashSet.Create(StringComparer.Ordinal, "version", "scan", "hooks", "logging", "rules", "policy", "exclude", "allowlist"),
            ["scan"] = ImmutableHashSet.Create(StringComparer.Ordinal, "pre_commit", "pre_push", "fail_closed", "max_file_bytes", "timeout_ms"),
            ["hooks"] = ImmutableHashSet.Create(StringComparer.Ordinal, "pre_commit", "pre_push", "post_commit_bypass_detection", "gitguard_executable_path"),
            ["exclude"] = ImmutableHashSet.Create(StringComparer.Ordinal, "paths", "files"),
            ["logging"] = ImmutableHashSet.Create(StringComparer.Ordinal, "verbose", "file"),
            ["logging.file"] = ImmutableHashSet.Create(StringComparer.Ordinal, "enabled", "path", "max_bytes", "max_files"),
            ["rules"] = ImmutableHashSet.Create(StringComparer.Ordinal, "secrets", "privacy", "local_path", "url", "custom"),
            ["rules.secrets"] = ImmutableHashSet.Create(StringComparer.Ordinal, "enabled", "disabled_rules", "use_gitleaks", "gitleaks_path", "min_entropy"),
            ["rules.privacy"] = ImmutableHashSet.Create(StringComparer.Ordinal, "enabled", "disabled_rules", "require_context", "test_file_patterns"),
            ["rules.local_path"] = ImmutableHashSet.Create(StringComparer.Ordinal, "enabled", "disabled_rules"),
            ["rules.url"] = ImmutableHashSet.Create(StringComparer.Ordinal, "enabled", "disabled_rules", "public_hostname_whitelist"),
            ["rules.custom[]"] = ImmutableHashSet.Create(StringComparer.Ordinal, "id", "name", "severity", "pattern", "context_keywords", "file_path_globs", "enabled"),
            ["policy"] = ImmutableHashSet.Create(StringComparer.Ordinal, "critical", "high", "medium", "low"),
            ["allowlist"] = ImmutableHashSet.Create(StringComparer.Ordinal, "entries"),
            ["allowlist.entries[]"] = ImmutableHashSet.Create(StringComparer.Ordinal, "rule_id", "path_pattern", "fingerprint", "reason"),
        }.ToImmutableDictionary(StringComparer.Ordinal);

    private readonly string _projectConfigPath;
    private readonly string _userConfigPath;
    private readonly IClock _clock;
    private readonly string _createdBy;
    private readonly string _defaultLogFilePath;

    /// <summary>初始化配置加载器。</summary>
    /// <param name="projectDirectory">项目配置所在目录（通常为仓库工作区根目录；非仓库场景为当前工作目录）。</param>
    /// <param name="userConfigPath">用户级配置文件路径；<see langword="null"/> 取 <c>~/.config/gitguard/config.yml</c>。</param>
    /// <param name="clock">时钟抽象（为配置声明的白名单条目生成 <c>created_at</c>）。</param>
    /// <param name="createdBy">白名单条目的登记人；<see langword="null"/> 取当前 OS 用户名。</param>
    /// <param name="defaultLogFilePath">
    /// <c>logging.file.path</c> 缺省时的默认日志路径；<see langword="null"/> 取 <see cref="LogFileSettings.Default"/> 的平台路径。
    /// 组合根注入 <c>&lt;data&gt;/logs/gitguard.log</c>，确保测试 / 自定义数据目录下日志落在同一数据根。
    /// </param>
    /// <exception cref="ArgumentException"><paramref name="projectDirectory"/> 为空或全空白。</exception>
    public GitGuardConfigLoader(
        string projectDirectory,
        string? userConfigPath = null,
        IClock? clock = null,
        string? createdBy = null,
        string? defaultLogFilePath = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectDirectory);

        _projectConfigPath = Path.Combine(Path.GetFullPath(projectDirectory), PROJECT_CONFIG_FILE_NAME);
        _userConfigPath = Path.GetFullPath(userConfigPath ?? ResolveDefaultUserConfigPath());
        _clock = clock ?? SystemClockFallback.Instance;
        _createdBy = string.IsNullOrWhiteSpace(createdBy) ? Environment.UserName : createdBy;
        _defaultLogFilePath = string.IsNullOrWhiteSpace(defaultLogFilePath)
            ? LogFileSettings.Default.Path
            : defaultLogFilePath;
    }

    /// <summary>项目级配置文件路径（<c>&lt;projectDirectory&gt;/.gitguard.yml</c>）。</summary>
    public string ProjectConfigPath => _projectConfigPath;

    /// <summary>用户级配置文件路径。</summary>
    public string UserConfigPath => _userConfigPath;

    /// <summary>加载并合并配置。</summary>
    /// <returns>有效配置（含加载链与宽松解析警告）。</returns>
    /// <exception cref="ConfigurationException">
    /// 文件存在但 YAML 语法错误、<c>version</c> 缺失或不受支持、字段取值非法、违反硬下限。
    /// </exception>
    public GitGuardConfig Load()
    {
        var warnings = new List<string>();
        var loadedFiles = new List<string>();

        GitGuardConfigDocument? user = ReadIfExists(_userConfigPath, loadedFiles, warnings);
        GitGuardConfigDocument? project = ReadIfExists(_projectConfigPath, loadedFiles, warnings);
        GitGuardConfigDocument merged = Merge(user, project);

        return Build(merged, loadedFiles, warnings, _clock.UtcNow, _createdBy, _defaultLogFilePath);
    }

    private static string ResolveDefaultUserConfigPath()
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrWhiteSpace(home))
        {
            throw new InvalidOperationException("无法解析当前用户的主目录路径（用户级配置定位失败）。");
        }

        return Path.Combine(home, ".config", "gitguard", "config.yml");
    }

    /// <summary>读取并校验单个配置文件；不存在时返回 <see langword="null"/>（FR-070「不存在时使用内置默认」）。</summary>
    private static GitGuardConfigDocument? ReadIfExists(string path, List<string> loadedFiles, List<string> warnings)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        string yaml;
        try
        {
            yaml = File.ReadAllText(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new ConfigurationException($"配置文件读取失败：{ex.Message}", ex, path, line: null);
        }

        GitGuardConfigDocument? document;
        try
        {
            document = _deserializer.Deserialize<GitGuardConfigDocument?>(yaml);
        }
        catch (YamlException ex)
        {
            throw new ConfigurationException(
                $"配置文件 YAML 语法错误：{ex.Message}",
                ex,
                path,
                (int)ex.Start.Line);
        }

        if (document is null)
        {
            throw new ConfigurationException("配置文件为空：至少需要 version: 1", path);
        }

        if (document.Version != GitGuardConfig.SUPPORTED_VERSION)
        {
            throw new ConfigurationException(
                document.Version is null
                    ? $"配置文件缺少 version 字段（必须为 version: {GitGuardConfig.SUPPORTED_VERSION}）"
                    : $"配置文件版本不受支持：{document.Version}（本版本仅支持 {GitGuardConfig.SUPPORTED_VERSION}）",
                path);
        }

        CollectUnknownKeyWarnings(yaml, path, warnings);
        loadedFiles.Add(path);
        return document;
    }

    /// <summary>深度合并：项目级（<paramref name="project"/>）覆盖用户级（<paramref name="user"/>）；列表整体替换、map 递归合并。</summary>
    private static GitGuardConfigDocument Merge(GitGuardConfigDocument? user, GitGuardConfigDocument? project)
    {
        if (user is null)
        {
            return project ?? new GitGuardConfigDocument();
        }

        if (project is null)
        {
            return user;
        }

        return new GitGuardConfigDocument
        {
            Version = project.Version ?? user.Version,
            Scan = MergeScan(user.Scan, project.Scan),
            Hooks = MergeHooks(user.Hooks, project.Hooks),
            Logging = MergeLogging(user.Logging, project.Logging),
            Rules = MergeRules(user.Rules, project.Rules),
            Policy = MergePolicy(user.Policy, project.Policy),
            Exclude = MergeExclude(user.Exclude, project.Exclude),
            Allowlist = MergeAllowlist(user.Allowlist, project.Allowlist),
        };
    }

    private static ScanSectionDto? MergeScan(ScanSectionDto? user, ScanSectionDto? project) => BothNull(user, project)
        ? null
        : new ScanSectionDto
        {
            PreCommit = project?.PreCommit ?? user?.PreCommit,
            PrePush = project?.PrePush ?? user?.PrePush,
            FailClosed = project?.FailClosed ?? user?.FailClosed,
            MaxFileBytes = project?.MaxFileBytes ?? user?.MaxFileBytes,
            TimeoutMs = project?.TimeoutMs ?? user?.TimeoutMs,
        };

    private static HooksSectionDto? MergeHooks(HooksSectionDto? user, HooksSectionDto? project) => BothNull(user, project)
        ? null
        : new HooksSectionDto
        {
            PreCommit = project?.PreCommit ?? user?.PreCommit,
            PrePush = project?.PrePush ?? user?.PrePush,
            PostCommitBypassDetection = project?.PostCommitBypassDetection ?? user?.PostCommitBypassDetection,
            GitguardExecutablePath = project?.GitguardExecutablePath ?? user?.GitguardExecutablePath,
        };

    private static LoggingSectionDto? MergeLogging(LoggingSectionDto? user, LoggingSectionDto? project) => BothNull(user, project)
        ? null
        : new LoggingSectionDto
        {
            Verbose = project?.Verbose ?? user?.Verbose,
            File = BothNull(user?.File, project?.File)
                ? null
                : new LogFileSectionDto
                {
                    Enabled = project?.File?.Enabled ?? user?.File?.Enabled,
                    Path = project?.File?.Path ?? user?.File?.Path,
                    MaxBytes = project?.File?.MaxBytes ?? user?.File?.MaxBytes,
                    MaxFiles = project?.File?.MaxFiles ?? user?.File?.MaxFiles,
                },
        };

    private static RulesSectionDto? MergeRules(RulesSectionDto? user, RulesSectionDto? project) => BothNull(user, project)
        ? null
        : new RulesSectionDto
        {
            Secrets = BothNull(user?.Secrets, project?.Secrets)
                ? null
                : new SecretRulesSectionDto
                {
                    Enabled = project?.Secrets?.Enabled ?? user?.Secrets?.Enabled,
                    DisabledRules = project?.Secrets?.DisabledRules ?? user?.Secrets?.DisabledRules,
                    UseGitleaks = project?.Secrets?.UseGitleaks ?? user?.Secrets?.UseGitleaks,
                    GitleaksPath = project?.Secrets?.GitleaksPath ?? user?.Secrets?.GitleaksPath,
                    MinEntropy = project?.Secrets?.MinEntropy ?? user?.Secrets?.MinEntropy,
                },
            Privacy = BothNull(user?.Privacy, project?.Privacy)
                ? null
                : new PrivacyRulesSectionDto
                {
                    Enabled = project?.Privacy?.Enabled ?? user?.Privacy?.Enabled,
                    DisabledRules = project?.Privacy?.DisabledRules ?? user?.Privacy?.DisabledRules,
                    RequireContext = project?.Privacy?.RequireContext ?? user?.Privacy?.RequireContext,
                    TestFilePatterns = project?.Privacy?.TestFilePatterns ?? user?.Privacy?.TestFilePatterns,
                },
            LocalPath = BothNull(user?.LocalPath, project?.LocalPath)
                ? null
                : new RuleCategorySectionDto
                {
                    Enabled = project?.LocalPath?.Enabled ?? user?.LocalPath?.Enabled,
                    DisabledRules = project?.LocalPath?.DisabledRules ?? user?.LocalPath?.DisabledRules,
                },
            Url = BothNull(user?.Url, project?.Url)
                ? null
                : new UrlRulesSectionDto
                {
                    Enabled = project?.Url?.Enabled ?? user?.Url?.Enabled,
                    DisabledRules = project?.Url?.DisabledRules ?? user?.Url?.DisabledRules,
                    PublicHostnameWhitelist = project?.Url?.PublicHostnameWhitelist ?? user?.Url?.PublicHostnameWhitelist,
                },

            // 列表替换语义：项目级声明 custom 即整体接管用户级
            Custom = project?.Custom ?? user?.Custom,
        };

    private static PolicySectionDto? MergePolicy(PolicySectionDto? user, PolicySectionDto? project) => BothNull(user, project)
        ? null
        : new PolicySectionDto
        {
            Critical = project?.Critical ?? user?.Critical,
            High = project?.High ?? user?.High,
            Medium = project?.Medium ?? user?.Medium,
            Low = project?.Low ?? user?.Low,
        };

    private static ExcludeSectionDto? MergeExclude(ExcludeSectionDto? user, ExcludeSectionDto? project) => BothNull(user, project)
        ? null
        : new ExcludeSectionDto
        {
            Paths = project?.Paths ?? user?.Paths,
            Files = project?.Files ?? user?.Files,
        };

    private static AllowlistSectionDto? MergeAllowlist(AllowlistSectionDto? user, AllowlistSectionDto? project) => BothNull(user, project)
        ? null
        : new AllowlistSectionDto
        {
            // 列表替换语义（Policy.md §3「配置文件合并」）：项目级声明 entries 即整体接管
            Entries = project?.Entries ?? user?.Entries,
        };

    private static bool BothNull(object? user, object? project) => user is null && project is null;

    /// <summary>把合并后的 DTO 映射为有效配置，并执行全部取值校验（DR-03）。</summary>
    private static GitGuardConfig Build(
        GitGuardConfigDocument merged,
        List<string> loadedFiles,
        List<string> warnings,
        DateTimeOffset now,
        string createdBy,
        string defaultLogFilePath)
    {
        ScanSectionDto scan = merged.Scan ?? new ScanSectionDto();
        if (scan.FailClosed == false)
        {
            warnings.Add("scan.fail_closed 已设置为 false；MVP 行为恒为 FailClosed（DR-03），该设置暂不生效。");
        }

        var scanSettings = new ScanSettings
        {
            PreCommit = scan.PreCommit ?? true,
            PrePush = scan.PrePush ?? true,
            MaxFileBytes = scan.MaxFileBytes ?? ScanSettings.DEFAULT_MAX_FILE_BYTES,
            Timeout = TimeSpan.FromMilliseconds(scan.TimeoutMs ?? (long)ScanSettings.DEFAULT_TIMEOUT.TotalMilliseconds),
        };
        ValidateScanSettings(scanSettings);
        HooksSettings hooks = BuildHooks(merged.Hooks);

        RulesSectionDto rules = merged.Rules ?? new RulesSectionDto();
        SecretRulesSettings secrets = BuildSecrets(rules.Secrets, warnings);
        PrivacyRulesSettings privacy = BuildPrivacy(rules.Privacy);

        LogFileSettings logFile = BuildLogFile(merged.Logging, defaultLogFilePath);
        if (merged.Logging?.Verbose == true)
        {
            warnings.Add("logging.verbose 已启用：默认以 Debug 级输出（等价于常带 --verbose）。");
        }

        SeverityPolicy policy = BuildPolicy(merged.Policy);
        ImmutableArray<AllowlistEntry> allowlist = BuildAllowlist(merged.Allowlist, now, createdBy);
        var customRules = BuildCustomRules(rules.Custom);
        ImmutableArray<string> excludePaths = NormalizeList(merged.Exclude?.Paths, "exclude.paths");
        ImmutableArray<string> excludeFiles = NormalizeList(merged.Exclude?.Files, "exclude.files");

        return new GitGuardConfig(
            GitGuardConfig.SUPPORTED_VERSION,
            scanSettings,
            policy,
            secrets,
            privacy,
            BuildCategoryRules(rules.LocalPath),
            BuildUrlRules(rules.Url),
            logFile,
            merged.Logging?.Verbose ?? false,
            allowlist,
            loadedFiles,
            warnings)
        {
            ExcludePaths = excludePaths,
            ExcludeFiles = excludeFiles,
            CustomRules = customRules,
            Hooks = hooks,
        };
    }

    /// <summary>映射 <c>hooks</c> 段落；空白可执行文件路径 → <see cref="ConfigurationException"/>（退出码 13）。</summary>
    private static HooksSettings BuildHooks(HooksSectionDto? dto)
    {
        string? executablePath = null;
        if (dto?.GitguardExecutablePath is { } rawPath)
        {
            if (string.IsNullOrWhiteSpace(rawPath))
            {
                throw new ConfigurationException(
                    "hooks.gitguard_executable_path 不能为空字符串；省略该字段表示自动探测当前可执行文件");
            }

            executablePath = rawPath.Trim();
        }

        return new HooksSettings
        {
            PreCommit = dto?.PreCommit ?? true,
            PrePush = dto?.PrePush ?? true,
            PostCommitBypassDetection = dto?.PostCommitBypassDetection ?? true,
            ExecutablePath = executablePath,
        };
    }

    private static void ValidateScanSettings(ScanSettings settings)
    {
        if (settings.MaxFileBytes <= 0)
        {
            throw new ConfigurationException($"scan.max_file_bytes 必须为正整数（当前 {settings.MaxFileBytes}）");
        }

        if (settings.Timeout <= TimeSpan.Zero)
        {
            throw new ConfigurationException($"scan.timeout_ms 必须为正整数（当前 {settings.Timeout.TotalMilliseconds:0}）");
        }
    }

    private static SecretRulesSettings BuildSecrets(SecretRulesSectionDto? dto, List<string> warnings)
    {
        if (dto?.MinEntropy is { } entropy && (double.IsNaN(entropy) || double.IsInfinity(entropy) || entropy < 0))
        {
            throw new ConfigurationException($"rules.secrets.min_entropy 必须为非负有限数（当前 {dto.MinEntropy}）");
        }

        if (dto?.MinEntropy == 0)
        {
            warnings.Add("rules.secrets.min_entropy 为 0：高熵门禁形同关闭，命中量与误报都会上升。");
        }

        string? gitleaksPath = null;
        if (dto?.GitleaksPath is { } rawPath)
        {
            if (string.IsNullOrWhiteSpace(rawPath))
            {
                throw new ConfigurationException("rules.secrets.gitleaks_path 不能为空字符串；省略该字段表示在 PATH 中查找 gitleaks");
            }

            gitleaksPath = rawPath.Trim();
        }

        return new SecretRulesSettings
        {
            Enabled = dto?.Enabled ?? true,
            DisabledRules = NormalizeList(dto?.DisabledRules, "rules.secrets.disabled_rules"),
            MinEntropy = dto?.MinEntropy ?? SecretRulesSettings.Default.MinEntropy,
            UseGitleaks = dto?.UseGitleaks ?? SecretRulesSettings.Default.UseGitleaks,
            GitleaksPath = gitleaksPath,
        };
    }

    private static PrivacyRulesSettings BuildPrivacy(PrivacyRulesSectionDto? dto) => new PrivacyRulesSettings
    {
        Enabled = dto?.Enabled ?? true,
        DisabledRules = NormalizeList(dto?.DisabledRules, "rules.privacy.disabled_rules"),
        RequireContext = dto?.RequireContext ?? true,
        TestFilePatterns = dto?.TestFilePatterns is null
            ? PrivacyRulesSettings.Default.TestFilePatterns
            : NormalizeList(dto.TestFilePatterns, "rules.privacy.test_file_patterns"),
    };

    private static CategoryRulesSettings BuildCategoryRules(RuleCategorySectionDto? dto) => new CategoryRulesSettings
    {
        Enabled = dto?.Enabled ?? true,
        DisabledRules = NormalizeList(dto?.DisabledRules, "rules.*.disabled_rules"),
    };

    /// <summary>映射 <c>rules.url</c>：白名单项须为主机名（精确或 <c>*.example.com</c>）→ 否则 13。</summary>
    private static UrlRulesSettings BuildUrlRules(UrlRulesSectionDto? dto) => new UrlRulesSettings
    {
        Enabled = dto?.Enabled ?? true,
        DisabledRules = NormalizeList(dto?.DisabledRules, "rules.url.disabled_rules"),
        PublicHostnameWhitelist = NormalizeHostnameWhitelist(dto?.PublicHostnameWhitelist),
    };

    /// <summary>
    /// 规范化公共主机名白名单：去空白 / 末尾根点、转小写、去重；
    /// 仅接受精确主机名或 <c>*.example.com</c> 通配，协议 / 路径 / 端口 / 空白等一律 FailClosed（退出码 13）。
    /// </summary>
    private static ImmutableArray<string> NormalizeHostnameWhitelist(List<string>? values)
    {
        if (values is null || values.Count == 0)
        {
            return ImmutableArray<string>.Empty;
        }

        var builder = ImmutableArray.CreateBuilder<string>(values.Count);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (string value in values)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ConfigurationException("rules.url.public_hostname_whitelist 含空项；列表项必须为主机名");
            }

            string host = value.Trim().TrimEnd('.').ToLowerInvariant();
            string candidate = host.StartsWith("*.", StringComparison.Ordinal) ? host[2..] : host;
            if (!IsValidHostname(candidate))
            {
                throw new ConfigurationException(
                    $"rules.url.public_hostname_whitelist 含非法主机名「{value}」；仅支持精确主机名或 *.example.com 通配，"
                    + "不得包含协议、路径、端口或空白");
            }

            if (seen.Add(host))
            {
                builder.Add(host);
            }
        }

        return builder.ToImmutable();
    }

    private static bool IsValidHostname(string candidate)
    {
        if (candidate.Length is 0 or > 253)
        {
            return false;
        }

        if (candidate[0] == '.' || candidate[^1] == '.' || candidate.Contains("..", StringComparison.Ordinal))
        {
            return false;
        }

        foreach (char c in candidate)
        {
            if (!char.IsAsciiLetterOrDigit(c) && c != '-' && c != '.')
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// 编译 <c>rules.custom</c> 声明的自定义规则（FR-080）：ID 必须为 <c>USR/</c> 前缀且类别段属于四类检测器，
    /// 严重度 / 正则 / 列表项在加载期校验；任一非法项 → <see cref="ConfigurationException"/>（退出码 13，FailClosed）。
    /// </summary>
    private static ImmutableArray<DetectionRule> BuildCustomRules(List<CustomRuleSectionDto>? dtos)
    {
        if (dtos is null || dtos.Count == 0)
        {
            return ImmutableArray<DetectionRule>.Empty;
        }

        var rules = ImmutableArray.CreateBuilder<DetectionRule>(dtos.Count);
        var seenIds = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < dtos.Count; i++)
        {
            DetectionRule rule = BuildCustomRule(dtos[i], i);
            if (!seenIds.Add(rule.RuleId))
            {
                throw new ConfigurationException($"rules.custom[{i}] 的规则 ID 重复：{rule.RuleId}");
            }

            rules.Add(rule);
        }

        return rules.ToImmutable();
    }

    private static DetectionRule BuildCustomRule(CustomRuleSectionDto dto, int index)
    {
        string location = $"rules.custom[{index}]";
        if (string.IsNullOrWhiteSpace(dto.Id))
        {
            throw new ConfigurationException($"{location} 缺少 id 字段");
        }

        string ruleId = dto.Id.Trim();
        if (!ruleId.StartsWith("USR/", StringComparison.Ordinal))
        {
            throw new ConfigurationException($"{location} 的规则 ID 必须以 USR/ 开头：{ruleId}");
        }

        if (!RuleIdFormat.IsValid(ruleId))
        {
            throw new ConfigurationException($"{location} 的规则 ID 不符合命名契约：{ruleId}");
        }

        string category = RuleSelection.CategoryOf(ruleId);
        if (!RuleSelection.SupportedCategories.Contains(category))
        {
            throw new ConfigurationException(
                $"{location} 的规则类别不受支持：{category}（应为 SECRET / PRIVACY / LOCALPATH / URL）");
        }

        if (string.IsNullOrWhiteSpace(dto.Name))
        {
            throw new ConfigurationException($"{location} 缺少 name 字段");
        }

        if (!Enum.TryParse(dto.Severity, ignoreCase: true, out Severity severity) || !Enum.IsDefined(severity))
        {
            throw new ConfigurationException(
                $"{location} 的 severity 非法：{dto.Severity ?? "(缺失)"}（应为 Low/Medium/High/Critical）");
        }

        if (string.IsNullOrWhiteSpace(dto.Pattern))
        {
            throw new ConfigurationException($"{location} 缺少 pattern 字段");
        }

        try
        {
            var pattern = RulePattern.Create(
                dto.Pattern,
                NormalizeList(dto.ContextKeywords, $"{location}.context_keywords"),
                NormalizeList(dto.FilePathGlobs, $"{location}.file_path_globs"));

            return DetectionRule.Create(
                ruleId,
                dto.Name.Trim(),
                severity,
                pattern,
                RuleSource.User,
                dto.Enabled ?? true);
        }
        catch (InvariantViolationException ex)
        {
            throw new ConfigurationException($"{location} 校验失败：{ex.Message}", ex, sourcePath: null, line: null);
        }
    }

    private static LogFileSettings BuildLogFile(LoggingSectionDto? logging, string defaultLogFilePath)
    {
        LogFileSectionDto? dto = logging?.File;
        var settings = new LogFileSettings
        {
            Enabled = dto?.Enabled ?? LogFileSettings.Default.Enabled,
            Path = string.IsNullOrWhiteSpace(dto?.Path) ? defaultLogFilePath : dto.Path,
            MaxBytes = dto?.MaxBytes ?? LogFileSettings.Default.MaxBytes,
            MaxFiles = dto?.MaxFiles ?? LogFileSettings.Default.MaxFiles,
        };

        if (settings.MaxBytes <= 0)
        {
            throw new ConfigurationException($"logging.file.max_bytes 必须为正整数（当前 {settings.MaxBytes}）");
        }

        if (settings.MaxFiles < 1)
        {
            throw new ConfigurationException($"logging.file.max_files 必须 ≥ 1（当前 {settings.MaxFiles}）");
        }

        return settings;
    }

    private static SeverityPolicy BuildPolicy(PolicySectionDto? dto)
    {
        if (dto is null)
        {
            return SeverityPolicy.Default;
        }

        var mapping = new Dictionary<Severity, PolicyAction>(SeverityPolicy.Default.Mapping);
        SetAction(mapping, Severity.Critical, dto.Critical, "policy.critical");
        SetAction(mapping, Severity.High, dto.High, "policy.high");
        SetAction(mapping, Severity.Medium, dto.Medium, "policy.medium");
        SetAction(mapping, Severity.Low, dto.Low, "policy.low");

        try
        {
            return new SeverityPolicy(mapping);
        }
        catch (InvariantViolationException ex)
        {
            // 硬下限（Critical ≠ Allow）与取值校验在 Core 域内强制，此处转换为配置错误（退出码 13）
            throw new ConfigurationException($"policy 段落校验失败：{ex.Message}", ex, sourcePath: null, line: null);
        }
    }

    private static void SetAction(Dictionary<Severity, PolicyAction> mapping, Severity severity, string? value, string key)
    {
        if (value is null)
        {
            return;
        }

        if (!Enum.TryParse(value, ignoreCase: true, out PolicyAction action) || !Enum.IsDefined(action))
        {
            throw new ConfigurationException(
                $"{key} 取值非法：{value}（应为 block / warn / allow）");
        }

        mapping[severity] = action;
    }

    private static ImmutableArray<AllowlistEntry> BuildAllowlist(AllowlistSectionDto? dto, DateTimeOffset now, string createdBy)
    {
        if (dto?.Entries is null || dto.Entries.Count == 0)
        {
            return ImmutableArray<AllowlistEntry>.Empty;
        }

        var entries = ImmutableArray.CreateBuilder<AllowlistEntry>(dto.Entries.Count);
        for (int i = 0; i < dto.Entries.Count; i++)
        {
            entries.Add(BuildAllowlistEntry(dto.Entries[i], i, now, createdBy));
        }

        return entries.ToImmutable();
    }

    private static AllowlistEntry BuildAllowlistEntry(AllowlistEntryDto dto, int index, DateTimeOffset now, string createdBy)
    {
        string location = $"allowlist.entries[{index}]";
        try
        {
            Fingerprint? fingerprint = null;
            if (!string.IsNullOrWhiteSpace(dto.Fingerprint))
            {
                fingerprint = Fingerprint.FromHex(dto.Fingerprint.Trim());
            }

            return AllowlistEntry.Create(
                string.IsNullOrWhiteSpace(dto.RuleId) ? null : dto.RuleId.Trim(),
                string.IsNullOrWhiteSpace(dto.PathPattern) ? null : dto.PathPattern,
                fingerprint,
                dto.Reason ?? string.Empty,
                now,
                createdBy);
        }
        catch (InvariantViolationException ex)
        {
            throw new ConfigurationException($"{location} 校验失败：{ex.Message}", ex, sourcePath: null, line: null);
        }
    }

    private static ImmutableArray<string> NormalizeList(List<string>? values, string key)
    {
        if (values is null || values.Count == 0)
        {
            return ImmutableArray<string>.Empty;
        }

        var builder = ImmutableArray.CreateBuilder<string>(values.Count);
        foreach (string value in values)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ConfigurationException($"{key} 含空项；列表项必须为非空字符串");
            }

            builder.Add(value.Trim());
        }

        return builder.ToImmutable();
    }

    /// <summary>
    /// 未知键扫描（Policy.md §9「未知字段：警告但不阻断」）：以原始 YAML 语法树逐层比对已知键集合，
    /// 仅对 <see cref="_knownKeys"/> 覆盖的层级做校验（更深的自定义结构不做判定）。
    /// </summary>
    private static void CollectUnknownKeyWarnings(string yaml, string path, List<string> warnings)
    {
        var stream = new YamlStream();
        try
        {
            stream.Load(new StringReader(yaml));
        }
        catch (YamlException)
        {
            // 语法错误已在反序列化阶段拦截；此处不重复报错
            return;
        }

        if (stream.Documents.Count == 0 || stream.Documents[0].RootNode is not YamlMappingNode root)
        {
            return;
        }

        VisitMapping(root, string.Empty, path, warnings);
    }

    private static void VisitMapping(YamlMappingNode mapping, string path, string sourcePath, List<string> warnings)
    {
        if (_knownKeys.TryGetValue(path, out ImmutableHashSet<string>? known))
        {
            foreach (YamlNode key in mapping.Children.Keys)
            {
                string keyText = key.ToString();
                if (!known.Contains(keyText))
                {
                    warnings.Add($"{sourcePath}：未知配置项 {FormatPath(path, keyText)}（宽松解析：已忽略）");
                }
            }
        }

        foreach (KeyValuePair<YamlNode, YamlNode> child in mapping.Children)
        {
            if (child.Value is YamlMappingNode childMapping)
            {
                VisitMapping(childMapping, FormatPath(path, child.Key.ToString()), sourcePath, warnings);
            }
            else if (child.Value is YamlSequenceNode childSequence)
            {
                VisitSequence(childSequence, FormatPath(path, child.Key.ToString()), sourcePath, warnings);
            }
        }
    }

    private static void VisitSequence(YamlSequenceNode sequence, string path, string sourcePath, List<string> warnings)
    {
        foreach (YamlNode item in sequence.Children)
        {
            if (item is YamlMappingNode itemMapping)
            {
                VisitMapping(itemMapping, path + "[]", sourcePath, warnings);
            }
        }
    }

    private static string FormatPath(string parent, string key) => parent.Length == 0 ? key : $"{parent}.{key}";

    /// <summary>
    /// 无 IClock 注入时的取时兜底：配置加载只需一个 UTC 时间戳，且该时间不参与业务判定
    /// （条目 <c>created_at</c> 仅作留痕）；正式运行由组合根注入 <c>SystemClock</c>。
    /// </summary>
    private sealed class SystemClockFallback : IClock
    {
        /// <summary>共享实例。</summary>
        public static SystemClockFallback Instance { get; } = new SystemClockFallback();

        /// <inheritdoc />
        public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
    }
}
