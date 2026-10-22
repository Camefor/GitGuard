using System.Text.Json;
using FluentAssertions;
using GitGuard.Core.Model;
using GitGuard.Infrastructure.FileSystem;

namespace GitGuard.Infrastructure.Tests;

/// <summary>
/// 待提交扫描标记文件实现测试（SRS FR-050 / 模块设计/Hook.md §3）：真实文件系统验证
/// 原子写入、字段往返、损坏内容降级为「无标记」并清理，以及 Unix 下 0600 权限。
/// </summary>
public sealed class FilePendingScanStoreTests
{
    private const string PARENT_SHA = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string TREE_SHA = "cccccccccccccccccccccccccccccccccccccccc";

    private static readonly DateTimeOffset _sampleRecordedAt = new DateTimeOffset(2026, 10, 16, 8, 30, 0, 123, TimeSpan.Zero);

    [Fact]
    public void WriteThenRead_RoundTripsAllFields()
    {
        using var directory = new TempDirectory();
        FilePendingScanStore store = CreateStore(directory);

        store.Write(new PendingScan(PARENT_SHA, TREE_SHA, skipped: true, _sampleRecordedAt));
        PendingScan? loaded = store.Read();

        loaded.Should().NotBeNull();
        loaded!.ParentSha.Should().Be(PARENT_SHA);
        loaded.TreeSha.Should().Be(TREE_SHA);
        loaded.Skipped.Should().BeTrue();
        loaded.RecordedAt.Should().Be(_sampleRecordedAt);
    }

    [Fact]
    public void Write_NullParent_RoundTripsNullParent()
    {
        using var directory = new TempDirectory();
        FilePendingScanStore store = CreateStore(directory);

        store.Write(new PendingScan(parentSha: null, TREE_SHA, skipped: false, _sampleRecordedAt));

        PendingScan loaded = store.Read() ?? throw new InvalidOperationException("标记应可读回。");
        loaded.ParentSha.Should().BeNull();
    }

    [Fact]
    public void Read_MissingFile_ReturnsNull()
    {
        using var directory = new TempDirectory();
        FilePendingScanStore store = CreateStore(directory);

        store.Read().Should().BeNull();
    }

    [Theory]
    [InlineData("{ not json")]
    [InlineData("")]
    [InlineData("{\"parent_sha\":null}")]
    [InlineData("{\"parent_sha\":null,\"tree_sha\":\"cccccccccccccccccccccccccccccccccccccccc\",\"skipped\":\"yes\",\"recorded_at\":0}")]
    [InlineData("{\"parent_sha\":null,\"tree_sha\":\"not-a-sha\",\"skipped\":false,\"recorded_at\":0}")]
    public void Read_CorruptContent_ReturnsNullAndDeletesFile(string content)
    {
        using var directory = new TempDirectory();
        FilePendingScanStore store = CreateStore(directory);
        Directory.CreateDirectory(Path.GetDirectoryName(store.FilePath)
            ?? throw new InvalidOperationException("标记文件路径缺少父目录。"));
        File.WriteAllText(store.FilePath, content);

        store.Read().Should().BeNull("损坏标记必须按无标记处理（宁可多记绕过）");
        File.Exists(store.FilePath).Should().BeFalse("损坏标记应被尽力清理");
    }

    [Fact]
    public void Write_CreatesStateDirectoryAndLeavesNoTemporaryFile()
    {
        using var directory = new TempDirectory();
        string stateDirectory = Path.Combine(directory.Root, "nested", "gitguard");
        var store = new FilePendingScanStore(stateDirectory);

        store.Write(new PendingScan(PARENT_SHA, TREE_SHA, skipped: false, _sampleRecordedAt));

        Directory.Exists(stateDirectory).Should().BeTrue();
        Directory.GetFiles(stateDirectory).Should().Equal(store.FilePath);
    }

    [Fact]
    public void Write_OverwritesExistingMarker()
    {
        using var directory = new TempDirectory();
        FilePendingScanStore store = CreateStore(directory);
        store.Write(new PendingScan(PARENT_SHA, TREE_SHA, skipped: false, _sampleRecordedAt));

        store.Write(new PendingScan(PARENT_SHA, TREE_SHA, skipped: true, _sampleRecordedAt.AddMinutes(1)));

        PendingScan loaded = store.Read() ?? throw new InvalidOperationException("标记应可读回。");
        loaded.Skipped.Should().BeTrue();
        loaded.RecordedAt.Should().Be(_sampleRecordedAt.AddMinutes(1));
    }

    [Fact]
    public void Write_FileIsUtf8WithoutBomAndUsesSnakeCaseKeys()
    {
        using var directory = new TempDirectory();
        FilePendingScanStore store = CreateStore(directory);
        store.Write(new PendingScan(PARENT_SHA, TREE_SHA, skipped: false, _sampleRecordedAt));

        byte[] bytes = File.ReadAllBytes(store.FilePath);
        bytes[0].Should().Be((byte)'{', "JSON 文件必须以 '{' 开头（UTF-8 无 BOM）");
        using var document = JsonDocument.Parse(bytes);
        JsonElement root = document.RootElement;
        root.GetProperty("parent_sha").GetString().Should().Be(PARENT_SHA);
        root.GetProperty("tree_sha").GetString().Should().Be(TREE_SHA);
        root.GetProperty("skipped").GetBoolean().Should().BeFalse();
        root.GetProperty("recorded_at").GetInt64().Should().Be(_sampleRecordedAt.ToUnixTimeMilliseconds());
    }

    [Fact]
    public void Write_OnUnix_RestrictsFileToOwnerOnly()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Skip("Windows 无 POSIX 权限位，0600 断言仅在 Unix 运行（08-权限设计 §9）。");
            return;
        }

        using var directory = new TempDirectory();
        FilePendingScanStore store = CreateStore(directory);

        store.Write(new PendingScan(PARENT_SHA, TREE_SHA, skipped: false, _sampleRecordedAt));

        UnixFileMode mode = File.GetUnixFileMode(store.FilePath);
        mode.Should().Be(UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }

    [Fact]
    public void Clear_ExistingFile_DeletesMarker()
    {
        using var directory = new TempDirectory();
        FilePendingScanStore store = CreateStore(directory);
        store.Write(new PendingScan(PARENT_SHA, TREE_SHA, skipped: false, _sampleRecordedAt));

        store.Clear();

        File.Exists(store.FilePath).Should().BeFalse();
        store.Read().Should().BeNull();
    }

    [Fact]
    public void Clear_MissingFile_DoesNotThrow()
    {
        using var directory = new TempDirectory();
        FilePendingScanStore store = CreateStore(directory);

        Action act = () => store.Clear();

        act.Should().NotThrow();
    }

    [Fact]
    public void FilePath_IsAbsolute()
    {
        using var directory = new TempDirectory();

        FilePendingScanStore store = CreateStore(directory);

        Path.IsPathRooted(store.FilePath).Should().BeTrue();
        Path.GetFileName(store.FilePath).Should().Be("pending-scan.json");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Ctor_EmptyStateDirectory_ThrowsArgumentException(string? stateDirectory)
    {
        Action act = () => _ = new FilePendingScanStore(stateDirectory!);

        act.Should().Throw<ArgumentException>();
    }

    private static FilePendingScanStore CreateStore(TempDirectory directory) =>
        new FilePendingScanStore(Path.Combine(directory.Root, "gitguard"));
}
