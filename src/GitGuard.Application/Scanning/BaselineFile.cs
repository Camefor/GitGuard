using System.Text.Json;
using GitGuard.Core;
using GitGuard.Core.Model;

namespace GitGuard.Application.Scanning;

/// <summary>
/// 基线文件（FR-061）读写：<c>gitguard.baseline.v1</c> JSON。
/// 基线以 HMAC 指纹为唯一键；生成时按指纹去重并排序，消费时只信任指纹字段。
/// 文件写入采用同目录临时文件 + 原子替换，失败统一抛 <see cref="BaselineException"/>（退出码 31）。
/// 基线文件不包含任何敏感原文（DR-02），仅含规则 ID、相对路径与指纹。
/// </summary>
internal static class BaselineFile
{
    /// <summary>默认基线文件名（仓库根目录）。</summary>
    internal const string DEFAULT_FILE_NAME = ".gitguard-baseline.json";

    private const string SCHEMA = "gitguard.baseline.v1";

    /// <summary>解析仓库根目录下的默认基线路径。</summary>
    /// <param name="repositoryRoot">仓库根目录绝对路径。</param>
    /// <returns>默认基线文件绝对路径。</returns>
    internal static string ResolveDefaultPath(string repositoryRoot) =>
        Path.Combine(repositoryRoot, DEFAULT_FILE_NAME);

    /// <summary>加载基线文件并返回指纹集合。</summary>
    /// <param name="path">基线文件绝对路径。</param>
    /// <returns>指纹集合（大小写敏感、已去重）。</returns>
    /// <exception cref="BaselineException">文件读取失败或内容不符合 <c>gitguard.baseline.v1</c> 契约。</exception>
    internal static IReadOnlySet<string> Load(string path) =>
        LoadEntries(path)
            .Select(static entry => entry.Fingerprint)
            .ToHashSet(StringComparer.Ordinal);

    /// <summary>把本次扫描的发现项写入基线文件（按指纹去重、按指纹排序）。</summary>
    /// <param name="path">基线文件绝对路径。</param>
    /// <param name="findings">本次扫描的全部发现项。</param>
    /// <returns>写入的指纹条目数。</returns>
    /// <exception cref="BaselineException">路径非法或写入失败。</exception>
    internal static int Save(string path, IReadOnlyList<Finding> findings)
    {
        // 同一指纹可能命中多个位置：基线以指纹为单位，保留排序后的首条元数据即可
        var entries = findings
            .GroupBy(static finding => finding.Fingerprint.Hex, StringComparer.Ordinal)
            .Select(static group => group
                .OrderBy(static finding => finding.FilePath, StringComparer.Ordinal)
                .ThenBy(static finding => finding.RuleId, StringComparer.Ordinal)
                .First())
            .Select(static finding => new Entry(finding.Fingerprint.Hex, finding.RuleId, finding.FilePath))
            .OrderBy(static entry => entry.Fingerprint, StringComparer.Ordinal)
            .ToList();

        Write(path, entries);
        return entries.Count;
    }

    /// <summary>向基线文件增量登记一条指纹（GGD-0062）：已存在时无副作用，否则合并后原子重写。</summary>
    /// <param name="path">基线文件绝对路径。</param>
    /// <param name="fingerprintHex">64 位小写十六进制指纹（由调用方校验）。</param>
    /// <param name="ruleId">规则 ID（写入条目元数据）。</param>
    /// <param name="filePath">仓库相对路径（写入条目元数据）。</param>
    /// <returns>新增条数为 1；指纹已在基线中时为 0。</returns>
    /// <exception cref="BaselineException">基线文件读取 / 写入失败。</exception>
    internal static int AddEntry(string path, string fingerprintHex, string ruleId, string filePath)
    {
        var entries = File.Exists(path)
            ? LoadEntries(path).ToList()
            : new List<Entry>();

        if (entries.Any(entry => entry.Fingerprint == fingerprintHex))
        {
            return 0;
        }

        entries.Add(new Entry(fingerprintHex, ruleId, filePath));
        entries.Sort(static (left, right) => string.CompareOrdinal(left.Fingerprint, right.Fingerprint));
        Write(path, entries);
        return 1;
    }

    /// <summary>读取基线文件全部条目（含指纹、规则 ID 与相对路径元数据）。</summary>
    /// <param name="path">基线文件绝对路径。</param>
    /// <returns>按文件顺序排列的条目列表。</returns>
    /// <exception cref="BaselineException">文件读取失败或内容不符合契约。</exception>
    internal static IReadOnlyList<Entry> LoadEntries(string path)
    {
        string json;
        try
        {
            json = File.ReadAllText(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            throw new BaselineException(
                BaselineException.BASELINE_INVALID,
                $"读取基线文件失败：{path}（{ex.Message}）",
                ex);
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("schema", out JsonElement schema) ||
                schema.ValueKind != JsonValueKind.String ||
                !string.Equals(schema.GetString(), SCHEMA, StringComparison.Ordinal))
            {
                throw Invalid(path, "schema 必须为 " + SCHEMA);
            }

            if (!root.TryGetProperty("entries", out JsonElement entries) || entries.ValueKind != JsonValueKind.Array)
            {
                throw Invalid(path, "entries 必须为数组");
            }

            var result = new List<Entry>();
            foreach (JsonElement entry in entries.EnumerateArray())
            {
                if (entry.ValueKind != JsonValueKind.Object ||
                    !entry.TryGetProperty("fingerprint", out JsonElement fingerprint) ||
                    fingerprint.ValueKind != JsonValueKind.String)
                {
                    throw Invalid(path, "每个条目必须包含字符串 fingerprint");
                }

                string? hex = fingerprint.GetString();
                if (hex is null || hex.Length != 64 || !hex.All(IsLowerHex))
                {
                    throw Invalid(path, "fingerprint 必须为 64 位小写十六进制");
                }

                result.Add(new Entry(hex, ReadString(entry, "rule_id"), ReadString(entry, "file_path")));
            }

            return result;
        }
        catch (JsonException ex)
        {
            throw new BaselineException(
                BaselineException.BASELINE_INVALID,
                $"基线文件不是合法 JSON：{path}（{ex.Message}）",
                ex);
        }
    }

    /// <summary>把条目按指纹排序后原子写入基线文件。</summary>
    private static void Write(string path, IReadOnlyList<Entry> entries)
    {
        string temporaryPath = path + ".tmp-" + Guid.CreateVersion7().ToString("N");
        try
        {
            using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
            {
                writer.WriteStartObject();
                writer.WriteString("schema", SCHEMA);
                writer.WriteStartArray("entries");
                foreach (Entry entry in entries)
                {
                    writer.WriteStartObject();
                    writer.WriteString("fingerprint", entry.Fingerprint);
                    writer.WriteString("rule_id", entry.RuleId);
                    writer.WriteString("file_path", entry.FilePath);
                    writer.WriteEndObject();
                }

                writer.WriteEndArray();
                writer.WriteEndObject();
            }

            File.Move(temporaryPath, path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            TryDelete(temporaryPath);
            throw new BaselineException(
                BaselineException.BASELINE_WRITE_FAILED,
                $"写入基线文件失败：{path}（{ex.Message}）",
                ex);
        }
    }

    private static string ReadString(JsonElement entry, string property) =>
        entry.TryGetProperty(property, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;

    private static bool IsLowerHex(char value) =>
        value is >= '0' and <= '9' or >= 'a' and <= 'f';

    private static BaselineException Invalid(string path, string reason) =>
        new BaselineException(BaselineException.BASELINE_INVALID, $"基线文件无效：{path}（{reason}）。");

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 临时文件清理失败不改变写入失败语义
        }
    }

    /// <summary>基线条目（指纹 + 元数据）。</summary>
    internal readonly struct Entry
    {
        /// <summary>初始化基线条目。</summary>
        /// <param name="fingerprint">64 位小写十六进制指纹。</param>
        /// <param name="ruleId">规则 ID。</param>
        /// <param name="filePath">仓库相对路径。</param>
        internal Entry(string fingerprint, string ruleId, string filePath)
        {
            Fingerprint = fingerprint;
            RuleId = ruleId;
            FilePath = filePath;
        }

        /// <summary>64 位小写十六进制指纹。</summary>
        internal string Fingerprint { get; }

        /// <summary>规则 ID。</summary>
        internal string RuleId { get; }

        /// <summary>仓库相对路径。</summary>
        internal string FilePath { get; }
    }
}
