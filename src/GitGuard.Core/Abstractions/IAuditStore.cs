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
    /// （<see cref="PolicyDecision.IgnoredByAllowlist"/> 为 <see langword="true"/> 记 <c>AllowedByAllowlist</c>；
    /// <see cref="PolicyDecision.IgnoredByBaseline"/> 为 <see langword="true"/> 记 <c>AllowedByBaseline</c>；否则记动作名）。
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="run"/> 或 <paramref name="findingDecisions"/> 为 <see langword="null"/>。</exception>
    /// <exception cref="ArgumentException">存在未提供策略判定的 Finding。</exception>
    /// <exception cref="SqliteException">写入失败（含 BUSY 重试耗尽）；调用方按 AuditWriteFailed 降级处理。</exception>
    void SaveScanRun(ScanRun run, IReadOnlyDictionary<Finding, PolicyDecision> findingDecisions);

    /// <summary>
    /// 以单一事务写入一次绕过事件（SRS FR-050 / DR-06）：<c>scan_runs</c> 行（<c>scan_type = Bypass</c>，
    /// 按 DDD §4.2 不携带 Finding）+ <c>BypassDetected</c> 审计事件
    /// （category = <c>Bypass</c>，载荷 <c>{ RunId, CommitSha, DetectedAt, Reason }</c>）。
    /// </summary>
    /// <param name="run">绕过事件对应的扫描运行聚合（必须为 <see cref="ScanType.Bypass"/>）。</param>
    /// <param name="reason">绕过原因（<c>--no-verify</c> / <c>GITGUARD_SKIP=1</c>）。</param>
    /// <param name="commitSha">被判定为绕过扫描的提交 SHA。</param>
    /// <exception cref="ArgumentNullException"><paramref name="run"/> 或 <paramref name="commitSha"/> 为 <see langword="null"/>。</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="run"/> 不是 Bypass 类型、携带 Finding，或 <paramref name="commitSha"/> 为空 / 非十六进制。
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="reason"/> 取值未定义。</exception>
    /// <exception cref="SqliteException">写入失败（含 BUSY 重试耗尽）；调用方按 FailClosed 处理（退出码 14）。</exception>
    void SaveBypassEvent(ScanRun run, BypassReason reason, string commitSha);

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
    /// 按分页与筛选条件查询扫描运行摘要（<c>started_at</c> 倒序，不加载 Finding 明细），
    /// 并返回同一筛选条件下的总记录数（<c>06-API接口设计</c> §5.6）。可选的 <see cref="AuditQuery.RepoPath"/>
    /// 将结果限定到单个仓库，供桌面端扫描历史分页使用。
    /// </summary>
    /// <param name="query">分页与筛选条件。</param>
    /// <returns>当前页运行摘要与总数。</returns>
    /// <exception cref="ArgumentNullException"><paramref name="query"/> 为 <see langword="null"/>。</exception>
    /// <exception cref="SqliteException">读取失败。</exception>
    AuditRunPage QueryScanRuns(AuditQuery query);

    /// <summary>
    /// 读取全部扫描运行（含 Finding 明细，<c>started_at</c> 倒序），供 <c>gitguard audit export</c> 导出。
    /// 返回的 <see cref="Finding.SnippetMasked"/> 保持脱敏，不包含原文。
    /// </summary>
    /// <returns>全部扫描运行（可能为空）。</returns>
    /// <exception cref="SqliteException">读取失败。</exception>
    IReadOnlyList<ScanRun> ListScanRunsForExport();

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

    /// <summary>
    /// 写入一条策略降级事件（08-权限设计 §7）：category = <c>Policy</c>，event_type = <c>PolicyOverride</c>，
    /// 载荷 <c>{ RuleId, From, To }</c>。用于记录 <c>disabled_rules</c> 或严重度级策略相对默认值的降级。
    /// </summary>
    /// <param name="ruleId">规则 ID，或严重度级合成标识 <c>SEVERITY/&lt;Severity&gt;</c>。</param>
    /// <param name="fromAction">降级前的动作。</param>
    /// <param name="toAction">降级后的动作（必须比 <paramref name="fromAction"/> 更宽松）。</param>
    /// <exception cref="ArgumentException"><paramref name="ruleId"/> 为空 / 全空白，或 <paramref name="toAction"/> 未比 <paramref name="fromAction"/> 更宽松。</exception>
    /// <exception cref="ArgumentOutOfRangeException">动作取值未定义。</exception>
    /// <exception cref="SqliteException">写入失败（含 BUSY 重试耗尽），调用方按 FailClosed 处理。</exception>
    void SavePolicyOverrideEvent(string ruleId, PolicyAction fromAction, PolicyAction toAction);

    /// <summary>
    /// 写入一条文件权限修复事件（08-权限设计 §5 / §7）：category = <c>Security</c>，
    /// event_type = <c>FilePermissionFixed</c>，载荷 <c>{ FilePath, PrevMode, NewMode }</c>。
    /// </summary>
    /// <param name="filePath">被修复的文件或目录路径。</param>
    /// <param name="previousMode">修复前的权限表示（Unix 为八进制 mode，Windows 为 SDDL）。</param>
    /// <param name="newMode">修复后的权限表示（Unix 为八进制 mode，Windows 为 SDDL）。</param>
    /// <exception cref="ArgumentException">任一参数为空或全空白。</exception>
    /// <exception cref="SqliteException">写入失败（含 BUSY 重试耗尽）。</exception>
    void SaveFilePermissionFixedEvent(string filePath, string previousMode, string newMode);

    /// <summary>
    /// 写入一条 FailClosed 退出事件（08-权限设计 §7 / 10-异常处理设计 §3）：category = <c>Error</c>，
    /// event_type = <c>FailClosed</c>，载荷 <c>{ ExitCode, ErrorCode, MessageHash }</c>。
    /// </summary>
    /// <param name="exitCode">FailClosed 退出码（≥ 10）。</param>
    /// <param name="errorCode">错误码（<c>ExitCodeMapper</c> 的稳定字符串）。</param>
    /// <param name="messageHash">脱敏后错误消息的 SHA-256 小写十六进制摘要（64 字符）；原文不入库。</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="exitCode"/> 小于 10。</exception>
    /// <exception cref="ArgumentException"><paramref name="errorCode"/> 为空 / 全空白，或 <paramref name="messageHash"/> 非 64 位十六进制。</exception>
    /// <exception cref="SqliteException">写入失败（含 BUSY 重试耗尽）。</exception>
    void SaveFailClosedEvent(int exitCode, string errorCode, string messageHash);
}
