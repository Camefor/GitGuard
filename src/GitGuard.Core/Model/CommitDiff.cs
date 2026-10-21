using GitGuard.Core.Domain;

namespace GitGuard.Core.Model;

/// <summary>
/// 单个提交的 unified-0 差异（SRS FR-030）：批量取差异时与提交 SHA 绑定，
/// 供推送扫描把每个 <see cref="Finding"/> 归属到产生它的提交。
/// </summary>
public sealed record CommitDiff
{
    /// <summary>创建提交差异。</summary>
    /// <param name="sha">提交 SHA（40 / 64 位十六进制）。</param>
    /// <param name="diffText">unified-0 差异文本；空提交为空字符串。</param>
    /// <exception cref="ArgumentNullException"><paramref name="diffText"/> 为 <see langword="null"/>。</exception>
    /// <exception cref="InvariantViolationException"><paramref name="sha"/> 格式非法。</exception>
    public CommitDiff(string sha, string diffText)
    {
        ArgumentNullException.ThrowIfNull(diffText);
        DomainGuard.Require(IsHexSha(sha), "FR-030", $"提交 SHA 非法：{sha}");

        Sha = sha;
        DiffText = diffText;
    }

    /// <summary>提交 SHA。</summary>
    public string Sha { get; }

    /// <summary>该提交的 unified-0 差异文本；无差异时为空字符串。</summary>
    public string DiffText { get; }

    private static bool IsHexSha(string? value) =>
        value is { Length: 40 or 64 }
        && value.All(static character => character is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F');
}
