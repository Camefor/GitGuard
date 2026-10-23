using GitGuard.Core.Domain;

namespace GitGuard.Core.Model;

/// <summary>
/// 已登记仓库（<c>05-数据库设计</c> §4.8 / GGD-0042）：桌面端本机仓库注册表的一行，
/// 与 <c>repositories</c> 表一一对应。登记只建立视图入口，不修改仓库内容，也不改变 Hook 安装状态。
/// 不变式（构造期强制）：<c>Id</c> 非空、<c>RepoPath</c> 为绝对路径且去除尾部分隔符、
/// <c>DisplayName</c> 非空白、<c>AddedAt</c> 以 UTC 记录。
/// </summary>
public sealed class RegisteredRepository : IEquatable<RegisteredRepository>
{
    private RegisteredRepository(Guid id, string repoPath, string displayName, DateTimeOffset addedAt)
    {
        Id = id;
        RepoPath = repoPath;
        DisplayName = displayName;
        AddedAt = addedAt;
    }

    /// <summary>登记身份标识（UUIDv7，与 <c>repositories.id</c> 对应）。</summary>
    public Guid Id { get; }

    /// <summary>规范化后的仓库工作区根目录绝对路径（去除尾部分隔符）。</summary>
    public string RepoPath { get; }

    /// <summary>列表展示名称；缺省为仓库目录名。</summary>
    public string DisplayName { get; }

    /// <summary>登记时间（UTC）。</summary>
    public DateTimeOffset AddedAt { get; }

    /// <summary>
    /// 创建仓库登记记录；身份标识在构造时生成（UUIDv7），并执行全部领域不变式校验。
    /// </summary>
    /// <param name="repoPath">仓库工作区根目录绝对路径。</param>
    /// <param name="displayName">列表展示名称。</param>
    /// <param name="addedAt">登记时间（UTC）。</param>
    /// <returns>通过全部不变式校验的仓库登记记录。</returns>
    /// <exception cref="ArgumentNullException"><paramref name="repoPath"/> 或 <paramref name="displayName"/> 为 <see langword="null"/>。</exception>
    /// <exception cref="InvariantViolationException">任一领域不变式被违反时抛出。</exception>
    public static RegisteredRepository Create(string repoPath, string displayName, DateTimeOffset addedAt) =>
        Restore(Guid.CreateVersion7(), repoPath, displayName, addedAt);

    /// <summary>
    /// 以既有身份标识重建仓库登记记录；供持久化层（<see cref="Abstractions.IRepositoryStore"/>）读回时还原原始
    /// <see cref="Id"/>，业务代码不得用于新建。其余校验与 <see cref="Create"/> 完全一致。
    /// </summary>
    /// <param name="id">既有身份标识（UUIDv7，来自持久化存储）。</param>
    /// <param name="repoPath">仓库工作区根目录绝对路径。</param>
    /// <param name="displayName">列表展示名称。</param>
    /// <param name="addedAt">登记时间（UTC）。</param>
    /// <returns>通过全部不变式校验的仓库登记记录。</returns>
    /// <exception cref="ArgumentNullException"><paramref name="repoPath"/> 或 <paramref name="displayName"/> 为 <see langword="null"/>。</exception>
    /// <exception cref="InvariantViolationException">任一领域不变式被违反时抛出。</exception>
    public static RegisteredRepository Restore(Guid id, string repoPath, string displayName, DateTimeOffset addedAt)
    {
        ArgumentNullException.ThrowIfNull(repoPath);
        ArgumentNullException.ThrowIfNull(displayName);

        DomainGuard.Require(id != Guid.Empty, "05-数据库设计 §4.8", "RegisteredRepository.Id 不能为空");
        DomainGuard.Require(!string.IsNullOrWhiteSpace(repoPath), "05-数据库设计 §4.8", "RepoPath 不能为空");
        DomainGuard.Require(Path.IsPathRooted(repoPath), "05-数据库设计 §4.8", $"RepoPath 必须是绝对路径：{repoPath}");
        DomainGuard.Require(!string.IsNullOrWhiteSpace(displayName), "05-数据库设计 §4.8", "DisplayName 不能为空");
        DomainGuard.Require(addedAt.Offset == TimeSpan.Zero, "13-开发规范 §4.1", $"AddedAt 必须以 UTC 记录（当前偏移 {addedAt.Offset}）");

        return new RegisteredRepository(
            id,
            Path.TrimEndingDirectorySeparator(repoPath),
            displayName.Trim(),
            addedAt);
    }

    /// <summary>按身份标识（<see cref="Id"/>）判断实体相等性。</summary>
    /// <param name="other">另一条仓库登记。</param>
    /// <returns>身份标识相同返回 <see langword="true"/>。</returns>
    public bool Equals(RegisteredRepository? other) =>
        other is not null && (ReferenceEquals(this, other) || Id == other.Id);

    /// <summary>按身份标识（<see cref="Id"/>）判断实体相等性。</summary>
    /// <param name="obj">比较对象。</param>
    /// <returns>为同一身份的 <see cref="RegisteredRepository"/> 时返回 <see langword="true"/>。</returns>
    public override bool Equals(object? obj) => Equals(obj as RegisteredRepository);

    /// <summary>返回身份标识的哈希值。</summary>
    /// <returns><see cref="Id"/> 的哈希值。</returns>
    public override int GetHashCode() => Id.GetHashCode();
}
