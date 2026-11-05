namespace GitGuard.Application.Scanning;

/// <summary>
/// 基线增量登记结果（GGD-0062）：返回基线文件路径与本次实际新增的指纹条数（0 或 1）。
/// </summary>
public sealed record BaselineAddResult
{
    /// <summary>初始化登记结果。</summary>
    /// <param name="path">基线文件绝对路径。</param>
    /// <param name="addedEntries">本次实际新增条数（0 = 已在基线中，1 = 已新增）。</param>
    /// <exception cref="ArgumentException"><paramref name="path"/> 为空或全空白。</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="addedEntries"/> 不在 0..1 之间。</exception>
    public BaselineAddResult(string path, int addedEntries)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (addedEntries != 0 && addedEntries != 1)
        {
            throw new ArgumentOutOfRangeException(nameof(addedEntries), addedEntries, "新增条数必须为 0 或 1。");
        }

        Path = path;
        AddedEntries = addedEntries;
    }

    /// <summary>基线文件绝对路径。</summary>
    public string Path { get; }

    /// <summary>本次实际新增的指纹条数（0 = 已在基线中，1 = 已新增）。</summary>
    public int AddedEntries { get; }
}
