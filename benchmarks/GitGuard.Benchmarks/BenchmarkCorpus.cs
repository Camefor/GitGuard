using System.Text;
using GitGuard.Core.Model;

namespace GitGuard.Benchmarks;

/// <summary>
/// 吞吐基准语料（NFR-P-004）：以真实代码形态的分行文本构造 <see cref="FileChange"/>，
/// 内容刻意不含任何密钥 / 隐私 / 路径 / URL 特征，确保只测检测成本、不测命中路径。
/// </summary>
internal static class BenchmarkCorpus
{
    private const int LINES_PER_FILE = 1200;
    private const int LINE_POOL_SIZE = 512;

    /// <summary>按目标大小生成语料。</summary>
    /// <param name="sizeMb">目标大小（MB）。</param>
    /// <returns>文件变更列表与实际字节数（UTF-8，含换行符）。</returns>
    internal static (IReadOnlyList<FileChange> Changes, long TotalBytes) Create(int sizeMb)
    {
        string[] linePool = BuildLinePool();
        long targetBytes = sizeMb * 1024L * 1024L;
        var changes = new List<FileChange>();
        long totalBytes = 0;
        int fileIndex = 0;

        // 以整文件为单位生成：最终大小最多超出目标一个文件（约 100 KB）
        while (totalBytes < targetBytes)
        {
            var lines = new ChangedLine[LINES_PER_FILE];
            for (int lineIndex = 0; lineIndex < lines.Length; lineIndex++)
            {
                string text = linePool[((fileIndex * 31) + lineIndex) % linePool.Length];
                lines[lineIndex] = new ChangedLine(lineIndex + 1, text);
                totalBytes += Encoding.UTF8.GetByteCount(text) + 1;
            }

            changes.Add(new FileChange($"src/generated/module_{fileIndex}.cs", lines));
            fileIndex++;
        }

        return (changes, totalBytes);
    }

    private static string[] BuildLinePool()
    {
        string[] pool = new string[LINE_POOL_SIZE];
        for (int index = 0; index < pool.Length; index++)
        {
            pool[index] = (index % 4) switch
            {
                0 => $"        public static int Compute{index}(int value) => (value * {(index % 97) + 1}) + {index % 31};",
                1 => $"        private readonly string _component{index} = \"unit-{index}\";",
                2 => $"        internal bool Flag{index} => _component{index}.Length > {index % 17};",
                _ => $"        // section {index}: generated benchmark content for detector throughput",
            };
        }

        return pool;
    }
}
