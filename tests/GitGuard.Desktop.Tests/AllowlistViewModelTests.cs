using FluentAssertions;
using GitGuard.Application.Hooks;
using GitGuard.Core;
using GitGuard.Core.Model;
using GitGuard.Core.Policy;
using GitGuard.Desktop.Localization;
using GitGuard.Desktop.Services;
using GitGuard.Desktop.ViewModels;

namespace GitGuard.Desktop.Tests;

/// <summary>
/// 白名单管理 ViewModel 测试（GGD-0044 / 11-前端UI设计 §2、12-前端组件设计 §3.3）：分页、条件与原因校验、
/// 新增 / 删除二次确认、错误映射与语言切换；数据访问由假对象驱动，不访问真实 SQLite。
/// </summary>
public sealed class AllowlistViewModelTests
{
    private static readonly DateTimeOffset _fixedCreatedAt =
        new DateTimeOffset(2026, 10, 20, 9, 30, 0, TimeSpan.Zero);

    [Fact]
    public async Task OpenAsync_LoadsFirstPageAndMapsEntryFields()
    {
        var fingerprint = Fingerprint.FromHex(new string('a', 64));
        AllowlistEntry entry = CreateEntry(fingerprint: fingerprint);
        var fake = new FakeRepositoryOperations
        {
            QueryHandler = (limit, offset) =>
            {
                limit.Should().Be(AllowlistViewModel.PAGE_SIZE);
                offset.Should().Be(0);
                return Task.FromResult(new AllowlistPage(new[] { entry }, 1));
            },
        };
        var viewModel = new AllowlistViewModel(fake, () => { }, DesktopLanguage.Chinese);

        await viewModel.OpenAsync("甲仓库", CreateRepositoryPath(), TestContext.Current.CancellationToken);

        viewModel.RepositoryName.Should().Be("甲仓库");
        viewModel.Entries.Should().ContainSingle();
        AllowlistEntryItemViewModel item = viewModel.Entries[0];
        item.RuleIdText.Should().Be("GGD/SECRET/AWS_KEY");
        item.PathPatternText.Should().Be("src/**");
        item.FingerprintText.Should().Be(fingerprint.Hex);
        item.FingerprintPreview.Should().Be(new string('a', 16) + "…");
        item.Reason.Should().Be("测试放行原因");
        item.MetaText.Should().Contain("tester");
        item.ConditionSummary.Should().Contain("GGD/SECRET/AWS_KEY");
        viewModel.PageText.Should().Be("第 1 / 1 页 · 共 1 条");
        viewModel.HasEntries.Should().BeTrue();
        viewModel.IsEmpty.Should().BeFalse();
        viewModel.CanGoNext.Should().BeFalse();
        viewModel.CanGoPrevious.Should().BeFalse();
    }

    [Fact]
    public async Task OpenAsync_EmptyAllowlist_ShowsEmptyState()
    {
        var fake = new FakeRepositoryOperations();
        var viewModel = new AllowlistViewModel(fake, () => { }, DesktopLanguage.Chinese);

        await viewModel.OpenAsync("甲仓库", CreateRepositoryPath(), TestContext.Current.CancellationToken);

        viewModel.IsEmpty.Should().BeTrue();
        viewModel.HasEntries.Should().BeFalse();
        viewModel.PageText.Should().Be("第 1 / 1 页 · 共 0 条");
    }

    [Fact]
    public async Task NextAndPreviousPageCommands_NavigateWithinBounds()
    {
        AllowlistEntry[] entries = Enumerable.Range(0, 25)
            .Select(index => CreateEntry(reason: $"测试放行原因 {index}"))
            .ToArray();
        var fake = new FakeRepositoryOperations
        {
            QueryHandler = (limit, offset) => Task.FromResult(
                new AllowlistPage(entries.Skip(offset).Take(limit), entries.Length)),
        };
        var viewModel = new AllowlistViewModel(fake, () => { }, DesktopLanguage.Chinese);
        await viewModel.OpenAsync("甲仓库", CreateRepositoryPath(), TestContext.Current.CancellationToken);

        viewModel.Entries.Should().HaveCount(20);
        viewModel.CurrentPage.Should().Be(1);
        viewModel.TotalPages.Should().Be(2);
        viewModel.CanGoNext.Should().BeTrue();

        await viewModel.NextPageCommand.ExecuteAsync(null);

        viewModel.Entries.Should().HaveCount(5);
        viewModel.CurrentPage.Should().Be(2);
        viewModel.CanGoNext.Should().BeFalse();
        viewModel.CanGoPrevious.Should().BeTrue();

        await viewModel.PreviousPageCommand.ExecuteAsync(null);

        viewModel.Entries.Should().HaveCount(20);
        viewModel.CurrentPage.Should().Be(1);
        fake.Queries.Should().Equal((20, 0), (20, 20), (20, 0));
    }

    [Fact]
    public async Task AddCommand_WithRuleAndReason_TrimsInputAddsAndReloadsFirstPage()
    {
        AllowlistEntry added = CreateEntry(pathPattern: "src/**");
        int queryCalls = 0;
        var fake = new FakeRepositoryOperations
        {
            QueryHandler = (_, _) =>
            {
                queryCalls++;
                return Task.FromResult(queryCalls == 1
                    ? new AllowlistPage(Array.Empty<AllowlistEntry>(), 0)
                    : new AllowlistPage(new[] { added }, 1));
            },
            AddHandler = (ruleId, pathPattern, fingerprintHex, reason) =>
            {
                ruleId.Should().Be("GGD/SECRET/AWS_KEY");
                pathPattern.Should().Be("src/**");
                fingerprintHex.Should().BeNull();
                reason.Should().Be("临时放行测试");
                return Task.FromResult(added);
            },
        };
        var viewModel = new AllowlistViewModel(fake, () => { }, DesktopLanguage.Chinese);
        await viewModel.OpenAsync("甲仓库", CreateRepositoryPath(), TestContext.Current.CancellationToken);
        viewModel.NewRuleId = "  GGD/SECRET/AWS_KEY  ";
        viewModel.NewPathPattern = "  src/**  ";
        viewModel.NewReason = "  临时放行测试  ";

        await viewModel.AddCommand.ExecuteAsync(null);

        fake.Adds.Should().ContainSingle();
        viewModel.Entries.Should().ContainSingle();
        viewModel.NewRuleId.Should().BeEmpty();
        viewModel.NewPathPattern.Should().BeEmpty();
        viewModel.NewReason.Should().BeEmpty();
        viewModel.PageMessage.Should().Be("白名单条目已添加。");
        viewModel.IsPageMessageError.Should().BeFalse();
        fake.Queries.Should().OnlyContain(query => query.Offset == 0);
    }

    [Fact]
    public async Task AddCommand_WithoutConditions_ShowsValidationAndSkipsBackend()
    {
        var fake = new FakeRepositoryOperations();
        var viewModel = new AllowlistViewModel(fake, () => { }, DesktopLanguage.Chinese);
        await viewModel.OpenAsync("甲仓库", CreateRepositoryPath(), TestContext.Current.CancellationToken);
        viewModel.NewReason = "测试放行原因";

        await viewModel.AddCommand.ExecuteAsync(null);

        viewModel.FormMessage.Should().Be("请至少填写规则 ID、路径 glob 或指纹之一。");
        fake.Adds.Should().BeEmpty();
    }

    [Fact]
    public async Task AddCommand_WithShortReason_ShowsValidationAndSkipsBackend()
    {
        var fake = new FakeRepositoryOperations();
        var viewModel = new AllowlistViewModel(fake, () => { }, DesktopLanguage.Chinese);
        await viewModel.OpenAsync("甲仓库", CreateRepositoryPath(), TestContext.Current.CancellationToken);
        viewModel.NewRuleId = "GGD/SECRET/AWS_KEY";
        viewModel.NewReason = "短";

        await viewModel.AddCommand.ExecuteAsync(null);

        viewModel.FormMessage.Should().Be("原因至少需要 5 个字符。");
        fake.Adds.Should().BeEmpty();
    }

    [Fact]
    public async Task AddCommand_WithInvalidRuleId_ShowsValidationAndSkipsBackend()
    {
        var fake = new FakeRepositoryOperations();
        var viewModel = new AllowlistViewModel(fake, () => { }, DesktopLanguage.Chinese);
        await viewModel.OpenAsync("甲仓库", CreateRepositoryPath(), TestContext.Current.CancellationToken);
        viewModel.NewRuleId = "AWS_KEY";
        viewModel.NewReason = "测试放行原因";

        await viewModel.AddCommand.ExecuteAsync(null);

        viewModel.FormMessage.Should().Be("规则 ID 格式不合法，应形如 GGD/SECRET/AWS_ACCESS_KEY。");
        fake.Adds.Should().BeEmpty();
    }

    [Fact]
    public async Task AddCommand_WithInvalidFingerprint_ShowsValidationAndSkipsBackend()
    {
        var fake = new FakeRepositoryOperations();
        var viewModel = new AllowlistViewModel(fake, () => { }, DesktopLanguage.Chinese);
        await viewModel.OpenAsync("甲仓库", CreateRepositoryPath(), TestContext.Current.CancellationToken);
        viewModel.NewFingerprint = "not-a-sha256";
        viewModel.NewReason = "测试放行原因";

        await viewModel.AddCommand.ExecuteAsync(null);

        viewModel.FormMessage.Should().Be("指纹必须是 64 位十六进制 SHA-256 文本。");
        fake.Adds.Should().BeEmpty();
    }

    [Fact]
    public async Task AddCommand_WithFingerprintOnly_AddsEntry()
    {
        string fingerprintHex = new string('A', 64);
        AllowlistEntry added = CreateEntry(
            ruleId: null,
            pathPattern: null,
            fingerprint: Fingerprint.FromHex(fingerprintHex),
            reason: "按指纹放行");
        var fake = new FakeRepositoryOperations
        {
            QueryHandler = (_, _) => Task.FromResult(new AllowlistPage(new[] { added }, 1)),
            AddHandler = (ruleId, pathPattern, fingerprint, reason) =>
            {
                ruleId.Should().BeNull();
                pathPattern.Should().BeNull();
                fingerprint.Should().Be(fingerprintHex);
                reason.Should().Be("按指纹放行");
                return Task.FromResult(added);
            },
        };
        var viewModel = new AllowlistViewModel(fake, () => { }, DesktopLanguage.Chinese);
        await viewModel.OpenAsync("甲仓库", CreateRepositoryPath(), TestContext.Current.CancellationToken);
        viewModel.NewFingerprint = fingerprintHex;
        viewModel.NewReason = "按指纹放行";

        await viewModel.AddCommand.ExecuteAsync(null);

        fake.Adds.Should().ContainSingle();
        viewModel.Entries.Should().ContainSingle().Which.HasFingerprint.Should().BeTrue();
    }

    [Fact]
    public async Task AddCommand_WithTooLongReason_ShowsValidationAndSkipsBackend()
    {
        var fake = new FakeRepositoryOperations();
        var viewModel = new AllowlistViewModel(fake, () => { }, DesktopLanguage.Chinese);
        await viewModel.OpenAsync("甲仓库", CreateRepositoryPath(), TestContext.Current.CancellationToken);
        viewModel.NewRuleId = "GGD/SECRET/AWS_KEY";
        viewModel.NewReason = new string('a', 257);

        await viewModel.AddCommand.ExecuteAsync(null);

        viewModel.FormMessage.Should().Be("原因不能超过 256 个字符。");
        fake.Adds.Should().BeEmpty();
    }

    [Fact]
    public async Task AddCommand_BackendValidationFailure_MapsLocalizedMessageAndKeepsInput()
    {
        var fake = new FakeRepositoryOperations
        {
            AddHandler = (_, _, _, _) => Task.FromException<AllowlistEntry>(
                new AllowlistException(AllowlistException.ALLOWLIST_VALIDATION_FAILED, "原始消息")),
        };
        var viewModel = new AllowlistViewModel(fake, () => { }, DesktopLanguage.Chinese);
        await viewModel.OpenAsync("甲仓库", CreateRepositoryPath(), TestContext.Current.CancellationToken);
        viewModel.NewRuleId = "GGD/SECRET/AWS_KEY";
        viewModel.NewReason = "测试放行原因";

        await viewModel.AddCommand.ExecuteAsync(null);

        viewModel.FormMessage.Should().Be("白名单参数不合法，请检查规则 ID、路径 glob、指纹与原因。");
        viewModel.NewRuleId.Should().Be("GGD/SECRET/AWS_KEY");
        viewModel.NewReason.Should().Be("测试放行原因");
    }

    [Fact]
    public async Task RequestDeleteCommand_ShowsConfirmationAndCancelHidesIt()
    {
        AllowlistEntry entry = CreateEntry();
        var fake = new FakeRepositoryOperations
        {
            QueryHandler = (_, _) => Task.FromResult(new AllowlistPage(new[] { entry }, 1)),
        };
        var viewModel = new AllowlistViewModel(fake, () => { }, DesktopLanguage.Chinese);
        await viewModel.OpenAsync("甲仓库", CreateRepositoryPath(), TestContext.Current.CancellationToken);

        viewModel.RequestDeleteCommand.Execute(viewModel.Entries[0]);

        viewModel.HasPendingDelete.Should().BeTrue();
        viewModel.PendingDeleteSummary.Should().Contain("GGD/SECRET/AWS_KEY");
        viewModel.ConfirmDeleteCommand.CanExecute(null).Should().BeTrue();

        viewModel.CancelDeleteCommand.Execute(null);

        viewModel.HasPendingDelete.Should().BeFalse();
        fake.RemovedIds.Should().BeEmpty();
    }

    [Fact]
    public async Task ConfirmDeleteCommand_RemovesEntryReloadsAndShowsMessage()
    {
        AllowlistEntry entry = CreateEntry();
        int queryCalls = 0;
        var fake = new FakeRepositoryOperations
        {
            QueryHandler = (_, _) =>
            {
                queryCalls++;
                return Task.FromResult(queryCalls == 1
                    ? new AllowlistPage(new[] { entry }, 1)
                    : new AllowlistPage(Array.Empty<AllowlistEntry>(), 0));
            },
            RemoveHandler = id =>
            {
                id.Should().Be(entry.Id);
                return Task.CompletedTask;
            },
        };
        var viewModel = new AllowlistViewModel(fake, () => { }, DesktopLanguage.Chinese);
        await viewModel.OpenAsync("甲仓库", CreateRepositoryPath(), TestContext.Current.CancellationToken);
        viewModel.RequestDeleteCommand.Execute(viewModel.Entries[0]);

        await viewModel.ConfirmDeleteCommand.ExecuteAsync(null);

        fake.RemovedIds.Should().ContainSingle().Which.Should().Be(entry.Id);
        viewModel.Entries.Should().BeEmpty();
        viewModel.HasPendingDelete.Should().BeFalse();
        viewModel.PageMessage.Should().Be("白名单条目已删除。");
        viewModel.IsBusy.Should().BeFalse();
    }

    [Fact]
    public async Task ConfirmDeleteCommand_NotFound_MapsMessageAndKeepsConfirmation()
    {
        AllowlistEntry entry = CreateEntry();
        var fake = new FakeRepositoryOperations
        {
            QueryHandler = (_, _) => Task.FromResult(new AllowlistPage(new[] { entry }, 1)),
            RemoveHandler = _ => Task.FromException(
                new AllowlistException(AllowlistException.ALLOWLIST_NOT_FOUND, "原始消息")),
        };
        var viewModel = new AllowlistViewModel(fake, () => { }, DesktopLanguage.Chinese);
        await viewModel.OpenAsync("甲仓库", CreateRepositoryPath(), TestContext.Current.CancellationToken);
        viewModel.RequestDeleteCommand.Execute(viewModel.Entries[0]);

        await viewModel.ConfirmDeleteCommand.ExecuteAsync(null);

        viewModel.PageMessage.Should().Be("白名单条目已不存在。");
        viewModel.IsPageMessageError.Should().BeTrue();
        viewModel.HasPendingDelete.Should().BeTrue();
    }

    [Fact]
    public async Task Relocalize_RefreshesLabelsAndEntryMeta()
    {
        AllowlistEntry entry = CreateEntry();
        var fake = new FakeRepositoryOperations
        {
            QueryHandler = (_, _) => Task.FromResult(new AllowlistPage(new[] { entry }, 1)),
        };
        var viewModel = new AllowlistViewModel(fake, () => { }, DesktopLanguage.Chinese);
        await viewModel.OpenAsync("甲仓库", CreateRepositoryPath(), TestContext.Current.CancellationToken);
        viewModel.EntriesTitle.Should().Be("白名单条目");
        viewModel.Entries[0].MetaText.Should().StartWith("由 tester 于");

        viewModel.Relocalize(DesktopLanguage.English);

        viewModel.Title.Should().Be("Allowlist");
        viewModel.EntriesTitle.Should().Be("Allowlist entries");
        viewModel.PageText.Should().Be("Page 1 of 1 · 1 entries");
        viewModel.Entries[0].MetaText.Should().StartWith("Added by tester on");
        viewModel.Entries[0].ConditionSummary.Should().Contain("Rule ID");
    }

    [Fact]
    public async Task LoadFailure_ShowsErrorAndClearsLoadingState()
    {
        var fake = new FakeRepositoryOperations
        {
            QueryHandler = (_, _) => Task.FromException<AllowlistPage>(new InvalidOperationException("读取失败")),
        };
        var viewModel = new AllowlistViewModel(fake, () => { }, DesktopLanguage.Chinese);

        await viewModel.OpenAsync("甲仓库", CreateRepositoryPath(), TestContext.Current.CancellationToken);

        viewModel.PageMessage.Should().Be("读取失败");
        viewModel.IsPageMessageError.Should().BeTrue();
        viewModel.IsLoadingPage.Should().BeFalse();
        viewModel.IsEmpty.Should().BeFalse();
    }

    private static AllowlistEntry CreateEntry(
        string? ruleId = "GGD/SECRET/AWS_KEY",
        string? pathPattern = "src/**",
        Fingerprint? fingerprint = null,
        string reason = "测试放行原因",
        string createdBy = "tester") =>
        AllowlistEntry.Create(ruleId, pathPattern, fingerprint, reason, _fixedCreatedAt, createdBy);

    private static string CreateRepositoryPath() =>
        Path.Combine(Path.GetTempPath(), "gitguard-desktop-tests", Guid.NewGuid().ToString("N"), "repo");

    /// <summary>可编程仓库 / 白名单操作假对象；仅白名单方法参与本组测试。</summary>
    private sealed class FakeRepositoryOperations : IRepositoryOperations
    {
        internal Func<int, int, Task<AllowlistPage>> QueryHandler { get; set; } =
            (_, _) => Task.FromResult(new AllowlistPage(Array.Empty<AllowlistEntry>(), 0));

        internal Func<string?, string?, string?, string, Task<AllowlistEntry>> AddHandler { get; set; } =
            (_, _, _, _) => Task.FromException<AllowlistEntry>(new NotSupportedException("测试未配置 AddHandler。"));

        internal Func<Guid, Task> RemoveHandler { get; set; } =
            _ => Task.FromException(new NotSupportedException("测试未配置 RemoveHandler。"));

        internal List<(int Limit, int Offset)> Queries { get; } = new List<(int Limit, int Offset)>();

        internal List<(string? RuleId, string? PathPattern, string? FingerprintHex, string Reason)> Adds { get; } =
            new List<(string? RuleId, string? PathPattern, string? FingerprintHex, string Reason)>();

        internal List<Guid> RemovedIds { get; } = new List<Guid>();

        public Task<IReadOnlyList<RegisteredRepository>> ListAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("白名单测试不读取仓库列表。");

        public Task<RegisteredRepository> RegisterAsync(string path, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("白名单测试不登记仓库。");

        public Task RemoveAsync(Guid id, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("白名单测试不移除仓库。");

        public Task<RepositoryDetail> LoadDetailAsync(string repoPath, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("白名单测试不读取仓库详情。");

        public Task<RepositoryDetail> ScanAsync(string repoPath, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("白名单测试不执行扫描。");

        public Task<string?> ResolveHookExecutableAsync(
            string repoPath,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("白名单测试不解析 GitGuard CLI。");

        public Task<RepositoryDetail> InstallHooksAsync(
            string repoPath,
            string executablePath,
            bool force,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("白名单测试不安装 Hook。");

        public Task<RepositoryDetail> UninstallHooksAsync(
            string repoPath,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("白名单测试不卸载 Hook。");

        public Task<AuditRunPage> QueryScanHistoryAsync(
            string repoPath,
            int limit,
            int offset,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("白名单测试不读取扫描历史。");

        public Task<ScanRun?> LoadScanRunAsync(Guid runId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("白名单测试不读取扫描明细。");

        public Task<AllowlistPage> QueryAllowlistAsync(
            int limit,
            int offset,
            CancellationToken cancellationToken = default)
        {
            Queries.Add((limit, offset));
            return QueryHandler(limit, offset);
        }

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

        public Task RemoveAllowlistAsync(Guid id, CancellationToken cancellationToken = default)
        {
            RemovedIds.Add(id);
            return RemoveHandler(id);
        }
    }
}
