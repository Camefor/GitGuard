using System.Globalization;
using System.Text;
using GitGuard.Cli.Localization;

namespace GitGuard.Cli.Logging;

/// <summary>
/// 文件日志 Sink（<c>docs/09-日志设计.md</c> §7 / §8）：每行一条 JSON，按 <c>max_bytes</c> 滚动，
/// 保留 <c>max_files</c> 份（当前文件 + <c>.1</c> ~ <c>.N-1</c>，总量 = max_files × max_bytes 封顶）。
/// IO 失败按 §5「日志文件写满 → 转到 stderr，不阻断业务」降级：停用 Sink 并由 <see cref="CliLogger"/> 提示一次。
/// </summary>
internal sealed class LogFileSink
{
    private readonly string _path;
    private readonly long _maxBytes;
    private readonly int _maxFiles;
    private readonly CliText _text;
    private bool _disabled;

    /// <summary>初始化文件 Sink。</summary>
    /// <param name="path">日志文件绝对路径（<c>~</c> 已由调用方展开）。</param>
    /// <param name="maxBytes">单文件滚动阈值（字节）。</param>
    /// <param name="maxFiles">保留份数（含当前文件）；小于 1 时按 1 处理。</param>
    /// <param name="text">降级提示文案表。</param>
    /// <exception cref="ArgumentException"><paramref name="path"/> 为空或全空白。</exception>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> 为 <see langword="null"/>。</exception>
    internal LogFileSink(string path, long maxBytes, int maxFiles, CliText text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(text);

        _path = path;
        _maxBytes = maxBytes > 0 ? maxBytes : 10L * 1024 * 1024;
        _maxFiles = Math.Max(maxFiles, 1);
        _text = text;
    }

    /// <summary>降级原因；未失败时为 <see langword="null"/>。</summary>
    internal string? FailureMessage { get; private set; }

    /// <summary>追加一行日志；失败时停用本 Sink 并记录原因（不抛出）。</summary>
    /// <param name="line">单行 JSON（不含换行符）。</param>
    /// <returns>写入成功返回 <see langword="true"/>。</returns>
    internal bool Write(string line)
    {
        if (_disabled)
        {
            return false;
        }

        try
        {
            string? directory = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            byte[] payload = Encoding.UTF8.GetBytes(line + Environment.NewLine);
            if (File.Exists(_path) && new FileInfo(_path).Length + payload.Length > _maxBytes)
            {
                Rotate();
            }

            using var stream = new FileStream(_path, FileMode.Append, FileAccess.Write, FileShare.Read);
            stream.Write(payload);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            _disabled = true;
            FailureMessage = _text.Format(CliTextKey.LogFileWriteFailed, ex.Message);
            return false;
        }
    }

    /// <summary>滚动：删除最旧一份，<c>.i</c> 依次后移，当前文件改名为 <c>.1</c>。</summary>
    private void Rotate()
    {
        string oldest = _path + "." + (_maxFiles - 1).ToString(CultureInfo.InvariantCulture);
        if (_maxFiles <= 1)
        {
            File.Delete(_path);
            return;
        }

        if (File.Exists(oldest))
        {
            File.Delete(oldest);
        }

        for (int index = _maxFiles - 2; index >= 1; index--)
        {
            string source = _path + "." + index.ToString(CultureInfo.InvariantCulture);
            if (File.Exists(source))
            {
                File.Move(source, _path + "." + (index + 1).ToString(CultureInfo.InvariantCulture), overwrite: true);
            }
        }

        File.Move(_path, _path + ".1", overwrite: true);
    }
}
