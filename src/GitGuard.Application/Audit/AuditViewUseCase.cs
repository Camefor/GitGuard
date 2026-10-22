using GitGuard.Core;
using GitGuard.Core.Abstractions;
using GitGuard.Core.Model;

namespace GitGuard.Application.Audit;

/// <summary>
/// 审计运行详情用例（<c>06-API接口设计</c> §4.9 / FR-023）：按运行身份标识读取一次扫描运行及其 Finding 明细。
/// 只读：不写审计、不触发配置加载与检测器装配。查询范围为整个本地审计库（含所有仓库），
/// 返回的 <see cref="Finding.SnippetMasked"/> 与指纹均保持脱敏形态，不恢复任何原文。
/// </summary>
public sealed class AuditViewUseCase
{
    private readonly IAuditStore _store;

    /// <summary>初始化审计运行详情用例。</summary>
    /// <param name="store">审计存储抽象。</param>
    /// <exception cref="ArgumentNullException"><paramref name="store"/> 为 <see langword="null"/>。</exception>
    public AuditViewUseCase(IAuditStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        _store = store;
    }

    /// <summary>执行单次运行查询。</summary>
    /// <param name="runId">运行身份标识（<c>scan_runs.id</c>）。</param>
    /// <returns>存在则返回重建的扫描运行聚合（含 Finding 明细）；不存在返回 <see langword="null"/>。</returns>
    /// <exception cref="ArgumentException"><paramref name="runId"/> 为空 GUID。</exception>
    /// <exception cref="SqliteException">审计库读取失败（CLI 映射退出码 14）。</exception>
    public ScanRun? Execute(Guid runId)
    {
        if (runId == Guid.Empty)
        {
            throw new ArgumentException("运行 ID 不能为空 GUID。", nameof(runId));
        }

        return _store.FindScanRun(runId);
    }
}
