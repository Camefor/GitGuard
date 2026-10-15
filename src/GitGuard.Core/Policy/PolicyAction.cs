namespace GitGuard.Core.Policy;

/// <summary>
/// 策略动作（FR-022 / <c>模块设计/Policy.md</c> §3）。数值按放行程度升序编码，可用于「取最严动作」的聚合。
/// </summary>
public enum PolicyAction
{
    /// <summary>放行：不阻断、不警告（用户可在配置中降级，Critical 除外）。</summary>
    Allow = 0,

    /// <summary>警告：退出码 0，终端提示。</summary>
    Warn = 1,

    /// <summary>阻断：任一 Finding 判为阻断即整次扫描退出码 1。</summary>
    Block = 2,
}
