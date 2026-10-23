using FluentAssertions;
using GitGuard.Application.Audit;
using GitGuard.Core.Model;
using GitGuard.Desktop.Localization;
using GitGuard.Desktop.Services;
using GitGuard.Desktop.ViewModels;

namespace GitGuard.Desktop.Tests;

/// <summary>
/// 跨仓库日志 ViewModel 测试（GGD-0046 / 11-前端UI设计 §2）：分页、结果 / 时间范围筛选、
/// JSON / CSV 导出与错误处理。审计数据访问与文件选择器由假对象驱动，不访问真实 SQLite。
/// </summary>
public sealed class LogsViewModelTests
{
    private static readonly DateTimeOffset _fixedNow =
        new DateTimeOffset(2026, 10, 23, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task EnsureLoadedAsync_LoadsFirstPageAndMapsRows()
    {
        var fake = new FakeAuditOperations
        {
            QueryHandler = _ => new AuditRunPage(new[] { CreateSummary(0) }, 1),
        };
        var picker = new FakeExportPathPicker();
        var viewModel = CreateViewModel(fake, picker);

        await viewModel.EnsureLoadedAsync(TestContext.Current.CancellationToken);

        fake.Queries.Should().ContainSingle();
        fake.Queries[0].Limit.Should().Be(LogsViewModel.PAGE_SIZE);
        fake.Queries[0].Offset.Should().Be(0);
        fake.Queries[0].Since.Should().BeNull();
        fake.Queries[0].Result.Should().BeNull();

        viewModel.Items.Should().ContainSingle();
        viewModel.Items[0].RepoName.Should().Be("demo");
        viewModel.Items[0].ResultText.Should().Be("干净");
        viewModel.HasItems.Should().BeTrue();
        viewModel.IsEmpty.Should().BeFalse();
        viewModel.TotalCount.Should().Be(1);
        viewModel.TotalPages.Should().Be(1);
        viewModel.PageText.Should().Be("第 1 / 1 页 · 共 1 条审计记录");
        viewModel.CanExport.Should().BeTrue();
        viewModel.SummaryText.Should().Be("共 1 条审计记录");
    }

    [Fact]
    public async Task EnsureLoadedAsync_EmptyDatabase_ShowsEmptyState()
    {
        var viewModel = CreateViewModel(new FakeAuditOperations(), new FakeExportPathPicker());

        await viewModel.EnsureLoadedAsync(TestContext.Current.CancellationToken);

        viewModel.IsEmpty.Should().BeTrue();
        viewModel.HasItems.Should().BeFalse();
        viewModel.CanExport.Should().BeFalse();
        viewModel.PageText.Should().Be("第 1 / 1 页 · 共 0 条审计记录");
    }

    [Fact]
    public async Task NextAndPreviousPageCommands_NavigateWithinBounds()
    {
        AuditRunSummary[] runs = Enumerable.Range(0, 25).Select(CreateSummary).ToArray();
        var fake = new FakeAuditOperations
        {
            QueryHandler = query => new AuditRunPage(
                runs.Skip(query.Offset).Take(query.Limit),
                runs.Length),
        };
        var viewModel = CreateViewModel(fake, new FakeExportPathPicker());
        await viewModel.EnsureLoadedAsync(TestContext.Current.CancellationToken);

        viewModel.Items.Should().HaveCount(20);
        viewModel.TotalPages.Should().Be(2);
        viewModel.CanGoNext.Should().BeTrue();
        viewModel.CanGoPrevious.Should().BeFalse();

        await viewModel.NextPageCommand.ExecuteAsync(null);

        viewModel.Items.Should().HaveCount(5);
        viewModel.CurrentPage.Should().Be(2);
        viewModel.CanGoNext.Should().BeFalse();
        viewModel.CanGoPrevious.Should().BeTrue();

        await viewModel.PreviousPageCommand.ExecuteAsync(null);

        viewModel.CurrentPage.Should().Be(1);
        fake.Queries.Select(query => query.Offset).Should().Equal(0, 20, 0);
    }

    [Fact]
    public async Task SelectingResultFilter_ReloadsFirstPageWithResultConstraint()
    {
        var fake = new FakeAuditOperations();
        var viewModel = CreateViewModel(fake, new FakeExportPathPicker());
        await viewModel.EnsureLoadedAsync(TestContext.Current.CancellationToken);

        viewModel.SelectedResultFilter = viewModel.ResultFilters.Single(
            option => option.Result == ScanResult.Blocked);
        await viewModel.RefreshCommand.ExecuteAsync(null);

        fake.Queries.Last().Result.Should().Be(ScanResult.Blocked);
        fake.Queries.Last().Offset.Should().Be(0);
    }

    [Fact]
    public async Task SelectingRangeFilter_UsesInjectedClockForSince()
    {
        var fake = new FakeAuditOperations();
        var viewModel = CreateViewModel(fake, new FakeExportPathPicker());
        await viewModel.EnsureLoadedAsync(TestContext.Current.CancellationToken);

        viewModel.SelectedRangeFilter = viewModel.RangeFilters.Single(option => option.Days == 7);
        await viewModel.RefreshCommand.ExecuteAsync(null);

        fake.Queries.Last().Since.Should().Be(_fixedNow.AddDays(-7));
    }

    [Fact]
    public async Task ExportJsonCommand_WritesAllRecordsThroughCliContract()
    {
        var fake = new FakeAuditOperations
        {
            QueryHandler = _ => new AuditRunPage(new[] { CreateSummary(0) }, 1),
            ExportHandler = (path, format) => new AuditExportReport(path, format, 1, 3),
        };
        var picker = new FakeExportPathPicker { Result = "/tmp/gitguard-audit.json" };
        var viewModel = CreateViewModel(fake, picker);
        await viewModel.EnsureLoadedAsync(TestContext.Current.CancellationToken);

        await viewModel.ExportJsonCommand.ExecuteAsync(null);

        fake.Exports.Should().ContainSingle();
        fake.Exports[0].Path.Should().Be("/tmp/gitguard-audit.json");
        fake.Exports[0].Format.Should().Be(AuditExportFormat.Json);
        picker.SuggestedFileName.Should().StartWith("gitguard-audit-").And.EndWith(".json");
        viewModel.Message.Should().Contain("已导出 1 条审计记录").And.Contain("/tmp/gitguard-audit.json");
        viewModel.IsMessageError.Should().BeFalse();
        viewModel.IsExporting.Should().BeFalse();
    }

    [Fact]
    public async Task ExportCsvCommand_UsesCsvFormatAndExtension()
    {
        var fake = new FakeAuditOperations
        {
            QueryHandler = _ => new AuditRunPage(new[] { CreateSummary(0) }, 1),
            ExportHandler = (path, format) => new AuditExportReport(path, format, 1, 0),
        };
        var picker = new FakeExportPathPicker { Result = "/tmp/gitguard-audit.csv" };
        var viewModel = CreateViewModel(fake, picker);
        await viewModel.EnsureLoadedAsync(TestContext.Current.CancellationToken);

        await viewModel.ExportCsvCommand.ExecuteAsync(null);

        fake.Exports[0].Format.Should().Be(AuditExportFormat.Csv);
        picker.SuggestedFileName.Should().EndWith(".csv");
    }

    [Fact]
    public async Task ExportCommand_WithoutPicker_DoesNothing()
    {
        var fake = new FakeAuditOperations
        {
            QueryHandler = _ => new AuditRunPage(new[] { CreateSummary(0) }, 1),
        };
        var viewModel = CreateViewModel(fake, exportPathPicker: null);
        await viewModel.EnsureLoadedAsync(TestContext.Current.CancellationToken);

        viewModel.CanExport.Should().BeFalse();
        await viewModel.ExportJsonCommand.ExecuteAsync(null);

        fake.Exports.Should().BeEmpty();
    }

    [Fact]
    public async Task ExportCommand_WhenPickerCancelled_DoesNotExportOrShowMessage()
    {
        var fake = new FakeAuditOperations
        {
            QueryHandler = _ => new AuditRunPage(new[] { CreateSummary(0) }, 1),
        };
        var picker = new FakeExportPathPicker { Result = null };
        var viewModel = CreateViewModel(fake, picker);
        await viewModel.EnsureLoadedAsync(TestContext.Current.CancellationToken);

        await viewModel.ExportJsonCommand.ExecuteAsync(null);

        fake.Exports.Should().BeEmpty();
        viewModel.Message.Should().BeNull();
    }

    [Fact]
    public async Task EnsureLoadedAsync_QueryFailure_ShowsLocalizedError()
    {
        var fake = new FakeAuditOperations
        {
            QueryHandler = _ => throw new InvalidOperationException("db locked"),
        };
        var viewModel = CreateViewModel(fake, new FakeExportPathPicker());

        await viewModel.EnsureLoadedAsync(TestContext.Current.CancellationToken);

        viewModel.IsMessageError.Should().BeTrue();
        viewModel.Message.Should().Be("审计日志加载失败：db locked");
        viewModel.IsEmpty.Should().BeFalse();
        viewModel.IsLoading.Should().BeFalse();
    }

    [Fact]
    public async Task ExportCommand_WhenExportFails_ShowsLocalizedError()
    {
        var fake = new FakeAuditOperations
        {
            QueryHandler = _ => new AuditRunPage(new[] { CreateSummary(0) }, 1),
            ExportHandler = (_, _) => throw new IOException("denied"),
        };
        var viewModel = CreateViewModel(fake, new FakeExportPathPicker { Result = "/tmp/gitguard-audit.json" });
        await viewModel.EnsureLoadedAsync(TestContext.Current.CancellationToken);

        await viewModel.ExportJsonCommand.ExecuteAsync(null);

        viewModel.IsMessageError.Should().BeTrue();
        viewModel.Message.Should().Be("导出失败：denied");
    }

    private static LogsViewModel CreateViewModel(
        IAuditOperations operations,
        IExportPathPicker? exportPathPicker) =>
        new LogsViewModel(operations, exportPathPicker, DesktopLanguage.Chinese, () => _fixedNow);

    private static AuditRunSummary CreateSummary(int index) =>
        new AuditRunSummary(
            Guid.NewGuid(),
            @"C:\repos\demo",
            ScanType.Manual,
            _fixedNow.AddMinutes(-index),
            120,
            index % 2 == 0 ? ScanResult.Clean : ScanResult.Blocked,
            0,
            index);

    /// <summary>审计数据访问假对象：按查询条件返回测试数据并记录导出调用。</summary>
    private sealed class FakeAuditOperations : IAuditOperations
    {
        internal Func<AuditQuery, AuditRunPage>? QueryHandler { get; set; }

        internal Func<string, AuditExportFormat, AuditExportReport>? ExportHandler { get; set; }

        internal List<AuditQuery> Queries { get; } = new List<AuditQuery>();

        internal List<(string Path, AuditExportFormat Format)> Exports { get; } = new List<(string, AuditExportFormat)>();

        public Task<AuditRunPage> QueryAsync(AuditQuery query, CancellationToken cancellationToken = default)
        {
            Queries.Add(query);
            return QueryHandler is null
                ? Task.FromResult(new AuditRunPage(Array.Empty<AuditRunSummary>(), 0))
                : Task.FromResult(QueryHandler(query));
        }

        public Task<AuditExportReport> ExportAsync(
            string outputPath,
            AuditExportFormat format,
            CancellationToken cancellationToken = default)
        {
            Exports.Add((outputPath, format));
            return Task.FromResult(
                ExportHandler?.Invoke(outputPath, format)
                ?? new AuditExportReport(outputPath, format, 0, 0));
        }
    }

    /// <summary>导出文件选择器假对象：返回预设路径并记录建议文件名。</summary>
    private sealed class FakeExportPathPicker : IExportPathPicker
    {
        internal string? Result { get; set; }

        internal string? SuggestedFileName { get; private set; }

        public Task<string?> PickSaveFileAsync(
            string title,
            string suggestedFileName,
            string extension,
            CancellationToken cancellationToken = default)
        {
            SuggestedFileName = suggestedFileName;
            return Task.FromResult(Result);
        }
    }
}
