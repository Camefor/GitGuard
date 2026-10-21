using System.Text.Json;
using GitGuard.Core.Abstractions;
using GitGuard.Core.Domain;
using GitGuard.Core.Model;

namespace GitGuard.Infrastructure.FileSystem;

/// <summary>
/// 待提交扫描标记的文件实现（SRS FR-050）：标记位于 <c>&lt;git-dir&gt;/gitguard/pending-scan.json</c>，
/// 先写同目录临时文件再原子改名，避免 post-commit 读到半写内容。文件为 UTF-8 无 BOM；
/// 内容损坏时按「无标记」处理并尽力清理，使 post-commit 退化为按 <c>--no-verify</c> 登记（宁可多记，不可漏记）。
/// </summary>
public sealed class FilePendingScanStore : IPendingScanStore
{
    private const string FILE_NAME = "pending-scan.json";
    private const string PARENT_SHA_PROPERTY = "parent_sha";
    private const string TREE_SHA_PROPERTY = "tree_sha";
    private const string SKIPPED_PROPERTY = "skipped";
    private const string RECORDED_AT_PROPERTY = "recorded_at";

    private readonly string _filePath;

    /// <summary>初始化标记存储。</summary>
    /// <param name="stateDirectory">标记目录（组合根传入 <c>&lt;git-dir&gt;/gitguard</c>）。</param>
    /// <exception cref="ArgumentException"><paramref name="stateDirectory"/> 为空或全空白。</exception>
    public FilePendingScanStore(string stateDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stateDirectory);
        _filePath = Path.Combine(Path.GetFullPath(stateDirectory), FILE_NAME);
    }

    /// <summary>标记文件的绝对路径（测试与诊断用）。</summary>
    public string FilePath => _filePath;

    /// <inheritdoc />
    public PendingScan? Read()
    {
        if (!File.Exists(_filePath))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllBytes(_filePath));
            var root = document.RootElement;
            string? parentSha = root.GetProperty(PARENT_SHA_PROPERTY).GetString();
            string treeSha = root.GetProperty(TREE_SHA_PROPERTY).GetString()
                ?? throw new InvalidOperationException("tree_sha 不能为 null。");
            bool skipped = root.GetProperty(SKIPPED_PROPERTY).GetBoolean();
            long recordedAtUnixMilliseconds = root.GetProperty(RECORDED_AT_PROPERTY).GetInt64();

            return new PendingScan(
                parentSha,
                treeSha,
                skipped,
                DateTimeOffset.FromUnixTimeMilliseconds(recordedAtUnixMilliseconds));
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException
            or FormatException or ArgumentOutOfRangeException or InvariantViolationException)
        {
            ClearBestEffort();
            return null;
        }
    }

    /// <inheritdoc />
    public void Write(PendingScan pendingScan)
    {
        ArgumentNullException.ThrowIfNull(pendingScan);

        EnsureDirectory();
        string temporaryPath = _filePath + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                using (var writer = new Utf8JsonWriter(stream))
                {
                    writer.WriteStartObject();
                    if (pendingScan.ParentSha is null)
                    {
                        writer.WriteNull(PARENT_SHA_PROPERTY);
                    }
                    else
                    {
                        writer.WriteString(PARENT_SHA_PROPERTY, pendingScan.ParentSha);
                    }

                    writer.WriteString(TREE_SHA_PROPERTY, pendingScan.TreeSha);
                    writer.WriteBoolean(SKIPPED_PROPERTY, pendingScan.Skipped);
                    writer.WriteNumber(RECORDED_AT_PROPERTY, pendingScan.RecordedAt.ToUnixTimeMilliseconds());
                    writer.WriteEndObject();
                }
            }

            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(temporaryPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }

            File.Move(temporaryPath, _filePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    /// <inheritdoc />
    public void Clear()
    {
        if (File.Exists(_filePath))
        {
            File.Delete(_filePath);
        }
    }

    private void EnsureDirectory()
    {
        string? directory = Path.GetDirectoryName(_filePath);
        if (string.IsNullOrEmpty(directory))
        {
            return;
        }

        if (OperatingSystem.IsWindows())
        {
            Directory.CreateDirectory(directory);
        }
        else
        {
            Directory.CreateDirectory(
                directory,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    private void ClearBestEffort()
    {
        try
        {
            Clear();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 清理失败不影响判定：损坏标记仍会被视为「无标记」，只是下次继续退化
        }
    }
}
