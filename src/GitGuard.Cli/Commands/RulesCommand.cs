using System.CommandLine;
using GitGuard.Application.Rules;
using GitGuard.Cli.Localization;
using GitGuard.Cli.Logging;

namespace GitGuard.Cli.Commands;

/// <summary>
/// <c>gitguard rules</c>（<c>06-API接口设计</c> §5.7 / FR-080）：只读规则目录查询。
/// <c>list</c> 支持类别与「有效启用」筛选，<c>view</c> 按规则 ID 展示详情；两者均默认隐藏正则原文，
/// 仅在显式 <c>--show-pattern</c> 时输出。命令不要求当前目录是 Git 仓库。
/// </summary>
internal static class RulesCommand
{
    /// <summary>创建 <c>rules</c> 命令组。</summary>
    /// <param name="runtime">运行时上下文。</param>
    /// <returns>已接线 <c>list</c> / <c>view</c> 子命令的命令组。</returns>
    /// <exception cref="ArgumentNullException"><paramref name="runtime"/> 为 <see langword="null"/>。</exception>
    internal static Command Create(CliRuntime runtime)
    {
        ArgumentNullException.ThrowIfNull(runtime);

        var command = new Command("rules", runtime.Text.Get(CliTextKey.RulesDescription));
        command.Subcommands.Add(CreateList(runtime));
        command.Subcommands.Add(CreateView(runtime));
        return command;
    }

    private static Command CreateList(CliRuntime runtime)
    {
        var categoryOption = new Option<string>("--category")
        {
            Description = runtime.Text.Get(CliTextKey.RulesCategoryOptionDescription),
        };
        var enabledOption = new Option<bool>("--enabled")
        {
            Description = runtime.Text.Get(CliTextKey.RulesEnabledOptionDescription),
        };
        var showPatternOption = new Option<bool>("--show-pattern")
        {
            Description = runtime.Text.Get(CliTextKey.RulesShowPatternOptionDescription),
        };

        var command = new Command("list", runtime.Text.Get(CliTextKey.RulesListDescription));
        command.Options.Add(categoryOption);
        command.Options.Add(enabledOption);
        command.Options.Add(showPatternOption);

        command.SetAction(parseResult => ExecuteList(
            runtime,
            parseResult,
            categoryOption,
            enabledOption,
            showPatternOption));
        return command;
    }

    private static Command CreateView(CliRuntime runtime)
    {
        var ruleIdArgument = new Argument<string>("rule-id")
        {
            Description = runtime.Text.Get(CliTextKey.RulesRuleIdArgumentDescription),
        };
        var showPatternOption = new Option<bool>("--show-pattern")
        {
            Description = runtime.Text.Get(CliTextKey.RulesShowPatternOptionDescription),
        };

        var command = new Command("view", runtime.Text.Get(CliTextKey.RulesViewDescription));
        command.Arguments.Add(ruleIdArgument);
        command.Options.Add(showPatternOption);

        command.SetAction(parseResult => ExecuteView(runtime, parseResult, ruleIdArgument, showPatternOption));
        return command;
    }

    private static int ExecuteList(
        CliRuntime runtime,
        ParseResult parseResult,
        Option<string> categoryOption,
        Option<bool> enabledOption,
        Option<bool> showPatternOption)
    {
        string? category = null;
        string? rawCategory = parseResult.GetValue(categoryOption);
        if (!string.IsNullOrWhiteSpace(rawCategory))
        {
            category = NormalizeCategory(rawCategory);
            if (category is null)
            {
                return UsageError.Report(runtime, runtime.Text.Get(CliTextKey.RulesCategoryInvalid));
            }
        }

        bool enabledOnly = parseResult.GetValue(enabledOption);
        bool showPattern = parseResult.GetValue(showPatternOption);
        IReadOnlyList<RuleCatalogEntry> catalog = runtime.Session.GetRuleCatalog();

        var rules = new List<RuleCatalogEntry>(catalog.Count);
        foreach (RuleCatalogEntry rule in catalog)
        {
            if (category is not null && !string.Equals(rule.Category, category, StringComparison.Ordinal))
            {
                continue;
            }

            if (enabledOnly && !rule.Enabled)
            {
                continue;
            }

            rules.Add(rule);
        }

        runtime.Writer.WriteRulesList(rules, category, enabledOnly, showPattern);
        runtime.Logger.Log(
            CliLogLevel.Information,
            "Rules",
            runtime.Text.Get(CliTextKey.LogRulesListed),
            new LogFields { ExitCode = ExitCodes.SUCCESS, TraceId = runtime.TraceId });
        return ExitCodes.SUCCESS;
    }

    private static int ExecuteView(
        CliRuntime runtime,
        ParseResult parseResult,
        Argument<string> ruleIdArgument,
        Option<bool> showPatternOption)
    {
        string? ruleId = parseResult.GetValue(ruleIdArgument)?.Trim();
        if (string.IsNullOrWhiteSpace(ruleId))
        {
            return UsageError.Report(runtime, runtime.Text.Get(CliTextKey.RulesRuleIdRequired));
        }

        RuleCatalogEntry? rule = FindRule(runtime.Session.GetRuleCatalog(), ruleId);
        if (rule is null)
        {
            string message = runtime.Text.Format(CliTextKey.RulesNotFound, ruleId);
            runtime.Writer.WriteError(
                ExitCodes.RULE_NOT_FOUND,
                ExitCodeMapper.RULE_NOT_FOUND,
                message,
                runtime.TraceId);
            runtime.Logger.Log(
                CliLogLevel.Warning,
                "Rules",
                message,
                new LogFields
                {
                    ErrorCode = ExitCodeMapper.RULE_NOT_FOUND,
                    ExitCode = ExitCodes.RULE_NOT_FOUND,
                    TraceId = runtime.TraceId,
                });
            return ExitCodes.RULE_NOT_FOUND;
        }

        runtime.Writer.WriteRule(rule, parseResult.GetValue(showPatternOption));
        runtime.Logger.Log(
            CliLogLevel.Information,
            "Rules",
            runtime.Text.Get(CliTextKey.LogRuleViewed),
            new LogFields { ExitCode = ExitCodes.SUCCESS, TraceId = runtime.TraceId });
        return ExitCodes.SUCCESS;
    }

    /// <summary>把 <c>--category</c> 的 CLI 取值（大小写不敏感）规范化为规则 ID 的类别段。</summary>
    private static string? NormalizeCategory(string raw) => raw.Trim().ToLowerInvariant() switch
    {
        "secret" => "SECRET",
        "privacy" => "PRIVACY",
        "localpath" => "LOCALPATH",
        "url" => "URL",
        _ => null,
    };

    private static RuleCatalogEntry? FindRule(IReadOnlyList<RuleCatalogEntry> rules, string ruleId)
    {
        foreach (RuleCatalogEntry rule in rules)
        {
            if (string.Equals(rule.RuleId, ruleId, StringComparison.Ordinal))
            {
                return rule;
            }
        }

        return null;
    }
}
