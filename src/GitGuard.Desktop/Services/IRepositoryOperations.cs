using GitGuard.Application.Hooks;
using GitGuard.Core.Model;
using GitGuard.Core.Policy;

namespace GitGuard.Desktop.Services;

/// <summary>
/// 仓库详情快照（GGD-0042）：Hook / Git 状态与最近一次扫描摘要；不含 Finding 明细（GGD-0043 加载）。
/// </summary>
public sealed record RepositoryDetail
{
    /// <summary>初始化仓库详情快照。</summary>
    /// <param name="status">状态查询结果（Git 版本、Hook 签名与配置文件加载链）。</param>
    /// <param name="latestScan">最近扫描摘要；尚无扫描时为 <see langword="null"/>。</param>
    /// <param name="auditWriteFailed">本次扫描的审计写入是否失败（仅 <see cref="IRepositoryOperations.ScanAsync"/> 会置位）。</param>
    public RepositoryDetail(StatusReport status, AuditRunSummary? latestScan, bool auditWriteFailed = false)
    {
        Status = status;
        LatestScan = latestScan;
        AuditWriteFailed = auditWriteFailed;
    }

    /// <summary>状态查询结果（Git 版本、Hook 签名与配置文件加载链）。</summary>
    public StatusReport Status { get; }

    /// <summary>最近扫描摘要；尚无扫描时为 <see langword="null"/>。</summary>
    public AuditRunSummary? LatestScan { get; }

    /// <summary>本次扫描的审计写入是否失败（仅 <see cref="IRepositoryOperations.ScanAsync"/> 会置位）。</summary>
    public bool AuditWriteFailed { get; }
}

/// <summary>
/// 桌面端仓库操作服务（GGD-0042，GGD-0044 起含全局白名单）：为 ViewModel 隐藏 Application 组合根、SQLite 与 Git 子进程细节，
/// 所有实现方法必须在后台线程执行（12-前端组件设计 §6：禁止在 UI 线程做 SQLite 查询）。
/// ViewModel 单测通过实现本接口的假对象驱动，无需启动 Avalonia 或访问真实仓库。
/// </summary>
public interface IRepositoryOperations
{
    /// <summary>读取全部已登记仓库（登记时间倒序）。</summary>
    /// <param name="cancellationToken">取消标记。</param>
    /// <returns>已登记仓库列表（可能为空）。</returns>
    Task<IReadOnlyList<RegisteredRepository>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>登记一个本地 Git 仓库。</summary>
    /// <param name="path">用户输入的路径（支持 <c>~</c> 前缀）。</param>
    /// <param name="cancellationToken">取消标记。</param>
    /// <returns>已持久化的登记记录。</returns>
    Task<RegisteredRepository> RegisterAsync(string path, CancellationToken cancellationToken = default);

    /// <summary>移除仓库登记（不删除仓库文件与审计历史）。</summary>
    /// <param name="id">登记身份标识。</param>
    /// <param name="cancellationToken">取消标记。</param>
    Task RemoveAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>读取仓库详情：当前 Git / Hook 状态与最近扫描摘要。</summary>
    /// <param name="repoPath">仓库工作区根目录绝对路径。</param>
    /// <param name="cancellationToken">取消标记。</param>
    /// <returns>仓库详情快照。</returns>
    Task<RepositoryDetail> LoadDetailAsync(string repoPath, CancellationToken cancellationToken = default);

    /// <summary>立即对 HEAD 全量树执行一次手动扫描，并返回扫描后的详情快照。</summary>
    /// <param name="repoPath">仓库工作区根目录绝对路径。</param>
    /// <param name="cancellationToken">取消标记。</param>
    /// <returns>扫描后的仓库详情快照（<see cref="RepositoryDetail.LatestScan"/> 为本次运行）。</returns>
    Task<RepositoryDetail> ScanAsync(string repoPath, CancellationToken cancellationToken = default);

    /// <summary>按仓库分页读取扫描历史摘要（开始时间倒序，不含 Finding 明细）。</summary>
    /// <param name="repoPath">仓库工作区根目录绝对路径。</param>
    /// <param name="limit">每页条数（1..1000）。</param>
    /// <param name="offset">跳过的记录数（≥ 0）。</param>
    /// <param name="cancellationToken">取消标记。</param>
    /// <returns>当前页扫描摘要与仓库内的总记录数。</returns>
    Task<AuditRunPage> QueryScanHistoryAsync(
        string repoPath,
        int limit,
        int offset,
        CancellationToken cancellationToken = default);

    /// <summary>按运行标识读取单次扫描的完整 Finding 明细（脱敏片段与指纹，不含原文）。</summary>
    /// <param name="runId">扫描运行身份标识。</param>
    /// <param name="cancellationToken">取消标记。</param>
    /// <returns>扫描运行聚合；记录不存在时为 <see langword="null"/>。</returns>
    Task<ScanRun?> LoadScanRunAsync(Guid runId, CancellationToken cancellationToken = default);

    /// <summary>分页读取全局白名单条目（登记时间倒序）。</summary>
    /// <param name="limit">每页条数（1..1000）。</param>
    /// <param name="offset">跳过的记录数（≥ 0）。</param>
    /// <param name="cancellationToken">取消标记。</param>
    /// <returns>当前页白名单条目与总记录数。</returns>
    Task<AllowlistPage> QueryAllowlistAsync(int limit, int offset, CancellationToken cancellationToken = default);

    /// <summary>新增一条全局白名单；参数校验与审计写入由 Application / 存储层完成。</summary>
    /// <param name="ruleId">可选：规则 ID 匹配条件。</param>
    /// <param name="pathPattern">可选：文件路径 glob 匹配条件。</param>
    /// <param name="fingerprintHex">可选：64 字符 SHA-256 十六进制指纹。</param>
    /// <param name="reason">登记原因（5 ~ 256 字符）。</param>
    /// <param name="cancellationToken">取消标记。</param>
    /// <returns>已持久化的白名单条目。</returns>
    Task<AllowlistEntry> AddAllowlistAsync(
        string? ruleId,
        string? pathPattern,
        string? fingerprintHex,
        string reason,
        CancellationToken cancellationToken = default);

    /// <summary>按身份标识删除白名单条目。</summary>
    /// <param name="id">条目身份标识。</param>
    /// <param name="cancellationToken">取消标记。</param>
    Task RemoveAllowlistAsync(Guid id, CancellationToken cancellationToken = default);
}
