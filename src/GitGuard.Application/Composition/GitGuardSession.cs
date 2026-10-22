using System.Collections.Immutable;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using GitGuard.Application.Allowlist;
using GitGuard.Application.Audit;
using GitGuard.Application.Bypass;
using GitGuard.Application.Configuration;
using GitGuard.Application.Hooks;
using GitGuard.Application.Scanning;
using GitGuard.Core;
using GitGuard.Core.Abstractions;
using GitGuard.Core.Model;
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
    private readonly Lazy<SqliteAuditStore> _auditStore;
    private readonly Lazy<FingerprintSaltLoadResult> _salt;
    private readonly Lazy<DetectorSet> _detectors;
    private readonly Lazy<ImmutableArray<string>> _warnings;
    private readonly Lazy<GitVersionProbeResult> _gitProbe;

    // 检测器装配期间填充（Lazy 的执行一次语义保证无并发写）
    private readonly List<string> _ruleWarnings = new List<string>();

    // 指纹盐加载先于审计库创建时暂存权限修复，待 AuditStore 可用后补写 FilePermissionFixed 事件
    private readonly List<FilePermissionFix> _pendingPermissionFixes = new List<FilePermissionFix>();

    private bool _policyOverridesRecorded;

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
        _auditStore = new Lazy<SqliteAuditStore>(CreateAuditStore);
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

    /// <summary>白名单存储（与审计数据共享本地数据库）；首次访问时打开（或创建并迁移）<c>audit.db</c>。</summary>
    public IAllowlistStore AllowlistStore => _auditStore.Value;

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

    /// <summary>
    /// 尽力记录一次 FailClosed 退出事件（08-权限设计 §7）：消息只保存 SHA-256 摘要，不落原文。
    /// 审计库本身不可用时返回 <see langword="false"/>，由 CLI 输出警告，不递归触发新的 FailClosed。
    /// </summary>
    /// <param name="exitCode">FailClosed 退出码（≥ 10）。</param>
    /// <param name="errorCode">错误码（<c>ExitCodeMapper</c> 的稳定字符串）。</param>
    /// <param name="message">已脱敏的错误消息；原文不会写入审计库。</param>
    /// <returns>写入成功返回 <see langword="true"/>；审计库不可用返回 <see langword="false"/>。</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="exitCode"/> 小于 10。</exception>
    /// <exception cref="ArgumentException"><paramref name="errorCode"/> 为空或全空白。</exception>
    /// <exception cref="ArgumentNullException"><paramref name="message"/> 为 <see langword="null"/>。</exception>
    public bool TryRecordFailClosed(int exitCode, string errorCode, string message)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(exitCode, 10, nameof(exitCode));
        ArgumentException.ThrowIfNullOrWhiteSpace(errorCode);
        ArgumentNullException.ThrowIfNull(message);

        try
        {
            string messageHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(message)));
            AuditStore.SaveFailClosedEvent(exitCode, errorCode, messageHash);
            return true;
        }
        catch (Exception ex) when (ex is SqliteException or IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>装配暂存区扫描用例（扫描设置与策略映射全部来自有效配置）。</summary>
    /// <param name="collectRuleTimings">是否采集规则级耗时（NFR-M-002，CLI <c>--verbose</c>）；<see langword="false"/> 时无计时开销。</param>
    /// <returns>已接线配置的暂存区扫描用例。</returns>
    /// <exception cref="ConfigurationException">配置无效（解析失败 / 违反硬下限 / 类别全禁）。</exception>
    /// <exception cref="GitException">当前工作目录不构成 Git 仓库。</exception>
    /// <exception cref="SqliteException">审计数据库无法打开或迁移。</exception>
    public ScanStagedUseCase CreateStagedScan(bool collectRuleTimings = false)
    {
        GitGuardConfig config = Config;
        IAuditStore auditStore = AuditStore;
        DetectorSet detectors = _detectors.Value;
        RecordPolicyOverrides(auditStore, detectors.PolicyOverrides);
        return new ScanStagedUseCase(
            GitAccess,
            detectors.Detectors,
            auditStore,
            _clock,
            config.Scan,
            BuildScanPolicy(config),
            collectRuleTimings);
    }

    /// <summary>装配推送扫描用例（扫描设置与策略映射全部来自有效配置）。</summary>
    /// <param name="collectRuleTimings">是否采集规则级耗时（NFR-M-002，CLI <c>--verbose</c>）；<see langword="false"/> 时无计时开销。</param>
    /// <returns>已接线配置的推送扫描用例。</returns>
    /// <exception cref="ConfigurationException">配置无效（解析失败 / 违反硬下限 / 类别全禁）。</exception>
    /// <exception cref="GitException">当前工作目录不构成 Git 仓库。</exception>
    /// <exception cref="SqliteException">审计数据库无法打开或迁移。</exception>
    public ScanPushUseCase CreatePushScan(bool collectRuleTimings = false)
    {
        GitGuardConfig config = Config;
        IAuditStore auditStore = AuditStore;
        DetectorSet detectors = _detectors.Value;
        RecordPolicyOverrides(auditStore, detectors.PolicyOverrides);
        return new ScanPushUseCase(
            GitAccess,
            detectors.Detectors,
            auditStore,
            _clock,
            config.Scan,
            BuildScanPolicy(config),
            collectRuleTimings);
    }

    /// <summary>装配全量扫描用例（FR-060：HEAD 全量树 + 可选历史提交；扫描设置与策略映射全部来自有效配置）。</summary>
    /// <param name="collectRuleTimings">是否采集规则级耗时（NFR-M-002，CLI <c>--verbose</c>）；<see langword="false"/> 时无计时开销。</param>
    /// <returns>已接线配置的全量扫描用例。</returns>
    /// <exception cref="ConfigurationException">配置无效（解析失败 / 违反硬下限 / 类别全禁）。</exception>
    /// <exception cref="GitException">当前工作目录不构成 Git 仓库。</exception>
    /// <exception cref="SqliteException">审计数据库无法打开或迁移。</exception>
    public ScanAllUseCase CreateFullScan(bool collectRuleTimings = false)
    {
        GitGuardConfig config = Config;
        IAuditStore auditStore = AuditStore;
        DetectorSet detectors = _detectors.Value;
        RecordPolicyOverrides(auditStore, detectors.PolicyOverrides);
        return new ScanAllUseCase(
            GitAccess,
            detectors.Detectors,
            auditStore,
            _clock,
            config.Scan,
            BuildScanPolicy(config),
            collectRuleTimings);
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

    /// <summary>装配审计列表用例（只读：仅在执行时打开审计库，不触发配置加载与检测器装配）。</summary>
    /// <returns>已接线的列表用例。</returns>
    /// <exception cref="SqliteException">审计数据库无法打开或迁移。</exception>
    public AuditListUseCase CreateAuditList() => new AuditListUseCase(AuditStore);

    /// <summary>装配审计导出用例（只读：仅在执行时打开审计库，不触发配置加载与检测器装配）。</summary>
    /// <returns>已接线的导出用例。</returns>
    /// <exception cref="SqliteException">审计数据库无法打开或迁移。</exception>
    public AuditExportUseCase CreateAuditExport() => new AuditExportUseCase(AuditStore, _clock);

    /// <summary>装配白名单新增用例（仅在执行时打开审计库，不触发配置加载与检测器装配）。</summary>
    /// <returns>已接线的新增用例。</returns>
    /// <exception cref="SqliteException">审计数据库无法打开或迁移。</exception>
    public AllowlistAddUseCase CreateAllowlistAdd() => new AllowlistAddUseCase(AllowlistStore, _clock);

    /// <summary>装配白名单删除用例（仅在执行时打开审计库，不触发配置加载与检测器装配）。</summary>
    /// <returns>已接线的删除用例。</returns>
    /// <exception cref="SqliteException">审计数据库无法打开或迁移。</exception>
    public AllowlistRemoveUseCase CreateAllowlistRemove() => new AllowlistRemoveUseCase(AllowlistStore);

    /// <summary>装配白名单列表用例（只读：仅在执行时打开审计库，不触发配置加载与检测器装配）。</summary>
    /// <returns>已接线的列表用例。</returns>
    /// <exception cref="SqliteException">审计数据库无法打开或迁移。</exception>
    public AllowlistListUseCase CreateAllowlistList() => new AllowlistListUseCase(AllowlistStore);

    /// <summary>装配状态查询用例（只读：不触发审计打开与检测器装配）。</summary>
    /// <returns>已接线的状态用例（配置加载链来自 <see cref="Config"/>）。</returns>
    /// <exception cref="ConfigurationException">配置无效（解析失败 / 违反硬下限）。</exception>
    /// <exception cref="GitException">当前工作目录不构成 Git 仓库。</exception>
    public StatusUseCase CreateStatus() => new StatusUseCase(GitAccess, Config, Version);

    private static void RecordPermissionFixes(IAuditStore store, IEnumerable<FilePermissionFix> fixes)
    {
        foreach (FilePermissionFix fix in fixes)
        {
            store.SaveFilePermissionFixedEvent(fix.FilePath, fix.PreviousMode, fix.NewMode);
        }
    }

    private ScanPolicy BuildScanPolicy(GitGuardConfig config)
    {
        // FR-041：配置文件条目与 SQLite 持久化条目合并后参与策略判定；
        // 审计库读取失败按 FailClosed 传播（SqliteException → 退出码 14），不得静默忽略白名单
        var entries = config.Allowlist.AddRange(AllowlistStore.ListAllAllowlistEntries());
        return new ScanPolicy(config.Policy, entries, config.ExcludePaths, config.ExcludeFiles);
    }

    private GitGuardConfig LoadConfig() =>
        new GitGuardConfigLoader(
            _options.WorkingDirectory,
            _paths.UserConfigPath,
            _clock,
            defaultLogFilePath: _paths.LogFilePath).Load();

    private GitAccess CreateGitAccess() => new GitAccess(_options.WorkingDirectory, _options.GitExecutable);

    private SqliteAuditStore CreateAuditStore()
    {
        var store = new SqliteAuditStore(_paths.AuditDatabasePath, Version);
        FlushPendingPermissionFixes(store);
        return store;
    }

    private FilePendingScanStore CreatePendingScanStore() =>
        new FilePendingScanStore(Path.Combine(GitAccess.GetGitDirectory(), "gitguard"));

    private FingerprintSaltLoadResult CreateSalt()
    {
        FingerprintSaltLoadResult result = new FingerprintSaltStore(_paths.FingerprintSaltPath, _secretRandom).LoadOrCreate();
        if (result.PermissionFixes.IsEmpty)
        {
            return result;
        }

        if (_auditStore.IsValueCreated)
        {
            RecordPermissionFixes(_auditStore.Value, result.PermissionFixes);
        }
        else
        {
            _pendingPermissionFixes.AddRange(result.PermissionFixes);
        }

        return result;
    }

    /// <summary>把指纹盐 / 缓存目录的权限修复补写到审计库（审计库创建晚于盐加载时使用）。</summary>
    private void FlushPendingPermissionFixes(IAuditStore store)
    {
        if (_pendingPermissionFixes.Count == 0)
        {
            return;
        }

        RecordPermissionFixes(store, _pendingPermissionFixes);
        _pendingPermissionFixes.Clear();
    }

    /// <summary>记录配置相对默认策略的降级项；同一 Session 只写一次，避免同进程多次装配产生重复事件。</summary>
    private void RecordPolicyOverrides(IAuditStore store, ImmutableArray<PolicyOverride> overrides)
    {
        if (_policyOverridesRecorded)
        {
            return;
        }

        foreach (PolicyOverride policyOverride in overrides)
        {
            store.SavePolicyOverrideEvent(policyOverride.RuleId, policyOverride.From, policyOverride.To);
        }

        _policyOverridesRecorded = true;
    }

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
