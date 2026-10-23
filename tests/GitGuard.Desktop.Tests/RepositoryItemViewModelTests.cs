using FluentAssertions;
using GitGuard.Application.Hooks;
using GitGuard.Core.Model;
using GitGuard.Core.Policy;
using GitGuard.Desktop.Localization;
using GitGuard.Desktop.Services;
using GitGuard.Desktop.ViewModels;

namespace GitGuard.Desktop.Tests;

/// <summary>仓库列表项、Hook 状态行与主窗口仓库页集成测试（GGD-0042 / 12-前端组件设计 §2-§3）。</summary>
public sealed class RepositoryItemViewModelTests
{
    [Fact]
    public void RepositoryItem_MissingDirectory_SetsMissingBadgeAndRelocalizes()
    {
        string missingPath = Path.Combine(Path.GetTempPath(), "gitguard-missing-" + Guid.NewGuid().ToString("N"));
        var repository = RegisteredRepository.Create(
            missingPath,
            "已删除仓库",
            new DateTimeOffset(2026, 10, 20, 9, 0, 0, TimeSpan.Zero));

        var item = new RepositoryItemViewModel(repository, DesktopLanguage.Chinese);

        item.Id.Should().Be(repository.Id);
        item.DisplayName.Should().Be("已删除仓库");
        item.RepoPath.Should().Be(repository.RepoPath);
        item.IsMissing.Should().BeTrue();
        item.MissingText.Should().Be("目录不存在");

        item.Relocalize(DesktopLanguage.English);

        item.MissingText.Should().Be("Missing");
    }

    [Fact]
    public void RepositoryItem_ExistingDirectory_IsNotMissing()
    {
        string existingPath = Path.Combine(Path.GetTempPath(), "gitguard-existing-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(existingPath);
        try
        {
            var repository = RegisteredRepository.Create(
                existingPath,
                "现存仓库",
                new DateTimeOffset(2026, 10, 20, 9, 0, 0, TimeSpan.Zero));

            var item = new RepositoryItemViewModel(repository, DesktopLanguage.Chinese);

            item.IsMissing.Should().BeFalse();
        }
        finally
        {
            Directory.Delete(existingPath, recursive: true);
        }
    }

    [Fact]
    public void HookRow_ManagedHook_ShowsVersionAndNoAttention()
    {
        var status = new HookStatus(HookType.PreCommit, "/repo/.git/hooks/pre-commit", HookStatusKind.Managed, "0.3.0");

        var row = new HookStatusRowViewModel(status, DesktopLanguage.Chinese);

        row.HookName.Should().Be("pre-commit");
        row.HookPath.Should().Be(status.HookPath);
        row.StatusText.Should().Be("已托管");
        row.VersionText.Should().Be("v0.3.0");
        row.NeedsAttention.Should().BeFalse();
    }

    [Fact]
    public void HookRow_UnmanagedHook_NeedsAttentionAndRelocalizes()
    {
        var status = new HookStatus(HookType.PrePush, "/repo/.git/hooks/pre-push", HookStatusKind.Legacy, null);

        var row = new HookStatusRowViewModel(status, DesktopLanguage.Chinese);

        row.StatusText.Should().Be("未托管脚本");
        row.VersionText.Should().BeEmpty();
        row.NeedsAttention.Should().BeTrue();

        row.Relocalize(DesktopLanguage.English);

        row.StatusText.Should().Be("Unmanaged script");
    }

    [Fact]
    public void MainViewModel_WithRepositoryOperations_UsesRepositoryPageAndRelocalizesCachedPage()
    {
        var repository = RegisteredRepository.Create(
            Path.Combine(Path.GetTempPath(), "gitguard-main-" + Guid.NewGuid().ToString("N")),
            "甲仓库",
            new DateTimeOffset(2026, 10, 20, 9, 0, 0, TimeSpan.Zero));
        var fake = new StubRepositoryOperations(repository);
        var viewModel = new MainViewModel(
            new AppState("0.3.0"),
            new DesktopServices { RepositoryOperations = fake });

        var page = viewModel.CurrentPage.Should().BeOfType<RepositoriesViewModel>().Subject;
        page.Repositories.Should().ContainSingle().Which.Id.Should().Be(repository.Id);

        viewModel.ToggleLanguageCommand.Execute(null);
        viewModel.SelectedNavigation = viewModel.NavigationItems[2];

        page.ListTitle.Should().Be("Registered repositories", "缓存的仓库页也必须跟随语言切换刷新");
        viewModel.CurrentPage.Section.Should().Be(DesktopSection.Logs);
    }

    /// <summary>仅实现仓库页所需行为的操作假对象。</summary>
    private sealed class StubRepositoryOperations : IRepositoryOperations
    {
        private readonly RegisteredRepository _repository;

        internal StubRepositoryOperations(RegisteredRepository repository) => _repository = repository;

        public Task<IReadOnlyList<RegisteredRepository>> ListAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<RegisteredRepository>>(new[] { _repository });

        public Task<RegisteredRepository> RegisterAsync(string path, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("本测试不登记仓库。");

        public Task RemoveAsync(Guid id, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("本测试不移除仓库。");

        public Task<RepositoryDetail> LoadDetailAsync(string repoPath, CancellationToken cancellationToken = default) =>
            Task.FromResult(
                new RepositoryDetail(
                    new StatusReport(
                        repoPath,
                        "git version 2.43.0",
                        null,
                        Path.Combine(repoPath, ".git", "hooks"),
                        Array.Empty<HookStatus>(),
                        "0.3.0",
                        Array.Empty<string>(),
                        Array.Empty<string>()),
                    latestScan: null));

        public Task<RepositoryDetail> ScanAsync(string repoPath, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("本测试不执行扫描。");

        public Task<AuditRunPage> QueryScanHistoryAsync(
            string repoPath,
            int limit,
            int offset,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("本测试不读取扫描历史。");

        public Task<ScanRun?> LoadScanRunAsync(Guid runId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("本测试不读取扫描明细。");

        public Task<AllowlistPage> QueryAllowlistAsync(
            int limit,
            int offset,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("本测试不读取白名单。");

        public Task<AllowlistEntry> AddAllowlistAsync(
            string? ruleId,
            string? pathPattern,
            string? fingerprintHex,
            string reason,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("本测试不新增白名单。");

        public Task RemoveAllowlistAsync(Guid id, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("本测试不删除白名单。");
    }
}
