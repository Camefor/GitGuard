using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;

namespace GitGuard.Cli;

/// <summary>
/// JSON 输出公共配置：中文报告与日志消息按原字符输出（不转义为 <c>\uXXXX</c>），
/// 同时保留 HTML 敏感字符（<c>&lt; &gt; &amp;</c>）的转义，避免本机日志被当作标记语言注入载体。
/// </summary>
internal static class JsonText
{
    /// <summary>手写 JSON 的写出选项（<see cref="Utf8JsonWriter"/> 全部经此构造）。</summary>
    internal static JsonWriterOptions WriterOptions { get; } = new JsonWriterOptions
    {
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
    };
}
