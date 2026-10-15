namespace GitGuard.Core.Model;

/// <summary>
/// 扫描类型（DDD §4.2）。取值与 <c>scan_runs.scan_type</c> 的 CHECK 约束一一对应。
/// </summary>
public enum ScanType
{
    /// <summary>提交前扫描（来源 <c>git diff --cached</c>，DR-04）。</summary>
    PreCommit = 0,

    /// <summary>推送前扫描（推送范围内的提交差异）。</summary>
    PrePush = 1,

    /// <summary>用户手动触发的扫描。</summary>
    Manual = 2,

    /// <summary>绕过事件登记（<c>--no-verify</c> / <c>GITGUARD_SKIP=1</c>，DR-06）；按 DDD §4.2 不允许携带 Finding。</summary>
    Bypass = 3,
}
