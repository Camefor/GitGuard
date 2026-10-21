namespace GitGuard.Application.Audit;

/// <summary>审计导出结果（CLI 摘要渲染用）：输出路径、格式与导出的运行 / Finding 数量。</summary>
public sealed record AuditExportReport
{
    /// <summary>初始化导出结果。</summary>
    /// <param name="outputPath">导出文件的绝对路径。</param>
    /// <param name="format">导出格式。</param>
    /// <param name="runCount">导出的扫描运行数量（≥ 0）。</param>
    /// <param name="findingCount">导出的 Finding 总数（≥ 0）。</param>
    /// <exception cref="ArgumentException"><paramref name="outputPath"/> 为空或全空白。</exception>
    /// <exception cref="ArgumentOutOfRangeException">数量为负或 <paramref name="format"/> 未定义。</exception>
    public AuditExportReport(string outputPath, AuditExportFormat format, int runCount, int findingCount)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        ArgumentOutOfRangeException.ThrowIfNegative(runCount);
        ArgumentOutOfRangeException.ThrowIfNegative(findingCount);
        if (!Enum.IsDefined(format))
        {
            throw new ArgumentOutOfRangeException(nameof(format), format, "未知的导出格式");
        }

        OutputPath = outputPath;
        Format = format;
        RunCount = runCount;
        FindingCount = findingCount;
    }

    /// <summary>导出文件的绝对路径。</summary>
    public string OutputPath { get; }

    /// <summary>导出格式。</summary>
    public AuditExportFormat Format { get; }

    /// <summary>导出的扫描运行数量。</summary>
    public int RunCount { get; }

    /// <summary>导出的 Finding 总数。</summary>
    public int FindingCount { get; }
}
