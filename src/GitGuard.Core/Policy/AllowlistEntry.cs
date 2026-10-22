using GitGuard.Core.Domain;
using GitGuard.Core.Model;

namespace GitGuard.Core.Policy;

/// <summary>
/// 白名单条目聚合根（DDD §3.3 / FR-040），与 <c>allowlist</c> 行一一对应。
/// 不变式：RuleId / PathPattern / Fingerprint 三者至少其一（否则没有匹配条件）；Reason 必填且 5 ~ 256 字符；
/// CreatedAt / CreatedBy 留痕；ExpiresAt 若存在必须晚于 CreatedAt（过期条目不再参与匹配）。
/// </summary>
public sealed class AllowlistEntry : IEquatable<AllowlistEntry>
{
    private AllowlistEntry(
        Guid id,
        string? ruleId,
        string? pathPattern,
        Fingerprint? fingerprint,
        string reason,
        DateTimeOffset createdAt,
        string createdBy,
        DateTimeOffset? expiresAt)
    {
        Id = id;
        RuleId = ruleId;
        PathPattern = pathPattern;
        Fingerprint = fingerprint;
        Reason = reason;
        CreatedAt = createdAt;
        CreatedBy = createdBy;
        ExpiresAt = expiresAt;
    }

    /// <summary>条目身份标识（UUIDv7，与 <c>allowlist.id</c> 对应）。</summary>
    public Guid Id { get; }

    /// <summary>规则 ID 匹配条件；<see langword="null"/> 表示通配。</summary>
    public string? RuleId { get; }

    /// <summary>文件路径 glob 匹配条件；<see langword="null"/> 表示通配。</summary>
    public string? PathPattern { get; }

    /// <summary>指纹精确匹配条件；<see langword="null"/> 表示通配。</summary>
    public Fingerprint? Fingerprint { get; }

    /// <summary>登记原因（必填，≥ 5 字符），用于审计追责。</summary>
    public string Reason { get; }

    /// <summary>登记时间（UTC）。</summary>
    public DateTimeOffset CreatedAt { get; }

    /// <summary>登记人（本机用户名）。</summary>
    public string CreatedBy { get; }

    /// <summary>可选过期时间（UTC）；到期后条目不再匹配。</summary>
    public DateTimeOffset? ExpiresAt { get; }

    /// <summary>
    /// 创建白名单条目；身份标识在构造时生成（UUIDv7）。
    /// </summary>
    /// <param name="ruleId">可选：规则 ID 匹配条件。</param>
    /// <param name="pathPattern">可选：文件路径 glob 匹配条件。</param>
    /// <param name="fingerprint">可选：指纹匹配条件。</param>
    /// <param name="reason">登记原因（5 ~ 256 字符，保存时去除首尾空白）。</param>
    /// <param name="createdAt">登记时间（UTC）。</param>
    /// <param name="createdBy">登记人。</param>
    /// <param name="expiresAt">可选：过期时间（UTC），必须晚于 <paramref name="createdAt"/>。</param>
    /// <returns>通过全部不变式校验的白名单条目。</returns>
    /// <exception cref="ArgumentNullException"><paramref name="reason"/> 或 <paramref name="createdBy"/> 为 <see langword="null"/>。</exception>
    /// <exception cref="InvariantViolationException">任一领域不变式被违反时抛出。</exception>
    public static AllowlistEntry Create(
        string? ruleId,
        string? pathPattern,
        Fingerprint? fingerprint,
        string reason,
        DateTimeOffset createdAt,
        string createdBy,
        DateTimeOffset? expiresAt = null)
    {
        string trimmedReason = ValidateAndNormalize(ruleId, pathPattern, fingerprint, reason, createdAt, createdBy, expiresAt);
        return new AllowlistEntry(Guid.CreateVersion7(), ruleId, pathPattern, fingerprint, trimmedReason, createdAt, createdBy, expiresAt);
    }

    /// <summary>
    /// 从持久化状态重建白名单条目（保留原始身份标识与登记时间，<c>GGD-0030</c>）。
    /// </summary>
    /// <param name="id">条目身份标识（UUIDv7）。</param>
    /// <param name="ruleId">可选：规则 ID 匹配条件。</param>
    /// <param name="pathPattern">可选：文件路径 glob 匹配条件。</param>
    /// <param name="fingerprint">可选：指纹匹配条件。</param>
    /// <param name="reason">登记原因（5 ~ 256 字符）。</param>
    /// <param name="createdAt">登记时间（UTC）。</param>
    /// <param name="createdBy">登记人。</param>
    /// <param name="expiresAt">可选：过期时间（UTC）。</param>
    /// <returns>通过全部不变式校验的白名单条目。</returns>
    /// <exception cref="ArgumentNullException"><paramref name="reason"/> 或 <paramref name="createdBy"/> 为 <see langword="null"/>。</exception>
    /// <exception cref="InvariantViolationException">身份标识为空或任一领域不变式被违反时抛出。</exception>
    public static AllowlistEntry Restore(
        Guid id,
        string? ruleId,
        string? pathPattern,
        Fingerprint? fingerprint,
        string reason,
        DateTimeOffset createdAt,
        string createdBy,
        DateTimeOffset? expiresAt)
    {
        DomainGuard.Require(id != Guid.Empty, "05-数据库设计 §4.3", "AllowlistEntry.Id 不能为空");
        string trimmedReason = ValidateAndNormalize(ruleId, pathPattern, fingerprint, reason, createdAt, createdBy, expiresAt);
        return new AllowlistEntry(id, ruleId, pathPattern, fingerprint, trimmedReason, createdAt, createdBy, expiresAt);
    }

    /// <summary>按身份标识（<see cref="Id"/>）判断实体相等性。</summary>
    /// <param name="other">另一白名单条目。</param>
    /// <returns>身份标识相同返回 <see langword="true"/>。</returns>
    public bool Equals(AllowlistEntry? other) =>
        other is not null && (ReferenceEquals(this, other) || Id == other.Id);

    /// <summary>按身份标识（<see cref="Id"/>）判断实体相等性。</summary>
    /// <param name="obj">比较对象。</param>
    /// <returns>为同一身份的 <see cref="AllowlistEntry"/> 时返回 <see langword="true"/>。</returns>
    public override bool Equals(object? obj) => Equals(obj as AllowlistEntry);

    /// <summary>返回身份标识的哈希值。</summary>
    /// <returns><see cref="Id"/> 的哈希值。</returns>
    public override int GetHashCode() => Id.GetHashCode();

    /// <summary>校验白名单条目不变式并返回去除首尾空白的登记原因。</summary>
    private static string ValidateAndNormalize(
        string? ruleId,
        string? pathPattern,
        Fingerprint? fingerprint,
        string reason,
        DateTimeOffset createdAt,
        string createdBy,
        DateTimeOffset? expiresAt)
    {
        ArgumentNullException.ThrowIfNull(reason);
        ArgumentNullException.ThrowIfNull(createdBy);

        DomainGuard.Require(ruleId is not null || pathPattern is not null || fingerprint is not null, "DDD §3.3", "AllowlistEntry 至少需要 RuleId / PathPattern / Fingerprint 之一作为匹配条件");

        if (ruleId is not null)
        {
            DomainGuard.Require(RuleIdFormat.IsValid(ruleId), "DDD §3.2", $"规则 ID 不符合命名契约：{ruleId}");
        }

        if (pathPattern is not null)
        {
            DomainGuard.Require(!string.IsNullOrWhiteSpace(pathPattern), "DDD §3.3", "PathPattern 不能为空字符串");
        }

        if (fingerprint is not null)
        {
            DomainGuard.Require(fingerprint.Value.Hex.Length == 64, "DDD §3.3", "Fingerprint 无效：必须为 64 字符 SHA-256 十六进制文本");
        }

        string trimmedReason = reason.Trim();
        DomainGuard.Require(trimmedReason.Length >= 5, "DDD §3.3", $"Reason 必填且至少 5 字符（当前 {trimmedReason.Length} 字符）");
        DomainGuard.Require(trimmedReason.Length <= 256, "SRS 数据模型", $"Reason 不能超过 256 字符（当前 {trimmedReason.Length} 字符）");

        DomainGuard.Require(createdAt.Offset == TimeSpan.Zero, "13-开发规范 §4.1", $"CreatedAt 必须以 UTC 记录（当前偏移 {createdAt.Offset}）");
        DomainGuard.Require(!string.IsNullOrWhiteSpace(createdBy), "DDD §3.3", "CreatedBy 不能为空");

        if (expiresAt is { } expiry)
        {
            DomainGuard.Require(expiry.Offset == TimeSpan.Zero, "13-开发规范 §4.1", $"ExpiresAt 必须以 UTC 记录（当前偏移 {expiry.Offset}）");
            DomainGuard.Require(expiry > createdAt, "DDD §3.3", "ExpiresAt 必须晚于 CreatedAt");
        }

        return trimmedReason;
    }
}
