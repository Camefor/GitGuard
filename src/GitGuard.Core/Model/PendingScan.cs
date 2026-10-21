using GitGuard.Core.Domain;

namespace GitGuard.Core.Model;

/// <summary>
/// 待提交扫描标记（SRS FR-050）：pre-commit 阶段记录「父提交 + 暂存树」的身份，
/// post-commit 阶段据此判断刚发生的提交是否经过 GitGuard 扫描。
/// <para>
/// 提交 SHA 在 pre-commit 时无法可靠预测（提交信息、时间戳尚未确定），因此以
/// <see cref="ParentSha"/>（pre-commit 时的 HEAD，可为空表示首个提交）与
/// <see cref="TreeSha"/>（<c>git write-tree</c> 的暂存树）共同作为预测依据；
/// post-commit 读取实际提交的树与首个父提交进行比对。
/// </para>
/// </summary>
public sealed record PendingScan
{
    /// <summary>
    /// 创建待提交扫描标记。
    /// </summary>
    /// <param name="parentSha">pre-commit 时的 HEAD 提交 SHA；仓库尚无提交时为 <see langword="null"/>。</param>
    /// <param name="treeSha">暂存树 SHA（<c>git write-tree</c>）。</param>
    /// <param name="skipped">是否因 <c>GITGUARD_SKIP=1</c> 跳过扫描。</param>
    /// <param name="recordedAt">记录时间（UTC）。</param>
    /// <exception cref="InvariantViolationException">SHA 格式非法或时间非 UTC 时抛出。</exception>
    public PendingScan(string? parentSha, string treeSha, bool skipped, DateTimeOffset recordedAt)
    {
        DomainGuard.Require(treeSha.Length is 40 or 64, "FR-050", $"暂存树 SHA 长度非法：{treeSha}");
        DomainGuard.Require(IsHex(treeSha), "FR-050", $"暂存树 SHA 必须是十六进制：{treeSha}");
        DomainGuard.Require(
            parentSha is null || (parentSha.Length is 40 or 64 && IsHex(parentSha)),
            "FR-050",
            $"父提交 SHA 非法：{parentSha}");
        DomainGuard.Require(recordedAt.Offset == TimeSpan.Zero, "13-开发规范 §4.1", $"RecordedAt 必须以 UTC 记录（当前偏移 {recordedAt.Offset}）");

        ParentSha = parentSha;
        TreeSha = treeSha;
        Skipped = skipped;
        RecordedAt = recordedAt;
    }

    /// <summary>pre-commit 时的 HEAD 提交 SHA；仓库尚无提交时为 <see langword="null"/>。</summary>
    public string? ParentSha { get; }

    /// <summary>暂存树 SHA（<c>git write-tree</c>）。</summary>
    public string TreeSha { get; }

    /// <summary>是否因 <c>GITGUARD_SKIP=1</c> 跳过扫描。</summary>
    public bool Skipped { get; }

    /// <summary>记录时间（UTC）。</summary>
    public DateTimeOffset RecordedAt { get; }

    private static bool IsHex(string value) =>
        value.All(static character => character is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F');
}
