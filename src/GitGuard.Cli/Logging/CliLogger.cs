using System.Buffers;
using System.Globalization;
using System.Text;
using System.Text.Json;
using GitGuard.Core.Services;

namespace GitGuard.Cli.Logging;

/// <summary>
/// 结构化日志字段（<c>docs/09-日志设计.md</c> §3）：全部可选；<see cref="RepoPath"/> 由 Sink 按级别渲染为
/// <c>repo_path_hash</c>（Information 及以上）或 <c>repo_path</c>（Debug / Trace），其余字段原样输出。
/// </summary>
internal sealed record LogFields
{
    /// <summary>扫描运行 ID（UUIDv7）。</summary>
    internal string? RunId { get; init; }

    /// <summary>仓库绝对路径（原始值；渲染时按级别匿名化）。</summary>
    internal string? RepoPath { get; init; }

    /// <summary>规则 ID。</summary>
    internal string? RuleId { get; init; }

    /// <summary>退出码。</summary>
    internal int? ExitCode { get; init; }

    /// <summary>耗时（毫秒）。</summary>
    internal long? DurationMs { get; init; }

    /// <summary>错误码（<c>10-异常处理设计</c> §4 登记值）。</summary>
    internal string? ErrorCode { get; init; }

    /// <summary>便于用户反馈时引用的短追踪 ID。</summary>
    internal string? TraceId { get; init; }
}

/// <summary>
/// CLI 日志器（<c>docs/09-日志设计.md</c> §2 / §3）：终端 stderr 输出人类可读单行（可选颜色），
/// 可选文件 Sink 输出 JSON-lines；全部文本经 <see cref="MessageSanitizer"/> 与
/// <see cref="SanitizingLogger.RepoPathForLog"/> 脱敏，禁止字段（snippet_raw / token / fingerprint_salt 等）在本层不存在。
/// </summary>
internal sealed class CliLogger
{
    private readonly CliLogLevel _minimumLevel;
    private readonly TextWriter _error;
    private readonly bool _color;
    private readonly LogFileSink? _fileSink;
    private bool _fileFailureReported;

    /// <summary>初始化日志器。</summary>
    /// <param name="minimumLevel">最低输出级别（Trace / Debug / Information）。</param>
    /// <param name="error">终端日志目标（stderr）。</param>
    /// <param name="color">是否允许彩色输出（终端 + 未禁用）。</param>
    /// <param name="fileSink">文件日志 Sink；<see langword="null"/> 表示不写文件。</param>
    /// <exception cref="ArgumentNullException"><paramref name="error"/> 为 <see langword="null"/>。</exception>
    internal CliLogger(CliLogLevel minimumLevel, TextWriter error, bool color, LogFileSink? fileSink)
    {
        ArgumentNullException.ThrowIfNull(error);

        _minimumLevel = minimumLevel;
        _error = error;
        _color = color;
        _fileSink = fileSink;
    }

    /// <summary>判断指定级别是否达到输出阈值。</summary>
    /// <param name="level">待判断级别。</param>
    /// <returns>达到阈值返回 <see langword="true"/>。</returns>
    internal bool IsEnabled(CliLogLevel level) => level >= _minimumLevel;

    /// <summary>写入一条日志（低于阈值时静默丢弃）。</summary>
    /// <param name="level">级别。</param>
    /// <param name="category">类别（如 <c>Cli</c> / <c>Scan.Staged</c> / <c>Hook.Install</c>）。</param>
    /// <param name="message">消息（已脱敏的简述，不得包含被扫描原文）。</param>
    /// <param name="fields">结构化字段；无字段为 <see langword="null"/>。</param>
    /// <exception cref="ArgumentException"><paramref name="category"/> 为空或全空白。</exception>
    /// <exception cref="ArgumentNullException"><paramref name="message"/> 为 <see langword="null"/>。</exception>
    internal void Log(CliLogLevel level, string category, string message, LogFields? fields = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(category);
        ArgumentNullException.ThrowIfNull(message);

        if (!IsEnabled(level))
        {
            return;
        }

        string sanitizedMessage = MessageSanitizer.Sanitize(message);
        var values = new List<LogFieldValue>(7);
        CollectFields(fields, level, values);

        WriteTerminal(level, category, sanitizedMessage, values);
        WriteFile(level, category, sanitizedMessage, values);
    }

    private static void CollectFields(LogFields? fields, CliLogLevel level, List<LogFieldValue> target)
    {
        if (fields is null)
        {
            return;
        }

        if (!string.IsNullOrEmpty(fields.RunId))
        {
            target.Add(new LogFieldValue("run_id", fields.RunId, null));
        }

        if (!string.IsNullOrEmpty(fields.RepoPath))
        {
            bool debugLevel = level <= CliLogLevel.Debug;
            string pathValue = SanitizingLogger.RepoPathForLog(fields.RepoPath, debugLevel);
            target.Add(debugLevel
                ? new LogFieldValue("repo_path", pathValue, null)
                : new LogFieldValue("repo_path_hash", pathValue, null));
        }

        if (!string.IsNullOrEmpty(fields.RuleId))
        {
            target.Add(new LogFieldValue("rule_id", fields.RuleId, null));
        }

        if (fields.ExitCode is { } exitCode)
        {
            target.Add(new LogFieldValue("exit_code", exitCode.ToString(CultureInfo.InvariantCulture), exitCode));
        }

        if (fields.DurationMs is { } durationMs)
        {
            target.Add(new LogFieldValue("duration_ms", durationMs.ToString(CultureInfo.InvariantCulture), durationMs));
        }

        if (!string.IsNullOrEmpty(fields.ErrorCode))
        {
            target.Add(new LogFieldValue("error_code", fields.ErrorCode, null));
        }

        if (!string.IsNullOrEmpty(fields.TraceId))
        {
            target.Add(new LogFieldValue("trace_id", fields.TraceId, null));
        }
    }

    private static string BuildJsonLine(
        CliLogLevel level,
        string category,
        string message,
        List<LogFieldValue> values)
    {
        var buffer = new ArrayBufferWriter<byte>(256);
        using (var writer = new Utf8JsonWriter(buffer, JsonText.WriterOptions))
        {
            writer.WriteStartObject();
            writer.WriteString("timestamp", FormatTimestamp(DateTimeOffset.UtcNow));
            writer.WriteString("level", LevelText(level));
            writer.WriteString("category", MessageSanitizer.Sanitize(category));
            writer.WriteString("message", message);
            foreach (LogFieldValue value in values)
            {
                if (value.Number is { } number)
                {
                    writer.WriteNumber(value.Name, number);
                }
                else
                {
                    writer.WriteString(value.Name, value.Text);
                }
            }

            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    private static string FormatTimestamp(DateTimeOffset timestamp) =>
        timestamp.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);

    private static string LevelText(CliLogLevel level) => level switch
    {
        CliLogLevel.Trace => "trace",
        CliLogLevel.Debug => "debug",
        CliLogLevel.Information => "information",
        CliLogLevel.Warning => "warning",
        CliLogLevel.Error => "error",
        CliLogLevel.Critical => "critical",
        _ => throw new ArgumentOutOfRangeException(nameof(level), level, "未知的日志级别"),
    };

    private static ConsoleColor? ColorOf(CliLogLevel level) => level switch
    {
        CliLogLevel.Trace => ConsoleColor.DarkGray,
        CliLogLevel.Debug => ConsoleColor.DarkGray,
        CliLogLevel.Warning => ConsoleColor.Yellow,
        CliLogLevel.Error => ConsoleColor.Red,
        CliLogLevel.Critical => ConsoleColor.Red,
        _ => null,
    };

    private void WriteTerminal(CliLogLevel level, string category, string message, List<LogFieldValue> values)
    {
        var line = new StringBuilder(64);
        line.Append('[').Append(LevelText(level)).Append("] ").Append(category).Append(": ").Append(message);
        foreach (LogFieldValue value in values)
        {
            line.Append(' ').Append(value.Name).Append('=').Append(value.Text);
        }

        if (_color && ColorOf(level) is { } color)
        {
            Console.ForegroundColor = color;
            _error.WriteLine(line.ToString());
            Console.ResetColor();
        }
        else
        {
            _error.WriteLine(line.ToString());
        }
    }

    private void WriteFile(CliLogLevel level, string category, string message, List<LogFieldValue> values)
    {
        LogFileSink? sink = _fileSink;
        if (sink is null)
        {
            return;
        }

        string line = BuildJsonLine(level, category, message, values);
        if (!sink.Write(line) && !_fileFailureReported)
        {
            _fileFailureReported = true;
            _error.WriteLine($"[warning] Cli: {MessageSanitizer.Sanitize(sink.FailureMessage)}");
        }
    }

    /// <summary>已渲染的单个结构化字段（数值字段保留数字类型以便文件 JSON 输出为 number）。</summary>
    private readonly struct LogFieldValue
    {
        internal LogFieldValue(string name, string? text, long? number)
        {
            Name = name;
            Text = text;
            Number = number;
        }

        internal string Name { get; }

        internal string? Text { get; }

        internal long? Number { get; }
    }
}
