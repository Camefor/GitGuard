using System.Collections.Immutable;
using GitGuard.Core;
using GitGuard.Core.Abstractions;
using GitGuard.Core.Model;

namespace GitGuard.Application.Hooks;

/// <summary>
/// Hook 安装用例（FR-010 / ADR-0004 / 模块设计/Hook.md §6.1）：在解析出的 hooks 目录写入 GitGuard 分发脚本；
/// 已有非托管文本脚本先备份为 <c>&lt;hook&gt;.local.bak</c> 再包裹（TC-FR-010-02 / DR-05）；
/// 已托管脚本覆盖刷新且不再次备份（TC-FR-010-03）；二进制 / 符号链接目标需 <c>--force</c>（TC-FR-010-05）。
/// 并发保护：全程持有 <c>&lt;git-common-dir&gt;/gitguard.install.lock</c> 独占句柄（06-API接口设计 §8）。
/// 退出码语义（由 CLI 映射）：写入权限不足 → 3；需 <c>--force</c> 或存在遗留备份冲突 → 4；锁冲突 → 50。
/// </summary>
public sealed class InstallHookUseCase
{
    private static readonly ImmutableArray<HookType> DEFAULT_HOOKS =
        ImmutableArray.Create(HookType.PreCommit, HookType.PrePush);

    private readonly IGitAccess _gitAccess;
    private readonly IAuditStore _auditStore;
    private readonly IClock _clock;
    private readonly string _version;

    /// <summary>初始化安装用例。</summary>
    /// <param name="gitAccess">Git 访问抽象（构造时已指向目标仓库）。</param>
    /// <param name="auditStore">审计存储（安装记录与 HookInstalled 事件）。</param>
    /// <param name="clock">时钟抽象（安装时间戳）。</param>
    /// <param name="gitGuardVersion">写入脚本标记行与审计记录的 GitGuard 版本；<see langword="null"/> 取本程序集版本。</param>
    /// <exception cref="ArgumentNullException">任一引用参数为 <see langword="null"/>。</exception>
    public InstallHookUseCase(IGitAccess gitAccess, IAuditStore auditStore, IClock clock, string? gitGuardVersion = null)
    {
        ArgumentNullException.ThrowIfNull(gitAccess);
        ArgumentNullException.ThrowIfNull(auditStore);
        ArgumentNullException.ThrowIfNull(clock);

        _gitAccess = gitAccess;
        _auditStore = auditStore;
        _clock = clock;
        _version = string.IsNullOrWhiteSpace(gitGuardVersion) ? ResolveDefaultVersion() : gitGuardVersion;
    }

    /// <summary>执行安装。</summary>
    /// <param name="executablePath">GitGuard 可执行文件路径；固化为绝对路径写入分发脚本（08-权限设计 §6）。</param>
    /// <param name="force">
    /// <see langword="true"/> 允许接管二进制文件 / 符号链接目标（TC-FR-010-05）；
    /// 普通文本脚本与已托管脚本无需该开关。
    /// </param>
    /// <param name="hooks">要安装的 Hook 列表；<see langword="null"/> 等价于 pre-commit + pre-push。</param>
    /// <returns>安装报告（逐 Hook 处置与备份路径）。</returns>
    /// <exception cref="ArgumentException"><paramref name="executablePath"/> 为空或全空白；<paramref name="hooks"/> 为空集合或含重复项。</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="hooks"/> 含未知取值或 post-commit（顺延 GGD-0013）。</exception>
    /// <exception cref="HookException">写入失败（退出码 3 / 4）或安装锁冲突（退出码 50）。</exception>
    /// <exception cref="SqliteException">审计写入失败（FailClosed，磁盘变更已生效）。</exception>
    public HookInstallReport Execute(string executablePath, bool force = false, IReadOnlyList<HookType>? hooks = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        var hookTypes = ResolveHookTypes(hooks);

        string repositoryPath = _gitAccess.GetRepositoryRoot();
        string hooksDirectory = ResolveHooksDirectory();
        string executableFullPath = Path.GetFullPath(executablePath);
        DateTimeOffset installedAt = _clock.UtcNow;

        using var installLock = new InstallLock(_gitAccess.GetGitCommonDirectory());
        HookFileOperations.EnsureDirectory(hooksDirectory);

        // 第一轮：全量预检（任何 Hook 冲突都在写入前失败，不留半安装状态）
        var plans = new List<HookInstallPlan>(hookTypes.Length);
        foreach (HookType hookType in hookTypes)
        {
            plans.Add(PlanHook(hooksDirectory, hookType, force));
        }

        // 第二轮：渲染全部脚本（内嵌资源缺失等构建期错误同样在写入前暴露）
        var scripts = new List<string>(plans.Count);
        foreach (HookInstallPlan plan in plans)
        {
            scripts.Add(HookScriptBuilder.Render(plan.HookType, executableFullPath, _version));
        }

        // 第三轮：逐个写入 + 审计落库
        var results = new List<HookInstallResult>(plans.Count);
        for (int i = 0; i < plans.Count; i++)
        {
            HookInstallPlan plan = plans[i];
            Apply(plan, scripts[i]);

            var installation = HookInstallation.Create(
                repositoryPath,
                plan.HookType,
                installedAt,
                _version,
                plan.BackupPath is null ? null : Path.GetFileName(plan.BackupPath));
            _auditStore.SaveHookInstallation(installation);

            results.Add(new HookInstallResult(plan.HookType, plan.Disposition, plan.HookPath, plan.BackupPath));
        }

        return new HookInstallReport(repositoryPath, _version, results);
    }

    private static HookInstallPlan PlanHook(string hooksDirectory, HookType hookType, bool force)
    {
        string hookPath = Path.Combine(hooksDirectory, hookType.ToHookName());
        string backupPath = Path.Combine(hooksDirectory, hookType.ToBackupFileName());
        string? existingBackup = File.Exists(backupPath) ? backupPath : null;
        HookFileState state = HookFileOperations.Inspect(hookPath);

        // 非托管目标 + 遗留备份：覆盖备份会丢失真正的原 Hook（DR-05 不破坏用户数据），拒绝接管且 --force 不适用
        if (existingBackup is not null && state != HookFileState.Missing && state != HookFileState.ManagedScript)
        {
            throw new HookException(
                HookException.UNKNOWN_HOOK_PRESENT,
                $"存在遗留备份 {existingBackup}，无法安全接管 {hookPath}；请先恢复或删除该备份后重试。");
        }

        return state switch
        {
            // 目标缺失但遗留备份仍在（托管脚本被手工删除）：写入分发脚本后会继续调用该备份
            HookFileState.Missing =>
                new HookInstallPlan(hookType, HookInstallDisposition.Installed, hookPath, existingBackup),

            // 已托管：覆盖刷新版本，不再次备份（TC-FR-010-03）；既有备份原样保留
            HookFileState.ManagedScript =>
                new HookInstallPlan(hookType, HookInstallDisposition.Refreshed, hookPath, existingBackup),

            // 普通文本脚本：静默备份 + 包裹（TC-FR-010-02 / DR-05）
            HookFileState.LegacyScript =>
                new HookInstallPlan(hookType, HookInstallDisposition.WrappedLegacy, hookPath, backupPath),

            // 二进制 / 符号链接：需 --force 显式接管（TC-FR-010-05 / SRS FR-010 异常处理）
            HookFileState.Binary or HookFileState.SymbolicLink when force =>
                new HookInstallPlan(hookType, HookInstallDisposition.WrappedLegacy, hookPath, backupPath),

            HookFileState.Binary or HookFileState.SymbolicLink =>
                throw new HookException(
                    HookException.UNKNOWN_HOOK_PRESENT,
                    $"{hookPath} 不是可安全包裹的脚本（二进制文件或符号链接），如需接管请使用 --force。"),

            _ => throw new HookException(HookException.UNKNOWN_HOOK_PRESENT, $"无法识别的 Hook 文件状态：{hookPath}"),
        };
    }

    private static void Apply(HookInstallPlan plan, string script)
    {
        if (plan.Disposition == HookInstallDisposition.WrappedLegacy)
        {
            string backupPath = plan.BackupPath
                ?? throw new InvalidOperationException($"WrappedLegacy 计划缺少备份路径：{plan.HookPath}");
            HookFileOperations.CreateBackup(plan.HookPath, backupPath);
            HookFileOperations.DeleteFile(plan.HookPath);
        }

        HookFileOperations.WriteScript(plan.HookPath, script);
    }

    private static ImmutableArray<HookType> ResolveHookTypes(IReadOnlyList<HookType>? hooks)
    {
        if (hooks is null)
        {
            return DEFAULT_HOOKS;
        }

        ArgumentOutOfRangeException.ThrowIfZero(hooks.Count, nameof(hooks));

        var builder = ImmutableArray.CreateBuilder<HookType>(hooks.Count);
        foreach (HookType hookType in hooks)
        {
            if (!Enum.IsDefined(hookType))
            {
                throw new ArgumentOutOfRangeException(nameof(hooks), hookType, "未知的 HookType 取值");
            }

            if (hookType == HookType.PostCommit)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(hooks),
                    hookType,
                    "post-commit 的安装（FR-050 绕过检测）顺延至 GGD-0013，当前不可安装");
            }

            if (builder.Contains(hookType))
            {
                throw new ArgumentException($"hooks 含重复的 Hook 类型：{hookType}", nameof(hooks));
            }

            builder.Add(hookType);
        }

        return builder.ToImmutable();
    }

    private static string ResolveDefaultVersion()
    {
        Version? version = typeof(InstallHookUseCase).Assembly.GetName().Version;
        return version is null ? "0.0.0" : version.ToString();
    }

    private string ResolveHooksDirectory() =>
        _gitAccess.GetConfiguredHooksPath() ?? Path.Combine(_gitAccess.GetGitCommonDirectory(), "hooks");
}
