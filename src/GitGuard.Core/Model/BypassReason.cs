namespace GitGuard.Core.Model;

/// <summary>
/// 绕过扫描的原因（SRS FR-050 / DR-06）：区分 <c>--no-verify</c> 与 <c>GITGUARD_SKIP=1</c> 两种显式绕过，
/// 作为 <c>BypassDetected</c> 审计事件载荷的扩展字段写入 <c>audit_logs.payload_json</c>。
/// </summary>
public enum BypassReason
{
    /// <summary>提交未经过 GitGuard 扫描（<c>git commit --no-verify</c>，或任何未触发 pre-commit 的提交）。</summary>
    NoVerify = 0,

    /// <summary>pre-commit 被执行，但按 <c>GITGUARD_SKIP=1</c> 显式跳过扫描。</summary>
    GitGuardSkip = 1,
}
