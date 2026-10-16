using GitGuard.Core.Model;

namespace GitGuard.Application.Scanning;

/// <summary>
/// <see cref="UnifiedDiffParser"/> 的单文件解析结果（交付扫描前的中间态）：
/// 跳过判定（二进制 / 大小）在检测管线入口依据本类型完成，通过后才构造 <see cref="FileChange"/>。
/// </summary>
public sealed class ParsedDiffFile
{
    /// <summary>初始化解析结果。</summary>
    /// <param name="path">仓库相对路径（git 输出原样，<c>/</c> 分隔）。</param>
    /// <param name="lines">新增 / 修改行（保持 diff 出现顺序，行号为新侧 1-based）。</param>
    /// <param name="isBinary">二进制差异标记（<c>Binary files ... differ</c> / <c>GIT binary patch</c>）。</param>
    /// <param name="addedBytes">新增内容字节数（UTF-8，含换行）；skip 大小判定信号。</param>
    /// <param name="commitSha">PrePush 扫描时所属提交的 SHA；PreCommit 为 <see langword="null"/>。</param>
    /// <exception cref="ArgumentException"><paramref name="path"/> 为空或全空白。</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="addedBytes"/> 为负。</exception>
    public ParsedDiffFile(string path, IReadOnlyList<ChangedLine> lines, bool isBinary, long addedBytes, string? commitSha = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentOutOfRangeException.ThrowIfNegative(addedBytes, nameof(addedBytes));

        Path = path;
        Lines = lines;
        IsBinary = isBinary;
        AddedBytes = addedBytes;
        CommitSha = commitSha;
    }

    /// <summary>仓库相对路径（<c>/</c> 分隔）。</summary>
    public string Path { get; }

    /// <summary>新增 / 修改行（保持 diff 出现顺序）。</summary>
    public IReadOnlyList<ChangedLine> Lines { get; }

    /// <summary>二进制差异标记。</summary>
    public bool IsBinary { get; }

    /// <summary>新增内容字节数（UTF-8，含换行）：
    /// DR-04 禁止读取工作区，故以差异中该文件的新增内容作为大小信号——全量新增文件即文件字节数，
    /// 修改文件为变更片段字节数。</summary>
    public long AddedBytes { get; }

    /// <summary>PrePush 扫描时所属提交的 SHA；PreCommit 为 <see langword="null"/>。</summary>
    public string? CommitSha { get; }

    /// <summary>转换为检测器输入（仅在未跳过时调用；路径 / SHA 不变式由 <see cref="FileChange"/> 二次校验）。</summary>
    /// <returns>文件变更。</returns>
    public FileChange ToFileChange() => new FileChange(Path, Lines, CommitSha);
}
