namespace GitGuard.Core.Model;

/// <summary>
/// Hook 安装记录状态（<c>05-数据库设计</c> §4.4）；取值与 <c>hook_installations.status</c> 的 CHECK 一一对应。
/// </summary>
public enum HookInstallationStatus
{
    /// <summary>GitGuard 分发脚本当前生效。</summary>
    Active = 0,

    /// <summary>
    /// 被后续安装替换。保留取值以匹配库表 CHECK；当前安装模型对同一 (仓库, Hook) 采用 upsert 刷新同一行，
    /// 不产生 <see cref="Replaced"/> 记录（DDD §3.4 复合主键现实，见 GGD-0008 文档同步）。
    /// </summary>
    Replaced = 1,

    /// <summary>已卸载（分发脚本已删除；原有 Hook 已还原或从未存在）。</summary>
    Uninstalled = 2,
}
