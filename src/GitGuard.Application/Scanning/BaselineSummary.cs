namespace GitGuard.Application.Scanning;

/// <summary>
/// 基线文件（FR-061）摘要：随扫描结果返回给 CLI 渲染。
/// 生成模式只写入基线、不抑制本次命中；消费模式按指纹放行既有命中并在报告中标记为历史噪声。
/// </summary>
public sealed record BaselineSummary
{
    /// <summary>初始化基线摘要。</summary>
    /// <param name="path">基线文件绝对路径。</param>
    /// <param name="generated">本次是否写入（生成）基线文件。</param>
    /// <param name="entries">基线文件中的指纹条目数（生成时为写入数，消费时为加载数）。</param>
    /// <param name="suppressedFindings">本次因基线命中而放行的 Finding 数；生成模式下恒为 0。</param>
    /// <exception cref="ArgumentException"><paramref name="path"/> 为空或全空白。</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="entries"/> 或 <paramref name="suppressedFindings"/> 为负。</exception>
    public BaselineSummary(string path, bool generated, int entries, int suppressedFindings)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentOutOfRangeException.ThrowIfNegative(entries, nameof(entries));
        ArgumentOutOfRangeException.ThrowIfNegative(suppressedFindings, nameof(suppressedFindings));
        if (generated && suppressedFindings != 0)
        {
            throw new ArgumentException("生成基线时不抑制本次命中，suppressedFindings 必须为 0。", nameof(suppressedFindings));
        }

        Path = path;
        Generated = generated;
        Entries = entries;
        SuppressedFindings = suppressedFindings;
    }

    /// <summary>基线文件绝对路径。</summary>
    public string Path { get; }

    /// <summary>本次是否写入基线文件（<see langword="true"/> 为生成模式）。</summary>
    public bool Generated { get; }

    /// <summary>基线文件中的指纹条目数。</summary>
    public int Entries { get; }

    /// <summary>本次因基线命中而放行的 Finding 数（历史噪声）。</summary>
    public int SuppressedFindings { get; }
}
