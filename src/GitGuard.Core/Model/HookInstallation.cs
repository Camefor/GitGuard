using GitGuard.Core.Domain;

namespace GitGuard.Core.Model;

/// <summary>
/// Hook 安装记录聚合根（DDD §3.4 / FR-010 / FR-011），与 <c>hook_installations</c> 行一一对应。
/// 身份标识为复合主键 (RepoPath, HookType)（DDD §4.1），同一仓库同一 Hook 至多一行有效记录。
/// 不变式（构造期强制）：
/// <list type="bullet">
///   <item><description><c>05-数据库设计 §4.1</c>：<see cref="RepoPath"/> 为规范化绝对路径（去除尾部分隔符）。</description></item>
///   <item><description><c>13-开发规范 §4.1</c>：<see cref="InstalledAt"/> 必须以 UTC 记录。</description></item>
///   <item><description><c>DR-05 / 模块设计/Hook.md §3</c>：<see cref="OriginalHookBackupPath"/> 为相对 hooks 目录的纯文件名，
///   以 <c>.local.bak</c> 结尾；非 <see langword="null"/> 即表示安装时包裹了原 Hook（该文件保存原文）。</description></item>
/// </list>
/// </summary>
public sealed class HookInstallation : IEquatable<HookInstallation>
{
    private HookInstallation(
        string repoPath,
        HookType hookType,
        DateTimeOffset installedAt,
        string version,
        string? originalHookBackupPath,
        HookInstallationStatus status)
    {
        RepoPath = repoPath;
        HookType = hookType;
        InstalledAt = installedAt;
        Version = version;
        OriginalHookBackupPath = originalHookBackupPath;
        Status = status;
    }

    /// <summary>规范化后的仓库绝对路径（去除尾部分隔符，<c>hook_installations.repo_path</c>）。</summary>
    public string RepoPath { get; }

    /// <summary>Hook 类型（复合主键第二部分；存储取值经 <see cref="HookTypeExtensions.ToHookName"/> 映射）。</summary>
    public HookType HookType { get; }

    /// <summary>安装时间（UTC）。</summary>
    public DateTimeOffset InstalledAt { get; }

    /// <summary>执行安装的 GitGuard 版本。</summary>
    public string Version { get; }

    /// <summary>
    /// 原 Hook 备份文件名（相对 hooks 目录，如 <c>pre-commit.local.bak</c>）；
    /// <see langword="null"/> 表示安装时目标 Hook 不存在、或目标已被 GitGuard 托管（无原 Hook 可包裹）。
    /// </summary>
    public string? OriginalHookBackupPath { get; }

    /// <summary>记录状态。</summary>
    public HookInstallationStatus Status { get; }

    /// <summary>
    /// 创建安装记录（状态 <see cref="HookInstallationStatus.Active"/>），执行全部领域不变式校验。
    /// </summary>
    /// <param name="repoPath">仓库绝对路径；尾部分隔符会被规范化去除。</param>
    /// <param name="hookType">Hook 类型。</param>
    /// <param name="installedAt">安装时间（UTC）。</param>
    /// <param name="version">执行安装的 GitGuard 版本（非空）。</param>
    /// <param name="originalHookBackupPath">原 Hook 备份文件名（相对 hooks 目录）；无备份时为 <see langword="null"/>。</param>
    /// <returns>通过全部不变式校验的安装记录。</returns>
    /// <exception cref="ArgumentNullException"><paramref name="repoPath"/> 或 <paramref name="version"/> 为 <see langword="null"/>。</exception>
    /// <exception cref="InvariantViolationException">任一领域不变式被违反时抛出。</exception>
    public static HookInstallation Create(
        string repoPath,
        HookType hookType,
        DateTimeOffset installedAt,
        string version,
        string? originalHookBackupPath = null) =>
        Restore(repoPath, hookType, installedAt, version, originalHookBackupPath, HookInstallationStatus.Active);

    /// <summary>
    /// 以既有字段重建安装记录；供持久化层（<c>IAuditStore</c>）读回时还原原始状态，业务代码不得用于新建。
    /// 其余校验与 <see cref="Create"/> 完全一致。
    /// </summary>
    /// <param name="repoPath">仓库绝对路径；尾部分隔符会被规范化去除。</param>
    /// <param name="hookType">Hook 类型。</param>
    /// <param name="installedAt">安装时间（UTC）。</param>
    /// <param name="version">执行安装的 GitGuard 版本（非空）。</param>
    /// <param name="originalHookBackupPath">原 Hook 备份文件名；无备份时为 <see langword="null"/>。</param>
    /// <param name="status">记录状态。</param>
    /// <returns>通过全部不变式校验的安装记录。</returns>
    /// <exception cref="ArgumentNullException"><paramref name="repoPath"/> 或 <paramref name="version"/> 为 <see langword="null"/>。</exception>
    /// <exception cref="InvariantViolationException">任一领域不变式被违反时抛出。</exception>
    public static HookInstallation Restore(
        string repoPath,
        HookType hookType,
        DateTimeOffset installedAt,
        string version,
        string? originalHookBackupPath,
        HookInstallationStatus status)
    {
        ArgumentNullException.ThrowIfNull(repoPath);
        ArgumentNullException.ThrowIfNull(version);

        DomainGuard.Require(!string.IsNullOrWhiteSpace(repoPath), "05-数据库设计 §4.1", "RepoPath 不能为空");
        DomainGuard.Require(Path.IsPathRooted(repoPath), "05-数据库设计 §4.1", $"RepoPath 必须是绝对路径：{repoPath}");
        DomainGuard.Require(Enum.IsDefined(hookType), "05-数据库设计 §4.4", $"未知的 HookType 取值：{(int)hookType}");
        DomainGuard.Require(Enum.IsDefined(status), "05-数据库设计 §4.4", $"未知的 HookInstallationStatus 取值：{(int)status}");
        DomainGuard.Require(installedAt.Offset == TimeSpan.Zero, "13-开发规范 §4.1", $"InstalledAt 必须以 UTC 记录（当前偏移 {installedAt.Offset}）");
        DomainGuard.Require(!string.IsNullOrWhiteSpace(version), "DDD §3.4", "Version 不能为空");

        if (originalHookBackupPath is not null)
        {
            DomainGuard.Require(!string.IsNullOrWhiteSpace(originalHookBackupPath), "DR-05", "OriginalHookBackupPath 不能为空字符串");
            DomainGuard.Require(
                Path.GetFileName(originalHookBackupPath) == originalHookBackupPath,
                "DR-05",
                $"OriginalHookBackupPath 必须是相对 hooks 目录的纯文件名：{originalHookBackupPath}");
            DomainGuard.Require(
                originalHookBackupPath.EndsWith(HookTypeExtensions.BACKUP_FILE_SUFFIX, StringComparison.Ordinal),
                "DR-05",
                $"OriginalHookBackupPath 必须以 {HookTypeExtensions.BACKUP_FILE_SUFFIX} 结尾：{originalHookBackupPath}");
        }

        string normalizedRepoPath = Path.TrimEndingDirectorySeparator(repoPath);

        return new HookInstallation(normalizedRepoPath, hookType, installedAt, version, originalHookBackupPath, status);
    }

    /// <summary>
    /// 返回状态置为 <see cref="HookInstallationStatus.Uninstalled"/> 的记录副本（FR-011：卸载不删除行，见 05-数据库设计 §7）。
    /// 已为 <see cref="HookInstallationStatus.Uninstalled"/> 时幂等返回等价副本。
    /// </summary>
    /// <returns>状态为已卸载的安装记录。</returns>
    public HookInstallation MarkUninstalled() =>
        new HookInstallation(RepoPath, HookType, InstalledAt, Version, OriginalHookBackupPath, HookInstallationStatus.Uninstalled);

    /// <summary>按身份标识（<see cref="RepoPath"/> + <see cref="HookType"/> 复合主键）判断实体相等性。</summary>
    /// <param name="other">另一安装记录。</param>
    /// <returns>复合主键相同返回 <see langword="true"/>。</returns>
    public bool Equals(HookInstallation? other) =>
        other is not null
        && (ReferenceEquals(this, other)
            || (HookType == other.HookType && string.Equals(RepoPath, other.RepoPath, StringComparison.Ordinal)));

    /// <summary>按身份标识（复合主键）判断实体相等性。</summary>
    /// <param name="obj">比较对象。</param>
    /// <returns>为同一身份的 <see cref="HookInstallation"/> 时返回 <see langword="true"/>。</returns>
    public override bool Equals(object? obj) => Equals(obj as HookInstallation);

    /// <summary>返回复合主键的哈希值。</summary>
    /// <returns><see cref="RepoPath"/> 与 <see cref="HookType"/> 组合的哈希值。</returns>
    public override int GetHashCode() => HashCode.Combine(RepoPath.GetHashCode(StringComparison.Ordinal), (int)HookType);
}
