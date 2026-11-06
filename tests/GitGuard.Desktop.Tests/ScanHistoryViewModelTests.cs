using FluentAssertions;
using GitGuard.Application.Hooks;
using GitGuard.Application.Scanning;
using GitGuard.Core;
using GitGuard.Core.Model;
using GitGuard.Core.Policy;
using GitGuard.Desktop.Localization;
using GitGuard.Desktop.Services;
using GitGuard.Desktop.ViewModels;

namespace GitGuard.Desktop.Tests;

/// <summary>
/// 扫描历史 ViewModel 测试（GGD-0043 / 11-前端UI设计 §3.4）：分页、运行与发现项选择、脱敏详情、
/// 编辑器跳转、空态 / 错误与语言切换；数据访问和编辑器启动均由假对象驱动，金丝雀原文不得出现在 UI。
/// </summary>
public sealed class ScanHistoryViewModelTests
{
    private const string CANARY_SECRET = "AKIAIOSFODNN7EXAMPLE";
    private const string MASKED_SNIPPET = "AKIA****************";
    private const string FINGERPRINT_HEX = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    [Fact]
    public async Task OpenAsync_LoadsFirstPageSelectsFirstRunAndFirstFinding()
    {
        string repositoryPath = CreateRepositoryPath();
        AuditRunSummary[] summaries = CreateSummaries(repositoryPath, 25);
        var fake = new FakeRepositoryOperations
        {
            QueryHandler = (_, limit, offset) => Task.FromResult(
                new AuditRunPage(summaries.Skip(offset).Take(limit), summaries.Length)),
            LoadHandler = id => Task.FromResult<ScanRun?>(CreateRun(repositoryPath, id)),
        };
        var viewModel = CreateViewModel(fake, out _);

        await viewModel.OpenAsync("甲仓库", repositoryPath, TestContext.Current.CancellationToken);

        fake.Queries.Should().ContainSingle();
        fake.Queries[0].RepoPath.Should().Be(repositoryPath);
        fake.Queries[0].Limit.Should().Be(ScanHistoryViewModel.PAGE_SIZE);
        fake.Queries[0].Offset.Should().Be(0);
        viewModel.Runs.Should().HaveCount(20);
        viewModel.CurrentPage.Should().Be(1);
        viewModel.TotalCount.Should().Be(25);
        viewModel.TotalPages.Should().Be(2);
        viewModel.SelectedRun.Should().BeSameAs(viewModel.Runs[0]);
        viewModel.Findings.Should().ContainSingle();
        viewModel.SelectedFinding.Should().BeSameAs(viewModel.Findings[0]);
        viewModel.HasRuns.Should().BeTrue();
        viewModel.IsEmpty.Should().BeFalse();
        viewModel.CanGoPrevious.Should().BeFalse();
        viewModel.CanGoNext.Should().BeTrue();
        viewModel.IsLoadingPage.Should().BeFalse();
        viewModel.IsLoadingRun.Should().BeFalse();
    }

    [Fact]
    public async Task NextAndPreviousPage_RequestExpectedOffsetsAndPages()
    {
        string repositoryPath = CreateRepositoryPath();
        AuditRunSummary[] summaries = CreateSummaries(repositoryPath, 25);
        var fake = new FakeRepositoryOperations
        {
            QueryHandler = (_, limit, offset) => Task.FromResult(
                new AuditRunPage(summaries.Skip(offset).Take(limit), summaries.Length)),
            LoadHandler = id => Task.FromResult<ScanRun?>(CreateRun(repositoryPath, id)),
        };
        var viewModel = CreateViewModel(fake, out _);
        await viewModel.OpenAsync("甲仓库", repositoryPath, TestContext.Current.CancellationToken);

        await viewModel.NextPageCommand.ExecuteAsync(null);

        fake.Queries.Should().HaveCount(2);
        fake.Queries[1].Offset.Should().Be(ScanHistoryViewModel.PAGE_SIZE);
        viewModel.CurrentPage.Should().Be(2);
        viewModel.Runs.Should().HaveCount(5);
        viewModel.CanGoPrevious.Should().BeTrue();
        viewModel.CanGoNext.Should().BeFalse();

        await viewModel.PreviousPageCommand.ExecuteAsync(null);

        fake.Queries.Should().HaveCount(3);
        fake.Queries[2].Offset.Should().Be(0);
        viewModel.CurrentPage.Should().Be(1);
        viewModel.Runs.Should().HaveCount(20);
    }

    [Fact]
    public async Task SelectingRun_LoadsThatRunsFindings()
    {
        string repositoryPath = CreateRepositoryPath();
        AuditRunSummary[] summaries = CreateSummaries(repositoryPath, 2);
        var fake = new FakeRepositoryOperations
        {
            QueryHandler = (_, limit, offset) => Task.FromResult(
                new AuditRunPage(summaries.Skip(offset).Take(limit), summaries.Length)),
            LoadHandler = id => Task.FromResult<ScanRun?>(CreateRun(repositoryPath, id)),
        };
        var viewModel = CreateViewModel(fake, out _);
        await viewModel.OpenAsync("甲仓库", repositoryPath, TestContext.Current.CancellationToken);
        int callsAfterOpen = fake.LoadCalls;

        viewModel.SelectedRun = viewModel.Runs[1];

        fake.LoadCalls.Should().Be(callsAfterOpen + 1);
        fake.LoadedIds.Should().Contain(summaries[1].RunId);
        viewModel.Findings.Should().ContainSingle();
        viewModel.SelectedFinding.Should().NotBeNull();
    }

    [Fact]
    public async Task Findings_OnlyExposeMaskedSnippetAndFingerprint()
    {
        string repositoryPath = CreateRepositoryPath();
        AuditRunSummary summary = CreateSummaries(repositoryPath, 1)[0];
        var fake = new FakeRepositoryOperations
        {
            QueryHandler = (_, _, _) => Task.FromResult(new AuditRunPage(new[] { summary }, 1)),
            LoadHandler = id => Task.FromResult<ScanRun?>(CreateRun(repositoryPath, id)),
        };
        var viewModel = CreateViewModel(fake, out _);

        await viewModel.OpenAsync("甲仓库", repositoryPath, TestContext.Current.CancellationToken);

        FindingItemViewModel finding = viewModel.Findings.Should().ContainSingle().Subject;
        finding.MaskedSnippetText.Should().Be(MASKED_SNIPPET);
        finding.FingerprintText.Should().Be(FINGERPRINT_HEX);
        finding.FilePath.Should().Be("src/appsettings.json");
        finding.LineNumber.Should().Be(42);
        string visibleText = string.Join(
            "|",
            finding.RuleId,
            finding.LocationText,
            finding.SeverityText,
            finding.MaskedSnippetText,
            finding.FingerprintText,
            finding.CommitText);
        visibleText.Should().NotContain(CANARY_SECRET, "金丝雀原文绝不能进入 UI（DR-02）");
    }

    [Fact]
    public async Task OpenInEditorCommand_PassesRepositoryRelativePathAndLine()
    {
        string repositoryPath = CreateRepositoryPath();
        AuditRunSummary summary = CreateSummaries(repositoryPath, 1)[0];
        var fake = new FakeRepositoryOperations
        {
            QueryHandler = (_, _, _) => Task.FromResult(new AuditRunPage(new[] { summary }, 1)),
            LoadHandler = id => Task.FromResult<ScanRun?>(CreateRun(repositoryPath, id)),
        };
        var viewModel = CreateViewModel(fake, out FakeEditorLauncher editor);
        await viewModel.OpenAsync("甲仓库", repositoryPath, TestContext.Current.CancellationToken);

        await viewModel.OpenInEditorCommand.ExecuteAsync(null);

        editor.Calls.Should().ContainSingle();
        editor.Calls[0].RepositoryPath.Should().Be(repositoryPath);
        editor.Calls[0].RelativeFilePath.Should().Be("src/appsettings.json");
        editor.Calls[0].LineNumber.Should().Be(42);
        viewModel.Message.Should().BeNull();
    }

    [Fact]
    public async Task OpenInEditorCommand_Failure_ShowsLocalizedMessageWithoutProcessDetail()
    {
        string repositoryPath = CreateRepositoryPath();
        AuditRunSummary summary = CreateSummaries(repositoryPath, 1)[0];
        var fake = new FakeRepositoryOperations
        {
            QueryHandler = (_, _, _) => Task.FromResult(new AuditRunPage(new[] { summary }, 1)),
            LoadHandler = id => Task.FromResult<ScanRun?>(CreateRun(repositoryPath, id)),
        };
        var viewModel = CreateViewModel(fake, out FakeEditorLauncher editor);
        editor.Handler = (_, _, _) => Task.FromResult(
            new EditorLaunchResult(EditorLaunchStatus.FileMissing, "原始进程错误详细信息"));
        await viewModel.OpenAsync("甲仓库", repositoryPath, TestContext.Current.CancellationToken);

        await viewModel.OpenInEditorCommand.ExecuteAsync(null);

        viewModel.Message.Should().Be("文件已不存在，可能已被移动或删除。");
        viewModel.Message.Should().NotContain("原始进程错误详细信息");
    }

    [Fact]
    public async Task OpenAsync_EmptyHistory_ShowsEmptyStateAndSelectRunPrompt()
    {
        var fake = new FakeRepositoryOperations
        {
            QueryHandler = (_, _, _) => Task.FromResult(new AuditRunPage(Array.Empty<AuditRunSummary>(), 0)),
        };
        var viewModel = CreateViewModel(fake, out _);

        await viewModel.OpenAsync("甲仓库", CreateRepositoryPath(), TestContext.Current.CancellationToken);

        viewModel.Runs.Should().BeEmpty();
        viewModel.Findings.Should().BeEmpty();
        viewModel.SelectedRun.Should().BeNull();
        viewModel.TotalCount.Should().Be(0);
        viewModel.TotalPages.Should().Be(1);
        viewModel.IsEmpty.Should().BeTrue();
        viewModel.IsSelectRunPromptVisible.Should().BeTrue();
        viewModel.Message.Should().BeNull();
    }

    [Fact]
    public async Task OpenAsync_QueryFailure_ShowsMessageAndStopsLoading()
    {
        var fake = new FakeRepositoryOperations
        {
            QueryHandler = (_, _, _) => Task.FromException<AuditRunPage>(new InvalidOperationException("数据库不可用")),
        };
        var viewModel = CreateViewModel(fake, out _);

        await viewModel.OpenAsync("甲仓库", CreateRepositoryPath(), TestContext.Current.CancellationToken);

        viewModel.Message.Should().Be("数据库不可用");
        viewModel.HasMessage.Should().BeTrue();
        viewModel.IsLoadingPage.Should().BeFalse();
        viewModel.IsEmpty.Should().BeFalse();
    }

    [Fact]
    public async Task SelectingRun_WhenRunNoLongerExists_ShowsRunNotFoundMessage()
    {
        string repositoryPath = CreateRepositoryPath();
        AuditRunSummary summary = CreateSummaries(repositoryPath, 1)[0];
        var fake = new FakeRepositoryOperations
        {
            QueryHandler = (_, _, _) => Task.FromResult(new AuditRunPage(new[] { summary }, 1)),
            LoadHandler = _ => Task.FromResult<ScanRun?>(null),
        };
        var viewModel = CreateViewModel(fake, out _);

        await viewModel.OpenAsync("甲仓库", repositoryPath, TestContext.Current.CancellationToken);

        viewModel.Findings.Should().BeEmpty();
        viewModel.Message.Should().Be("所选扫描记录已不存在。");
    }

    [Fact]
    public async Task OpenAsync_ConcurrentOpen_DiscardsStalePageResponse()
    {
        string repositoryPath = CreateRepositoryPath();
        var first = new TaskCompletionSource<AuditRunPage>();
        var second = new TaskCompletionSource<AuditRunPage>();
        int calls = 0;
        var fake = new FakeRepositoryOperations
        {
            QueryHandler = (_, _, _) =>
            {
                calls++;
                return calls == 1 ? first.Task : second.Task;
            },
        };
        var viewModel = CreateViewModel(fake, out _);

        Task firstOpen = viewModel.OpenAsync("旧仓库", repositoryPath, TestContext.Current.CancellationToken);
        Task secondOpen = viewModel.OpenAsync("新仓库", repositoryPath, TestContext.Current.CancellationToken);

        second.SetResult(new AuditRunPage(CreateSummaries(repositoryPath, 2), 2));
        await secondOpen;
        first.SetResult(new AuditRunPage(CreateSummaries(repositoryPath, 9), 9));
        await firstOpen;

        viewModel.RepositoryName.Should().Be("新仓库");
        viewModel.Runs.Should().HaveCount(2);
        viewModel.TotalCount.Should().Be(2);
    }

    [Fact]
    public async Task Relocalize_RefreshesPageItemsAndFindingLabels()
    {
        string repositoryPath = CreateRepositoryPath();
        AuditRunSummary summary = CreateSummaries(repositoryPath, 1)[0];
        var fake = new FakeRepositoryOperations
        {
            QueryHandler = (_, _, _) => Task.FromResult(new AuditRunPage(new[] { summary }, 1)),
            LoadHandler = id => Task.FromResult<ScanRun?>(CreateRun(repositoryPath, id)),
        };
        var viewModel = CreateViewModel(fake, out _);
        await viewModel.OpenAsync("甲仓库", repositoryPath, TestContext.Current.CancellationToken);

        viewModel.Relocalize(DesktopLanguage.English);

        viewModel.HistoryTitle.Should().Be("Scan history");
        viewModel.BackButtonText.Should().Be("Back to repository");
        viewModel.TimelineTitle.Should().Be("Timeline");
        viewModel.FindingsTitle.Should().Be("Findings");
        viewModel.FindingDetailTitle.Should().Be("Finding details");
        viewModel.OpenInEditorText.Should().Be("Open in editor");
        viewModel.PageText.Should().Be("Page 1 of 1 · 1 scans");
        viewModel.Runs[0].ResultText.Should().Be("Blocked");
        viewModel.Findings[0].SeverityText.Should().Be("High");
    }

    [Fact]
    public async Task OpenAllowlistFormCommand_ShowsFormWithReasonRequired()
    {
        string repositoryPath = CreateRepositoryPath();
        AuditRunSummary summary = CreateSummaries(repositoryPath, 1)[0];
        var fake = new FakeRepositoryOperations
        {
            QueryHandler = (_, _, _) => Task.FromResult(new AuditRunPage(new[] { summary }, 1)),
            LoadHandler = id => Task.FromResult<ScanRun?>(CreateRun(repositoryPath, id)),
        };
        var viewModel = CreateViewModel(fake, out _);
        await viewModel.OpenAsync("甲仓库", repositoryPath, TestContext.Current.CancellationToken);

        viewModel.OpenAllowlistFormCommand.Execute(null);

        viewModel.IsAllowlistFormVisible.Should().BeTrue();
        viewModel.IsBaselineFormVisible.Should().BeFalse();

        await viewModel.ConfirmAllowlistCommand.ExecuteAsync(null);

        viewModel.ActionMessage.Should().Be("原因至少需要 5 个字符。");
        fake.Adds.Should().BeEmpty();
        viewModel.IsAllowlistFormVisible.Should().BeTrue();
    }

    [Fact]
    public async Task ConfirmAllowlistCommand_AddsEntryClosesFormAndShowsMessage()
    {
        string repositoryPath = CreateRepositoryPath();
        AuditRunSummary summary = CreateSummaries(repositoryPath, 1)[0];
        var fake = new FakeRepositoryOperations
        {
            QueryHandler = (_, _, _) => Task.FromResult(new AuditRunPage(new[] { summary }, 1)),
            LoadHandler = id => Task.FromResult<ScanRun?>(CreateRun(repositoryPath, id)),
            AddHandler = (ruleId, pathPattern, fingerprintHex, reason) =>
            {
                ruleId.Should().Be("GGD/SECRET/AWS_KEY");
                pathPattern.Should().Be("src/appsettings.json");
                fingerprintHex.Should().Be(FINGERPRINT_HEX);
                reason.Should().Be("误报：测试夹具示例密钥");
                return Task.FromResult(AllowlistEntry.Create(
                    ruleId,
                    pathPattern,
                    Fingerprint.FromHex(fingerprintHex!),
                    reason,
                    new DateTimeOffset(2026, 10, 20, 9, 0, 0, TimeSpan.Zero),
                    "tester"));
            },
        };
        var viewModel = CreateViewModel(fake, out _);
        await viewModel.OpenAsync("甲仓库", repositoryPath, TestContext.Current.CancellationToken);

        viewModel.OpenAllowlistFormCommand.Execute(null);
        viewModel.AllowlistReason = "误报：测试夹具示例密钥";

        await viewModel.ConfirmAllowlistCommand.ExecuteAsync(null);

        fake.Adds.Should().ContainSingle();
        viewModel.IsAllowlistFormVisible.Should().BeFalse();
        viewModel.Message.Should().Be("已加入白名单。");
    }

    [Fact]
    public async Task OpenAllowlistFormCommand_HighSeverityFinding_ShowsWarning()
    {
        string repositoryPath = CreateRepositoryPath();
        AuditRunSummary summary = CreateSummaries(repositoryPath, 1)[0];
        var fake = new FakeRepositoryOperations
        {
            QueryHandler = (_, _, _) => Task.FromResult(new AuditRunPage(new[] { summary }, 1)),
            LoadHandler = id => Task.FromResult<ScanRun?>(CreateRun(repositoryPath, id)),
        };
        var viewModel = CreateViewModel(fake, out _);
        await viewModel.OpenAsync("甲仓库", repositoryPath, TestContext.Current.CancellationToken);

        viewModel.NeedsAllowlistSeverityWarning.Should().BeTrue();
    }

    [Fact]
    public async Task ConfirmAllowlistCommand_BackendValidationFailure_KeepsFormAndShowsMessage()
    {
        string repositoryPath = CreateRepositoryPath();
        AuditRunSummary summary = CreateSummaries(repositoryPath, 1)[0];
        var fake = new FakeRepositoryOperations
        {
            QueryHandler = (_, _, _) => Task.FromResult(new AuditRunPage(new[] { summary }, 1)),
            LoadHandler = id => Task.FromResult<ScanRun?>(CreateRun(repositoryPath, id)),
            AddHandler = (_, _, _, _) => Task.FromException<AllowlistEntry>(
                new AllowlistException(AllowlistException.ALLOWLIST_VALIDATION_FAILED, "原始消息")),
        };
        var viewModel = CreateViewModel(fake, out _);
        await viewModel.OpenAsync("甲仓库", repositoryPath, TestContext.Current.CancellationToken);

        viewModel.OpenAllowlistFormCommand.Execute(null);
        viewModel.AllowlistReason = "误报：测试夹具示例密钥";

        await viewModel.ConfirmAllowlistCommand.ExecuteAsync(null);

        viewModel.IsAllowlistFormVisible.Should().BeTrue();
        viewModel.ActionMessage.Should().Be("白名单参数不合法，请检查规则 ID、路径 glob、指纹与原因。");
    }

    [Fact]
    public async Task ConfirmBaselineCommand_AddsBaselineEntryAndShowsMessage()
    {
        string repositoryPath = CreateRepositoryPath();
        AuditRunSummary summary = CreateSummaries(repositoryPath, 1)[0];
        var fake = new FakeRepositoryOperations
        {
            QueryHandler = (_, _, _) => Task.FromResult(new AuditRunPage(new[] { summary }, 1)),
            LoadHandler = id => Task.FromResult<ScanRun?>(CreateRun(repositoryPath, id)),
            BaselineAddHandler = (repoPath, fingerprintHex, ruleId, filePath) =>
            {
                repoPath.Should().Be(repositoryPath);
                fingerprintHex.Should().Be(FINGERPRINT_HEX);
                ruleId.Should().Be("GGD/SECRET/AWS_KEY");
                filePath.Should().Be("src/appsettings.json");
                return Task.FromResult(new BaselineAddResult(
                    Path.Combine(repositoryPath, ".gitguard-baseline.json"), 1));
            },
        };
        var viewModel = CreateViewModel(fake, out _);
        await viewModel.OpenAsync("甲仓库", repositoryPath, TestContext.Current.CancellationToken);

        viewModel.OpenBaselineFormCommand.Execute(null);

        viewModel.IsBaselineFormVisible.Should().BeTrue();
        viewModel.IsAllowlistFormVisible.Should().BeFalse();

        await viewModel.ConfirmBaselineCommand.ExecuteAsync(null);

        fake.BaselineAdds.Should().ContainSingle();
        viewModel.IsBaselineFormVisible.Should().BeFalse();
        viewModel.Message.Should().Be("已加入基线。");
    }

    [Fact]
    public async Task ConfirmBaselineCommand_AlreadyPresent_ShowsAlreadyPresentMessage()
    {
        string repositoryPath = CreateRepositoryPath();
        AuditRunSummary summary = CreateSummaries(repositoryPath, 1)[0];
        var fake = new FakeRepositoryOperations
        {
            QueryHandler = (_, _, _) => Task.FromResult(new AuditRunPage(new[] { summary }, 1)),
            LoadHandler = id => Task.FromResult<ScanRun?>(CreateRun(repositoryPath, id)),
            BaselineAddHandler = (_, _, _, _) => Task.FromResult(
                new BaselineAddResult(Path.Combine(repositoryPath, ".gitguard-baseline.json"), 0)),
        };
        var viewModel = CreateViewModel(fake, out _);
        await viewModel.OpenAsync("甲仓库", repositoryPath, TestContext.Current.CancellationToken);

        viewModel.OpenBaselineFormCommand.Execute(null);
        await viewModel.ConfirmBaselineCommand.ExecuteAsync(null);

        viewModel.Message.Should().Be("该指纹已在基线中，无需重复登记。");
    }

    [Fact]
    public async Task FindingItem_ActionText_ShowsLocalizedAllowlistIgnored()
    {
        string repositoryPath = CreateRepositoryPath();
        AuditRunSummary summary = CreateSummaries(repositoryPath, 1)[0];
        var fake = new FakeRepositoryOperations
        {
            QueryHandler = (_, _, _) => Task.FromResult(new AuditRunPage(new[] { summary }, 1)),
            LoadHandler = id => Task.FromResult<ScanRun?>(CreateRun(repositoryPath, id, "AllowedByAllowlist")),
        };
        var viewModel = CreateViewModel(fake, out _);
        await viewModel.OpenAsync("甲仓库", repositoryPath, TestContext.Current.CancellationToken);

        FindingItemViewModel finding = viewModel.Findings.Should().ContainSingle().Subject;
        finding.Action.Should().Be("AllowedByAllowlist");
        finding.HasAction.Should().BeTrue();
        finding.ActionText.Should().Be("已放行 · 白名单");
    }

    [Fact]
    public async Task ConfirmAllowlistCommand_WithRuleAndPathGranularity_AddsEntryWithoutFingerprint()
    {
        string repositoryPath = CreateRepositoryPath();
        AuditRunSummary summary = CreateSummaries(repositoryPath, 1)[0];
        var fake = new FakeRepositoryOperations
        {
            QueryHandler = (_, _, _) => Task.FromResult(new AuditRunPage(new[] { summary }, 1)),
            LoadHandler = id => Task.FromResult<ScanRun?>(CreateRun(repositoryPath, id)),
            AddHandler = (_, _, _, _) => Task.FromResult(AllowlistEntry.Create(
                "GGD/SECRET/AWS_KEY",
                "src/appsettings.json",
                null,
                "误报：测试",
                new DateTimeOffset(2026, 10, 20, 9, 0, 0, TimeSpan.Zero),
                "tester")),
        };
        var viewModel = CreateViewModel(fake, out _);
        await viewModel.OpenAsync("甲仓库", repositoryPath, TestContext.Current.CancellationToken);

        viewModel.OpenAllowlistFormCommand.Execute(null);
        viewModel.AllowlistReason = "误报：测试夹具示例密钥";
        viewModel.AllowlistByRuleAndPath = true;

        await viewModel.ConfirmAllowlistCommand.ExecuteAsync(null);

        fake.Adds.Should().ContainSingle();
        fake.Adds[0].RuleId.Should().Be("GGD/SECRET/AWS_KEY");
        fake.Adds[0].PathPattern.Should().Be("src/appsettings.json");
        fake.Adds[0].FingerprintHex.Should().BeNull();
    }

    [Fact]
    public void AllowlistGranularity_DefaultsToFingerprint_AndSwitchingIsExclusive()
    {
        var viewModel = new ScanHistoryViewModel(
            new FakeRepositoryOperations(), new FakeEditorLauncher(), () => { }, DesktopLanguage.Chinese);

        viewModel.AllowlistByFingerprint.Should().BeTrue();
        viewModel.AllowlistByRuleAndPath.Should().BeFalse();

        viewModel.AllowlistByRuleAndPath = true;

        viewModel.AllowlistByRuleAndPath.Should().BeTrue();
        viewModel.AllowlistByFingerprint.Should().BeFalse();

        viewModel.AllowlistByFingerprint = true;

        viewModel.AllowlistByFingerprint.Should().BeTrue();
        viewModel.AllowlistByRuleAndPath.Should().BeFalse();
    }

    private static ScanHistoryViewModel CreateViewModel(
        FakeRepositoryOperations operations,
        out FakeEditorLauncher editor)
    {
        editor = new FakeEditorLauncher();
        return new ScanHistoryViewModel(operations, editor, () => { }, DesktopLanguage.Chinese);
    }

    private static string CreateRepositoryPath() =>
        Path.Combine(Path.GetTempPath(), "gitguard-history-tests", Guid.NewGuid().ToString("N"), "repo");

    private static AuditRunSummary[] CreateSummaries(string repositoryPath, int count) =>
        Enumerable.Range(0, count)
            .Select(index => new AuditRunSummary(
                Guid.CreateVersion7(),
                repositoryPath,
                ScanType.Manual,
                new DateTimeOffset(2026, 10, 20, 10, 0, 0, TimeSpan.Zero).AddMinutes(-index),
                45,
                ScanResult.Blocked,
                1,
                1))
            .ToArray();

    private static ScanRun CreateRun(string repositoryPath, Guid runId, string? findingAction = null) =>
        ScanRun.Restore(
            runId,
            repositoryPath,
            ScanType.Manual,
            new DateTimeOffset(2026, 10, 20, 10, 0, 0, TimeSpan.Zero),
            45,
            ScanResult.Blocked,
            1,
            new[] { CreateFinding(findingAction) });

    private static Finding CreateFinding(string? action = null) =>
        Finding.Create(
            "GGD/SECRET/AWS_KEY",
            "src/appsettings.json",
            42,
            Severity.High,
            Fingerprint.FromHex(FINGERPRINT_HEX),
            SnippetMasked.Create(MASKED_SNIPPET),
            action: action);

    /// <summary>可编程仓库操作假对象；仅扫描历史方法参与本组测试。</summary>
    private sealed class FakeRepositoryOperations : IRepositoryOperations
    {
        internal Func<string, int, int, Task<AuditRunPage>> QueryHandler { get; set; } =
            (_, _, _) => Task.FromResult(new AuditRunPage(Array.Empty<AuditRunSummary>(), 0));

        internal Func<Guid, Task<ScanRun?>> LoadHandler { get; set; } =
            _ => Task.FromResult<ScanRun?>(null);

        internal List<(string RepoPath, int Limit, int Offset)> Queries { get; } = new List<(string RepoPath, int Limit, int Offset)>();

        internal List<Guid> LoadedIds { get; } = new List<Guid>();

        internal int LoadCalls => LoadedIds.Count;

        internal Func<string?, string?, string?, string, Task<AllowlistEntry>> AddHandler { get; set; } =
            (_, _, _, _) => Task.FromException<AllowlistEntry>(new NotSupportedException("测试未配置 AddHandler。"));

        internal Func<string, string, string, string, Task<BaselineAddResult>> BaselineAddHandler { get; set; } =
            (_, _, _, _) => Task.FromException<BaselineAddResult>(new NotSupportedException("测试未配置 BaselineAddHandler。"));

        internal List<(string? RuleId, string? PathPattern, string? FingerprintHex, string Reason)> Adds { get; } =
            new List<(string? RuleId, string? PathPattern, string? FingerprintHex, string Reason)>();

        internal List<(string RepoPath, string FingerprintHex, string RuleId, string FilePath)> BaselineAdds { get; } =
            new List<(string RepoPath, string FingerprintHex, string RuleId, string FilePath)>();

        public Task<IReadOnlyList<RegisteredRepository>> ListAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("扫描历史测试不读取仓库列表。");

        public Task<RegisteredRepository> RegisterAsync(string path, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("扫描历史测试不登记仓库。");

        public Task RemoveAsync(Guid id, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("扫描历史测试不移除仓库。");

        public Task<RepositoryDetail> LoadDetailAsync(string repoPath, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("扫描历史测试不读取仓库详情。");

        public Task<RepositoryDetail> ScanAsync(string repoPath, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("扫描历史测试不执行扫描。");

        public Task<string?> ResolveHookExecutableAsync(
            string repoPath,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("扫描历史测试不解析 GitGuard CLI。");

        public Task<RepositoryDetail> InstallHooksAsync(
            string repoPath,
            string executablePath,
            bool force,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("扫描历史测试不安装 Hook。");

        public Task<RepositoryDetail> UninstallHooksAsync(
            string repoPath,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("扫描历史测试不卸载 Hook。");

        public Task<AuditRunPage> QueryScanHistoryAsync(
            string repoPath,
            int limit,
            int offset,
            CancellationToken cancellationToken = default)
        {
            Queries.Add((repoPath, limit, offset));
            return QueryHandler(repoPath, limit, offset);
        }

        public Task<ScanRun?> LoadScanRunAsync(Guid runId, CancellationToken cancellationToken = default)
        {
            LoadedIds.Add(runId);
            return LoadHandler(runId);
        }

        public Task<AllowlistPage> QueryAllowlistAsync(
            int limit,
            int offset,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("扫描历史测试不读取白名单。");

        public Task<AllowlistEntry> AddAllowlistAsync(
            string? ruleId,
            string? pathPattern,
            string? fingerprintHex,
            string reason,
            CancellationToken cancellationToken = default)
        {
            Adds.Add((ruleId, pathPattern, fingerprintHex, reason));
            return AddHandler(ruleId, pathPattern, fingerprintHex, reason);
        }

        public Task RemoveAllowlistAsync(Guid id, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("扫描历史测试不删除白名单。");

        public Task<BaselineAddResult> AddBaselineAsync(
            string repoPath,
            string fingerprintHex,
            string ruleId,
            string filePath,
            CancellationToken cancellationToken = default)
        {
            BaselineAdds.Add((repoPath, fingerprintHex, ruleId, filePath));
            return BaselineAddHandler(repoPath, fingerprintHex, ruleId, filePath);
        }
    }

    /// <summary>记录调用的编辑器启动器假对象。</summary>
    private sealed class FakeEditorLauncher : IEditorLauncher
    {
        internal Func<string, string, int, Task<EditorLaunchResult>> Handler { get; set; } =
            (_, _, _) => Task.FromResult(new EditorLaunchResult(EditorLaunchStatus.Opened));

        internal List<(string RepositoryPath, string RelativeFilePath, int LineNumber)> Calls { get; } =
            new List<(string RepositoryPath, string RelativeFilePath, int LineNumber)>();

        public Task<EditorLaunchResult> OpenAsync(
            string repositoryPath,
            string relativeFilePath,
            int lineNumber,
            CancellationToken cancellationToken = default)
        {
            Calls.Add((repositoryPath, relativeFilePath, lineNumber));
            return Handler(repositoryPath, relativeFilePath, lineNumber);
        }
    }
}
