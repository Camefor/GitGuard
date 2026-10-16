using System.Collections.Immutable;
using GitGuard.Core;
using GitGuard.Core.Abstractions;
using GitGuard.Core.Model;

namespace GitGuard.Application.Hooks;

/// <summary>
/// Hook 卸载用例（FR-011 / ADR-0004）：删除 GitGuard 分发脚本；存在 <c>&lt;hook&gt;.local.bak</c> 时改名还原为原 Hook
/// （TC-FR-010-04：uninstall 后恢复为原 Husky 脚本）。未发现 GitGuard 托管痕迹的 Hook 不做任何修改，
/// 报告 <see cref="HookUninstallDisposition.NotInstalled"/>（CLI 映射退出码 5，视作成功加警告）；
/// <c>force</c> 仅在存在备份（GitGuard 留痕）时允许接管已被手工替换的目标。
/// 并发保护：与安装共用 <c>gitguard.install.lock</c> 独占句柄（06-API接口设计 §8，冲突 → 退出码 50）。
/// </summary>
public sealed class UninstallHookUseCase
{
    private static readonly ImmutableArray<HookType> DEFAULT_HOOKS =
        ImmutableArray.Create(HookType.PreCommit, HookType.PrePush);

    private readonly IGitAccess _gitAccess;
    private readonly IAuditStore _auditStore;
    private readonly IClock _clock;
    private readonly string _version;

    /// <summary>初始化卸载用例。</summary>
    /// <param name="gitAccess">Git 访问抽象（构造时已指向目标仓库）。</param>
    /// <param name="auditStore">审计存储（卸载记录与 HookUninstalled 事件；行不存在时以其字段插入）。</param>
    /// <param name="clock">时钟抽象（记录行不存在时使用的安装时间戳）。</param>
    /// <param name="gitGuardVersion">记录行不存在时写入的 GitGuard 版本；<see langword="null"/> 取本程序集版本。</param>
    /// <exception cref="ArgumentNullException">任一引用参数为 <see langword="null"/>。</exception>
    public UninstallHookUseCase(IGitAccess gitAccess, IAuditStore auditStore, IClock clock, string? gitGuardVersion = null)
    {
        ArgumentNullException.ThrowIfNull(gitAccess);
        ArgumentNullException.ThrowIfNull(auditStore);
        ArgumentNullException.ThrowIfNull(clock);

        _gitAccess = gitAccess;
        _auditStore = auditStore;
        _clock = clock;
        _version = string.IsNullOrWhiteSpace(gitGuardVersion) ? ResolveDefaultVersion() : gitGuardVersion;
    }

    /// <summary>执行卸载。</summary>
    /// <param name="force">
    /// <see langword="true"/> 允许在存在备份的前提下接管已被手工替换的目标脚本；
    /// 无备份的非托管 Hook 一律不动（防止误删用户自己的 Hook）。
    /// </param>
    /// <param name="hooks">要卸载的 Hook 列表；<see langword="null"/> 等价于 pre-commit + pre-push。</param>
    /// <returns>卸载报告（逐 Hook 处置与还原路径）。</returns>
    /// <exception cref="ArgumentException"><paramref name="hooks"/> 为空集合或含重复项。</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="hooks"/> 含未知取值或 post-commit（顺延 GGD-0013）。</exception>
    /// <exception cref="HookException">删除 / 还原失败（退出码 3）或安装锁冲突（退出码 50）。</exception>
    /// <exception cref="SqliteException">审计写入失败（FailClosed，磁盘变更已生效）。</exception>
    public HookUninstallReport Execute(bool force = false, IReadOnlyList<HookType>? hooks = null)
    {
        var hookTypes = ResolveHookTypes(hooks);
        string repositoryPath = _gitAccess.GetRepositoryRoot();
        string hooksDirectory = ResolveHooksDirectory();

        using var installLock = new InstallLock(_gitAccess.GetGitCommonDirectory());

        var results = new List<HookUninstallResult>(hookTypes.Length);
        foreach (HookType hookType in hookTypes)
        {
            results.Add(UninstallHook(repositoryPath, hooksDirectory, hookType, force));
        }

        return new HookUninstallReport(repositoryPath, results);
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
                    "post-commit 的安装 / 卸载（FR-050 绕过检测）顺延至 GGD-0013，当前不可操作");
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
        Version? version = typeof(UninstallHookUseCase).Assembly.GetName().Version;
        return version is null ? "0.0.0" : version.ToString();
    }

    private HookUninstallResult UninstallHook(string repositoryPath, string hooksDirectory, HookType hookType, bool force)
    {
        string hookPath = Path.Combine(hooksDirectory, hookType.ToHookName());
        string backupPath = Path.Combine(hooksDirectory, hookType.ToBackupFileName());
        bool backupExists = File.Exists(backupPath);
        HookFileState state = HookFileOperations.Inspect(hookPath);

        if (state == HookFileState.ManagedScript)
        {
            return backupExists
                ? RestoreLegacy(repositoryPath, hookType, hookPath, backupPath)
                : Remove(repositoryPath, hookType, hookPath);
        }

        if (!backupExists)
        {
            // 无托管脚本、无备份：GitGuard 从未在此安装（或已清理干净），不做任何修改、不写审计
            return new HookUninstallResult(hookType, HookUninstallDisposition.NotInstalled, hookPath, null);
        }

        // 目标缺失：托管脚本被手工删除，备份仍在 → 直接还原；
        // 目标存在但非托管：仅在 --force（用户显式接管）时删除目标并还原备份
        if (state == HookFileState.Missing || force)
        {
            return RestoreLegacy(repositoryPath, hookType, hookPath, backupPath);
        }

        return new HookUninstallResult(hookType, HookUninstallDisposition.NotInstalled, hookPath, null);
    }

    private HookUninstallResult RestoreLegacy(string repositoryPath, HookType hookType, string hookPath, string backupPath)
    {
        HookFileOperations.RestoreBackup(backupPath, hookPath);
        SaveUninstallation(repositoryPath, hookType, Path.GetFileName(backupPath), restoredLegacyHook: true);
        return new HookUninstallResult(hookType, HookUninstallDisposition.RestoredLegacy, hookPath, backupPath);
    }

    private HookUninstallResult Remove(string repositoryPath, HookType hookType, string hookPath)
    {
        HookFileOperations.DeleteFile(hookPath);
        SaveUninstallation(repositoryPath, hookType, backupFileName: null, restoredLegacyHook: false);
        return new HookUninstallResult(hookType, HookUninstallDisposition.Removed, hookPath, null);
    }

    private void SaveUninstallation(string repositoryPath, HookType hookType, string? backupFileName, bool restoredLegacyHook)
    {
        var installation = HookInstallation.Create(repositoryPath, hookType, _clock.UtcNow, _version, backupFileName);
        _auditStore.SaveHookUninstallation(installation, restoredLegacyHook);
    }

    private string ResolveHooksDirectory() =>
        _gitAccess.GetConfiguredHooksPath() ?? Path.Combine(_gitAccess.GetGitCommonDirectory(), "hooks");
}
