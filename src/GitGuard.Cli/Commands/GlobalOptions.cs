using System.CommandLine;
using GitGuard.Cli.Localization;

namespace GitGuard.Cli.Commands;

/// <summary>
/// 全局选项集合（<c>模块设计/Cli.md</c> §7）：全部为根命令递归选项，可出现在子命令前后任意位置。
/// 受限取值（<c>--format</c>）由框架在解析期拦截为解析错误；取值读取仍做异常防护，
/// 保证任何非法输入都收敛到用法错误（退出码 7）而不是未分类异常（99）。
/// </summary>
internal sealed class GlobalOptionSet
{
    /// <summary><c>--format</c> 的文本取值。</summary>
    internal const string FORMAT_TEXT = "text";

    /// <summary><c>--format</c> 的 JSON 取值。</summary>
    internal const string FORMAT_JSON = "json";

    /// <summary>创建并接线全部全局选项。</summary>
    /// <param name="text">帮助文案（按输出语言选择）。</param>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> 为 <see langword="null"/>。</exception>
    internal GlobalOptionSet(CliText text)
    {
        ArgumentNullException.ThrowIfNull(text);

        Format = new Option<string>("--format")
        {
            Description = text.Get(CliTextKey.FormatOptionDescription),
            Recursive = true,
            DefaultValueFactory = _ => FORMAT_TEXT,
        };
        Format.AcceptOnlyFromAmong(FORMAT_TEXT, FORMAT_JSON);

        NoColor = new Option<bool>("--no-color")
        {
            Description = text.Get(CliTextKey.NoColorOptionDescription),
            Recursive = true,
        };

        Verbose = new Option<bool>("--verbose")
        {
            Description = text.Get(CliTextKey.VerboseOptionDescription),
            Recursive = true,
        };

        Trace = new Option<bool>("--trace")
        {
            Description = text.Get(CliTextKey.TraceOptionDescription),
            Recursive = true,
        };

        LogFile = new Option<string>("--log-file")
        {
            Description = text.Get(CliTextKey.LogFileOptionDescription),
            Recursive = true,
        };

        Language = new Option<string>(CliLanguageResolver.LANGUAGE_OPTION)
        {
            Description = text.Get(CliTextKey.LanguageOptionDescription),
            Recursive = true,
        };
        Language.AcceptOnlyFromAmong(CliLanguageResolver.ZH_CN, CliLanguageResolver.EN_US);
    }

    /// <summary><c>--format text|json</c>。</summary>
    internal Option<string> Format { get; }

    /// <summary><c>--no-color</c>。</summary>
    internal Option<bool> NoColor { get; }

    /// <summary><c>--verbose</c>。</summary>
    internal Option<bool> Verbose { get; }

    /// <summary><c>--trace</c>。</summary>
    internal Option<bool> Trace { get; }

    /// <summary><c>--log-file &lt;path&gt;</c>。</summary>
    internal Option<string> LogFile { get; }

    /// <summary><c>--language zh-CN|en-US</c>。</summary>
    internal Option<string> Language { get; }

    /// <summary>把全部全局选项挂到根命令（递归下发到子命令）。</summary>
    /// <param name="root">根命令。</param>
    /// <exception cref="ArgumentNullException"><paramref name="root"/> 为 <see langword="null"/>。</exception>
    internal void AddTo(Command root)
    {
        ArgumentNullException.ThrowIfNull(root);

        root.Options.Add(Format);
        root.Options.Add(NoColor);
        root.Options.Add(Verbose);
        root.Options.Add(Trace);
        root.Options.Add(LogFile);
        root.Options.Add(Language);
    }

    /// <summary>读取全局选项取值；受限取值转换失败时返回 <see langword="null"/>（调用方按用法错误 7 处理）。</summary>
    /// <param name="parseResult">解析结果（调用方应已确认无解析错误）。</param>
    /// <returns>取值视图；读取失败为 <see langword="null"/>。</returns>
    /// <exception cref="ArgumentNullException"><paramref name="parseResult"/> 为 <see langword="null"/>。</exception>
    internal GlobalOptionValues? Read(ParseResult parseResult)
    {
        ArgumentNullException.ThrowIfNull(parseResult);

        if (!TryRead(parseResult, Format, out string? format) ||
            !TryRead(parseResult, NoColor, out bool noColor) ||
            !TryRead(parseResult, Verbose, out bool verbose) ||
            !TryRead(parseResult, Trace, out bool trace) ||
            !TryRead(parseResult, LogFile, out string? logFile))
        {
            return null;
        }

        return new GlobalOptionValues(
            string.IsNullOrEmpty(format) ? FORMAT_TEXT : format,
            noColor,
            verbose,
            trace,
            string.IsNullOrWhiteSpace(logFile) ? null : logFile);
    }

    /// <summary>尽力读取 <c>--format</c>（用于解析失败时的输出分流）；取值不可用时返回 <see langword="null"/>。</summary>
    /// <param name="parseResult">解析结果（可含错误，不保证各选项已成功绑定）。</param>
    /// <returns><c>text</c> / <c>json</c>；不可用为 <see langword="null"/>。</returns>
    /// <exception cref="ArgumentNullException"><paramref name="parseResult"/> 为 <see langword="null"/>。</exception>
    internal string? TryGetFormat(ParseResult parseResult)
    {
        ArgumentNullException.ThrowIfNull(parseResult);

        return TryRead(parseResult, Format, out string? format) ? format : null;
    }

    private static bool TryRead<T>(ParseResult parseResult, Option<T> option, out T? value)
    {
        try
        {
            value = parseResult.GetValue(option);
            return true;
        }
        catch (InvalidOperationException)
        {
            value = default;
            return false;
        }
    }
}

/// <summary>全局选项取值快照。</summary>
internal sealed record GlobalOptionValues
{
    /// <summary>初始化取值快照。</summary>
    /// <param name="format">输出格式（<c>text</c> / <c>json</c>）。</param>
    /// <param name="noColor"><c>--no-color</c>。</param>
    /// <param name="verbose"><c>--verbose</c>。</param>
    /// <param name="trace"><c>--trace</c>。</param>
    /// <param name="logFile">文件日志路径；未指定为 <see langword="null"/>。</param>
    internal GlobalOptionValues(string format, bool noColor, bool verbose, bool trace, string? logFile)
    {
        Format = format;
        NoColor = noColor;
        Verbose = verbose;
        Trace = trace;
        LogFile = logFile;
    }

    /// <summary>输出格式（恒为 <c>text</c> 或 <c>json</c>）。</summary>
    internal string Format { get; }

    /// <summary>是否启用 JSON 输出（机器可读，走 stdout）。</summary>
    internal bool Json => string.Equals(Format, GlobalOptionSet.FORMAT_JSON, StringComparison.Ordinal);

    /// <summary>是否显式禁用彩色输出。</summary>
    internal bool NoColor { get; }

    /// <summary>是否启用 Debug 级日志。</summary>
    internal bool Verbose { get; }

    /// <summary>是否启用 Trace 级日志（必须与 <see cref="Verbose"/> 同用）。</summary>
    internal bool Trace { get; }

    /// <summary>文件日志路径（尚未展开 <c>~</c>）；未指定为 <see langword="null"/>。</summary>
    internal string? LogFile { get; }
}
