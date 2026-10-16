using GitGuard.Core.Model;
using GitGuard.Core.Policy;

namespace GitGuard.Core.Abstractions;

/// <summary>
/// 本地审计存储抽象（SAD §5 / 模块设计/Audit.md §2）：扫描运行与发现项的写入、读取。
/// MVP 实现为 <c>GitGuard.Infrastructure.Sqlite.SqliteAuditStore</c>（SQLite 单文件，WAL）。
/// 约定（模块设计/Audit.md §3）：ScanRun 与 Findings 必须单事务写入，禁止产生孤儿记录。
/// </summary>
public interface IAuditStore
{
    /// <summary>
    /// 以单一事务写入一次扫描运行：<c>scan_runs</c> 行 + 全部 <c>findings</c> 行 + <c>ScanCompleted</c> 审计事件。
    /// </summary>
    /// <param name="run">扫描运行聚合（含 <see cref="ScanRun.Findings"/>）。</param>
    /// <param name="findingDecisions">
    /// 每条 Finding 的策略判定，用于写入 <c>findings.action</c> 列
    /// （<see cref="PolicyDecision.IgnoredByAllowlist"/> 为 <see langword="true"/> 记 <c>AllowedByAllowlist</c>，否则记动作名）。
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="run"/> 或 <paramref name="findingDecisions"/> 为 <see langword="null"/>。</exception>
    /// <exception cref="ArgumentException">存在未提供策略判定的 Finding。</exception>
    /// <exception cref="SqliteException">写入失败（含 BUSY 重试耗尽）；调用方按 AuditWriteFailed 降级处理。</exception>
    void SaveScanRun(ScanRun run, IReadOnlyDictionary<Finding, PolicyDecision> findingDecisions);

    /// <summary>按运行身份标识读取扫描运行聚合（含 Findings）。</summary>
    /// <param name="runId">运行身份标识（<c>scan_runs.id</c>）。</param>
    /// <returns>存在则返回重建的聚合（身份标识与原始写入一致）；不存在返回 <see langword="null"/>。</returns>
    /// <exception cref="SqliteException">读取失败。</exception>
    ScanRun? FindScanRun(Guid runId);

    /// <summary>按仓库查询最近的扫描运行（<c>started_at</c> 倒序，含 Findings）。</summary>
    /// <param name="repoPath">规范化后的仓库绝对路径（<c>scan_runs.repo_path</c>）。</param>
    /// <param name="limit">最多返回条数；必须 ≥ 1。</param>
    /// <returns>倒序排列的扫描运行列表（可能为空）。</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="limit"/> 小于 1。</exception>
    /// <exception cref="SqliteException">读取失败。</exception>
    IReadOnlyList<ScanRun> ListRecentScanRuns(string repoPath, int limit);

    /// <summary>
    /// 写入 Hook 安装记录：<c>hook_installations</c> 以 (repo_path, hook_type) 复合主键 upsert
    /// （刷新 installed_at / version / 备份路径并置 <c>Active</c>）+ <c>HookInstalled</c> 审计事件
    /// （category = <c>Hook</c>，08-权限设计 §7），单事务提交。
    /// </summary>
    /// <param name="installation">安装记录聚合（<c>DDD §5</c> 载荷：RepoPath / HookType / Version / HadLegacyHook）。</param>
    /// <exception cref="ArgumentNullException"><paramref name="installation"/> 为 <see langword="null"/>。</exception>
    /// <exception cref="SqliteException">写入失败（含 BUSY 重试耗尽），调用方按 FailClosed 处理。</exception>
    void SaveHookInstallation(HookInstallation installation);

    /// <summary>
    /// 写入 Hook 卸载结果：<c>hook_installations</c> upsert 并置 <c>Uninstalled</c>（行已存在时保留既有
    /// installed_at / version / 备份路径，仅翻转状态，见 05-数据库设计 §7）+ <c>HookUninstalled</c> 审计事件，单事务提交。
    /// </summary>
    /// <param name="installation">卸载记录；行不存在时以其字段插入。</param>
    /// <param name="restoredLegacyHook">是否还原了原有 Hook（FR-011；<c>DDD §5</c> 载荷字段 RestoredLegacyHook）。</param>
    /// <exception cref="ArgumentNullException"><paramref name="installation"/> 为 <see langword="null"/>。</exception>
    /// <exception cref="SqliteException">写入失败（含 BUSY 重试耗尽），调用方按 FailClosed 处理。</exception>
    void SaveHookUninstallation(HookInstallation installation, bool restoredLegacyHook);
}
