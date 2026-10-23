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
/// 仓库页 ViewModel 测试（GGD-0042 / 11-前端UI设计 §2-§3）：列表加载、登记、扫描、移除二次确认、
/// 错误映射与语言切换；数据访问由 <see cref="IRepositoryOperations"/> 假对象驱动。
/// </summary>
public sealed class RepositoriesViewModelTests
{
    [Fact]
    public async Task EnsureLoadedAsync_LoadsListSelectsFirstAndShowsDetail()
    {
        var first = CreateRepository("repo-a", "甲仓库");
        var second = CreateRepository("repo-b", "乙仓库");
        var fake = new FakeRepositoryOperations
        {
            ListHandler = () => Task.FromResult<IReadOnlyList<RegisteredRepository>>(new[] { first, second }),
            LoadDetailHandler = path => Task.FromResult(CreateDetail(path)),
        };
        var viewModel = new RepositoriesViewModel(fake, DesktopLanguage.Chinese);

        await viewModel.EnsureLoadedAsync();

        viewModel.Repositories.Should().HaveCount(2);
        viewModel.SelectedRepository!.Id.Should().Be(first.Id);
        viewModel.IsDetailVisible.Should().BeTrue();
        viewModel.IsDetailLoading.Should().BeFalse();
        viewModel.HookRows.Should().HaveCount(3);
        viewModel.GitGuardVersionText.Should().Be("0.3.0");
        viewModel.GitVersionText.Should().Be("git version 2.43.0");
        viewModel.HookRows.Select(row => row.HookName).Should().Equal("pre-commit", "pre-push", "post-commit");
        viewModel.HasHookAttention.Should().BeTrue("post-commit 为未托管脚本");
    }

    [Fact]
    public async Task EnsureLoadedAsync_CalledTwice_LoadsOnlyOnce()
    {
        var fake = new FakeRepositoryOperations
        {
            ListHandler = () => Task.FromResult<IReadOnlyList<RegisteredRepository>>(Array.Empty<RegisteredRepository>()),
        };
        var viewModel = new RepositoriesViewModel(fake, DesktopLanguage.Chinese);

        await viewModel.EnsureLoadedAsync();
        await viewModel.EnsureLoadedAsync();

        fake.ListCalls.Should().Be(1);
    }

    [Fact]
    public async Task EnsureLoadedAsync_EmptyRegistry_ShowsEmptyState()
    {
        var fake = new FakeRepositoryOperations();
        var viewModel = new RepositoriesViewModel(fake, DesktopLanguage.Chinese);

        await viewModel.EnsureLoadedAsync();

        viewModel.IsEmpty.Should().BeTrue();
        viewModel.IsDetailVisible.Should().BeFalse();
        viewModel.IsSelectPromptVisible.Should().BeFalse();
        viewModel.ListMessage.Should().BeNull();
    }

    [Fact]
    public async Task EnsureLoadedAsync_ListFailure_MapsRepositoryErrorToLocalizedMessage()
    {
        var fake = new FakeRepositoryOperations
        {
            ListHandler = () => Task.FromException<IReadOnlyList<RegisteredRepository>>(
                new RepositoryException(RepositoryException.REPOSITORY_ALREADY_REGISTERED, "原始消息")),
        };
        var viewModel = new RepositoriesViewModel(fake, DesktopLanguage.Chinese);

        await viewModel.EnsureLoadedAsync();

        viewModel.HasListMessage.Should().BeTrue();
        viewModel.ListMessage.Should().Be("该仓库已经登记。");
        viewModel.IsBusy.Should().BeFalse();
    }

    [Fact]
    public async Task EnsureLoadedAsync_SetsBusyWhileListIsLoading()
    {
        var gate = new TaskCompletionSource<IReadOnlyList<RegisteredRepository>>();
        var fake = new FakeRepositoryOperations { ListHandler = () => gate.Task };
        var viewModel = new RepositoriesViewModel(fake, DesktopLanguage.Chinese);

        Task loading = viewModel.EnsureLoadedAsync();

        viewModel.IsBusy.Should().BeTrue();
        gate.SetResult(Array.Empty<RegisteredRepository>());
        await loading;
        viewModel.IsBusy.Should().BeFalse();
    }

    [Fact]
    public async Task AddCommand_RegistersRepositoryAndSelectsIt()
    {
        var existing = CreateRepository("repo-a", "旧仓库");
        var added = CreateRepository("repo-b", "新仓库");
        int listCalls = 0;
        var fake = new FakeRepositoryOperations
        {
            ListHandler = () =>
            {
                listCalls++;
                return Task.FromResult<IReadOnlyList<RegisteredRepository>>(
                    listCalls == 1 ? new[] { existing } : new[] { added, existing });
            },
            RegisterHandler = path =>
            {
                path.Should().Be("D:\\code\\new-repo");
                return Task.FromResult(added);
            },
            LoadDetailHandler = path => Task.FromResult(CreateDetail(path)),
        };
        var viewModel = new RepositoriesViewModel(fake, DesktopLanguage.Chinese);
        await viewModel.EnsureLoadedAsync();
        viewModel.NewRepositoryPath = "D:\\code\\new-repo";

        await viewModel.AddCommand.ExecuteAsync(null);

        fake.RegisterCalls.Should().Be(1);
        fake.ListCalls.Should().Be(2);
        viewModel.NewRepositoryPath.Should().BeEmpty();
        viewModel.SelectedRepository!.Id.Should().Be(added.Id);
        viewModel.Repositories.Select(item => item.Id).Should().Equal(added.Id, existing.Id);
    }

    [Fact]
    public async Task AddCommand_Failure_MapsMessageAndKeepsInput()
    {
        var fake = new FakeRepositoryOperations
        {
            RegisterHandler = _ => Task.FromException<RegisteredRepository>(
                new RepositoryException(RepositoryException.REPOSITORY_NOT_A_REPOSITORY, "原始消息")),
        };
        var viewModel = new RepositoriesViewModel(fake, DesktopLanguage.Chinese);
        await viewModel.EnsureLoadedAsync();
        viewModel.NewRepositoryPath = "D:\\plain";

        await viewModel.AddCommand.ExecuteAsync(null);

        viewModel.ListMessage.Should().Be("所选目录不是 Git 工作区。");
        viewModel.NewRepositoryPath.Should().Be("D:\\plain");
    }

    [Fact]
    public async Task ScanCommand_UpdatesLatestScanAndCompletionMessage()
    {
        var repository = CreateRepository("repo-a", "甲仓库");
        var fake = new FakeRepositoryOperations
        {
            ListHandler = () => Task.FromResult<IReadOnlyList<RegisteredRepository>>(new[] { repository }),
            LoadDetailHandler = path => Task.FromResult(CreateDetail(path)),
            ScanHandler = path => Task.FromResult(CreateDetail(path, CreateBlockedSummary(path))),
        };
        var viewModel = new RepositoriesViewModel(fake, DesktopLanguage.Chinese);
        await viewModel.EnsureLoadedAsync();
        viewModel.HasLatestScan.Should().BeFalse();

        await viewModel.ScanCommand.ExecuteAsync(null);

        fake.ScanCalls.Should().Be(1);
        viewModel.IsScanning.Should().BeFalse();
        viewModel.HasLatestScan.Should().BeTrue();
        viewModel.LatestScanResultText.Should().Be("已阻断");
        viewModel.DetailMessage.Should().Be("扫描完成：已阻断，1 个发现。");
    }

    [Fact]
    public async Task ScanCommand_AuditWriteFailed_ShowsDegradedWarning()
    {
        var repository = CreateRepository("repo-a", "甲仓库");
        var fake = new FakeRepositoryOperations
        {
            ListHandler = () => Task.FromResult<IReadOnlyList<RegisteredRepository>>(new[] { repository }),
            LoadDetailHandler = path => Task.FromResult(CreateDetail(path)),
            ScanHandler = path => Task.FromResult(CreateDetail(path, CreateBlockedSummary(path), auditWriteFailed: true)),
        };
        var viewModel = new RepositoriesViewModel(fake, DesktopLanguage.Chinese);
        await viewModel.EnsureLoadedAsync();

        await viewModel.ScanCommand.ExecuteAsync(null);

        viewModel.DetailMessage.Should().Be("扫描已完成，但审计记录写入失败。");
    }

    [Fact]
    public async Task RequestRemoveCommand_ShowsConfirmationAndCancelHidesIt()
    {
        var repository = CreateRepository("repo-a", "甲仓库");
        var fake = new FakeRepositoryOperations
        {
            ListHandler = () => Task.FromResult<IReadOnlyList<RegisteredRepository>>(new[] { repository }),
            LoadDetailHandler = path => Task.FromResult(CreateDetail(path)),
        };
        var viewModel = new RepositoriesViewModel(fake, DesktopLanguage.Chinese);
        await viewModel.EnsureLoadedAsync();

        viewModel.RequestRemoveCommand.Execute(null);
        viewModel.IsRemoveConfirmationVisible.Should().BeTrue();

        viewModel.CancelRemoveCommand.Execute(null);
        viewModel.IsRemoveConfirmationVisible.Should().BeFalse();
        fake.RemoveCalls.Should().Be(0);
    }

    [Fact]
    public async Task ConfirmRemoveCommand_RemovesRegistrationAndRefreshesList()
    {
        var repository = CreateRepository("repo-a", "甲仓库");
        int listCalls = 0;
        var fake = new FakeRepositoryOperations
        {
            ListHandler = () =>
            {
                listCalls++;
                return Task.FromResult<IReadOnlyList<RegisteredRepository>>(
                    listCalls == 1 ? new[] { repository } : Array.Empty<RegisteredRepository>());
            },
            LoadDetailHandler = path => Task.FromResult(CreateDetail(path)),
            RemoveHandler = id =>
            {
                id.Should().Be(repository.Id);
                return Task.CompletedTask;
            },
        };
        var viewModel = new RepositoriesViewModel(fake, DesktopLanguage.Chinese);
        await viewModel.EnsureLoadedAsync();
        viewModel.RequestRemoveCommand.Execute(null);

        await viewModel.ConfirmRemoveCommand.ExecuteAsync(null);

        fake.RemoveCalls.Should().Be(1);
        viewModel.Repositories.Should().BeEmpty();
        viewModel.IsRemoveConfirmationVisible.Should().BeFalse();
        viewModel.SelectedRepository.Should().BeNull();
        viewModel.IsDetailVisible.Should().BeFalse();
    }

    [Fact]
    public async Task Relocalize_RefreshesPageLabelsItemsHooksAndLatestScan()
    {
        var repository = CreateRepository("repo-a", "甲仓库");
        var fake = new FakeRepositoryOperations
        {
            ListHandler = () => Task.FromResult<IReadOnlyList<RegisteredRepository>>(new[] { repository }),
            LoadDetailHandler = path => Task.FromResult(CreateDetail(path, CreateBlockedSummary(path))),
        };
        var viewModel = new RepositoriesViewModel(fake, DesktopLanguage.Chinese);
        await viewModel.EnsureLoadedAsync();

        viewModel.Relocalize(DesktopLanguage.English);

        viewModel.ListTitle.Should().Be("Registered repositories");
        viewModel.AddButtonText.Should().Be("Register");
        viewModel.ScanButtonText.Should().Be("Scan now");
        viewModel.HookRows[0].StatusText.Should().Be("Managed");
        viewModel.HookRows[2].StatusText.Should().Be("Unmanaged script");
        viewModel.LatestScanResultText.Should().Be("Blocked");
        viewModel.DetailMessage.Should().BeNull();
    }

    [Fact]
    public async Task SelectingRepository_LoadsThatRepositoryDetail()
    {
        var first = CreateRepository("repo-a", "甲仓库");
        var second = CreateRepository("repo-b", "乙仓库");
        var fake = new FakeRepositoryOperations
        {
            ListHandler = () => Task.FromResult<IReadOnlyList<RegisteredRepository>>(new[] { first, second }),
            LoadDetailHandler = path => Task.FromResult(CreateDetail(path)),
        };
        var viewModel = new RepositoriesViewModel(fake, DesktopLanguage.Chinese);
        await viewModel.EnsureLoadedAsync();
        int initialLoadCalls = fake.LoadDetailCalls;

        viewModel.SelectedRepository = viewModel.Repositories[1];

        fake.LoadDetailCalls.Should().Be(initialLoadCalls + 1);
        viewModel.HookRows.Should().HaveCount(3);
        viewModel.IsDetailVisible.Should().BeTrue();
    }

    [Fact]
    public async Task SelectingNull_HidesDetailPanel()
    {
        var repository = CreateRepository("repo-a", "甲仓库");
        var fake = new FakeRepositoryOperations
        {
            ListHandler = () => Task.FromResult<IReadOnlyList<RegisteredRepository>>(new[] { repository }),
            LoadDetailHandler = path => Task.FromResult(CreateDetail(path)),
        };
        var viewModel = new RepositoriesViewModel(fake, DesktopLanguage.Chinese);
        await viewModel.EnsureLoadedAsync();

        viewModel.SelectedRepository = null;

        viewModel.IsDetailVisible.Should().BeFalse();
        viewModel.HookRows.Should().BeEmpty();
    }

    [Fact]
    public async Task OpenHistoryCommand_ShowsHistoryForSelectedRepositoryAndBackClosesIt()
    {
        var repository = CreateRepository("repo-a", "甲仓库");
        var summary = CreateBlockedSummary(repository.RepoPath);
        var fake = new FakeRepositoryOperations
        {
            ListHandler = () => Task.FromResult<IReadOnlyList<RegisteredRepository>>(new[] { repository }),
            LoadDetailHandler = path => Task.FromResult(CreateDetail(path)),
            QueryScanHistoryHandler = (path, _, _) =>
            {
                path.Should().Be(repository.RepoPath);
                return Task.FromResult(new AuditRunPage(new[] { summary }, 1));
            },
            LoadScanRunHandler = id => Task.FromResult<ScanRun?>(CreateRun(repository.RepoPath, id)),
        };
        var viewModel = new RepositoriesViewModel(fake, DesktopLanguage.Chinese);
        await viewModel.EnsureLoadedAsync();

        await viewModel.OpenHistoryCommand.ExecuteAsync(null);

        viewModel.IsHistoryVisible.Should().BeTrue();
        viewModel.CanInteract.Should().BeFalse();
        viewModel.History.RepositoryName.Should().Be("甲仓库");
        viewModel.History.RepositoryPath.Should().Be(repository.RepoPath);
        viewModel.History.Runs.Should().ContainSingle();
        viewModel.History.Findings.Should().ContainSingle();

        viewModel.History.BackCommand.Execute(null);

        viewModel.IsHistoryVisible.Should().BeFalse();
        viewModel.CanInteract.Should().BeTrue();
        viewModel.History.Runs.Should().BeEmpty();
        viewModel.History.RepositoryName.Should().BeEmpty();
    }

    [Fact]
    public async Task SelectingAnotherRepository_WhileHistoryOpen_ClosesHistory()
    {
        var first = CreateRepository("repo-a", "甲仓库");
        var second = CreateRepository("repo-b", "乙仓库");
        var fake = new FakeRepositoryOperations
        {
            ListHandler = () => Task.FromResult<IReadOnlyList<RegisteredRepository>>(new[] { first, second }),
            LoadDetailHandler = path => Task.FromResult(CreateDetail(path)),
        };
        var viewModel = new RepositoriesViewModel(fake, DesktopLanguage.Chinese);
        await viewModel.EnsureLoadedAsync();
        await viewModel.OpenHistoryCommand.ExecuteAsync(null);
        viewModel.IsHistoryVisible.Should().BeTrue();

        viewModel.SelectedRepository = viewModel.Repositories[1];

        viewModel.IsHistoryVisible.Should().BeFalse();
        viewModel.History.Runs.Should().BeEmpty();
    }

    [Fact]
    public async Task OpenAllowlistCommand_ShowsGlobalAllowlistAndBackClosesIt()
    {
        var repository = CreateRepository("repo-a", "甲仓库");
        var entry = AllowlistEntry.Create(
            "GGD/SECRET/AWS_KEY",
            "src/**",
            null,
            "测试放行原因",
            new DateTimeOffset(2026, 10, 20, 9, 30, 0, TimeSpan.Zero),
            "tester");
        var fake = new FakeRepositoryOperations
        {
            ListHandler = () => Task.FromResult<IReadOnlyList<RegisteredRepository>>(new[] { repository }),
            LoadDetailHandler = path => Task.FromResult(CreateDetail(path)),
            QueryAllowlistHandler = (limit, offset) =>
            {
                limit.Should().Be(AllowlistViewModel.PAGE_SIZE);
                offset.Should().Be(0);
                return Task.FromResult(new AllowlistPage(new[] { entry }, 1));
            },
        };
        var viewModel = new RepositoriesViewModel(fake, DesktopLanguage.Chinese);
        await viewModel.EnsureLoadedAsync();

        await viewModel.OpenAllowlistCommand.ExecuteAsync(null);

        viewModel.IsAllowlistVisible.Should().BeTrue();
        viewModel.CanInteract.Should().BeFalse();
        viewModel.Allowlist.RepositoryName.Should().Be("甲仓库");
        viewModel.Allowlist.RepositoryPath.Should().Be(repository.RepoPath);
        viewModel.Allowlist.Entries.Should().ContainSingle();
        viewModel.Allowlist.Entries[0].Reason.Should().Be("测试放行原因");

        viewModel.Allowlist.BackCommand.Execute(null);

        viewModel.IsAllowlistVisible.Should().BeFalse();
        viewModel.CanInteract.Should().BeTrue();
        viewModel.Allowlist.Entries.Should().BeEmpty();
        viewModel.Allowlist.RepositoryName.Should().BeEmpty();
    }

    [Fact]
    public async Task SelectingAnotherRepository_WhileAllowlistOpen_ClosesAllowlist()
    {
        var first = CreateRepository("repo-a", "甲仓库");
        var second = CreateRepository("repo-b", "乙仓库");
        var fake = new FakeRepositoryOperations
        {
            ListHandler = () => Task.FromResult<IReadOnlyList<RegisteredRepository>>(new[] { first, second }),
            LoadDetailHandler = path => Task.FromResult(CreateDetail(path)),
        };
        var viewModel = new RepositoriesViewModel(fake, DesktopLanguage.Chinese);
        await viewModel.EnsureLoadedAsync();
        await viewModel.OpenAllowlistCommand.ExecuteAsync(null);
        viewModel.IsAllowlistVisible.Should().BeTrue();

        viewModel.SelectedRepository = viewModel.Repositories[1];

        viewModel.IsAllowlistVisible.Should().BeFalse();
        viewModel.Allowlist.Entries.Should().BeEmpty();
    }

    private static ScanRun CreateRun(string repoPath, Guid runId)
    {
        var finding = Finding.Create(
            "GGD/SECRET/AWS_KEY",
            "src/appsettings.json",
            42,
            Severity.High,
            Fingerprint.FromHex(new string('f', 64)),
            SnippetMasked.Create("secret-********"));
        return ScanRun.Restore(
            runId,
            repoPath,
            ScanType.Manual,
            new DateTimeOffset(2026, 10, 20, 10, 0, 0, TimeSpan.Zero),
            45,
            ScanResult.Blocked,
            1,
            new[] { finding });
    }

    private static RegisteredRepository CreateRepository(string directoryName, string displayName)
    {
        string path = Path.Combine(Path.GetTempPath(), "gitguard-desktop-tests", Guid.NewGuid().ToString("N"), directoryName);
        return RegisteredRepository.Create(path, displayName, new DateTimeOffset(2026, 10, 20, 9, 0, 0, TimeSpan.Zero));
    }

    private static RepositoryDetail CreateDetail(
        string repoPath,
        AuditRunSummary? latestScan = null,
        bool auditWriteFailed = false)
    {
        string hooksDirectory = Path.Combine(repoPath, ".git", "hooks");
        var hooks = new[]
        {
            new HookStatus(HookType.PreCommit, Path.Combine(hooksDirectory, "pre-commit"), HookStatusKind.Managed, "0.3.0"),
            new HookStatus(HookType.PrePush, Path.Combine(hooksDirectory, "pre-push"), HookStatusKind.Missing, null),
            new HookStatus(HookType.PostCommit, Path.Combine(hooksDirectory, "post-commit"), HookStatusKind.Legacy, null),
        };
        var status = new StatusReport(
            repoPath,
            "git version 2.43.0",
            null,
            hooksDirectory,
            hooks,
            "0.3.0",
            Array.Empty<string>(),
            Array.Empty<string>());
        return new RepositoryDetail(status, latestScan, auditWriteFailed);
    }

    private static AuditRunSummary CreateBlockedSummary(string repoPath) =>
        new AuditRunSummary(
            Guid.CreateVersion7(),
            repoPath,
            ScanType.Manual,
            new DateTimeOffset(2026, 10, 20, 10, 0, 0, TimeSpan.Zero),
            45,
            ScanResult.Blocked,
            1,
            1);

    /// <summary>可编程仓库操作假对象：默认返回已完成任务，便于 ViewModel 同步推进。</summary>
    private sealed class FakeRepositoryOperations : IRepositoryOperations
    {
        internal Func<Task<IReadOnlyList<RegisteredRepository>>> ListHandler { get; set; } =
            () => Task.FromResult<IReadOnlyList<RegisteredRepository>>(Array.Empty<RegisteredRepository>());

        internal Func<string, Task<RegisteredRepository>> RegisterHandler { get; set; } =
            _ => Task.FromException<RegisteredRepository>(new NotSupportedException("测试未配置 RegisterHandler。"));

        internal Func<Guid, Task> RemoveHandler { get; set; } =
            _ => Task.FromException(new NotSupportedException("测试未配置 RemoveHandler。"));

        internal Func<string, Task<RepositoryDetail>> LoadDetailHandler { get; set; } =
            path => Task.FromResult(CreateDetail(path));

        internal Func<string, Task<RepositoryDetail>> ScanHandler { get; set; } =
            path => Task.FromResult(CreateDetail(path));

        internal Func<string, int, int, Task<AuditRunPage>> QueryScanHistoryHandler { get; set; } =
            (_, _, _) => Task.FromResult(new AuditRunPage(Array.Empty<AuditRunSummary>(), 0));

        internal Func<Guid, Task<ScanRun?>> LoadScanRunHandler { get; set; } =
            _ => Task.FromResult<ScanRun?>(null);

        internal Func<int, int, Task<AllowlistPage>> QueryAllowlistHandler { get; set; } =
            (_, _) => Task.FromResult(new AllowlistPage(Array.Empty<AllowlistEntry>(), 0));

        internal Func<string?, string?, string?, string, Task<AllowlistEntry>> AddAllowlistHandler { get; set; } =
            (_, _, _, _) => Task.FromException<AllowlistEntry>(new NotSupportedException("测试未配置 AddAllowlistHandler。"));

        internal Func<Guid, Task> RemoveAllowlistHandler { get; set; } =
            _ => Task.FromException(new NotSupportedException("测试未配置 RemoveAllowlistHandler。"));

        internal int ListCalls { get; private set; }

        internal int RegisterCalls { get; private set; }

        internal int RemoveCalls { get; private set; }

        internal int LoadDetailCalls { get; private set; }

        internal int ScanCalls { get; private set; }

        internal int QueryScanHistoryCalls { get; private set; }

        internal int LoadScanRunCalls { get; private set; }

        internal int QueryAllowlistCalls { get; private set; }

        internal int AddAllowlistCalls { get; private set; }

        internal int RemoveAllowlistCalls { get; private set; }

        public Task<IReadOnlyList<RegisteredRepository>> ListAsync(CancellationToken cancellationToken = default)
        {
            ListCalls++;
            return ListHandler();
        }

        public Task<RegisteredRepository> RegisterAsync(string path, CancellationToken cancellationToken = default)
        {
            RegisterCalls++;
            return RegisterHandler(path);
        }

        public Task RemoveAsync(Guid id, CancellationToken cancellationToken = default)
        {
            RemoveCalls++;
            return RemoveHandler(id);
        }

        public Task<RepositoryDetail> LoadDetailAsync(string repoPath, CancellationToken cancellationToken = default)
        {
            LoadDetailCalls++;
            return LoadDetailHandler(repoPath);
        }

        public Task<RepositoryDetail> ScanAsync(string repoPath, CancellationToken cancellationToken = default)
        {
            ScanCalls++;
            return ScanHandler(repoPath);
        }

        public Task<AuditRunPage> QueryScanHistoryAsync(
            string repoPath,
            int limit,
            int offset,
            CancellationToken cancellationToken = default)
        {
            QueryScanHistoryCalls++;
            return QueryScanHistoryHandler(repoPath, limit, offset);
        }

        public Task<ScanRun?> LoadScanRunAsync(Guid runId, CancellationToken cancellationToken = default)
        {
            LoadScanRunCalls++;
            return LoadScanRunHandler(runId);
        }

        public Task<AllowlistPage> QueryAllowlistAsync(
            int limit,
            int offset,
            CancellationToken cancellationToken = default)
        {
            QueryAllowlistCalls++;
            return QueryAllowlistHandler(limit, offset);
        }

        public Task<AllowlistEntry> AddAllowlistAsync(
            string? ruleId,
            string? pathPattern,
            string? fingerprintHex,
            string reason,
            CancellationToken cancellationToken = default)
        {
            AddAllowlistCalls++;
            return AddAllowlistHandler(ruleId, pathPattern, fingerprintHex, reason);
        }

        public Task RemoveAllowlistAsync(Guid id, CancellationToken cancellationToken = default)
        {
            RemoveAllowlistCalls++;
            return RemoveAllowlistHandler(id);
        }
    }
}
