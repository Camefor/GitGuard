using GitGuard.Application.Audit;
using GitGuard.Application.Composition;
using GitGuard.Core.Model;

namespace GitGuard.Desktop.Services;

/// <summary>
/// <see cref="IAuditOperations"/> 的 Application 组合根实现（GGD-0046）：
/// 复用 <see cref="GitGuardSession.CreateAuditList"/> 与 <see cref="GitGuardSession.CreateAuditExport"/>，
/// 保证日志页查询与导出和 CLI 使用同一契约；全部操作以 <c>Task.Run</c> 移出 UI 线程。
/// </summary>
public sealed class AuditOperations : IAuditOperations
{
    private readonly GitGuardSession _session;

    /// <summary>初始化审计数据访问服务。</summary>
    /// <param name="session">桌面端主会话（审计库为用户级共享数据库）。</param>
    /// <exception cref="ArgumentNullException"><paramref name="session"/> 为 <see langword="null"/>。</exception>
    public AuditOperations(GitGuardSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        _session = session;
    }

    /// <inheritdoc />
    public Task<AuditRunPage> QueryAsync(AuditQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        return Task.Run(() => _session.CreateAuditList().Execute(query), cancellationToken);
    }

    /// <inheritdoc />
    public Task<AuditExportReport> ExportAsync(
        string outputPath,
        AuditExportFormat format,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        return Task.Run(
            () => _session.CreateAuditExport().Execute(GitGuardSession.ExpandUserPath(outputPath), format),
            cancellationToken);
    }
}
