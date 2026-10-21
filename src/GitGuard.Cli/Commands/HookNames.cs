using GitGuard.Core.Model;

namespace GitGuard.Cli.Commands;

/// <summary>
/// <c>--hook</c> 取值解析：支持 <c>pre-commit</c> / <c>pre-push</c>（FR-010）与 <c>post-commit</c>（FR-050 绕过检测）。
/// 未知取值在此拦截为用法错误（7），避免穿透到用例抛出未分类异常（99）。
/// </summary>
internal static class HookNames
{
    /// <summary>解析 <c>--hook</c> 取值。</summary>
    /// <param name="name">选项取值；<see langword="null"/> 表示未指定（交由用例取默认集合）。</param>
    /// <param name="error">失败原因；成功时为 <see langword="null"/>。</param>
    /// <returns>待安装 / 卸载的 Hook 列表；未指定时为 <see langword="null"/>。</returns>
    internal static IReadOnlyList<HookType>? Resolve(string? name, out string? error)
    {
        error = null;
        if (name is null)
        {
            return null;
        }

        string normalized = name.Trim();
        if (string.Equals(normalized, HookType.PreCommit.ToHookName(), StringComparison.OrdinalIgnoreCase))
        {
            return new[] { HookType.PreCommit };
        }

        if (string.Equals(normalized, HookType.PrePush.ToHookName(), StringComparison.OrdinalIgnoreCase))
        {
            return new[] { HookType.PrePush };
        }

        if (string.Equals(normalized, HookType.PostCommit.ToHookName(), StringComparison.OrdinalIgnoreCase))
        {
            return new[] { HookType.PostCommit };
        }

        error = $"未知的 --hook 取值：{name}（可用：pre-commit、pre-push、post-commit）。";
        return null;
    }
}
