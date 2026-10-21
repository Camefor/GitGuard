using System.CommandLine;
using System.Globalization;
using GitGuard.Application.Composition;
using GitGuard.Application.Configuration;
using GitGuard.Cli.Commands;
using GitGuard.Cli.Localization;
using GitGuard.Cli.Logging;
using GitGuard.Cli.Reporting;
using GitGuard.Core;

namespace GitGuard.Cli;

/// <summary>
/// CLI 入口编排（<c>模块设计/Cli.md</c> §6）：解析 → 全局选项分流（格式 / 颜色 / 日志）→ 派发命令 →
/// 未分类异常统一映射退出码（<c>10-异常处理设计</c> §3）。本类不承载任何命令业务。
/// 处理顺序保证：解析失败（含未知命令 / 选项冲突 / 受限取值非法）→ 退出码 7 且不触碰仓库与配置；
/// <c>--trace</c> 未与 <c>--verbose</c> 同用 → 同样按用法错误收敛。
/// </summary>
public static class CliApplication
{
    /// <summary>运行一次完整的 CLI 调用并返回进程退出码。</summary>
    /// <param name="args">命令行参数（不含可执行文件名）。</param>
    /// <param name="host">宿主缝合点（真实进程用 <see cref="CliHost.CreateDefault"/>）。</param>
    /// <param name="cancellationToken">外部取消令牌（Ctrl+C）；扫描超时语义见退出码 12。</param>
    /// <returns>退出码（<see cref="ExitCodes"/> 常量之一）。</returns>
    /// <exception cref="ArgumentNullException"><paramref name="args"/> 或 <paramref name="host"/> 为 <see langword="null"/>。</exception>
    public static async Task<int> RunAsync(string[] args, CliHost host, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(host);

        // 语言必须在构建命令树与解析之前确定：帮助文本与框架解析错误在解析 / 调用期生成
        CliLanguage language = CliLanguageResolver.Resolve(args, host.LanguageEnvironment);
        var text = CliText.For(language);
        var previousUICulture = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = language.ToCulture();
        try
        {
            return await RunCoreAsync(args, host, text, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            // 并行测试与宿主进程复用：恢复调用前的区域设置，避免跨调用泄漏
            CultureInfo.CurrentUICulture = previousUICulture;
        }
    }

    /// <summary>
    /// 生成短追踪 ID（<c>XXXX-XXXX</c>）：取 UUIDv7 的随机尾段而非时间戳前缀，
    /// 保证同一秒内多次运行的 ID 不重复（用户反馈与日志对齐用）。
    /// </summary>
    /// <returns>追踪 ID 字符串。</returns>
    internal static string CreateTraceId()
    {
        string value = Guid.CreateVersion7().ToString("N");
        return string.Create(CultureInfo.InvariantCulture, $"{value[^8..^4]}-{value[^4..]}");
    }

    private static async Task<int> RunCoreAsync(
        string[] args,
        CliHost host,
        CliText text,
        CancellationToken cancellationToken)
    {
        string traceId = CreateTraceId();
        var runtime = new CliRuntime(host, traceId, text);
        var globalOptions = new GlobalOptionSet(text);
        RootCommand root = CommandTree.Create(runtime, globalOptions);

        ParseResult parseResult = root.Parse(args);
        if (parseResult.Errors.Count > 0)
        {
            return ReportUsageErrors(host, text, globalOptions, parseResult, traceId);
        }

        GlobalOptionValues? options = globalOptions.Read(parseResult);
        if (options is null)
        {
            return ReportUsageError(host, false, text, traceId, text.Get(CliTextKey.InvalidOptionValues));
        }

        if (options.Trace && !options.Verbose)
        {
            return ReportUsageError(host, options.Json, text, traceId, text.Get(CliTextKey.TraceRequiresVerbose));
        }

        bool color = host.IsErrorTerminal && !host.NoColorEnvironment && !options.NoColor;
        var writer = new ReportWriter(host.Output, host.Error, options.Json, color, text);
        CliLogger logger = CreateLogger(runtime, options, color);
        runtime.Configure(writer, logger);

        string commandName = parseResult.CommandResult.Command.Name;
        logger.Log(
            CliLogLevel.Debug,
            "Cli",
            text.Format(CliTextKey.LogCommandStarted, commandName),
            new LogFields { TraceId = traceId });

        var configuration = new InvocationConfiguration
        {
            Output = host.Output,
            Error = host.Error,
            EnableDefaultExceptionHandler = false,
        };

        try
        {
            return await parseResult.InvokeAsync(configuration, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            return ReportFailure(runtime, exception);
        }
    }

    /// <summary>解析失败：输出全部解析错误与 <c>--help</c> 提示（退出码 7，不执行任何命令）。</summary>
    private static int ReportUsageErrors(
        CliHost host,
        CliText text,
        GlobalOptionSet globalOptions,
        ParseResult parseResult,
        string traceId)
    {
        var details = new List<string>(parseResult.Errors.Count);
        foreach (var error in parseResult.Errors)
        {
            details.Add(error.Message);
        }

        // 尽力按 --format 分流：JSON 调用方也应拿到机器可读错误；--format 本身非法时回退文本
        bool json = string.Equals(globalOptions.TryGetFormat(parseResult), GlobalOptionSet.FORMAT_JSON, StringComparison.Ordinal);
        var writer = new ReportWriter(host.Output, host.Error, json, color: false, text);
        writer.WriteUsageError(details, traceId);
        return ExitCodes.USAGE_ERROR;
    }

    private static int ReportUsageError(CliHost host, bool json, CliText text, string traceId, string detail)
    {
        var writer = new ReportWriter(host.Output, host.Error, json, color: false, text);
        writer.WriteUsageError(new[] { detail }, traceId);
        return ExitCodes.USAGE_ERROR;
    }

    /// <summary>未分类异常出口：映射退出码 → 输出错误报告 → 记日志 → 退出码 99 补写脱敏崩溃日志。</summary>
    private static int ReportFailure(CliRuntime runtime, Exception exception)
    {
        int exitCode = ExitCodeMapper.Map(exception);
        string errorCode = ExitCodeMapper.ErrorCodeOf(exception);
        string message = Describe(exception);

        runtime.Writer.WriteError(exitCode, errorCode, message, runtime.TraceId);
        runtime.Logger.Log(
            exitCode == ExitCodes.UNKNOWN_ERROR ? CliLogLevel.Critical : CliLogLevel.Error,
            "Cli",
            message,
            new LogFields { ErrorCode = errorCode, ExitCode = exitCode, TraceId = runtime.TraceId });

        if (exitCode == ExitCodes.UNKNOWN_ERROR)
        {
            if (CrashLogWriter.TryWrite(runtime.Session, runtime.TraceId, exception) is { } crashLogPath)
            {
                runtime.Logger.Log(
                    CliLogLevel.Warning,
                    "Cli",
                    runtime.Text.Format(CliTextKey.LogCrashLogWritten, crashLogPath),
                    new LogFields { TraceId = runtime.TraceId });
            }
        }

        return exitCode;
    }

    private static string Describe(Exception exception)
    {
        string message = MessageSanitizer.Sanitize(exception.Message);
        return message.Length == 0 ? exception.GetType().Name : message.ReplaceLineEndings(" ");
    }

    private static CliLogger CreateLogger(CliRuntime runtime, GlobalOptionValues options, bool color) =>
        new CliLogger(ResolveLevel(runtime, options), runtime.Host.Error, color, CreateFileSink(runtime, options));

    /// <summary>解析日志级别：<c>--verbose --trace</c> → Trace；<c>--verbose</c> 或 <c>logging.verbose</c> → Debug；否则 Information。</summary>
    private static CliLogLevel ResolveLevel(CliRuntime runtime, GlobalOptionValues options)
    {
        if (options.Trace)
        {
            return CliLogLevel.Trace;
        }

        if (options.Verbose)
        {
            return CliLogLevel.Debug;
        }

        try
        {
            return runtime.Session.Config.LogVerbose ? CliLogLevel.Debug : CliLogLevel.Information;
        }
        catch (ConfigurationException)
        {
            // 配置无效由具体命令以退出码 13 报告；日志级别初始化不抢先失败
            return CliLogLevel.Information;
        }
    }

    /// <summary>文件日志 Sink：<c>--log-file</c> 优先，其次配置 <c>logging.file</c>；均未启用时为 <see langword="null"/>。</summary>
    private static LogFileSink? CreateFileSink(CliRuntime runtime, GlobalOptionValues options)
    {
        if (options.LogFile is { } commandLinePath)
        {
            return new LogFileSink(
                GitGuardSession.ExpandUserPath(commandLinePath),
                LogFileSettings.Default.MaxBytes,
                LogFileSettings.Default.MaxFiles,
                runtime.Text);
        }

        try
        {
            LogFileSettings settings = runtime.Session.Config.LogFile;
            return settings.Enabled
                ? new LogFileSink(GitGuardSession.ExpandUserPath(settings.Path), settings.MaxBytes, settings.MaxFiles, runtime.Text)
                : null;
        }
        catch (ConfigurationException)
        {
            return null;
        }
    }
}
