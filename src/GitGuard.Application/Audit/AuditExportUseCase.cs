using GitGuard.Core;
using GitGuard.Core.Abstractions;
using GitGuard.Core.Model;

namespace GitGuard.Application.Audit;

/// <summary>
/// 审计导出用例（<c>06-API接口设计</c> §5.6 / FR-023）：把本机审计库中的扫描运行与 Finding 明细导出为
/// JSON 或 CSV 文件。导出内容仅包含脱敏片段（<see cref="Finding.SnippetMasked"/>）与 HMAC 指纹，
/// 不恢复、不写入任何原文（<c>模块设计/Audit.md</c> §3）。
/// 写入采用同目录临时文件 + 原子替换，失败映射退出码 31（<see cref="AuditExportException"/>）。
/// </summary>
public sealed class AuditExportUseCase
{
    private readonly IAuditStore _store;
    private readonly IClock _clock;

    /// <summary>初始化审计导出用例。</summary>
    /// <param name="store">审计存储抽象。</param>
    /// <param name="clock">时钟（导出时间戳）。</param>
    /// <exception cref="ArgumentNullException">任一参数为 <see langword="null"/>。</exception>
    public AuditExportUseCase(IAuditStore store, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(clock);

        _store = store;
        _clock = clock;
    }

    /// <summary>执行导出。</summary>
    /// <param name="outputPath">输出文件路径（相对路径按当前工作目录解析）。</param>
    /// <param name="format">导出格式。</param>
    /// <returns>导出结果（绝对路径、格式与数量）。</returns>
    /// <exception cref="ArgumentException"><paramref name="outputPath"/> 为空或全空白。</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="format"/> 未定义。</exception>
    /// <exception cref="SqliteException">审计库读取失败（CLI 映射退出码 14）。</exception>
    /// <exception cref="AuditExportException">输出路径不可写或写入失败（CLI 映射退出码 31）。</exception>
    public AuditExportReport Execute(string outputPath, AuditExportFormat format)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        if (!Enum.IsDefined(format))
        {
            throw new ArgumentOutOfRangeException(nameof(format), format, "未知的导出格式");
        }

        string fullPath = ResolveOutputPath(outputPath);
        IReadOnlyList<ScanRun> runs = _store.ListScanRunsForExport();
        WriteAtomically(fullPath, format, runs, _clock.UtcNow);

        int findingCount = runs.Sum(static run => run.Findings.Length);
        return new AuditExportReport(fullPath, format, runs.Count, findingCount);
    }

    private static string ResolveOutputPath(string outputPath)
    {
        try
        {
            return Path.GetFullPath(outputPath);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
        {
            throw new AuditExportException($"审计导出失败：输出路径非法（{outputPath}）。", ex);
        }
        catch (PathTooLongException ex)
        {
            throw new AuditExportException($"审计导出失败：输出路径过长（{outputPath}）。", ex);
        }
    }

    private static void WriteAtomically(string fullPath, AuditExportFormat format, IReadOnlyList<ScanRun> runs, DateTimeOffset exportedAt)
    {
        string temporaryPath = fullPath + ".tmp-" + Guid.CreateVersion7().ToString("N");
        try
        {
            using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                if (format == AuditExportFormat.Json)
                {
                    AuditExportWriter.WriteJson(stream, runs, exportedAt);
                }
                else
                {
                    AuditExportWriter.WriteCsv(stream, runs);
                }
            }

            File.Move(temporaryPath, fullPath, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            TryDelete(temporaryPath);
            throw new AuditExportException($"审计导出失败：无法写入 {fullPath}（{ex.Message}）", ex);
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 临时文件清理失败不改变导出的失败语义
        }
    }
}
