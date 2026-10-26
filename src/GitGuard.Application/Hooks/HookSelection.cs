using System.Collections.Immutable;
using GitGuard.Application.Configuration;
using GitGuard.Core;
using GitGuard.Core.Model;

namespace GitGuard.Application.Hooks;

/// <summary>
/// Hook 安装集合选择（GGD-0054，模块设计/Hook.md §8）：CLI 与桌面端共享同一套「配置决定缺省集合」规则，
/// 显式传入的 Hook 列表始终优先于 <c>hooks.*</c> 开关，防止两端行为分叉。
/// </summary>
public static class HookSelection
{
    /// <summary>配置禁用全部 Hook 且未显式指定时的统一错误消息（CLI 退出码 13 / 桌面端可展示提示）。</summary>
    public const string NO_HOOKS_ENABLED_MESSAGE =
        "hooks 段已禁用全部 Hook（pre_commit / pre_push / post_commit_bypass_detection 均为 false），无可安装项；"
        + "请调整配置或使用 --hook 显式指定";

    /// <summary>按 <c>hooks.*</c> 注册开关生成缺省安装集合（顺序固定：pre-commit → pre-push → post-commit）。</summary>
    /// <param name="settings">Hook 安装设置。</param>
    /// <returns>启用的 Hook 集合；可能为空。</returns>
    /// <exception cref="ArgumentNullException"><paramref name="settings"/> 为 <see langword="null"/>。</exception>
    public static ImmutableArray<HookType> FromSettings(HooksSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var hooks = ImmutableArray.CreateBuilder<HookType>(3);
        if (settings.PreCommit)
        {
            hooks.Add(HookType.PreCommit);
        }

        if (settings.PrePush)
        {
            hooks.Add(HookType.PrePush);
        }

        if (settings.PostCommitBypassDetection)
        {
            hooks.Add(HookType.PostCommit);
        }

        return hooks.ToImmutable();
    }

    /// <summary>
    /// 解析本次安装的目标 Hook：显式列表优先；否则按配置生成，配置全部禁用时抛出
    /// <see cref="ConfigurationException"/>（FailClosed，不得静默安装空集合）。
    /// </summary>
    /// <param name="settings">Hook 安装设置。</param>
    /// <param name="explicitHooks">显式指定的 Hook（如 CLI <c>--hook</c>）；<see langword="null"/> 表示按配置选择。</param>
    /// <returns>非空的 Hook 列表。</returns>
    /// <exception cref="ConfigurationException">配置禁用全部 Hook 且未显式指定。</exception>
    public static IReadOnlyList<HookType> Resolve(HooksSettings settings, IReadOnlyList<HookType>? explicitHooks)
    {
        if (explicitHooks is not null)
        {
            return explicitHooks;
        }

        ImmutableArray<HookType> configuredHooks = FromSettings(settings);
        if (configuredHooks.IsEmpty)
        {
            throw new ConfigurationException(NO_HOOKS_ENABLED_MESSAGE);
        }

        return configuredHooks;
    }
}
