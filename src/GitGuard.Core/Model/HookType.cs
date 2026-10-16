namespace GitGuard.Core.Model;

/// <summary>
/// Hook 类型（DDD §4.2 / <c>05-数据库设计</c> §4.4）。枚举名与
/// <c>hook_installations.hook_type</c> 的 CHECK 取值不同：存储取值经 <see cref="HookTypeExtensions.ToHookName"/> 映射。
/// </summary>
public enum HookType
{
    /// <summary>提交前 Hook（FR-010）。</summary>
    PreCommit = 0,

    /// <summary>推送前 Hook（FR-010）。</summary>
    PrePush = 1,

    /// <summary>提交后 Hook（FR-050 绕过检测的承载者，实现顺延至 GGD-0013）；当前仅用于匹配库表 CHECK 取值。</summary>
    PostCommit = 2,
}

/// <summary>
/// <see cref="HookType"/> 与 Git hook 名称（githooks(5) 约定）的双向映射。
/// </summary>
public static class HookTypeExtensions
{
    /// <summary>原 Hook 备份文件名的固定后缀（DR-05 / 模块设计/Hook.md §3）。</summary>
    public const string BACKUP_FILE_SUFFIX = ".local.bak";

    /// <summary>映射为 Git hook 名称（如 <c>pre-commit</c>），即 <c>hook_installations.hook_type</c> 的存储取值。</summary>
    /// <param name="hookType">Hook 类型。</param>
    /// <returns>Git hook 名称。</returns>
    /// <exception cref="Domain.InvariantViolationException"><paramref name="hookType"/> 取值未定义。</exception>
    public static string ToHookName(this HookType hookType) => hookType switch
    {
        HookType.PreCommit => "pre-commit",
        HookType.PrePush => "pre-push",
        HookType.PostCommit => "post-commit",
        _ => throw new Domain.InvariantViolationException($"[05-数据库设计 §4.4] 未知的 HookType 取值：{(int)hookType}"),
    };

    /// <summary>映射为原 Hook 备份文件名（相对 hooks 目录，如 <c>pre-commit.local.bak</c>）。</summary>
    /// <param name="hookType">Hook 类型。</param>
    /// <returns>备份文件名。</returns>
    /// <exception cref="Domain.InvariantViolationException"><paramref name="hookType"/> 取值未定义。</exception>
    public static string ToBackupFileName(this HookType hookType) =>
        hookType.ToHookName() + BACKUP_FILE_SUFFIX;

    /// <summary>解析 Git hook 名称（<c>pre-commit</c> / <c>pre-push</c> / <c>post-commit</c>）。</summary>
    /// <param name="hookName">hook 名称；可为 <see langword="null"/>。</param>
    /// <param name="hookType">解析结果；失败时为 <see langword="default"/>。</param>
    /// <returns>名称为已知 hook 时返回 <see langword="true"/>。</returns>
    public static bool TryParseHookName(string? hookName, out HookType hookType)
    {
        switch (hookName)
        {
            case "pre-commit":
                hookType = HookType.PreCommit;
                return true;
            case "pre-push":
                hookType = HookType.PrePush;
                return true;
            case "post-commit":
                hookType = HookType.PostCommit;
                return true;
            default:
                hookType = default;
                return false;
        }
    }
}
