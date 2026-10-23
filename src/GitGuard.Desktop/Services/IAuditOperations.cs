using GitGuard.Application.Audit;
using GitGuard.Core.Model;

namespace GitGuard.Desktop.Services;

/// <summary>
/// 跨仓库审计数据访问契约（GGD-0046）：查询整个本地审计库并导出 JSON / CSV。
/// 实现必须把 SQLite 访问移出 UI 线程；导出语义与 <c>gitguard audit export</c> 完全一致。
/// </summary>
public interface IAuditOperations
{
    /// <summary>按分页与筛选条件查询审计运行摘要。</summary>
    /// <param name="query">查询条件（跨仓库、可选时间与结果筛选）。</param>
    /// <param name="cancellationToken">取消标记。</param>
    /// <returns>当前页运行摘要与总数。</returns>
    Task<AuditRunPage> QueryAsync(AuditQuery query, CancellationToken cancellationToken = default);

    /// <summary>把全部审计记录导出为 JSON / CSV 文件。</summary>
    /// <param name="outputPath">目标文件路径（支持 <c>~</c> 前缀）。</param>
    /// <param name="format">导出格式。</param>
    /// <param name="cancellationToken">取消标记。</param>
    /// <returns>导出结果（绝对路径与数量）。</returns>
    Task<AuditExportReport> ExportAsync(
        string outputPath,
        AuditExportFormat format,
        CancellationToken cancellationToken = default);
}
