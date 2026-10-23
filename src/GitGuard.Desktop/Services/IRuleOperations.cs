using GitGuard.Application.Rules;

namespace GitGuard.Desktop.Services;

/// <summary>
/// 规则目录快照（GGD-0045）：条目与 <c>gitguard rules list</c> 同源（<c>GitGuardSession.GetRuleCatalog</c>），
/// 额外携带 Gitleaks 适配器配置状态，用于规则页解释动态规则为何不在静态目录中。
/// </summary>
public sealed record RuleCatalogSnapshot
{
    /// <summary>初始化规则目录快照。</summary>
    /// <param name="rules">规则条目（内置在前、自定义在后，保持规则集书写顺序）。</param>
    /// <param name="gitleaksEnabled">Gitleaks 适配器是否启用（<c>rules.secrets.use_gitleaks</c>）。</param>
    /// <param name="gitleaksPath">Gitleaks 可执行文件路径；<see langword="null"/> 表示在 PATH 中查找。</param>
    /// <exception cref="ArgumentNullException"><paramref name="rules"/> 为 <see langword="null"/>。</exception>
    public RuleCatalogSnapshot(
        IReadOnlyList<RuleCatalogEntry> rules,
        bool gitleaksEnabled,
        string? gitleaksPath)
    {
        ArgumentNullException.ThrowIfNull(rules);

        Rules = rules;
        GitleaksEnabled = gitleaksEnabled;
        GitleaksPath = gitleaksPath;
    }

    /// <summary>规则条目（与 <c>gitguard rules list</c> 数据一致）。</summary>
    public IReadOnlyList<RuleCatalogEntry> Rules { get; }

    /// <summary>Gitleaks 适配器是否启用。</summary>
    public bool GitleaksEnabled { get; }

    /// <summary>Gitleaks 可执行文件路径；<see langword="null"/> 表示在 PATH 中查找。</summary>
    public string? GitleaksPath { get; }
}

/// <summary>
/// 规则目录数据访问契约（GGD-0045）：为规则页隐藏 <c>GitGuardSession</c> 与配置加载细节，
/// 实现必须在后台线程构建目录（内置规则加载是同步 IO）。ViewModel 单测通过假对象驱动。
/// </summary>
public interface IRuleOperations
{
    /// <summary>读取当前配置下的规则目录与 Gitleaks 适配器状态。</summary>
    /// <param name="cancellationToken">取消标记。</param>
    /// <returns>规则目录快照。</returns>
    Task<RuleCatalogSnapshot> LoadCatalogAsync(CancellationToken cancellationToken = default);
}
