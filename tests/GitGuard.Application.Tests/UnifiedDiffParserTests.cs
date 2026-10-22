using FluentAssertions;
using GitGuard.Application.Scanning;
using GitGuard.Core;

namespace GitGuard.Application.Tests;

/// <summary>unified diff 解析器单元测试（纯文本处理；FailClosed 与 DR-02 不回显检查）。</summary>
public sealed class UnifiedDiffParserTests
{
    private const string COMMIT_SHA = "0123456789abcdef0123456789abcdef01234567";

    [Fact]
    public void Parse_EmptyText_ReturnsEmptyList()
    {
        IReadOnlyList<ParsedDiffFile> files = UnifiedDiffParser.Parse(string.Empty);

        files.Should().BeEmpty();
    }

    [Fact]
    public void Parse_NewFile_ExtractsPathLinesAndLineNumbers()
    {
        const string diff =
            "diff --git a/config/app.env b/config/app.env\n" +
            "new file mode 100644\n" +
            "index 0000000..ce01362\n" +
            "--- /dev/null\n" +
            "+++ b/config/app.env\n" +
            "@@ -0,0 +1,2 @@\n" +
            "+first\n" +
            "+second\n";

        IReadOnlyList<ParsedDiffFile> files = UnifiedDiffParser.Parse(diff);

        files.Should().ContainSingle();
        ParsedDiffFile file = files[0];
        file.Path.Should().Be("config/app.env");
        file.IsBinary.Should().BeFalse();
        file.Lines.Should().HaveCount(2);
        file.Lines[0].Number.Should().Be(1);
        file.Lines[0].Text.Should().Be("first");
        file.Lines[1].Number.Should().Be(2);
        file.Lines[1].Text.Should().Be("second");
        file.AddedBytes.Should().Be(("first".Length + 1) + ("second".Length + 1));
    }

    [Fact]
    public void Parse_ModifiedFile_TracksNewSideLineNumbersAcrossHunks()
    {
        // 单元层面同时覆盖上下文行 / 删除行的行号游标语义（-U0 实际不产出上下文行，防御性支持）
        const string diff =
            "diff --git a/app.txt b/app.txt\n" +
            "index 1111111..2222222 100644\n" +
            "--- a/app.txt\n" +
            "+++ b/app.txt\n" +
            "@@ -1,3 +1,3 @@\n" +
            "-old a\n" +
            "+new a\n" +
            " keep1\n" +
            " keep2\n" +
            "@@ -10,2 +10,2 @@\n" +
            "-old b\n" +
            "+new b\n" +
            " keep3\n";

        IReadOnlyList<ParsedDiffFile> files = UnifiedDiffParser.Parse(diff);

        ParsedDiffFile file = files.Should().ContainSingle().Subject;
        file.Lines.Should().HaveCount(2);
        file.Lines[0].Number.Should().Be(1);
        file.Lines[0].Text.Should().Be("new a");
        file.Lines[1].Number.Should().Be(10);
        file.Lines[1].Text.Should().Be("new b");
    }

    [Fact]
    public void Parse_DeletedFile_HasNoAddedLines()
    {
        const string diff =
            "diff --git a/gone.txt b/gone.txt\n" +
            "deleted file mode 100644\n" +
            "index ce01362..0000000\n" +
            "--- a/gone.txt\n" +
            "+++ /dev/null\n" +
            "@@ -1,2 +0,0 @@\n" +
            "-doomed\n" +
            "-also doomed\n";

        IReadOnlyList<ParsedDiffFile> files = UnifiedDiffParser.Parse(diff);

        ParsedDiffFile file = files.Should().ContainSingle().Subject;
        file.Path.Should().Be("gone.txt");
        file.Lines.Should().BeEmpty();
        file.AddedBytes.Should().Be(0);
    }

    [Fact]
    public void Parse_RenameWithoutContentChange_UsesRenameTargetPath()
    {
        const string diff =
            "diff --git a/old/name.txt b/new/name.txt\n" +
            "similarity index 100%\n" +
            "rename from old/name.txt\n" +
            "rename to new/name.txt\n";

        IReadOnlyList<ParsedDiffFile> files = UnifiedDiffParser.Parse(diff);

        ParsedDiffFile file = files.Should().ContainSingle().Subject;
        file.Path.Should().Be("new/name.txt");
        file.Lines.Should().BeEmpty();
    }

    [Fact]
    public void Parse_BinaryFilesMarker_IsMarkedBinary()
    {
        const string diff =
            "diff --git a/assets/logo.png b/assets/logo.png\n" +
            "new file mode 100644\n" +
            "index 0000000..3cd4e34\n" +
            "Binary files /dev/null and b/assets/logo.png differ\n";

        IReadOnlyList<ParsedDiffFile> files = UnifiedDiffParser.Parse(diff);

        ParsedDiffFile file = files.Should().ContainSingle().Subject;
        file.Path.Should().Be("assets/logo.png");
        file.IsBinary.Should().BeTrue();
        file.Lines.Should().BeEmpty();
    }

    [Fact]
    public void Parse_GitBinaryPatch_IsMarkedBinary()
    {
        const string diff =
            "diff --git a/img.bin b/img.bin\n" +
            "index 1111111..2222222 100644\n" +
            "GIT binary patch\n" +
            "literal 8\n" +
            "Pc$@dummy1\n";

        IReadOnlyList<ParsedDiffFile> files = UnifiedDiffParser.Parse(diff);

        ParsedDiffFile file = files.Should().ContainSingle().Subject;
        file.IsBinary.Should().BeTrue();
    }

    [Fact]
    public void Parse_QuotedHeaderWithOctalEscapes_DecodesUtf8Path()
    {
        // git 对含特殊字符的路径输出 C 风格引用：\346\226\207\344\273\266 = UTF-8「文件」
        const string diff =
            "diff --git \"a/\\346\\226\\207\\344\\273\\266.txt\" \"b/\\346\\226\\207\\344\\273\\266.txt\"\n" +
            "index 1111111..2222222 100644\n" +
            "--- \"a/\\346\\226\\207\\344\\273\\266.txt\"\n" +
            "+++ \"b/\\346\\226\\207\\344\\273\\266.txt\"\n" +
            "@@ -1 +1 @@\n" +
            "-old\n" +
            "+new\n";

        IReadOnlyList<ParsedDiffFile> files = UnifiedDiffParser.Parse(diff);

        ParsedDiffFile file = files.Should().ContainSingle().Subject;
        file.Path.Should().Be("文件.txt");
        file.Lines.Should().ContainSingle().Which.Text.Should().Be("new");
    }

    [Fact]
    public void Parse_QuotedNewFilePath_OverridesGitHeaderPath()
    {
        // +++ 行是目标路径的权威来源（含空格路径整体加引号）
        const string diff =
            "diff --git a/placeholder.txt b/placeholder.txt\n" +
            "index 1111111..2222222 100644\n" +
            "--- a/placeholder.txt\n" +
            "+++ \"b/pa th.txt\"\n" +
            "@@ -1 +1 @@\n" +
            "-old\n" +
            "+new\n";

        IReadOnlyList<ParsedDiffFile> files = UnifiedDiffParser.Parse(diff);

        files.Should().ContainSingle().Which.Path.Should().Be("pa th.txt");
    }

    [Fact]
    public void Parse_UnquotedPathWithSpace_StripsGitAppendedTab()
    {
        // git 对含空格的路径在 +++ 行末尾追加 TAB（与可选时间戳字段分隔，见 git diff-format）
        const string diff =
            "diff --git a/my file.txt b/my file.txt\n" +
            "new file mode 100644\n" +
            "--- /dev/null\n" +
            "+++ b/my file.txt\t\n" +
            "@@ -0,0 +1 @@\n" +
            "+hello\n";

        IReadOnlyList<ParsedDiffFile> files = UnifiedDiffParser.Parse(diff);

        ParsedDiffFile file = files.Should().ContainSingle().Subject;
        file.Path.Should().Be("my file.txt");
        file.Lines.Should().ContainSingle().Which.Text.Should().Be("hello");
    }

    [Fact]
    public void Parse_QuotedPathWithSpace_IgnoresTrailingTab()
    {
        const string diff =
            "diff --git \"a/some dir/x.txt\" \"b/some dir/x.txt\"\n" +
            "index 1111111..2222222 100644\n" +
            "--- \"a/some dir/x.txt\"\n" +
            "+++ \"b/some dir/x.txt\"\t\n" +
            "@@ -1 +1 @@\n" +
            "-old\n" +
            "+new\n";

        IReadOnlyList<ParsedDiffFile> files = UnifiedDiffParser.Parse(diff);

        files.Should().ContainSingle().Which.Path.Should().Be("some dir/x.txt");
    }

    [Fact]
    public void Parse_MultipleFiles_PreserveDiffOrder()
    {
        const string diff =
            "diff --git a/a.txt b/a.txt\n" +
            "--- a/a.txt\n" +
            "+++ b/a.txt\n" +
            "@@ -0,0 +1 @@\n" +
            "+a\n" +
            "diff --git a/b.txt b/b.txt\n" +
            "--- a/b.txt\n" +
            "+++ b/b.txt\n" +
            "@@ -0,0 +1 @@\n" +
            "+b\n";

        IReadOnlyList<ParsedDiffFile> files = UnifiedDiffParser.Parse(diff);

        files.Select(file => file.Path).Should().ContainInOrder("a.txt", "b.txt");
    }

    [Fact]
    public void Parse_CommitShaArgument_IsAttachedToEveryFile()
    {
        IReadOnlyList<ParsedDiffFile> files = UnifiedDiffParser.Parse(DiffSamples.SINGLE_ADDED_LINE, COMMIT_SHA);

        ParsedDiffFile file = files.Should().ContainSingle().Subject;
        file.CommitSha.Should().Be(COMMIT_SHA);
        file.ToFileChange().CommitSha.Should().Be(COMMIT_SHA);
    }

    [Fact]
    public void Parse_AddsUtf8ByteCountPlusNewlinePerLine()
    {
        const string diff =
            "diff --git a/x.txt b/x.txt\n" +
            "--- a/x.txt\n" +
            "+++ b/x.txt\n" +
            "@@ -0,0 +1,2 @@\n" +
            "+abc\n" +
            "+é\n";

        IReadOnlyList<ParsedDiffFile> files = UnifiedDiffParser.Parse(diff);

        // "abc" = 3 字节 + 换行；"é" = 2 字节（UTF-8）+ 换行
        files.Should().ContainSingle().Which.AddedBytes.Should().Be(3 + 1 + 2 + 1);
    }

    [Fact]
    public void Parse_NoNewlineMarker_DoesNotProduceLine()
    {
        const string diff =
            "diff --git a/x.txt b/x.txt\n" +
            "--- a/x.txt\n" +
            "+++ b/x.txt\n" +
            "@@ -1 +1 @@\n" +
            "-old\n" +
            "\\ No newline at end of file\n" +
            "+new\n" +
            "\\ No newline at end of file\n";

        IReadOnlyList<ParsedDiffFile> files = UnifiedDiffParser.Parse(diff);

        ParsedDiffFile file = files.Should().ContainSingle().Subject;
        file.Lines.Should().ContainSingle().Which.Text.Should().Be("new");
    }

    [Fact]
    public void Parse_CrlfText_TreatsCrAsTerminator()
    {
        const string diff =
            "diff --git a/x.txt b/x.txt\r\n" +
            "--- a/x.txt\r\n" +
            "+++ b/x.txt\r\n" +
            "@@ -1 +1 @@\r\n" +
            "-old\r\n" +
            "+new\r\n";

        IReadOnlyList<ParsedDiffFile> files = UnifiedDiffParser.Parse(diff);

        ParsedDiffFile file = files.Should().ContainSingle().Subject;
        file.Path.Should().Be("x.txt");
        file.Lines.Should().ContainSingle().Which.Text.Should().Be("new");
    }

    [Fact]
    public void Parse_ContentBeforeFirstDiffHeader_IsIgnored()
    {
        const string diff =
            "warning: unrelated preamble\n" +
            DiffSamples.SINGLE_ADDED_LINE;

        IReadOnlyList<ParsedDiffFile> files = UnifiedDiffParser.Parse(diff);

        files.Should().ContainSingle().Which.Path.Should().Be("app.txt");
    }

    [Fact]
    public void Parse_MalformedHunkHeader_ThrowsFailClosedWithoutEchoingContent()
    {
        const string diff =
            "diff --git a/app.txt b/app.txt\n" +
            "--- a/app.txt\n" +
            "+++ b/app.txt\n" +
            "@@ -1 +x @@\n" +
            "+SENTINEL_RAW_CONTENT\n";

        Action act = () => UnifiedDiffParser.Parse(diff);

        GitException exception = act.Should().Throw<GitException>().Which;
        exception.ErrorCode.Should().Be(GitException.GIT_COMMAND_FAILED);
        exception.Message.Should().NotContain("SENTINEL_RAW_CONTENT");
    }

    [Fact]
    public void Parse_MalformedGitHeader_ThrowsFailClosed()
    {
        const string diff = "diff --git broken-header-without-target\n";

        Action act = () => UnifiedDiffParser.Parse(diff);

        act.Should().Throw<GitException>()
            .Which.ErrorCode.Should().Be(GitException.GIT_COMMAND_FAILED);
    }
}
