using GitGuard.Application.Composition;
using GitGuard.Cli.Logging;
using GitGuard.Cli.Reporting;

namespace GitGuard.Cli.Commands;

/// <summary>
/// 一次 CLI 调用的运行时上下文：进程级依赖（组合根会话、宿主缝合点、trace_id）在构建命令树前创建，
/// 由命令动作闭包捕获；渲染器与日志器依赖解析结果（<c>--format</c> / <c>--log-file</c>），
/// 在解析完成后经 <see cref="Configure"/> 注入，因此动作执行时必然已就绪。
/// </summary>
internal sealed class CliRuntime
{
    private ReportWriter? _writer;
    private CliLogger? _logger;

    /// <summary>初始化运行时上下文（不触碰配置、仓库与磁盘）。</summary>
    /// <param name="host">宿主缝合点。</param>
    /// <param name="traceId">本次调用的短追踪 ID。</param>
    /// <exception cref="ArgumentNullException"><paramref name="host"/> 为 <see langword="null"/>。</exception>
    /// <exception cref="ArgumentException"><paramref name="traceId"/> 为空或全空白。</exception>
    internal CliRuntime(CliHost host, string traceId)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentException.ThrowIfNullOrWhiteSpace(traceId);

        Host = host;
        TraceId = traceId;
        Session = new GitGuardSession(new GitGuardSessionOptions
        {
            WorkingDirectory = host.WorkingDirectory,
            GitExecutable = host.GitExecutable,
            DataDirectory = host.DataDirectory,
            UserConfigDirectory = host.UserConfigDirectory,
            GitGuardVersion = host.GitGuardVersion,
        });
    }

    /// <summary>宿主缝合点。</summary>
    internal CliHost Host { get; }

    /// <summary>本次调用的短追踪 ID（错误报告与日志的 <c>trace_id</c>）。</summary>
    internal string TraceId { get; }

    /// <summary>组合根会话（各子系统惰性初始化；未使用的命令不付出其代价）。</summary>
    internal GitGuardSession Session { get; }

    /// <summary>结果渲染器；<see cref="Configure"/> 之前访问属于实现缺陷。</summary>
    internal ReportWriter Writer => _writer ?? throw new InvalidOperationException("渲染器尚未配置。");

    /// <summary>日志器；<see cref="Configure"/> 之前访问属于实现缺陷。</summary>
    internal CliLogger Logger => _logger ?? throw new InvalidOperationException("日志器尚未配置。");

    /// <summary>注入解析完成后的渲染器与日志器（每次运行至多调用一次）。</summary>
    /// <param name="writer">结果渲染器。</param>
    /// <param name="logger">日志器。</param>
    /// <exception cref="ArgumentNullException">任一参数为 <see langword="null"/>。</exception>
    /// <exception cref="InvalidOperationException">重复配置。</exception>
    internal void Configure(ReportWriter writer, CliLogger logger)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(logger);

        if (_writer is not null || _logger is not null)
        {
            throw new InvalidOperationException("运行时上下文已配置。");
        }

        _writer = writer;
        _logger = logger;
    }
}
