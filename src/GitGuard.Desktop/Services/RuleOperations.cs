using System.Collections.Immutable;
using GitGuard.Application.Composition;
using GitGuard.Application.Configuration;
using GitGuard.Application.Rules;

namespace GitGuard.Desktop.Services;

/// <summary>
/// 规则目录数据访问实现（GGD-0045）：以 <c>Task.Run</c> 把配置加载与内置规则加载移出 UI 线程，
/// 复用 <see cref="GitGuardSession.GetRuleCatalog"/> 保证与 <c>gitguard rules list</c> 完全同源。
/// </summary>
public sealed class RuleOperations : IRuleOperations
{
    private readonly GitGuardSession _session;
    private readonly ISettingsOperations? _settingsOperations;

    /// <summary>初始化规则目录数据访问实现。</summary>
    /// <param name="session">应用级组合根（仅用于规则目录与配置快照）。</param>
    /// <param name="settingsOperations">
    /// 桌面设置服务；存在 Gitleaks 覆盖时优先展示桌面端生效值，<see langword="null"/> 时完全采用配置文件。
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="session"/> 为 <see langword="null"/>。</exception>
    public RuleOperations(GitGuardSession session, ISettingsOperations? settingsOperations = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        _session = session;
        _settingsOperations = settingsOperations;
    }

    /// <inheritdoc />
    public Task<RuleCatalogSnapshot> LoadCatalogAsync(CancellationToken cancellationToken = default) =>
        Task.Run(
            () =>
            {
                cancellationToken.ThrowIfCancellationRequested();

                // 与 CLI 共用同一目录构建逻辑；Config 仅用于读取 Gitleaks 开关，不装配检测器
                ImmutableArray<RuleCatalogEntry> catalog = _session.GetRuleCatalog();
                SecretRulesSettings secrets = _session.Config.Secrets;
                DesktopGitleaksSettings? overrideSettings = _settingsOperations?.Current.Gitleaks;
                return new RuleCatalogSnapshot(
                    catalog,
                    overrideSettings?.Enabled ?? secrets.UseGitleaks,
                    overrideSettings is null ? secrets.GitleaksPath : overrideSettings.Path);
            },
            cancellationToken);
}
