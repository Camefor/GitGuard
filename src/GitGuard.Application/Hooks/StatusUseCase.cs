using System.Collections.Immutable;
using GitGuard.Application.Configuration;
using GitGuard.Core;
using GitGuard.Core.Abstractions;
using GitGuard.Core.Model;

namespace GitGuard.Application.Hooks;

/// <summary>
/// 状态查询用例（FR-012 / <c>06-API接口设计</c> §5.3）：汇总 Git 版本、<c>core.hooksPath</c>、
/// 每个 Hook 的文件签名、GitGuard 版本与配置文件加载链。全程只读：不写审计、不装配检测器、不触碰指纹盐。
/// 退出码语义（由 CLI 映射）：0 一切正常；2 非 Git 仓库（<see cref="IGitAccess"/> 构造期 FailClosed）；
/// 10 git 命令失败；6 存在未被 GitGuard 托管的 Hook（<see cref="StatusReport.NeedsAttention"/>）。
/// </summary>
public sealed class StatusUseCase
{
    /// <summary>安装 / 检查的 Hook 集合（FR-010：pre-commit + pre-push；FR-050：post-commit）。</summary>
    private static readonly ImmutableArray<HookType> INSPECTED_HOOKS =
        ImmutableArray.Create(HookType.PreCommit, HookType.PrePush, HookType.PostCommit);

    private readonly IGitAccess _gitAccess;
    private readonly GitGuardConfig _config;
    private readonly string _version;

    /// <summary>初始化状态查询用例。</summary>
    /// <param name="gitAccess">Git 访问抽象（构造时已指向目标仓库）。</param>
    /// <param name="config">有效配置（提供加载链与警告；不触发检测器装配）。</param>
    /// <param name="gitGuardVersion">GitGuard 版本（展示用）。</param>
    /// <exception cref="ArgumentNullException">任一引用参数为 <see langword="null"/>。</exception>
    /// <exception cref="ArgumentException"><paramref name="gitGuardVersion"/> 为空或全空白。</exception>
    public StatusUseCase(IGitAccess gitAccess, GitGuardConfig config, string gitGuardVersion)
    {
        ArgumentNullException.ThrowIfNull(gitAccess);
        ArgumentNullException.ThrowIfNull(config);
        ArgumentException.ThrowIfNullOrWhiteSpace(gitGuardVersion);

        _gitAccess = gitAccess;
        _config = config;
        _version = gitGuardVersion;
    }

    /// <summary>执行状态查询。</summary>
    /// <returns>状态报告（含逐 Hook 签名与 <see cref="StatusReport.NeedsAttention"/>）。</returns>
    /// <exception cref="GitException">git 命令失败（含非 Git 仓库）。</exception>
    public StatusReport Execute()
    {
        string repositoryPath = _gitAccess.GetRepositoryRoot();
        string? configuredHooksPath = _gitAccess.GetConfiguredHooksPath();
        string hooksDirectory = configuredHooksPath ?? Path.Combine(_gitAccess.GetGitCommonDirectory(), "hooks");

        var hooks = ImmutableArray.CreateBuilder<HookStatus>(INSPECTED_HOOKS.Length);
        foreach (HookType hookType in INSPECTED_HOOKS)
        {
            string hookPath = Path.Combine(hooksDirectory, hookType.ToHookName());
            hooks.Add(InspectHook(hookType, hookPath));
        }

        return new StatusReport(
            repositoryPath,
            _gitAccess.GetVersion(),
            configuredHooksPath,
            hooksDirectory,
            hooks.ToImmutable(),
            _version,
            _config.LoadedConfigFiles,
            _config.Warnings);
    }

    private static HookStatus InspectHook(HookType hookType, string hookPath)
    {
        HookFileState state = HookFileOperations.Inspect(hookPath);
        HookStatusKind kind = state switch
        {
            HookFileState.Missing => HookStatusKind.Missing,
            HookFileState.ManagedScript => HookStatusKind.Managed,
            HookFileState.LegacyScript => HookStatusKind.Legacy,
            HookFileState.Binary => HookStatusKind.Binary,
            HookFileState.SymbolicLink => HookStatusKind.SymbolicLink,
            _ => throw new InvalidOperationException($"未知的 HookFileState 取值：{(int)state}"),
        };

        string? managedVersion = state == HookFileState.ManagedScript
            ? HookFileOperations.TryReadManagedVersion(hookPath)
            : null;

        return new HookStatus(hookType, hookPath, kind, managedVersion);
    }
}
