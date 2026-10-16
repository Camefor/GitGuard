using System.Collections.Immutable;
using GitGuard.Core.Model;

namespace GitGuard.Application.Hooks;

/// <summary>单个 Hook 目标的探测结果分类（<c>06-API接口设计</c> §5.3「每个 Hook 文件签名」的展示口径）。</summary>
public enum HookStatusKind
{
    /// <summary>目标不存在：GitGuard 未安装该 Hook。</summary>
    Missing = 0,

    /// <summary>GitGuard 托管脚本（首两行含 <c># gitguard-managed v</c> 标记）。</summary>
    Managed = 1,

    /// <summary>存在非托管文本脚本：安装将备份包裹（TC-FR-010-02）。</summary>
    Legacy = 2,

    /// <summary>目标为二进制文件：安装需 <c>--force</c> 才可接管（TC-FR-010-05）。</summary>
    Binary = 3,

    /// <summary>目标为符号链接：安装需 <c>--force</c> 才可接管。</summary>
    SymbolicLink = 4,
}

/// <summary>单个 Hook 的状态（FR-012 输出项）。</summary>
public sealed record HookStatus
{
    /// <summary>初始化 Hook 状态。</summary>
    /// <param name="hookType">Hook 类型。</param>
    /// <param name="hookPath">目标 Hook 绝对路径。</param>
    /// <param name="kind">探测结果分类。</param>
    /// <param name="managedVersion">托管标记行声明的 GitGuard 版本；非托管或无标记时为 <see langword="null"/>。</param>
    /// <exception cref="ArgumentException"><paramref name="hookPath"/> 为空或全空白。</exception>
    public HookStatus(HookType hookType, string hookPath, HookStatusKind kind, string? managedVersion)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(hookPath);

        HookType = hookType;
        HookPath = hookPath;
        Kind = kind;
        ManagedVersion = managedVersion;
    }

    /// <summary>Hook 类型。</summary>
    public HookType HookType { get; }

    /// <summary>目标 Hook 绝对路径。</summary>
    public string HookPath { get; }

    /// <summary>探测结果分类。</summary>
    public HookStatusKind Kind { get; }

    /// <summary>托管脚本标记行中的 GitGuard 版本（如 <c>0.1.0</c>）；非托管时为 <see langword="null"/>。</summary>
    public string? ManagedVersion { get; }

    /// <summary>是否存在需要接管的 Hook（存在但未被 GitGuard 托管）→ CLI 映射退出码 6。</summary>
    public bool NeedsAttention => Kind is HookStatusKind.Legacy or HookStatusKind.Binary or HookStatusKind.SymbolicLink;
}

/// <summary>
/// 一次 <c>gitguard status</c> 的完整结果（FR-012 / <c>06-API接口设计</c> §5.3）：
/// Git 版本、<c>core.hooksPath</c>、逐 Hook 文件签名、GitGuard 版本与配置文件加载链。
/// </summary>
public sealed record StatusReport
{
    /// <summary>初始化状态报告。</summary>
    /// <param name="repoPath">仓库工作区根目录绝对路径。</param>
    /// <param name="gitVersion">本机 git 版本行（如 <c>git version 2.43.0</c>）。</param>
    /// <param name="configuredHooksPath"><c>core.hooksPath</c> 配置值；未配置时为 <see langword="null"/>。</param>
    /// <param name="hooksDirectory">实际检查的 hooks 目录（配置值或 <c>&lt;git-common-dir&gt;/hooks</c>）。</param>
    /// <param name="hooks">逐 Hook 状态（pre-commit / pre-push）。</param>
    /// <param name="gitGuardVersion">GitGuard 版本。</param>
    /// <param name="configFiles">配置文件加载链（低优先级在前）；空表示全部使用内置默认。</param>
    /// <param name="warnings">配置宽松解析警告。</param>
    /// <exception cref="ArgumentNullException">任一引用参数为 <see langword="null"/>。</exception>
    /// <exception cref="ArgumentException"><paramref name="repoPath"/> / <paramref name="gitVersion"/> / <paramref name="hooksDirectory"/> / <paramref name="gitGuardVersion"/> 为空或全空白。</exception>
    public StatusReport(
        string repoPath,
        string gitVersion,
        string? configuredHooksPath,
        string hooksDirectory,
        IReadOnlyList<HookStatus> hooks,
        string gitGuardVersion,
        IReadOnlyList<string> configFiles,
        IReadOnlyList<string> warnings)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repoPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(gitVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(hooksDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(gitGuardVersion);
        ArgumentNullException.ThrowIfNull(hooks);
        ArgumentNullException.ThrowIfNull(configFiles);
        ArgumentNullException.ThrowIfNull(warnings);

        RepoPath = repoPath;
        GitVersion = gitVersion;
        ConfiguredHooksPath = configuredHooksPath;
        HooksDirectory = hooksDirectory;
        Hooks = hooks.ToImmutableArray();
        GitGuardVersion = gitGuardVersion;
        ConfigFiles = configFiles.ToImmutableArray();
        Warnings = warnings.ToImmutableArray();
    }

    /// <summary>仓库工作区根目录绝对路径。</summary>
    public string RepoPath { get; }

    /// <summary>本机 git 版本行（如 <c>git version 2.43.0</c>）。</summary>
    public string GitVersion { get; }

    /// <summary><c>core.hooksPath</c> 配置值；未配置时为 <see langword="null"/>（回退 <see cref="HooksDirectory"/>）。</summary>
    public string? ConfiguredHooksPath { get; }

    /// <summary>实际检查的 hooks 目录。</summary>
    public string HooksDirectory { get; }

    /// <summary>逐 Hook 状态（pre-commit / pre-push）。</summary>
    public ImmutableArray<HookStatus> Hooks { get; }

    /// <summary>GitGuard 版本。</summary>
    public string GitGuardVersion { get; }

    /// <summary>配置文件加载链（低优先级在前）；空表示全部使用内置默认。</summary>
    public ImmutableArray<string> ConfigFiles { get; }

    /// <summary>配置宽松解析警告。</summary>
    public ImmutableArray<string> Warnings { get; }

    /// <summary>是否存在需要接管的 Hook（存在但未被 GitGuard 托管）→ CLI 映射退出码 6。</summary>
    public bool NeedsAttention => Hooks.Any(static hook => hook.NeedsAttention);
}
