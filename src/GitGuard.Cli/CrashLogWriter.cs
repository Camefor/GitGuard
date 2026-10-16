using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using GitGuard.Application.Composition;

namespace GitGuard.Cli;

/// <summary>
/// 未分类异常（退出码 99）的脱敏崩溃日志（<c>10-异常处理设计</c> §8）：写入
/// <c>&lt;data&gt;/crash/&lt;时间戳&gt;-&lt;trace_id&gt;.log</c>，内容经 <see cref="MessageSanitizer"/> 清洗
/// （去控制字符、主目录折叠为 <c>~</c>）。落盘失败只放弃写日志，绝不改变退出码或掩盖原始异常。
/// </summary>
internal static class CrashLogWriter
{
    /// <summary>尝试写入崩溃日志。</summary>
    /// <param name="session">组合根会话（提供崩溃日志目录与版本）。</param>
    /// <param name="traceId">本次调用的短追踪 ID。</param>
    /// <param name="exception">未分类异常（含内部异常与堆栈）。</param>
    /// <returns>写入成功的日志文件绝对路径；失败为 <see langword="null"/>。</returns>
    /// <exception cref="ArgumentNullException">任一参数为 <see langword="null"/>。</exception>
    internal static string? TryWrite(GitGuardSession session, string traceId, Exception exception)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(exception);
        ArgumentException.ThrowIfNullOrWhiteSpace(traceId);

        try
        {
            string directory = session.CrashLogDirectory;
            Directory.CreateDirectory(directory);

            string fileName = string.Create(
                CultureInfo.InvariantCulture,
                $"{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}-{traceId}.log");
            string path = Path.Combine(directory, fileName);

            File.WriteAllText(path, BuildContent(session.Version, traceId, exception), new UTF8Encoding(false));
            return path;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or InvalidOperationException or ArgumentException)
        {
            return null;
        }
    }

    private static string BuildContent(string version, string traceId, Exception exception)
    {
        string content = string.Join(
            '\n',
            "GitGuard 崩溃日志（已脱敏；请连同 --verbose 日志一并提交 Issue）",
            string.Create(CultureInfo.InvariantCulture, $"时间（UTC）：{DateTimeOffset.UtcNow:yyyy-MM-dd'T'HH:mm:ss.fff'Z'}"),
            string.Create(CultureInfo.InvariantCulture, $"trace_id：{traceId}"),
            string.Create(CultureInfo.InvariantCulture, $"gitguard：{version}"),
            string.Create(CultureInfo.InvariantCulture, $"运行时：{RuntimeInformation.FrameworkDescription}（{RuntimeInformation.RuntimeIdentifier}）"),
            string.Empty,
            exception.ToString());
        return MessageSanitizer.Sanitize(content) + '\n';
    }
}
