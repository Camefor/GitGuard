using System.Buffers;
using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;
using GitGuard.Core.Model;

namespace GitGuard.Application.Audit;

/// <summary>
/// 审计导出文件写出（<c>模块设计/Audit.md</c> §3 导出脱敏）：JSON / CSV 两种格式，
/// 只写出 <see cref="Finding.SnippetMasked"/> 与指纹等脱敏字段，不接触原文。
/// </summary>
internal static class AuditExportWriter
{
    private const string EXPORT_SCHEMA = "gitguard.audit.export.v1";

    private const string CSV_HEADER =
        "run_id,repo_path,scan_type,started_at,duration_ms,result,exit_code," +
        "finding_id,rule_id,file_path,line_number,severity,fingerprint,snippet_masked,commit_sha";

    private static readonly JsonWriterOptions JSON_OPTIONS = new JsonWriterOptions
    {
        Indented = true,
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
    };

    private static readonly SearchValues<char> CSV_SPECIAL_CHARS = SearchValues.Create(",\"\r\n");

    /// <summary>写出 JSON 导出（UTF-8 无 BOM）。</summary>
    /// <param name="stream">目标流。</param>
    /// <param name="runs">扫描运行（含 Finding 明细）。</param>
    /// <param name="exportedAt">导出时间（UTC）。</param>
    /// <exception cref="ArgumentNullException">任一引用参数为 <see langword="null"/>。</exception>
    internal static void WriteJson(Stream stream, IReadOnlyList<ScanRun> runs, DateTimeOffset exportedAt)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(runs);

        using var writer = new Utf8JsonWriter(stream, JSON_OPTIONS);
        writer.WriteStartObject();
        writer.WriteString("schema", EXPORT_SCHEMA);
        writer.WriteString("exported_at", FormatTimestamp(exportedAt));
        writer.WriteNumber("run_count", runs.Count);
        writer.WriteNumber("finding_count", runs.Sum(static run => run.Findings.Length));

        writer.WriteStartArray("runs");
        foreach (ScanRun run in runs)
        {
            WriteRunJson(writer, run);
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    /// <summary>写出 CSV 导出（UTF-8 with BOM、CRLF；每个 Finding 一行，无 Finding 的运行保留一行运行级数据）。</summary>
    /// <param name="stream">目标流。</param>
    /// <param name="runs">扫描运行（含 Finding 明细）。</param>
    /// <exception cref="ArgumentNullException">任一引用参数为 <see langword="null"/>。</exception>
    internal static void WriteCsv(Stream stream, IReadOnlyList<ScanRun> runs)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(runs);

        using var writer = new StreamWriter(
            stream,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: true),
            bufferSize: 4096,
            leaveOpen: true);
        writer.NewLine = "\r\n";
        writer.WriteLine(CSV_HEADER);

        foreach (ScanRun run in runs)
        {
            if (run.Findings.IsEmpty)
            {
                writer.WriteLine(BuildCsvRow(run, finding: null));
                continue;
            }

            foreach (Finding finding in run.Findings)
            {
                writer.WriteLine(BuildCsvRow(run, finding));
            }
        }
    }

    private static void WriteRunJson(Utf8JsonWriter writer, ScanRun run)
    {
        writer.WriteStartObject();
        writer.WriteString("run_id", run.Id.ToString());
        writer.WriteString("repo_path", run.RepoPath);
        writer.WriteString("scan_type", run.ScanType.ToString());
        writer.WriteString("started_at", FormatTimestamp(run.StartedAt));
        if (run.DurationMs is { } duration)
        {
            writer.WriteNumber("duration_ms", duration);
        }
        else
        {
            writer.WriteNull("duration_ms");
        }

        writer.WriteString("result", run.Result.ToString());
        writer.WriteNumber("exit_code", run.ExitCode);

        writer.WriteStartArray("findings");
        foreach (Finding finding in run.Findings)
        {
            WriteFindingJson(writer, finding);
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static void WriteFindingJson(Utf8JsonWriter writer, Finding finding)
    {
        writer.WriteStartObject();
        writer.WriteString("finding_id", finding.Id.ToString());
        writer.WriteString("rule_id", finding.RuleId);
        writer.WriteString("file_path", finding.FilePath);
        writer.WriteNumber("line_number", finding.LineNumber);
        writer.WriteString("severity", finding.Severity.ToString());
        writer.WriteString("fingerprint", finding.Fingerprint.Hex);
        writer.WriteString("snippet_masked", finding.SnippetMasked.Value);
        if (finding.CommitSha is { } commitSha)
        {
            writer.WriteString("commit_sha", commitSha);
        }
        else
        {
            writer.WriteNull("commit_sha");
        }

        writer.WriteEndObject();
    }

    private static string BuildCsvRow(ScanRun run, Finding? finding)
    {
        string[] fields =
        [
            run.Id.ToString(),
            run.RepoPath,
            run.ScanType.ToString(),
            FormatTimestamp(run.StartedAt),
            run.DurationMs?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            run.Result.ToString(),
            run.ExitCode.ToString(CultureInfo.InvariantCulture),
            finding?.Id.ToString() ?? string.Empty,
            finding?.RuleId ?? string.Empty,
            finding?.FilePath ?? string.Empty,
            finding?.LineNumber.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            finding?.Severity.ToString() ?? string.Empty,
            finding?.Fingerprint.Hex ?? string.Empty,
            finding?.SnippetMasked.Value ?? string.Empty,
            finding?.CommitSha ?? string.Empty,
        ];

        return string.Join(',', fields.Select(EscapeCsvField));
    }

    private static string EscapeCsvField(string value)
    {
        // 防电子表格公式注入：以公式起始字符开头的字段加单引号前缀，再做常规 CSV 转义
        string guarded = value.Length > 0 && value[0] is '=' or '+' or '-' or '@' or '\t' or '\r'
            ? "'" + value
            : value;

        return guarded.AsSpan().IndexOfAny(CSV_SPECIAL_CHARS) >= 0
            ? "\"" + guarded.Replace("\"", "\"\"", StringComparison.Ordinal) + "\""
            : guarded;
    }

    private static string FormatTimestamp(DateTimeOffset value) =>
        value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);
}
