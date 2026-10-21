using System.Collections.Immutable;
using GitGuard.Core.Domain;

namespace GitGuard.Core.Model;

/// <summary>
/// 提交对象的只读元数据（SRS FR-050）：post-commit 绕过检测只关心提交树与父提交，
/// 不读取提交信息、作者等可能包含敏感内容的字段（DR-02）。
/// </summary>
public sealed record CommitMetadata
{
    /// <summary>
    /// 创建提交元数据。
    /// </summary>
    /// <param name="sha">提交 SHA。</param>
    /// <param name="treeSha">提交树 SHA。</param>
    /// <param name="parents">父提交 SHA 列表（合并提交多于一个；根提交为空）。</param>
    /// <exception cref="InvariantViolationException">SHA 格式非法或 <paramref name="parents"/> 含空元素时抛出。</exception>
    public CommitMetadata(string sha, string treeSha, IEnumerable<string> parents)
    {
        ArgumentNullException.ThrowIfNull(parents);

        var parentShas = parents.ToImmutableArray();
        DomainGuard.Require(IsHexSha(sha), "FR-050", $"提交 SHA 非法：{sha}");
        DomainGuard.Require(IsHexSha(treeSha), "FR-050", $"提交树 SHA 非法：{treeSha}");
        DomainGuard.Require(
            parentShas.All(IsHexSha),
            "FR-050",
            "父提交 SHA 列表包含非法取值");

        Sha = sha;
        TreeSha = treeSha;
        Parents = parentShas;
    }

    /// <summary>提交 SHA。</summary>
    public string Sha { get; }

    /// <summary>提交树 SHA。</summary>
    public string TreeSha { get; }

    /// <summary>父提交 SHA 列表（首个元素即 first-parent）。</summary>
    public ImmutableArray<string> Parents { get; }

    private static bool IsHexSha(string? value) =>
        value is { Length: 40 or 64 } && value.All(static character => character is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F');
}
