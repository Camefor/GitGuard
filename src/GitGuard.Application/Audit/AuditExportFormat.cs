namespace GitGuard.Application.Audit;

/// <summary>审计导出文件格式（<c>06-API接口设计</c> §5.6：<c>--format json|csv</c>）。</summary>
public enum AuditExportFormat
{
    /// <summary>JSON（<c>gitguard.audit.export.v1</c>）。</summary>
    Json,

    /// <summary>CSV（每行一条 Finding；无 Finding 的运行保留一行运行级数据）。</summary>
    Csv,
}
