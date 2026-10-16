using GitGuard.Core;

namespace GitGuard.Application.Scanning;

/// <summary>
/// pre-push stdin 的单行推送信息（SRS FR-030 / <c>06-API接口设计</c> §5.4）：
/// <c>&lt;local-ref&gt; &lt;local-sha&gt; &lt;remote-ref&gt; &lt;remote-sha&gt;</c>。
/// 零 SHA 语义：local 侧全零 = 删除分支（无可扫描提交）；remote 侧全零 = 新分支（范围取 local-sha 的全部可达提交）。
/// </summary>
public sealed class PushUpdate
{
    private const int SHA1_HEX_LENGTH = 40;
    private const int SHA256_HEX_LENGTH = 64;
    private static readonly char[] FIELD_SEPARATORS = new[] { ' ', '\t' };

    /// <summary>初始化推送信息行。</summary>
    /// <param name="localRef">本地引用（如 <c>refs/heads/main</c>）。</param>
    /// <param name="localSha">本地 SHA（40 / 64 位十六进制；全零表示删除）。</param>
    /// <param name="remoteRef">远端引用。</param>
    /// <param name="remoteSha">远端 SHA（40 / 64 位十六进制；全零表示新分支）。</param>
    /// <exception cref="ArgumentException">引用为空或 SHA 不符合 40 / 64 位十六进制契约。</exception>
    public PushUpdate(string localRef, string localSha, string remoteRef, string remoteSha)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(localRef);
        ArgumentException.ThrowIfNullOrWhiteSpace(remoteRef);
        if (!IsValidSha(localSha))
        {
            throw new ArgumentException("local-sha 必须为 40 / 64 位十六进制。", nameof(localSha));
        }

        if (!IsValidSha(remoteSha))
        {
            throw new ArgumentException("remote-sha 必须为 40 / 64 位十六进制。", nameof(remoteSha));
        }

        LocalRef = localRef;
        LocalSha = localSha;
        RemoteRef = remoteRef;
        RemoteSha = remoteSha;
    }

    /// <summary>本地引用。</summary>
    public string LocalRef { get; }

    /// <summary>本地 SHA（全零表示删除分支）。</summary>
    public string LocalSha { get; }

    /// <summary>远端引用。</summary>
    public string RemoteRef { get; }

    /// <summary>远端 SHA（全零表示新分支）。</summary>
    public string RemoteSha { get; }

    /// <summary>删除分支推送（local-sha 全零）：无提交可扫描。</summary>
    public bool IsDeletion => IsZeroSha(LocalSha);

    /// <summary>新分支推送（remote-sha 全零）：范围 = <see cref="LocalSha"/> 的全部可达提交。</summary>
    public bool IsNewBranch => !IsDeletion && IsZeroSha(RemoteSha);

    /// <summary>git rev-list 的版本区间：新分支取 <see cref="LocalSha"/>，否则 <c>&lt;remote-sha&gt;..&lt;local-sha&gt;</c>。</summary>
    public string RevisionRange => IsNewBranch ? LocalSha : $"{RemoteSha}..{LocalSha}";

    /// <summary>
    /// 解析 stdin 全文：空行忽略；字段数不为 4 或 SHA 非法即 FailClosed
    /// （<see cref="GitException.GIT_COMMAND_FAILED"/> → 退出码 10）。异常消息不回显行内容（DR-02）。
    /// </summary>
    /// <param name="pushStdin">stdin 全文；空字符串返回空列表。</param>
    /// <returns>按输入顺序排列的推送信息行。</returns>
    /// <exception cref="ArgumentNullException"><paramref name="pushStdin"/> 为 <see langword="null"/>。</exception>
    /// <exception cref="GitException">存在无法解析的行。</exception>
    public static IReadOnlyList<PushUpdate> ParseAll(string pushStdin)
    {
        ArgumentNullException.ThrowIfNull(pushStdin);

        var updates = new List<PushUpdate>();
        foreach (string rawLine in LineReader.Enumerate(pushStdin))
        {
            string line = rawLine.Trim();
            if (line.Length == 0)
            {
                continue;
            }

            string[] fields = line.Split(FIELD_SEPARATORS, StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length != 4 || !IsValidSha(fields[1]) || !IsValidSha(fields[3]))
            {
                throw new GitException(
                    GitException.GIT_COMMAND_FAILED,
                    "pre-push stdin 推送信息解析失败：期望 4 个字段且 SHA 为 40 / 64 位十六进制；按 FailClosed 处理。");
            }

            updates.Add(new PushUpdate(fields[0], fields[1], fields[2], fields[3]));
        }

        return updates;
    }

    private static bool IsZeroSha(string sha) => sha.All(static character => character == '0');

    private static bool IsValidSha(string sha) =>
        sha.Length is SHA1_HEX_LENGTH or SHA256_HEX_LENGTH && sha.All(char.IsAsciiHexDigit);
}
