using GitGuard.Core.Domain;

namespace GitGuard.Core.Model;

/// <summary>
/// 单个文件的差异变更（检测器输入，模块设计 Secret/Privacy/LocalPathUrl §2.1）。
/// 统一 diff 的 Hunk 在解析层（Application）按行号展开为 <see cref="Lines"/> ——
/// 检测语义只依赖「行号 + 行文本」，Hunk 分组信息不被任何规则使用。
/// </summary>
public sealed class FileChange
{
    /// <summary>初始化文件变更。</summary>
    /// <param name="path">仓库相对路径（分隔符归一为 <c>/</c>）。</param>
    /// <param name="lines">新增 / 修改行；空列表表示该文件无文本变更（检测器直接空跑）。</param>
    /// <param name="commitSha">PrePush 扫描时所属提交的 SHA（40 或 64 位十六进制）；PreCommit / Manual 为 <see langword="null"/>。</param>
    /// <exception cref="ArgumentNullException"><paramref name="path"/> 或 <paramref name="lines"/> 为 <see langword="null"/>。</exception>
    /// <exception cref="InvariantViolationException">路径为空、为绝对路径，或 <paramref name="commitSha"/> 非 40 / 64 位十六进制时抛出。</exception>
    public FileChange(string path, IReadOnlyList<ChangedLine> lines, string? commitSha = null)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(lines);

        // 分隔符统一为 '/'：git diff 三平台均输出 '/'，归一兜底调用方传入的 Windows 书写习惯
        string normalizedPath = path.Replace('\\', '/');
        DomainGuard.Require(!string.IsNullOrWhiteSpace(normalizedPath), "DDD §4.1", "FileChange.Path 不能为空");
        DomainGuard.Require(!System.IO.Path.IsPathRooted(normalizedPath), "DDD §4.1", $"FileChange.Path 必须是仓库相对路径：{normalizedPath}");

        if (commitSha is not null)
        {
            bool validSha = (commitSha.Length == 40 || commitSha.Length == 64)
                && commitSha.All(char.IsAsciiHexDigit);

            DomainGuard.Require(validSha, "DDD §4.1", $"CommitSha 必须为 40 或 64 位十六进制：{commitSha}");
        }

        Path = normalizedPath;
        Lines = lines.ToArray();
        CommitSha = commitSha;
    }

    /// <summary>仓库相对路径（<c>/</c> 分隔）。</summary>
    public string Path { get; }

    /// <summary>新增 / 修改行（保持 diff 中出现的顺序）。</summary>
    public IReadOnlyList<ChangedLine> Lines { get; }

    /// <summary>PrePush 扫描时所属提交的 SHA；PreCommit / Manual 为 <see langword="null"/>。</summary>
    public string? CommitSha { get; }
}
