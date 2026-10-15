namespace GitGuard.Core.Model;

/// <summary>
/// 扫描结果（DDD §4.2）。与 CLI 退出码的一致性由 <see cref="ScanRun"/> 的不变式强制：
/// <c>Clean/Warned ⇒ 0</c>、<c>Blocked ⇒ 1</c>、<c>Failed ⇒ ≥ 10</c>（DR-03 / <c>06-API接口设计</c> §6）。
/// </summary>
public enum ScanResult
{
    /// <summary>干净：无任何 Finding。</summary>
    Clean = 0,

    /// <summary>有发现但无阻断：退出码 0，终端给出警告。</summary>
    Warned = 1,

    /// <summary>存在阻断级发现：退出码 1。</summary>
    Blocked = 2,

    /// <summary>扫描失败（FailClosed）：退出码 ≥ 10。</summary>
    Failed = 3,
}
