namespace GitGuard.Application.Scanning;

/// <summary>
/// 扫描管线的基线上下文（FR-061）：消费模式携带待放行指纹，生成模式只用于回填报告摘要。
/// </summary>
internal sealed record BaselineContext
{
    /// <summary>初始化基线上下文。</summary>
    /// <param name="path">基线文件绝对路径。</param>
    /// <param name="generated">是否生成本次基线。</param>
    /// <param name="entries">基线条目数。</param>
    /// <param name="fingerprints">消费模式下待放行的指纹集合；生成模式必须为空。</param>
    internal BaselineContext(string path, bool generated, int entries, IReadOnlySet<string> fingerprints)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(fingerprints);
        ArgumentOutOfRangeException.ThrowIfNegative(entries, nameof(entries));
        if (generated && fingerprints.Count > 0)
        {
            throw new ArgumentException("生成基线时不抑制本次命中，指纹集合必须为空。", nameof(fingerprints));
        }

        Path = path;
        Generated = generated;
        Entries = entries;
        Fingerprints = fingerprints;
    }

    /// <summary>基线文件绝对路径。</summary>
    internal string Path { get; }

    /// <summary>是否生成本次基线。</summary>
    internal bool Generated { get; }

    /// <summary>基线条目数。</summary>
    internal int Entries { get; }

    /// <summary>消费模式下待放行的指纹集合。</summary>
    internal IReadOnlySet<string> Fingerprints { get; }
}
