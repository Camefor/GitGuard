using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using GitGuard.Core;
using GitGuard.Core.Model;

namespace GitGuard.Application.Scanning;

/// <summary>
/// unified diff 解析器（<c>模块设计/Secret.md §2.1</c>：Hunk 在解析层按行号展开为逐文件变更）：
/// 把 <c>git diff --cached -U0</c> / <c>git log --no-walk=unsorted --stdin -p -U0 --root</c> 的输出
/// 解析为 <see cref="ParsedDiffFile"/> 列表。纯文本处理，不读取工作区（DR-04）。
/// FailClosed：git 输出不符合 unified 格式（如 hunk 头无法解析）时抛 <see cref="GitException"/>（GIT_COMMAND_FAILED → 退出码 10），
/// 异常消息不回显差异内容（DR-02）。
/// </summary>
public static partial class UnifiedDiffParser
{
    private const string DIFF_HEADER_PREFIX = "diff --git ";
    private const string OLD_FILE_PREFIX = "--- ";
    private const string NEW_FILE_PREFIX = "+++ ";
    private const string HUNK_HEADER_PREFIX = "@@";
    private const string RENAME_TO_PREFIX = "rename to ";
    private const string COPY_TO_PREFIX = "copy to ";
    private const string BINARY_FILES_PREFIX = "Binary files ";
    private const string BINARY_FILES_SUFFIX = " differ";
    private const string GIT_BINARY_PATCH_LINE = "GIT binary patch";
    private const string DEV_NULL_PATH = "/dev/null";
    private const string OLD_SIDE_PREFIX = "a/";
    private const string NEW_SIDE_PREFIX = "b/";
    private const string TARGET_PATH_SEPARATOR = " " + NEW_SIDE_PREFIX;

    /// <summary>
    /// 解析 unified diff 文本。
    /// </summary>
    /// <param name="diffText">差异文本；空字符串（无变更）返回空列表。</param>
    /// <param name="commitSha">PrePush 扫描时所属提交的 SHA；PreCommit 为 <see langword="null"/>。</param>
    /// <returns>按 diff 出现顺序排列的逐文件解析结果（含无新增行的纯删除文件与二进制文件，交由扫描管线做跳过判定）。</returns>
    /// <exception cref="ArgumentNullException"><paramref name="diffText"/> 为 <see langword="null"/>。</exception>
    /// <exception cref="GitException">输出与 unified 格式不符（hunk 头 / 文件头无法解析），按 FailClosed 处理。</exception>
    public static IReadOnlyList<ParsedDiffFile> Parse(string diffText, string? commitSha = null)
    {
        ArgumentNullException.ThrowIfNull(diffText);

        var files = new List<ParsedDiffFile>();
        FileBuilder? current = null;

        foreach (string line in LineReader.Enumerate(diffText))
        {
            if (line.StartsWith(DIFF_HEADER_PREFIX, StringComparison.Ordinal))
            {
                if (current is not null)
                {
                    files.Add(current.Build());
                }

                current = new FileBuilder(ExtractTargetPathFromGitHeader(line), commitSha);
                continue;
            }

            if (current is null)
            {
                // 首个 diff --git 之前不应有内容；防御性忽略（兼容外部工具包裹 diff 输出的场景）
                continue;
            }

            if (current.InHunk)
            {
                AppendHunkLine(current, line);
                continue;
            }

            if (line.StartsWith(NEW_FILE_PREFIX, StringComparison.Ordinal))
            {
                // +++ 行的 b/ 路径是目标路径的权威来源（重命名 / 含空格路径下 diff --git 头有歧义）
                string? targetPath = ExtractNewFilePath(line);
                if (targetPath is not null)
                {
                    current.Path = targetPath;
                }
            }
            else if (line.StartsWith(OLD_FILE_PREFIX, StringComparison.Ordinal))
            {
                // 旧路径不参与扫描
            }
            else if (line.StartsWith(HUNK_HEADER_PREFIX, StringComparison.Ordinal))
            {
                current.BeginHunk(ParseHunkNewStart(line));
            }
            else if (line.StartsWith(RENAME_TO_PREFIX, StringComparison.Ordinal))
            {
                current.Path = ExtractMetadataPath(line, RENAME_TO_PREFIX);
            }
            else if (line.StartsWith(COPY_TO_PREFIX, StringComparison.Ordinal))
            {
                current.Path = ExtractMetadataPath(line, COPY_TO_PREFIX);
            }
            else if (line.StartsWith(BINARY_FILES_PREFIX, StringComparison.Ordinal) &&
                     line.EndsWith(BINARY_FILES_SUFFIX, StringComparison.Ordinal))
            {
                current.IsBinary = true;
            }
            else if (line == GIT_BINARY_PATCH_LINE)
            {
                current.IsBinary = true;
            }

            // 其余为 index / new file mode / similarity index 等元数据行：忽略
        }

        if (current is not null)
        {
            files.Add(current.Build());
        }

        return files;
    }

    /// <summary>追加 hunk 内容行：<c>@@</c> 开启下一个 hunk；<c>+</c> 记新增行；<c>-</c> / 上下文 / 无换行标记分别消费新侧行号。</summary>
    private static void AppendHunkLine(FileBuilder file, string line)
    {
        if (line.Length == 0)
        {
            return;
        }

        if (line.StartsWith(HUNK_HEADER_PREFIX, StringComparison.Ordinal))
        {
            file.BeginHunk(ParseHunkNewStart(line));
            return;
        }

        switch (line[0])
        {
            case '+':
                file.AddLine(line[1..]);
                break;
            case ' ':
                file.SkipContextLine();
                break;
            default:
                // '-' 删除行 / '\' 无换行标记：不产生新侧内容
                break;
        }
    }

    /// <summary>解析 hunk 头的新侧起始行号：<c>@@ -a[,b] +c[,d] @@ ...</c> 取 c。</summary>
    private static int ParseHunkNewStart(string line)
    {
        Match match = HunkHeaderRegex().Match(line);
        if (!match.Success ||
            !int.TryParse(match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out int start))
        {
            throw CreateParseException();
        }

        return start;
    }

    /// <summary>从 <c>+++</c> 行提取目标路径；删除文件（<c>/dev/null</c>）返回 <see langword="null"/>。</summary>
    private static string? ExtractNewFilePath(string line)
    {
        string pathText = line[NEW_FILE_PREFIX.Length..];
        if (pathText == DEV_NULL_PATH)
        {
            return null;
        }

        if (pathText.StartsWith('"'))
        {
            return StripSidePrefix(DecodeQuotedPath(pathText, 0, out _));
        }

        // 路径含空格时 git 在 +++ 行末尾追加一个 TAB（与可选时间戳字段分隔）；
        // 路径内的原始 TAB 一律以 \t 转义输出，故此处可安全剥离末尾 TAB
        return StripSidePrefix(TrimTrailingTab(pathText));
    }

    /// <summary>剥离 git 追加的末尾 TAB（仅出现在含空格的未引用路径上）。</summary>
    private static string TrimTrailingTab(string path) =>
        path.EndsWith('\t') ? path.Substring(0, path.Length - 1) : path;

    /// <summary>提取重命名 / 复制目标路径（<c>rename to</c> / <c>copy to</c> 行）。</summary>
    private static string ExtractMetadataPath(string line, string prefix)
    {
        string pathText = line[prefix.Length..];
        return pathText.StartsWith('"')
            ? DecodeQuotedPath(pathText, 0, out _)
            : pathText;
    }

    /// <summary>从 <c>diff --git a/&lt;源&gt; b/&lt;目标&gt;</c> 头提取目标路径（兜底来源：二进制文件与无内容变更的重命名没有 +++ 行）。</summary>
    private static string ExtractTargetPathFromGitHeader(string headerLine)
    {
        string rest = headerLine[DIFF_HEADER_PREFIX.Length..];

        if (rest.StartsWith('"'))
        {
            // git 在任一路径需要引用时把两段路径整体加引号（quote_two）：解析第一段后取第二段
            _ = DecodeQuotedPath(rest, 0, out int afterSource);
            int targetStart = afterSource;
            while (targetStart < rest.Length && rest[targetStart] == ' ')
            {
                targetStart++;
            }

            if (targetStart >= rest.Length || rest[targetStart] != '"')
            {
                throw CreateParseException();
            }

            return StripSidePrefix(DecodeQuotedPath(rest, targetStart, out _));
        }

        int separator = rest.IndexOf(TARGET_PATH_SEPARATOR, StringComparison.Ordinal);
        if (separator < 0)
        {
            throw CreateParseException();
        }

        int targetPathStart = separator + TARGET_PATH_SEPARATOR.Length;
        return rest[targetPathStart..];
    }

    /// <summary>剥离 a/ / b/ 侧前缀（引用形式的前缀在引号内，故在解码后处理）。</summary>
    private static string StripSidePrefix(string path) =>
        path.StartsWith(NEW_SIDE_PREFIX, StringComparison.Ordinal) || path.StartsWith(OLD_SIDE_PREFIX, StringComparison.Ordinal)
            ? path[2..]
            : path;

    /// <summary>
    /// 解码 git C 风格引用路径（<c>"a/pa\th"</c>，非 ASCII 字节以八进制转义）；返回解码文本，
    /// <paramref name="nextIndex"/> 指向收尾引号之后。
    /// </summary>
    private static string DecodeQuotedPath(string text, int start, out int nextIndex)
    {
        var bytes = new List<byte>();
        int position = start + 1;

        while (position < text.Length)
        {
            char current = text[position];
            if (current == '"')
            {
                nextIndex = position + 1;
                return Encoding.UTF8.GetString(bytes.ToArray());
            }

            if (current == '\\' && position + 1 < text.Length)
            {
                position = AppendEscape(text, position + 1, bytes);
                continue;
            }

            int runStart = position;
            while (position < text.Length && text[position] is not '"' and not '\\')
            {
                position++;
            }

            bytes.AddRange(Encoding.UTF8.GetBytes(text[runStart..position]));
        }

        throw CreateParseException();
    }

    /// <summary>解码单个 C 风格转义序列；返回下一个待处理位置。</summary>
    private static int AppendEscape(string text, int position, List<byte> bytes)
    {
        char escape = text[position];
        switch (escape)
        {
            case 'a': bytes.Add(0x07); return position + 1;
            case 'b': bytes.Add(0x08); return position + 1;
            case 't': bytes.Add(0x09); return position + 1;
            case 'n': bytes.Add(0x0A); return position + 1;
            case 'v': bytes.Add(0x0B); return position + 1;
            case 'f': bytes.Add(0x0C); return position + 1;
            case 'r': bytes.Add(0x0D); return position + 1;
            case '"': bytes.Add(0x22); return position + 1;
            case '\\': bytes.Add(0x5C); return position + 1;
            default:
                if (escape is >= '0' and <= '7' &&
                    position + 2 < text.Length &&
                    text[position + 1] is >= '0' and <= '7' &&
                    text[position + 2] is >= '0' and <= '7')
                {
                    bytes.Add((byte)(((escape - '0') << 6) | ((text[position + 1] - '0') << 3) | (text[position + 2] - '0')));
                    return position + 3;
                }

                // 未知转义（git 不会输出）：按字面字符编码，保持鲁棒
                bytes.AddRange(Encoding.UTF8.GetBytes(escape.ToString()));
                return position + 1;
        }
    }

    /// <summary>构造 FailClosed 解析异常；不回显差异内容（可能是敏感原文，DR-02）。</summary>
    private static GitException CreateParseException() =>
        new GitException(GitException.GIT_COMMAND_FAILED, "unified diff 输出无法解析（hunk 头或文件头格式不符）；按 FailClosed 处理。");

    [GeneratedRegex(@"^@@ -\d+(?:,\d+)? \+(\d+)(?:,\d+)? @@", RegexOptions.CultureInvariant)]
    private static partial Regex HunkHeaderRegex();

    /// <summary>单文件解析累积器：路径 / 行号游标 / 二进制标记 / 新增字节数。</summary>
    private sealed class FileBuilder
    {
        private readonly List<ChangedLine> _lines = new List<ChangedLine>();
        private int _nextNewLine;
        private long _addedBytes;

        internal FileBuilder(string path, string? commitSha)
        {
            Path = path;
            CommitSha = commitSha;
        }

        internal string Path { get; set; }

        internal string? CommitSha { get; }

        internal bool IsBinary { get; set; }

        internal bool InHunk { get; private set; }

        internal void BeginHunk(int newStartLine)
        {
            _nextNewLine = newStartLine;
            InHunk = true;
        }

        internal void AddLine(string text)
        {
            _lines.Add(new ChangedLine(_nextNewLine, text));
            _nextNewLine++;

            // 大小信号：UTF-8 字节数 + 换行（行终止符已由 LineReader 归一）
            _addedBytes += Encoding.UTF8.GetByteCount(text) + 1;
        }

        internal void SkipContextLine() => _nextNewLine++;

        internal ParsedDiffFile Build() => new ParsedDiffFile(Path, _lines, IsBinary, _addedBytes, CommitSha);
    }
}
