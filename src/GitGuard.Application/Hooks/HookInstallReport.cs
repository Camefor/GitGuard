using System.Collections.Immutable;
using GitGuard.Core.Model;

namespace GitGuard.Application.Hooks;

/// <summary>单个 Hook 的安装处置（<c>06-API接口设计</c> §5.1 安装摘要的数据来源）。</summary>
public enum HookInstallDisposition
{
    /// <summary>目标 Hook 不存在：全新写入分发脚本。</summary>
    Installed = 0,

    /// <summary>目标已是 GitGuard 托管脚本：覆盖刷新版本，不再次备份（TC-FR-010-03）。</summary>
    Refreshed = 1,

    /// <summary>目标存在但非托管：先备份为 <c>&lt;hook&gt;.local.bak</c> 再写入分发脚本（TC-FR-010-02 / DR-05）。</summary>
    WrappedLegacy = 2,
}

/// <summary>单个 Hook 的安装结果。</summary>
public sealed record HookInstallResult
{
    /// <summary>初始化安装结果。</summary>
    /// <param name="hookType">Hook 类型。</param>
    /// <param name="disposition">处置方式。</param>
    /// <param name="hookPath">分发脚本写入的绝对路径。</param>
    /// <param name="backupPath">原 Hook 备份的绝对路径；无备份时为 <see langword="null"/>。</param>
    /// <exception cref="ArgumentNullException"><paramref name="hookPath"/> 为 <see langword="null"/>。</exception>
    /// <exception cref="ArgumentException"><paramref name="hookPath"/> 为空或全空白。</exception>
    public HookInstallResult(HookType hookType, HookInstallDisposition disposition, string hookPath, string? backupPath)
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
    public HookInstallDisposition Disposition { get; }

    /// <summary>分发脚本写入的绝对路径。</summary>
    public string HookPath { get; }

    /// <summary>原 Hook 备份的绝对路径（相对 hooks 目录的文件名亦记录于审计 <c>hook_installations</c>）；<see langword="null"/> 表示无备份。</summary>
    public string? BackupPath { get; }
}

/// <summary>一次 <c>gitguard install</c> 的完整结果（供 CLI 渲染摘要与映射退出码）。</summary>
public sealed record HookInstallReport
{
    /// <summary>初始化安装报告。</summary>
    /// <param name="repoPath">仓库绝对路径。</param>
    /// <param name="version">执行安装的 GitGuard 版本。</param>
    /// <param name="results">逐 Hook 的安装结果（保持请求顺序）。</param>
    /// <exception cref="ArgumentNullException">任一引用参数为 <see langword="null"/>。</exception>
    /// <exception cref="ArgumentException"><paramref name="repoPath"/> 或 <paramref name="version"/> 为空或全空白。</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="results"/> 为空集合。</exception>
    public HookInstallReport(string repoPath, string version, IReadOnlyList<HookInstallResult> results)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repoPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(version);
        ArgumentNullException.ThrowIfNull(results);
        ArgumentOutOfRangeException.ThrowIfZero(results.Count, nameof(results));

        RepoPath = repoPath;
        Version = version;
        Results = results.ToImmutableArray();
    }

    /// <summary>仓库绝对路径。</summary>
    public string RepoPath { get; }

    /// <summary>执行安装的 GitGuard 版本。</summary>
    public string Version { get; }

    /// <summary>逐 Hook 的安装结果（保持请求顺序）。</summary>
    public ImmutableArray<HookInstallResult> Results { get; }
}
