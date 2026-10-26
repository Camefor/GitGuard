using FluentAssertions;
using GitGuard.Application.Hooks;
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

    private static ScanRun CreateRun(string repositoryPath, Guid runId) =>
        ScanRun.Restore(
            runId,
            repositoryPath,
            ScanType.Manual,
            new DateTimeOffset(2026, 10, 20, 10, 0, 0, TimeSpan.Zero),
            45,
            ScanResult.Blocked,
            1,
            new[] { CreateFinding() });

    private static Finding CreateFinding() =>
        Finding.Create(
            "GGD/SECRET/AWS_KEY",
            "src/appsettings.json",
            42,
            Severity.High,
            Fingerprint.FromHex(FINGERPRINT_HEX),
            SnippetMasked.Create(MASKED_SNIPPET));

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
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("扫描历史测试不新增白名单。");

        public Task RemoveAllowlistAsync(Guid id, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("扫描历史测试不删除白名单。");
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
