using GitGuard.Application.Composition;
using GitGuard.Application.Configuration;
using GitGuard.Application.Hooks;
using GitGuard.Application.Scanning;
using GitGuard.Core.Model;
using GitGuard.Core.Policy;

namespace GitGuard.Desktop.Services;

/// <summary>
/// <see cref="IRepositoryOperations"/> 的 Application 组合根实现（GGD-0042，GGD-0044 起含白名单）：
/// 列表 / 登记 / 移除与全局白名单走主会话用例；详情与扫描通过 <see cref="GitGuardSession.CreateScopedSession"/> 绑定目标仓库，
/// 共享同一用户数据目录与用户配置目录。全部操作以 <c>Task.Run</c> 移出 UI 线程。
/// </summary>
public sealed class RepositoryOperations : IRepositoryOperations
{
    private readonly GitGuardSession _session;
    private readonly ISettingsOperations? _settingsOperations;

    /// <summary>初始化仓库操作服务。</summary>
    /// <param name="session">桌面端主会话（绑定应用基目录；仅用于注册表与共享数据目录）。</param>
    /// <param name="settingsOperations">
    /// 桌面设置服务；提供 Gitleaks 覆盖，<see langword="null"/> 时完全采用配置文件（单测可省略）。
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="session"/> 为 <see langword="null"/>。</exception>
    public RepositoryOperations(GitGuardSession session, ISettingsOperations? settingsOperations = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        _session = session;
        _settingsOperations = settingsOperations;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<RegisteredRepository>> ListAsync(CancellationToken cancellationToken = default) =>
        Task.Run(() => _session.CreateListRepositories().Execute(), cancellationToken);

    /// <inheritdoc />
    public Task<RegisteredRepository> RegisterAsync(string path, CancellationToken cancellationToken = default) =>
        Task.Run(() => _session.CreateRegisterRepository().Execute(path), cancellationToken);

    /// <inheritdoc />
    public Task RemoveAsync(Guid id, CancellationToken cancellationToken = default) =>
        Task.Run(() => _session.CreateRemoveRepository().Execute(id), cancellationToken);

    /// <inheritdoc />
    public Task<RepositoryDetail> LoadDetailAsync(string repoPath, CancellationToken cancellationToken = default) =>
        Task.Run(
            () =>
            {
                GitGuardSession scope = _session.CreateScopedSession(repoPath, CreateConfigOverrides());
                StatusReport status = scope.CreateStatus().Execute();
                AuditRunSummary? latestScan = scope.CreateLatestRepositoryScan().Execute(repoPath);
                return new RepositoryDetail(status, latestScan);
            },
            cancellationToken);

    /// <inheritdoc />
    public Task<RepositoryDetail> ScanAsync(string repoPath, CancellationToken cancellationToken = default) =>
        Task.Run(
            async () =>
            {
                GitGuardSession scope = _session.CreateScopedSession(repoPath, CreateConfigOverrides());
                ScanOutcome outcome = await scope
                    .CreateFullScan()
                    .ExecuteAsync(ScanAllOptions.Default, cancellationToken)
                    .ConfigureAwait(false);
                StatusReport status = scope.CreateStatus().Execute();
                var latestScan = new AuditRunSummary(
                    outcome.Run.Id,
                    outcome.Run.RepoPath,
                    outcome.Run.ScanType,
                    outcome.Run.StartedAt,
                    outcome.Run.DurationMs,
                    outcome.Run.Result,
                    outcome.Run.ExitCode,
                    outcome.Run.Findings.Length);
                return new RepositoryDetail(status, latestScan, outcome.AuditWriteFailed);
            },
            cancellationToken);

    /// <inheritdoc />
    public Task<AuditRunPage> QueryScanHistoryAsync(
        string repoPath,
        int limit,
        int offset,
        CancellationToken cancellationToken = default) =>
        Task.Run(
            () => _session.CreateAuditList().Execute(new AuditQuery(limit, offset, repoPath: repoPath)),
            cancellationToken);

    /// <inheritdoc />
    public Task<ScanRun?> LoadScanRunAsync(Guid runId, CancellationToken cancellationToken = default) =>
        Task.Run(() => _session.CreateAuditView().Execute(runId), cancellationToken);

    /// <inheritdoc />
    public Task<AllowlistPage> QueryAllowlistAsync(
        int limit,
        int offset,
        CancellationToken cancellationToken = default) =>
        Task.Run(() => _session.CreateAllowlistList().Execute(new AllowlistQuery(limit, offset)), cancellationToken);

    /// <inheritdoc />
    public Task<AllowlistEntry> AddAllowlistAsync(
        string? ruleId,
        string? pathPattern,
        string? fingerprintHex,
        string reason,
        CancellationToken cancellationToken = default) =>
        Task.Run(
            () => _session
                .CreateAllowlistAdd()
                .Execute(ruleId, pathPattern, fingerprintHex, reason, Environment.UserName),
            cancellationToken);

    /// <inheritdoc />
    public Task RemoveAllowlistAsync(Guid id, CancellationToken cancellationToken = default) =>
        Task.Run(() => _session.CreateAllowlistRemove().Execute(id), cancellationToken);

    /// <summary>读取当前桌面 Gitleaks 覆盖；未设置时返回 <see langword="null"/>（完全采用配置文件）。</summary>
    private GitGuardConfigOverrides? CreateConfigOverrides() =>
        _settingsOperations?.Current.Gitleaks is { } gitleaks
            ? new GitGuardConfigOverrides
            {
                UseGitleaks = gitleaks.Enabled,
                GitleaksPath = gitleaks.Path,
            }
            : null;
}
