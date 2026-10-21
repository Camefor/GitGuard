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
    /// <summary>内置默认策略：<see cref="SeverityPolicy.Default"/> + 空白名单 + 空排除列表（未加载配置文件时的行为）。</summary>
    public static ScanPolicy Default { get; } = new ScanPolicy(
        SeverityPolicy.Default,
        ImmutableArray<AllowlistEntry>.Empty,
        ImmutableArray<string>.Empty,
        ImmutableArray<string>.Empty);

    /// <summary>初始化扫描级策略。</summary>
    /// <param name="mapping">严重度 → 动作映射（已通过 Critical 硬下限校验）。</param>
    /// <param name="allowlist">白名单条目（<c>allowlist.entries</c>）；空集合表示无白名单。</param>
    /// <param name="excludePaths">排除的路径 glob（<c>exclude.paths</c>，匹配仓库相对路径）。</param>
    /// <param name="excludeFiles">排除的文件名 glob（<c>exclude.files</c>，匹配任意目录下的文件名）。</param>
    /// <exception cref="ArgumentNullException">任一引用参数为 <see langword="null"/>。</exception>
    public ScanPolicy(
        SeverityPolicy mapping,
        IReadOnlyList<AllowlistEntry> allowlist,
        IReadOnlyList<string> excludePaths,
        IReadOnlyList<string> excludeFiles)
    {
        ArgumentNullException.ThrowIfNull(mapping);
        ArgumentNullException.ThrowIfNull(allowlist);
        ArgumentNullException.ThrowIfNull(excludePaths);
        ArgumentNullException.ThrowIfNull(excludeFiles);

        Mapping = mapping;
        Allowlist = allowlist.ToImmutableArray();
        ExcludePaths = excludePaths.ToImmutableArray();
        ExcludeFiles = excludeFiles.ToImmutableArray();
    }

    /// <summary>严重度 → 动作映射。</summary>
    public SeverityPolicy Mapping { get; }

    /// <summary>白名单条目（策略判定的「ignored_by_allowlist」匹配依据）。</summary>
    public ImmutableArray<AllowlistEntry> Allowlist { get; }

    /// <summary>排除的路径 glob（<c>exclude.paths</c>，匹配仓库相对路径）。</summary>
    public ImmutableArray<string> ExcludePaths { get; }

    /// <summary>排除的文件名 glob（<c>exclude.files</c>，匹配任意目录下的文件名）。</summary>
    public ImmutableArray<string> ExcludeFiles { get; }

    /// <summary>判断仓库相对路径是否命中排除列表（路径 glob 匹配完整路径，文件名 glob 匹配 basename）。</summary>
    /// <param name="repositoryRelativePath">仓库相对路径（<c>/</c> 或 <c>\</c> 分隔均可）。</param>
    /// <returns>命中任一 <c>exclude.paths</c> 或 <c>exclude.files</c> 时返回 <see langword="true"/>。</returns>
    /// <exception cref="ArgumentNullException"><paramref name="repositoryRelativePath"/> 为 <see langword="null"/>。</exception>
    public bool IsExcluded(string repositoryRelativePath)
    {
        ArgumentNullException.ThrowIfNull(repositoryRelativePath);
        if (repositoryRelativePath.Length == 0)
        {
            return false;
        }

        string fileName = GetFileName(repositoryRelativePath);
        return ExcludePaths.Any(pattern => GlobMatcher.IsMatch(pattern, repositoryRelativePath))
            || ExcludeFiles.Any(pattern => GlobMatcher.IsMatch(pattern, fileName));
    }

    private static string GetFileName(string path)
    {
        for (int index = path.Length - 1; index >= 0; index--)
        {
            if (path[index] is '/' or '\\')
            {
                return path.Substring(index + 1);
            }
        }

        return path;
    }
}
