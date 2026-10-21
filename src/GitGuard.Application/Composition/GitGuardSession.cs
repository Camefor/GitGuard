using System.Collections.Immutable;
using System.Reflection;
using GitGuard.Application.Bypass;
using GitGuard.Application.Configuration;
using GitGuard.Application.Hooks;
using GitGuard.Application.Scanning;
using GitGuard.Core;
using GitGuard.Core.Abstractions;
using GitGuard.Core.Services;
using GitGuard.Detection.Detectors;
using GitGuard.Detection.Rules;
using GitGuard.Infrastructure.FileSystem;
using GitGuard.Infrastructure.Git;
using GitGuard.Infrastructure.Security;
using GitGuard.Infrastructure.Sqlite;
using GitGuard.Infrastructure.Time;

namespace GitGuard.Application.Composition;

/// <summary>
/// 组合根（SAD §5.1 / ADR-0001）：把配置加载、Git 访问、审计存储、指纹器与检测器装配为可执行的用例集，
/// 使 CLI 层无需引用 Infrastructure 即可完成全部命令；路径等对 CLI 可见的数据一律以纯 string / Core 类型暴露。
/// 依赖装配全部惰性：各命令只付出其所需子系统的初始化代价，FailClosed 失败也只在真正使用该子系统的命令中暴露
/// （例如 <c>version</c> 不触碰仓库与配置，非 Git 仓库只影响 scan / install / uninstall / status）。
/// 进程生命周期内配置不可变（07-缓存设计 §4）：Session 持有首读快照，配置改动需重启进程。
/// </summary>
public sealed class GitGuardSession
{
    private readonly GitGuardSessionOptions _options;
    private readonly IClock _clock;
    private readonly ISecretRandom _secretRandom;
    private readonly UserDataPaths _paths;

    private readonly Lazy<GitGuardConfig> _config;
    private readonly Lazy<IGitAccess> _gitAccess;
    private readonly Lazy<IAuditStore> _auditStore;
    private readonly Lazy<FingerprintSaltLoadResult> _salt;
    private readonly Lazy<DetectorSet> _detectors;
    private readonly Lazy<ImmutableArray<string>> _warnings;
    private readonly Lazy<GitVersionProbeResult> _gitProbe;

    // 检测器装配期间填充（Lazy 的执行一次语义保证无并发写）
    private readonly List<string> _ruleWarnings = new List<string>();

    /// <summary>初始化组合根。</summary>
    /// <param name="options">外部注入点（工作目录 / git 可执行文件 / 数据目录 / 版本）；<see langword="null"/> 取默认值。</param>
    /// <param name="clock">时钟抽象；<see langword="null"/> 取系统时钟。</param>
    /// <param name="secretRandom">密码学随机源（指纹盐）；<see langword="null"/> 取系统实现。</param>
    /// <exception cref="ArgumentException">工作目录或 git 可执行文件为空或全空白。</exception>
    public GitGuardSession(
        GitGuardSessionOptions? options = null,
        IClock? clock = null,
        ISecretRandom? secretRandom = null)
    {
        _options = options ?? new GitGuardSessionOptions();
        ArgumentException.ThrowIfNullOrWhiteSpace(_options.WorkingDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(_options.GitExecutable);

        _clock = clock ?? new SystemClock();
        _secretRandom = secretRandom ?? SystemSecretRandom.Instance;
        _paths = new UserDataPaths(_options.DataDirectory, _options.UserConfigDirectory);
        Version = string.IsNullOrWhiteSpace(_options.GitGuardVersion) ? ResolveDefaultVersion() : _options.GitGuardVersion;

        _config = new Lazy<GitGuardConfig>(LoadConfig);
        _gitAccess = new Lazy<IGitAccess>(CreateGitAccess);
        _auditStore = new Lazy<IAuditStore>(CreateAuditStore);
        _salt = new Lazy<FingerprintSaltLoadResult>(CreateSalt);
        _detectors = new Lazy<DetectorSet>(CreateDetectors);
        _warnings = new Lazy<ImmutableArray<string>>(CollectWarnings);
        _gitProbe = new Lazy<GitVersionProbeResult>(() => GitVersionProbe.Probe(_options.GitExecutable));
    }

    /// <summary>内置规则库版本（<c>gitguard version</c> 展示用）。</summary>
    public static int RulesVersion => BuiltinRuleLoader.SUPPORTED_VERSION;

    /// <summary>GitGuard 版本（SemVer；写入 Hook 标记行、审计 <c>gitguard_version</c> 与 <c>version</c> 命令输出）。</summary>
    public string Version { get; }

    /// <summary>用户数据目录根路径。</summary>
    public string UserDataDirectory => _paths.DataDirectory;

    /// <summary>用户级配置文件路径（FR-071：<c>~/.config/gitguard/config.yml</c>）。</summary>
    public string UserConfigPath => _paths.UserConfigPath;

    /// <summary>崩溃日志目录（退出码 99 时写入脱敏崩溃日志，10-异常处理设计 §8）。</summary>
    public string CrashLogDirectory => _paths.CrashDirectory;

    /// <summary>有效配置（三层合并 + 硬下限校验）；首次访问触发加载，失败抛 <see cref="ConfigurationException"/>。</summary>
    public GitGuardConfig Config => _config.Value;

    /// <summary>Git 访问；首次访问时在 <see cref="GitGuardSessionOptions.WorkingDirectory"/> 预检仓库有效性。</summary>
    public IGitAccess GitAccess => _gitAccess.Value;

    /// <summary>审计存储；首次访问时打开（或创建并迁移）<c>audit.db</c>。</summary>
    public IAuditStore AuditStore => _auditStore.Value;

    /// <summary>
    /// 本次配置下参与评估的启用规则总数（扫描报告 <c>summary.rules_evaluated</c> 的来源）；
    /// 首次访问触发检测器装配（与 <see cref="Warnings"/> 同源）。
    /// </summary>
    public int RuleCount => _detectors.Value.RulesEvaluated;

    /// <summary>本机 git 版本行（如 <c>git version 2.43.0</c>）；git 不可用时为 <see langword="null"/>。</summary>
    public string? GitVersionLine => _gitProbe.Value.VersionLine;

    /// <summary>git 不可用时的失败原因；可用时为 <see langword="null"/>。</summary>
    public string? GitFailureMessage => _gitProbe.Value.FailureMessage;

    /// <summary>
    /// 本次运行的全部非致命警告，按产生顺序排列：配置宽松解析警告 → 规则选择警告（<c>disabled_rules</c> 未知 ID）
    /// → 指纹盐降级警告。首次读取会触发配置加载、检测器装配与指纹盐读取；只需配置层警告的命令
    /// （install / uninstall / status）应改读 <see cref="GitGuardConfig.Warnings"/>。
    /// </summary>
    public ImmutableArray<string> Warnings => _warnings.Value;

    /// <summary>
    /// 展开路径的 <c>~</c> 前缀为用户主目录（<c>logging.file.path</c> / <c>--log-file</c>）；
    /// 无前缀或主目录不可解析时原样返回，不做相对路径解析。
    /// </summary>
    /// <param name="path">原始路径。</param>
    /// <returns>展开后的路径。</returns>
    /// <exception cref="ArgumentException"><paramref name="path"/> 为空或全空白。</exception>
    public static string ExpandUserPath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        // 仅展开 "~" 与 "~/"、"~\"；"~user" 形式不做支持（无对应语义）
        if (path[0] != '~' || (path.Length > 1 && path[1] != '/' && path[1] != '\\'))
        {
            return path;
        }

        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrWhiteSpace(home))
        {
            return path;
        }

        return path.Length == 1 ? home : Path.Combine(home, path.Substring(2));
    }

    /// <summary>解析本程序集版本（保留 SemVer 主体，剥离 <c>+build</c> 元数据）；不可得时回退 <c>0.0.0</c>。</summary>
    /// <returns>版本字符串。</returns>
    public static string ResolveDefaultVersion()
    {
        Assembly assembly = typeof(GitGuardSession).Assembly;

        string? informational = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(informational))
        {
            int metadataIndex = informational.IndexOf('+', StringComparison.Ordinal);
            return metadataIndex > 0 ? informational.Substring(0, metadataIndex) : informational;
        }

        Version? version = assembly.GetName().Version;
        return version is null ? "0.0.0" : version.ToString();
    }

    /// <summary>装配暂存区扫描用例（扫描设置与策略映射全部来自有效配置）。</summary>
    /// <returns>已接线配置的暂存区扫描用例。</returns>
    /// <exception cref="ConfigurationException">配置无效（解析失败 / 违反硬下限 / 类别全禁）。</exception>
    /// <exception cref="GitException">当前工作目录不构成 Git 仓库。</exception>
    /// <exception cref="SqliteException">审计数据库无法打开或迁移。</exception>
    public ScanStagedUseCase CreateStagedScan()
    {
        GitGuardConfig config = Config;
        return new ScanStagedUseCase(GitAccess, _detectors.Value.Detectors, AuditStore, _clock, config.Scan, BuildScanPolicy(config));
    }

    /// <summary>装配推送扫描用例（扫描设置与策略映射全部来自有效配置）。</summary>
    /// <returns>已接线配置的推送扫描用例。</returns>
    /// <exception cref="ConfigurationException">配置无效（解析失败 / 违反硬下限 / 类别全禁）。</exception>
    /// <exception cref="GitException">当前工作目录不构成 Git 仓库。</exception>
    /// <exception cref="SqliteException">审计数据库无法打开或迁移。</exception>
    public ScanPushUseCase CreatePushScan()
    {
        GitGuardConfig config = Config;
        return new ScanPushUseCase(GitAccess, _detectors.Value.Detectors, AuditStore, _clock, config.Scan, BuildScanPolicy(config));
    }

    /// <summary>装配待提交预测记录器（FR-050：pre-commit 扫描通过 / 按 GITGUARD_SKIP 跳过后记录）。</summary>
    /// <returns>已接线的记录器（不触发审计库打开，标记存储按当前工作树隔离）。</returns>
    /// <exception cref="GitException">当前工作目录不构成 Git 仓库。</exception>
    public PendingScanRecorder CreatePendingScanRecorder() =>
        new PendingScanRecorder(GitAccess, CreatePendingScanStore(), _clock);

    /// <summary>装配绕过检测服务（FR-050：post-commit 比对预测并登记绕过事件）。</summary>
    /// <returns>已接线的绕过检测服务（标记存储按当前工作树隔离）。</returns>
    /// <exception cref="GitException">当前工作目录不构成 Git 仓库。</exception>
    /// <exception cref="SqliteException">审计数据库无法打开或迁移。</exception>
    public BypassDetectionService CreateBypassDetection() =>
        new BypassDetectionService(GitAccess, AuditStore, CreatePendingScanStore(), _clock);

    /// <summary>装配 Hook 安装用例。</summary>
    /// <returns>已接线的安装用例（版本取 <see cref="Version"/>）。</returns>
    /// <exception cref="GitException">当前工作目录不构成 Git 仓库。</exception>
    /// <exception cref="SqliteException">审计数据库无法打开或迁移。</exception>
    public InstallHookUseCase CreateInstall() => new InstallHookUseCase(GitAccess, AuditStore, _clock, Version);

    /// <summary>装配 Hook 卸载用例。</summary>
    /// <returns>已接线的卸载用例（版本取 <see cref="Version"/>）。</returns>
    /// <exception cref="GitException">当前工作目录不构成 Git 仓库。</exception>
    /// <exception cref="SqliteException">审计数据库无法打开或迁移。</exception>
    public UninstallHookUseCase CreateUninstall() => new UninstallHookUseCase(GitAccess, AuditStore, _clock, Version);

    /// <summary>装配状态查询用例（只读：不触发审计打开与检测器装配）。</summary>
    /// <returns>已接线的状态用例（配置加载链来自 <see cref="Config"/>）。</returns>
    /// <exception cref="ConfigurationException">配置无效（解析失败 / 违反硬下限）。</exception>
    /// <exception cref="GitException">当前工作目录不构成 Git 仓库。</exception>
    public StatusUseCase CreateStatus() => new StatusUseCase(GitAccess, Config, Version);

    private static ScanPolicy BuildScanPolicy(GitGuardConfig config) => new ScanPolicy(config.Policy, config.Allowlist);

    private GitGuardConfig LoadConfig() =>
        new GitGuardConfigLoader(_options.WorkingDirectory, _paths.UserConfigPath, _clock).Load();

    private GitAccess CreateGitAccess() => new GitAccess(_options.WorkingDirectory, _options.GitExecutable);

    private SqliteAuditStore CreateAuditStore() => new SqliteAuditStore(_paths.AuditDatabasePath, Version);

    private FilePendingScanStore CreatePendingScanStore() =>
        new FilePendingScanStore(Path.Combine(GitAccess.GetGitDirectory(), "gitguard"));

    private FingerprintSaltLoadResult CreateSalt() =>
        new FingerprintSaltStore(_paths.FingerprintSaltPath, _secretRandom).LoadOrCreate();

    private DetectorSet CreateDetectors()
    {
        var factory = new FindingFactory(new Fingerprinter(_salt.Value.Salt));
        return DetectorAssembly.Assemble(Config, factory, _ruleWarnings);
    }

    private ImmutableArray<string> CollectWarnings()
    {
        var warnings = new List<string>(_config.Value.Warnings);

        _ = _detectors.Value; // 触发装配以填充 _ruleWarnings 与指纹盐结果
        warnings.AddRange(_ruleWarnings);

        if (_salt.Value.Warning is { } saltWarning)
        {
            warnings.Add(saltWarning);
        }

        return warnings.ToImmutableArray();
    }
}
