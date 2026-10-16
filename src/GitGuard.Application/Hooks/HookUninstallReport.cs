using System.Collections.Immutable;
using GitGuard.Core.Model;

namespace GitGuard.Application.Hooks;

/// <summary>单个 Hook 的卸载处置。</summary>
public enum HookUninstallDisposition
{
    /// <summary>删除 GitGuard 分发脚本；原本无 Hook（TC-FR-010-01 的还原路径）。</summary>
    Removed = 0,

    /// <summary>删除分发脚本并把 <c>&lt;hook&gt;.local.bak</c> 还原为原 Hook（TC-FR-010-02 / TC-FR-010-04）。</summary>
    RestoredLegacy = 1,

    /// <summary>目标 Hook 不存在或非 GitGuard 托管（未加 <c>--force</c>）：未做任何修改（<c>06-API接口设计</c> §5.2 退出码 5 的来源）。</summary>
    NotInstalled = 2,
}

/// <summary>单个 Hook 的卸载结果。</summary>
public sealed record HookUninstallResult
{
    /// <summary>初始化卸载结果。</summary>
    /// <param name="hookType">Hook 类型。</param>
    /// <param name="disposition">处置方式。</param>
    /// <param name="hookPath">目标 Hook 的绝对路径。</param>
    /// <param name="backupPath">被还原的原 Hook 备份绝对路径；无还原时为 <see langword="null"/>。</param>
    /// <exception cref="ArgumentNullException"><paramref name="hookPath"/> 为 <see langword="null"/>。</exception>
    /// <exception cref="ArgumentException"><paramref name="hookPath"/> 为空或全空白。</exception>
    public HookUninstallResult(HookType hookType, HookUninstallDisposition disposition, string hookPath, string? backupPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(hookPath);

        HookType = hookType;
        Disposition = disposition;
        HookPath = hookPath;
        BackupPath = backupPath;
    }

    /// <summary>Hook 类型。</summary>
    public HookType HookType { get; }

    /// <summary>处置方式。</summary>
    public HookUninstallDisposition Disposition { get; }

    /// <summary>目标 Hook 的绝对路径。</summary>
    public string HookPath { get; }

    /// <summary>被还原的原 Hook 备份绝对路径；<see langword="null"/> 表示未还原。</summary>
    public string? BackupPath { get; }
}

/// <summary>一次 <c>gitguard uninstall</c> 的完整结果（供 CLI 渲染摘要与映射退出码）。</summary>
public sealed record HookUninstallReport
{
    /// <summary>初始化卸载报告。</summary>
    /// <param name="repoPath">仓库绝对路径。</param>
    /// <param name="results">逐 Hook 的卸载结果（保持请求顺序）。</param>
    /// <exception cref="ArgumentNullException">任一引用参数为 <see langword="null"/>。</exception>
    /// <exception cref="ArgumentException"><paramref name="repoPath"/> 为空或全空白。</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="results"/> 为空集合。</exception>
    public HookUninstallReport(string repoPath, IReadOnlyList<HookUninstallResult> results)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repoPath);
        ArgumentNullException.ThrowIfNull(results);
        ArgumentOutOfRangeException.ThrowIfZero(results.Count, nameof(results));

        RepoPath = repoPath;
        Results = results.ToImmutableArray();
    }

    /// <summary>仓库绝对路径。</summary>
    public string RepoPath { get; }

    /// <summary>逐 Hook 的卸载结果（保持请求顺序）。</summary>
    public ImmutableArray<HookUninstallResult> Results { get; }
}
