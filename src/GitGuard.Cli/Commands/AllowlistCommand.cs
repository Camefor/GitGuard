using System.CommandLine;
using GitGuard.Cli.Localization;
using GitGuard.Cli.Logging;
using GitGuard.Core.Model;
using GitGuard.Core.Policy;

namespace GitGuard.Cli.Commands;

/// <summary>
/// <c>gitguard allowlist</c>（<c>06-API接口设计</c> §5.5 / FR-040 / FR-041）：本地白名单管理。
/// <c>add</c> 按规则 / 路径 / 指纹登记放行条件，<c>remove</c> 按 ID 删除，<c>list</c> 分页查看；
/// 条目持久化到本地审计库的 <c>allowlist</c> 表，并由存储层写入 <c>AllowlistChanged</c> 审计事件。
/// 命令本身不要求当前目录是 Git 仓库。
/// </summary>
internal static class AllowlistCommand
{
    /// <summary>创建 <c>allowlist</c> 命令组。</summary>
    /// <param name="runtime">运行时上下文。</param>
    /// <returns>已接线 <c>add</c> / <c>remove</c> / <c>list</c> 子命令的命令组。</returns>
    /// <exception cref="ArgumentNullException"><paramref name="runtime"/> 为 <see langword="null"/>。</exception>
    internal static Command Create(CliRuntime runtime)
    {
        ArgumentNullException.ThrowIfNull(runtime);

        var command = new Command("allowlist", runtime.Text.Get(CliTextKey.AllowlistDescription));
        command.Subcommands.Add(CreateAdd(runtime));
        command.Subcommands.Add(CreateRemove(runtime));
        command.Subcommands.Add(CreateList(runtime));
        return command;
    }

    private static Command CreateAdd(CliRuntime runtime)
    {
        var ruleOption = new Option<string>("--rule")
        {
            Description = runtime.Text.Get(CliTextKey.AllowlistRuleOptionDescription),
        };
        var pathOption = new Option<string>("--path")
        {
            Description = runtime.Text.Get(CliTextKey.AllowlistPathOptionDescription),
        };
        var fingerprintOption = new Option<string>("--fingerprint")
        {
            Description = runtime.Text.Get(CliTextKey.AllowlistFingerprintOptionDescription),
        };
        var reasonOption = new Option<string>("--reason")
        {
            Description = runtime.Text.Get(CliTextKey.AllowlistReasonOptionDescription),
        };

        var command = new Command("add", runtime.Text.Get(CliTextKey.AllowlistAddDescription));
        command.Options.Add(ruleOption);
        command.Options.Add(pathOption);
        command.Options.Add(fingerprintOption);
        command.Options.Add(reasonOption);

        command.SetAction(parseResult => ExecuteAdd(
            runtime,
            parseResult,
            ruleOption,
            pathOption,
            fingerprintOption,
            reasonOption));
        return command;
    }

    private static Command CreateRemove(CliRuntime runtime)
    {
        var idOption = new Option<string>("--id")
        {
            Description = runtime.Text.Get(CliTextKey.AllowlistIdOptionDescription),
        };

        var command = new Command("remove", runtime.Text.Get(CliTextKey.AllowlistRemoveDescription));
        command.Options.Add(idOption);

        command.SetAction(parseResult => ExecuteRemove(runtime, parseResult, idOption));
        return command;
    }

    private static Command CreateList(CliRuntime runtime)
    {
        var limitOption = new Option<int>("--limit")
        {
            Description = runtime.Text.Get(CliTextKey.AllowlistListLimitDescription),
            DefaultValueFactory = _ => AllowlistQuery.DEFAULT_LIMIT,
        };
        var offsetOption = new Option<int>("--offset")
        {
            Description = runtime.Text.Get(CliTextKey.AllowlistListOffsetDescription),
            DefaultValueFactory = _ => 0,
        };

        var command = new Command("list", runtime.Text.Get(CliTextKey.AllowlistListDescription));
        command.Options.Add(limitOption);
        command.Options.Add(offsetOption);

        command.SetAction(parseResult => ExecuteList(runtime, parseResult, limitOption, offsetOption));
        return command;
    }

    private static int ExecuteAdd(
        CliRuntime runtime,
        ParseResult parseResult,
        Option<string> ruleOption,
        Option<string> pathOption,
        Option<string> fingerprintOption,
        Option<string> reasonOption)
    {
        // 空字符串交由领域校验统一转成 AllowlistException（退出码 20），与 --reason 缺失同语义
        AllowlistEntry entry = runtime.Session.CreateAllowlistAdd().Execute(
            parseResult.GetValue(ruleOption),
            parseResult.GetValue(pathOption),
            parseResult.GetValue(fingerprintOption),
            parseResult.GetValue(reasonOption) ?? string.Empty,
            Environment.UserName);

        runtime.Writer.WriteAllowlistAdd(entry);
        runtime.Logger.Log(
            CliLogLevel.Information,
            "Allowlist",
            runtime.Text.Get(CliTextKey.LogAllowlistAdded),
            new LogFields { ExitCode = ExitCodes.SUCCESS, TraceId = runtime.TraceId });
        return ExitCodes.SUCCESS;
    }

    private static int ExecuteRemove(
        CliRuntime runtime,
        ParseResult parseResult,
        Option<string> idOption)
    {
        string? rawId = parseResult.GetValue(idOption);
        if (string.IsNullOrWhiteSpace(rawId))
        {
            return UsageError.Report(runtime, runtime.Text.Get(CliTextKey.AllowlistIdRequired));
        }

        if (!Guid.TryParse(rawId, out Guid id))
        {
            return UsageError.Report(runtime, runtime.Text.Get(CliTextKey.AllowlistIdInvalid));
        }

        runtime.Session.CreateAllowlistRemove().Execute(id);
        runtime.Writer.WriteAllowlistRemove(id);
        runtime.Logger.Log(
            CliLogLevel.Information,
            "Allowlist",
            runtime.Text.Get(CliTextKey.LogAllowlistRemoved),
            new LogFields { ExitCode = ExitCodes.SUCCESS, TraceId = runtime.TraceId });
        return ExitCodes.SUCCESS;
    }

    private static int ExecuteList(
        CliRuntime runtime,
        ParseResult parseResult,
        Option<int> limitOption,
        Option<int> offsetOption)
    {
        int limit = parseResult.GetValue(limitOption);
        if (limit is < 1 or > AllowlistQuery.MAX_LIMIT)
        {
            return UsageError.Report(
                runtime,
                runtime.Text.Format(CliTextKey.AllowlistLimitInvalid, AllowlistQuery.MAX_LIMIT));
        }

        int offset = parseResult.GetValue(offsetOption);
        if (offset < 0)
        {
            return UsageError.Report(runtime, runtime.Text.Get(CliTextKey.AllowlistOffsetInvalid));
        }

        var query = new AllowlistQuery(limit, offset);
        AllowlistPage page = runtime.Session.CreateAllowlistList().Execute(query);
        runtime.Writer.WriteAllowlistList(query, page);
        runtime.Logger.Log(
            CliLogLevel.Information,
            "Allowlist",
            runtime.Text.Get(CliTextKey.LogAllowlistListed),
            new LogFields { ExitCode = ExitCodes.SUCCESS, TraceId = runtime.TraceId });
        return ExitCodes.SUCCESS;
    }
}
