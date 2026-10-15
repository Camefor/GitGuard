using GitGuard.Core.Domain;

namespace GitGuard.Core.Model;

/// <summary>
/// 发现项实体（DDD §4.1），身份由 <see cref="Id"/> 标识，仅作为 <see cref="ScanRun"/> 聚合成员存在。
/// 依据 DDD §9 反模式约束：禁止用原文直接构造 —— <see cref="SnippetMasked"/> 与 <see cref="Fingerprint"/> 必须由
/// <c>Masker</c> / <c>Fingerprinter</c> 产出（DR-02）。
/// </summary>
public sealed class Finding : IEquatable<Finding>
{
    private Finding(
        Guid id,
        string ruleId,
        string filePath,
        int lineNumber,
        Severity severity,
        Fingerprint fingerprint,
        SnippetMasked snippetMasked,
        string? commitSha)
    {
        Id = id;
        RuleId = ruleId;
        FilePath = filePath;
        LineNumber = lineNumber;
        Severity = severity;
        Fingerprint = fingerprint;
        SnippetMasked = snippetMasked;
        CommitSha = commitSha;
    }

    /// <summary>发现项身份标识（UUIDv7，与 <c>findings.id</c> 对应）。</summary>
    public Guid Id { get; }

    /// <summary>命中的规则 ID，必须符合命名契约（<c>13-开发规范</c> §4.1）。</summary>
    public string RuleId { get; }

    /// <summary>相对仓库根的路径（正斜杠分隔），不允许为绝对路径。</summary>
    public string FilePath { get; }

    /// <summary>1-based 行号。</summary>
    public int LineNumber { get; }

    /// <summary>严重度。</summary>
    public Severity Severity { get; }

    /// <summary>脱敏指纹（SHA-256 + Salt）。</summary>
    public Fingerprint Fingerprint { get; }

    /// <summary>已脱敏片段，唯一允许持久化/展示的敏感内容形态。</summary>
    public SnippetMasked SnippetMasked { get; }

    /// <summary>PrePush 扫描时所属提交的 SHA（40 或 64 位十六进制）；PreCommit / Manual 为 <see langword="null"/>。</summary>
    public string? CommitSha { get; }

    /// <summary>
    /// 创建发现项；身份标识在构造时生成（UUIDv7）。
    /// </summary>
    /// <param name="ruleId">命中的规则 ID。</param>
    /// <param name="filePath">相对仓库根的路径。</param>
    /// <param name="lineNumber">1-based 行号。</param>
    /// <param name="severity">严重度。</param>
    /// <param name="fingerprint">由 <c>Fingerprinter</c> 产出的指纹。</param>
    /// <param name="snippetMasked">由 <c>Masker</c> 产出的脱敏片段。</param>
    /// <param name="commitSha">可选：所属提交 SHA（40 或 64 位十六进制）。</param>
    /// <returns>通过全部不变式校验的发现项。</returns>
    /// <exception cref="ArgumentNullException"><paramref name="ruleId"/> 或 <paramref name="filePath"/> 为 <see langword="null"/>。</exception>
    /// <exception cref="InvariantViolationException">任一领域不变式被违反时抛出。</exception>
    public static Finding Create(
        string ruleId,
        string filePath,
        int lineNumber,
        Severity severity,
        Fingerprint fingerprint,
        SnippetMasked snippetMasked,
        string? commitSha = null)
    {
        ArgumentNullException.ThrowIfNull(ruleId);
        ArgumentNullException.ThrowIfNull(filePath);

        DomainGuard.Require(RuleIdFormat.IsValid(ruleId), "DDD §3.2", $"规则 ID 不符合 ^(GGD|USR|GLK)/[A-Z]+/[A-Z0-9_-]+$ 契约：{ruleId}");
        DomainGuard.Require(!string.IsNullOrWhiteSpace(filePath), "DDD §4.1", "FilePath 不能为空");
        DomainGuard.Require(!Path.IsPathRooted(filePath), "DDD §4.1", $"FilePath 必须是相对仓库根的路径：{filePath}");
        DomainGuard.Require(lineNumber >= 1, "DDD §4.1", $"LineNumber 为 1-based，必须 ≥ 1（当前 {lineNumber}）");
        DomainGuard.Require(Enum.IsDefined(severity), "DDD §4.2", $"未知的 Severity 取值：{(int)severity}");
        DomainGuard.Require(fingerprint.Hex.Length == 64, "DR-02", "Fingerprint 无效：必须由 Fingerprinter 产出（64 字符 SHA-256 十六进制）");
        DomainGuard.Require(snippetMasked.Value.Length > 0, "DR-02", "SnippetMasked 无效：必须由 Masker 产出，禁止以原文代替");

        if (commitSha is not null)
        {
            bool validSha = (commitSha.Length == 40 || commitSha.Length == 64)
                && commitSha.All(char.IsAsciiHexDigit);

            DomainGuard.Require(validSha, "DDD §4.1", $"CommitSha 必须为 40 或 64 位十六进制：{commitSha}");
        }

        return new Finding(Guid.CreateVersion7(), ruleId, filePath, lineNumber, severity, fingerprint, snippetMasked, commitSha);
    }

    /// <summary>按身份标识（<see cref="Id"/>）判断实体相等性。</summary>
    /// <param name="other">另一发现项。</param>
    /// <returns>身份标识相同返回 <see langword="true"/>。</returns>
    public bool Equals(Finding? other) =>
        other is not null && (ReferenceEquals(this, other) || Id == other.Id);

    /// <summary>按身份标识（<see cref="Id"/>）判断实体相等性。</summary>
    /// <param name="obj">比较对象。</param>
    /// <returns>为同一身份的 <see cref="Finding"/> 时返回 <see langword="true"/>。</returns>
    public override bool Equals(object? obj) => Equals(obj as Finding);

    /// <summary>返回身份标识的哈希值。</summary>
    /// <returns><see cref="Id"/> 的哈希值。</returns>
    public override int GetHashCode() => Id.GetHashCode();
}
