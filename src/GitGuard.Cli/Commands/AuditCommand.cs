using System.CommandLine;
using System.Globalization;
using GitGuard.Application.Audit;
using GitGuard.Application.Composition;
using GitGuard.Cli.Localization;
using GitGuard.Cli.Logging;
using GitGuard.Core.Model;

namespace GitGuard.Cli.Commands;

/// <summary>
/// <c>gitguard audit</c>（<c>06-API接口设计</c> §5.6 / FR-023）：本地审计查询与导出。
/// <c>list</c> 支持分页、起始时间与结果筛选；<c>view</c> 按运行 ID 展示摘要与 Finding 明细；
/// <c>export</c> 把脱敏审计记录写入 JSON / CSV 文件。
/// 查询范围为整个本地审计库（含所有仓库），命令本身不要求当前目录是 Git 仓库。
/// </summary>
internal static class AuditCommand
{
    private const string EXPORT_FORMAT_JSON = "json";
    private const string EXPORT_FORMAT_CSV = "csv";

    /// <summary>创建 <c>audit</c> 命令组。</summary>
    /// <param name="runtime">运行时上下文。</param>
    /// <returns>已接线 <c>list</c> / <c>view</c> / <c>export</c> 子命令的命令组。</returns>
    /// <exception cref="ArgumentNullException"><paramref name="runtime"/> 为 <see langword="null"/>。</exception>
    internal static Command Create(CliRuntime runtime)
    {
        ArgumentNullException.ThrowIfNull(runtime);

        var command = new Command("audit", runtime.Text.Get(CliTextKey.AuditDescription));
        command.Subcommands.Add(CreateList(runtime));
        command.Subcommands.Add(CreateView(runtime));
        command.Subcommands.Add(CreateExport(runtime));
        return command;
    }

    private static Command CreateList(CliRuntime runtime)
    {
        var limitOption = new Option<int>("--limit")
        {
            Description = runtime.Text.Get(CliTextKey.AuditListLimitDescription),
            DefaultValueFactory = _ => AuditQuery.DEFAULT_LIMIT,
        };
        var offsetOption = new Option<int>("--offset")
        {
            Description = runtime.Text.Get(CliTextKey.AuditListOffsetDescription),
            DefaultValueFactory = _ => 0,
        };
        var sinceOption = new Option<string>("--since")
        {
            Description = runtime.Text.Get(CliTextKey.AuditListSinceDescription),
        };
        var resultOption = new Option<string>("--result")
        {
            Description = runtime.Text.Get(CliTextKey.AuditListResultDescription),
        };

        var command = new Command("list", runtime.Text.Get(CliTextKey.AuditListDescription));
        command.Options.Add(limitOption);
        command.Options.Add(offsetOption);
        command.Options.Add(sinceOption);
        command.Options.Add(resultOption);

        command.SetAction(parseResult =>
            ExecuteList(runtime, parseResult, limitOption, offsetOption, sinceOption, resultOption));
        return command;
    }

    private static Command CreateView(CliRuntime runtime)
    {
        var runIdArgument = new Argument<string>("run-id")
        {
            Description = runtime.Text.Get(CliTextKey.AuditRunIdArgumentDescription),
        };

        var command = new Command("view", runtime.Text.Get(CliTextKey.AuditViewDescription));
        command.Arguments.Add(runIdArgument);

        command.SetAction(parseResult => ExecuteView(runtime, parseResult, runIdArgument));
        return command;
    }

    private static Command CreateExport(CliRuntime runtime)
    {
        var outputOption = new Option<string>("--output")
        {
            Description = runtime.Text.Get(CliTextKey.AuditExportOutputDescription),
        };

        // 局部 --format 表示导出文件格式；全局 --format 仍控制摘要输出（text/json）
        var formatOption = new Option<string>("--format")
        {
            Description = runtime.Text.Get(CliTextKey.AuditExportFormatOptionDescription),
            DefaultValueFactory = _ => EXPORT_FORMAT_JSON,
        };
        formatOption.AcceptOnlyFromAmong(EXPORT_FORMAT_JSON, EXPORT_FORMAT_CSV);

        var command = new Command("export", runtime.Text.Get(CliTextKey.AuditExportDescription));
        command.Options.Add(outputOption);
        command.Options.Add(formatOption);

        command.SetAction(parseResult => ExecuteExport(runtime, parseResult, outputOption, formatOption));
        return command;
    }

    private static int ExecuteList(
        CliRuntime runtime,
        ParseResult parseResult,
        Option<int> limitOption,
        Option<int> offsetOption,
        Option<string> sinceOption,
        Option<string> resultOption)
    {
        int limit = parseResult.GetValue(limitOption);
        if (limit is < 1 or > AuditQuery.MAX_LIMIT)
        {
            return UsageError.Report(
                runtime,
                runtime.Text.Format(CliTextKey.AuditLimitInvalid, AuditQuery.MAX_LIMIT));
        }

        int offset = parseResult.GetValue(offsetOption);
        if (offset < 0)
        {
            return UsageError.Report(runtime, runtime.Text.Get(CliTextKey.AuditOffsetInvalid));
        }

        DateTimeOffset? since = null;
        string? rawSince = parseResult.GetValue(sinceOption);
        if (!string.IsNullOrWhiteSpace(rawSince))
        {
            if (!DateTimeOffset.TryParse(
                    rawSince,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                    out DateTimeOffset parsedSince))
            {
                return UsageError.Report(runtime, runtime.Text.Get(CliTextKey.AuditSinceInvalid));
            }

            since = parsedSince;
        }

        ScanResult? result = null;
        string? rawResult = parseResult.GetValue(resultOption);
        if (!string.IsNullOrWhiteSpace(rawResult))
        {
            // 只接受枚举名（拒绝 "1" 这类数字取值，避免含义不明的筛选）
            if (!Enum.GetNames<ScanResult>().Any(
                    name => string.Equals(name, rawResult, StringComparison.OrdinalIgnoreCase)))
            {
                return UsageError.Report(runtime, runtime.Text.Get(CliTextKey.AuditResultInvalid));
            }

            result = Enum.Parse<ScanResult>(rawResult, ignoreCase: true);
        }

        var query = new AuditQuery(limit, offset, since, result);
        AuditRunPage page = runtime.Session.CreateAuditList().Execute(query);
        runtime.Writer.WriteAuditList(query, page);
        runtime.Logger.Log(
            CliLogLevel.Information,
            "Audit.List",
            runtime.Text.Get(CliTextKey.LogAuditListed),
            new LogFields { ExitCode = ExitCodes.SUCCESS, TraceId = runtime.TraceId });
        return ExitCodes.SUCCESS;
    }

    private static int ExecuteView(
        CliRuntime runtime,
        ParseResult parseResult,
        Argument<string> runIdArgument)
    {
        string? rawRunId = parseResult.GetValue(runIdArgument)?.Trim();
        if (string.IsNullOrWhiteSpace(rawRunId))
        {
            return UsageError.Report(runtime, runtime.Text.Get(CliTextKey.AuditRunIdRequired));
        }

        if (!Guid.TryParse(rawRunId, out Guid runId) || runId == Guid.Empty)
        {
            return UsageError.Report(runtime, runtime.Text.Get(CliTextKey.AuditRunIdInvalid));
        }

        ScanRun? run = runtime.Session.CreateAuditView().Execute(runId);
        if (run is null)
        {
            string message = runtime.Text.Format(CliTextKey.AuditRunNotFound, runId);
            runtime.Writer.WriteError(
                ExitCodes.AUDIT_RUN_NOT_FOUND,
                ExitCodeMapper.AUDIT_RUN_NOT_FOUND,
                message,
                runtime.TraceId);
            runtime.Logger.Log(
                CliLogLevel.Warning,
                "Audit.View",
                message,
                new LogFields
                {
                    ErrorCode = ExitCodeMapper.AUDIT_RUN_NOT_FOUND,
                    ExitCode = ExitCodes.AUDIT_RUN_NOT_FOUND,
                    TraceId = runtime.TraceId,
                });
            return ExitCodes.AUDIT_RUN_NOT_FOUND;
        }

        runtime.Writer.WriteAuditView(run);
        runtime.Logger.Log(
            CliLogLevel.Information,
            "Audit.View",
            runtime.Text.Get(CliTextKey.LogAuditViewed),
            new LogFields { ExitCode = ExitCodes.SUCCESS, TraceId = runtime.TraceId });
        return ExitCodes.SUCCESS;
    }

    private static int ExecuteExport(
        CliRuntime runtime,
        ParseResult parseResult,
        Option<string> outputOption,
        Option<string> formatOption)
    {
        string? outputPath = parseResult.GetValue(outputOption);
        if (string.IsNullOrWhiteSpace(outputPath))
        {
            return UsageError.Report(runtime, runtime.Text.Get(CliTextKey.AuditOutputRequired));
        }

        AuditExportFormat format = string.Equals(
            parseResult.GetValue(formatOption),
            EXPORT_FORMAT_CSV,
            StringComparison.Ordinal)
            ? AuditExportFormat.Csv
            : AuditExportFormat.Json;

        AuditExportReport report = runtime.Session.CreateAuditExport().Execute(
            GitGuardSession.ExpandUserPath(outputPath),
            format);
        runtime.Writer.WriteAuditExport(report);
        runtime.Logger.Log(
            CliLogLevel.Information,
            "Audit.Export",
            runtime.Text.Get(CliTextKey.LogAuditExported),
            new LogFields { ExitCode = ExitCodes.SUCCESS, TraceId = runtime.TraceId });
        return ExitCodes.SUCCESS;
    }
}
