using System.Collections.Immutable;
using GitGuard.Core.Policy;

namespace GitGuard.Application.Scanning;

/// <summary>
/// 扫描级策略参数（模块设计/Policy.md §5）：严重度 → 动作映射与白名单条目的组合，
/// 由组合根（<c>Composition/GitGuardSession</c>）从 <see cref="Configuration.GitGuardConfig"/> 映射后注入扫描用例，
/// 取代扫描管线内置的 <see cref="SeverityPolicy.Default"/>。
/// </summary>
public sealed record ScanPolicy
{
    /// <summary>内置默认策略：<see cref="SeverityPolicy.Default"/> + 空白名单（未加载配置文件时的行为）。</summary>
    public static ScanPolicy Default { get; } = new ScanPolicy(SeverityPolicy.Default, ImmutableArray<AllowlistEntry>.Empty);

    /// <summary>初始化扫描级策略。</summary>
    /// <param name="mapping">严重度 → 动作映射（已通过 Critical 硬下限校验）。</param>
    /// <param name="allowlist">白名单条目（<c>allowlist.entries</c>）；空集合表示无白名单。</param>
    /// <exception cref="ArgumentNullException">任一引用参数为 <see langword="null"/>。</exception>
    public ScanPolicy(SeverityPolicy mapping, IReadOnlyList<AllowlistEntry> allowlist)
    {
        ArgumentNullException.ThrowIfNull(mapping);
        ArgumentNullException.ThrowIfNull(allowlist);

        Mapping = mapping;
        Allowlist = allowlist.ToImmutableArray();
    }

    /// <summary>严重度 → 动作映射。</summary>
    public SeverityPolicy Mapping { get; }

    /// <summary>白名单条目（策略判定的「ignored_by_allowlist」匹配依据）。</summary>
    public ImmutableArray<AllowlistEntry> Allowlist { get; }
}
