using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using GitGuard.Application.Audit;
using GitGuard.Core.Model;
using GitGuard.Desktop.Localization;
using GitGuard.Desktop.Services;

namespace GitGuard.Desktop.ViewModels;

/// <summary>
/// 跨仓库日志 ViewModel（GGD-0046 / <c>11-前端UI设计</c> §2）：分页浏览整个本地审计库，
/// 支持结果与时间范围筛选，并通过 <see cref="IAuditOperations"/> 导出与 CLI 契约一致的 JSON / CSV。
/// 查询经 <see cref="IAuditOperations"/> 在后台线程执行，请求版本号用于丢弃过期响应。
/// </summary>
public sealed class LogsViewModel : SectionViewModel
{
    /// <summary>每页审计运行数量（与 <see cref="AuditQuery.DEFAULT_LIMIT"/> 一致）。</summary>
    public const int PAGE_SIZE = AuditQuery.DEFAULT_LIMIT;

    private readonly IAuditOperations _operations;
    private readonly IExportPathPicker? _exportPathPicker;
    private readonly Func<DateTimeOffset> _utcNow;

    private DesktopLanguage _language;
    private AuditResultFilterOption _selectedResultFilter;
    private AuditRangeFilterOption _selectedRangeFilter;
    private int _currentPage = 1;
    private int _totalCount;
    private int _requestVersion;
    private bool _hasLoaded;
    private bool _isLoading;
    private bool _isExporting;
    private string? _message;
    private bool _isMessageError;

    /// <summary>初始化跨仓库日志 ViewModel。</summary>
    /// <param name="operations">审计数据访问服务。</param>
    /// <param name="exportPathPicker">导出文件选择器；<see langword="null"/> 时禁用导出（单测 / 预览）。</param>
    /// <param name="language">初始界面语言。</param>
    /// <param name="utcNow">当前 UTC 时间提供器（时间范围筛选与导出文件名；单测可注入固定时间）。</param>
    /// <exception cref="ArgumentNullException"><paramref name="operations"/> 为 <see langword="null"/>。</exception>
    public LogsViewModel(
        IAuditOperations operations,
        IExportPathPicker? exportPathPicker,
        DesktopLanguage language,
        Func<DateTimeOffset>? utcNow = null)
        : base(DesktopSection.Logs, language)
    {
        ArgumentNullException.ThrowIfNull(operations);

        _operations = operations;
        _exportPathPicker = exportPathPicker;
        _language = language;
        _utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);

        ResultFilters = new[]
        {
            new AuditResultFilterOption(null, language),
            new AuditResultFilterOption(ScanResult.Clean, language),
            new AuditResultFilterOption(ScanResult.Warned, language),
            new AuditResultFilterOption(ScanResult.Blocked, language),
            new AuditResultFilterOption(ScanResult.Failed, language),
        };
        RangeFilters = new[]
        {
            new AuditRangeFilterOption(null, language),
            new AuditRangeFilterOption(7, language),
            new AuditRangeFilterOption(30, language),
            new AuditRangeFilterOption(180, language),
        };
        _selectedResultFilter = ResultFilters[0];
        _selectedRangeFilter = RangeFilters[0];

        Items = new ObservableCollection<AuditLogItemViewModel>();
        RefreshCommand = new AsyncRelayCommand(RefreshAsync);
        PreviousPageCommand = new AsyncRelayCommand(PreviousPageAsync);
        NextPageCommand = new AsyncRelayCommand(NextPageAsync);
        ExportJsonCommand = new AsyncRelayCommand(() => ExportAsync(AuditExportFormat.Json));
        ExportCsvCommand = new AsyncRelayCommand(() => ExportAsync(AuditExportFormat.Csv));
    }

    /// <summary>当前页审计记录。</summary>
    public ObservableCollection<AuditLogItemViewModel> Items { get; }

    /// <summary>结果筛选选项（全部 / 干净 / 警告 / 阻断 / 失败）。</summary>
    public IReadOnlyList<AuditResultFilterOption> ResultFilters { get; }

    /// <summary>时间范围筛选选项（全部 / 最近 7 / 30 / 180 天）。</summary>
    public IReadOnlyList<AuditRangeFilterOption> RangeFilters { get; }

    /// <summary>刷新当前页命令。</summary>
    public IAsyncRelayCommand RefreshCommand { get; }

    /// <summary>上一页命令。</summary>
    public IAsyncRelayCommand PreviousPageCommand { get; }

    /// <summary>下一页命令。</summary>
    public IAsyncRelayCommand NextPageCommand { get; }

    /// <summary>导出 JSON 命令。</summary>
    public IAsyncRelayCommand ExportJsonCommand { get; }

    /// <summary>导出 CSV 命令。</summary>
    public IAsyncRelayCommand ExportCsvCommand { get; }

    /// <summary>当前结果筛选。</summary>
    public AuditResultFilterOption SelectedResultFilter
    {
        get => _selectedResultFilter;
        set
        {
            if (value is null || !SetProperty(ref _selectedResultFilter, value) || !_hasLoaded)
            {
                return;
            }

            _currentPage = 1;
            _ = LoadPageAsync(1);
        }
    }

    /// <summary>当前时间范围筛选。</summary>
    public AuditRangeFilterOption SelectedRangeFilter
    {
        get => _selectedRangeFilter;
        set
        {
            if (value is null || !SetProperty(ref _selectedRangeFilter, value) || !_hasLoaded)
            {
                return;
            }

            _currentPage = 1;
            _ = LoadPageAsync(1);
        }
    }

    /// <summary>当前页码（1-based）。</summary>
    public int CurrentPage => _currentPage;

    /// <summary>筛选条件下的审计记录总数。</summary>
    public int TotalCount => _totalCount;

    /// <summary>总页数（无记录时为 1，便于分页文案显示）。</summary>
    public int TotalPages => Math.Max(1, (_totalCount + PAGE_SIZE - 1) / PAGE_SIZE);

    /// <summary>是否正在加载当前页。</summary>
    public bool IsLoading
    {
        get => _isLoading;
        private set
        {
            if (SetProperty(ref _isLoading, value))
            {
                NotifyInteractionState();
            }
        }
    }

    /// <summary>是否正在导出。</summary>
    public bool IsExporting
    {
        get => _isExporting;
        private set
        {
            if (SetProperty(ref _isExporting, value))
            {
                NotifyInteractionState();
            }
        }
    }

    /// <summary>页面级消息（查询 / 导出结果）。</summary>
    public string? Message
    {
        get => _message;
        private set
        {
            if (SetProperty(ref _message, value))
            {
                OnPropertyChanged(nameof(HasMessage));
                OnPropertyChanged(nameof(IsEmpty));
            }
        }
    }

    /// <summary>当前消息是否为错误。</summary>
    public bool IsMessageError
    {
        get => _isMessageError;
        private set => SetProperty(ref _isMessageError, value);
    }

    /// <summary>是否存在页面级消息。</summary>
    public bool HasMessage => !string.IsNullOrWhiteSpace(Message);

    /// <summary>当前页是否有记录。</summary>
    public bool HasItems => Items.Count > 0;

    /// <summary>是否展示无审计记录空状态。</summary>
    public bool IsEmpty => _hasLoaded && !IsLoading && !HasItems && !HasMessage;

    /// <summary>页面是否可交互。</summary>
    public bool CanInteract => !IsLoading && !IsExporting;

    /// <summary>导出按钮是否可用。</summary>
    public bool CanExport => _exportPathPicker is not null && !IsLoading && !IsExporting && _totalCount > 0;

    /// <summary>上一页是否可用。</summary>
    public bool CanGoPrevious => !IsLoading && _currentPage > 1;

    /// <summary>下一页是否可用。</summary>
    public bool CanGoNext => !IsLoading && _currentPage < TotalPages;

    /// <summary>加载提示。</summary>
    public string LoadingText => DesktopText.AuditLogLoading(_language);

    /// <summary>空状态文案。</summary>
    public string EmptyText => DesktopText.AuditLogEmpty(_language);

    /// <summary>刷新按钮文案。</summary>
    public string RefreshButtonText => DesktopText.AuditLogRefreshButton(_language);

    /// <summary>上一页按钮文案。</summary>
    public string PreviousPageText => DesktopText.ScanHistoryPreviousPage(_language);

    /// <summary>下一页按钮文案。</summary>
    public string NextPageText => DesktopText.ScanHistoryNextPage(_language);

    /// <summary>分页状态文案。</summary>
    public string PageText => DesktopText.AuditLogPage(_currentPage, TotalPages, _totalCount, _language);

    /// <summary>总数摘要文案。</summary>
    public string SummaryText => DesktopText.AuditLogSummary(_totalCount, _language);

    /// <summary>导出提示（说明导出全部记录且与 CLI 一致）。</summary>
    public string ExportHintText => DesktopText.AuditLogExportHint(_language);

    /// <summary>导出 JSON 按钮文案。</summary>
    public string ExportJsonText => DesktopText.AuditLogExportJson(_language);

    /// <summary>导出 CSV 按钮文案。</summary>
    public string ExportCsvText => DesktopText.AuditLogExportCsv(_language);

    /// <summary>结果筛选标签。</summary>
    public string ResultFilterLabel => DesktopText.AuditLogResultFilterLabelText(_language);

    /// <summary>时间范围筛选标签。</summary>
    public string RangeFilterLabel => DesktopText.AuditLogRangeFilterLabelText(_language);

    /// <summary>时间列标题。</summary>
    public string ColumnTimeText => DesktopText.AuditLogColumnTime(_language);

    /// <summary>仓库列标题。</summary>
    public string ColumnRepositoryText => DesktopText.AuditLogColumnRepository(_language);

    /// <summary>结果列标题。</summary>
    public string ColumnResultText => DesktopText.AuditLogColumnResult(_language);

    /// <summary>明细列标题。</summary>
    public string ColumnDetailText => DesktopText.AuditLogColumnDetail(_language);

    /// <summary>首次进入日志页时加载第 1 页（只执行一次）。</summary>
    /// <param name="cancellationToken">取消标记。</param>
    /// <returns>加载完成的任务。</returns>
    public async Task EnsureLoadedAsync(CancellationToken cancellationToken = default)
    {
        if (_hasLoaded || IsLoading)
        {
            return;
        }

        await LoadPageAsync(1, cancellationToken).ConfigureAwait(true);
    }

    /// <inheritdoc />
    public override void Relocalize(DesktopLanguage language)
    {
        base.Relocalize(language);
        _language = language;

        foreach (AuditResultFilterOption option in ResultFilters)
        {
            option.Relocalize(language);
        }

        foreach (AuditRangeFilterOption option in RangeFilters)
        {
            option.Relocalize(language);
        }

        foreach (AuditLogItemViewModel item in Items)
        {
            item.Relocalize(language);
        }

        OnPropertyChanged(nameof(LoadingText));
        OnPropertyChanged(nameof(EmptyText));
        OnPropertyChanged(nameof(RefreshButtonText));
        OnPropertyChanged(nameof(PreviousPageText));
        OnPropertyChanged(nameof(NextPageText));
        OnPropertyChanged(nameof(PageText));
        OnPropertyChanged(nameof(SummaryText));
        OnPropertyChanged(nameof(ExportHintText));
        OnPropertyChanged(nameof(ExportJsonText));
        OnPropertyChanged(nameof(ExportCsvText));
        OnPropertyChanged(nameof(ResultFilterLabel));
        OnPropertyChanged(nameof(RangeFilterLabel));
        OnPropertyChanged(nameof(ColumnTimeText));
        OnPropertyChanged(nameof(ColumnRepositoryText));
        OnPropertyChanged(nameof(ColumnResultText));
        OnPropertyChanged(nameof(ColumnDetailText));
    }

    private async Task RefreshAsync() => await LoadPageAsync(_currentPage).ConfigureAwait(true);

    private async Task PreviousPageAsync()
    {
        if (!CanGoPrevious)
        {
            return;
        }

        await LoadPageAsync(_currentPage - 1).ConfigureAwait(true);
    }

    private async Task NextPageAsync()
    {
        if (!CanGoNext)
        {
            return;
        }

        await LoadPageAsync(_currentPage + 1).ConfigureAwait(true);
    }

    private async Task LoadPageAsync(int page, CancellationToken cancellationToken = default)
    {
        int version = ++_requestVersion;
        IsLoading = true;
        Message = null;
        IsMessageError = false;

        try
        {
            DateTimeOffset? since = SelectedRangeFilter.Days is { } days
                ? _utcNow().ToUniversalTime().AddDays(-days)
                : null;
            var query = new AuditQuery(
                PAGE_SIZE,
                (page - 1) * PAGE_SIZE,
                since,
                SelectedResultFilter.Result);
            AuditRunPage result = await _operations.QueryAsync(query, cancellationToken).ConfigureAwait(true);
            if (version != _requestVersion)
            {
                return;
            }

            _currentPage = page;
            _totalCount = result.TotalCount;
            Items.Clear();
            foreach (AuditRunSummary summary in result.Runs)
            {
                Items.Add(new AuditLogItemViewModel(summary, _language));
            }

            _hasLoaded = true;

            // 数据被清理后当前页可能越界：回退到最后一个有效页
            if (Items.Count == 0 && _totalCount > 0 && page > TotalPages)
            {
                IsLoading = false;
                await LoadPageAsync(TotalPages, cancellationToken).ConfigureAwait(true);
                return;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // 页面离开或调用方取消：保持现有列表
        }
        catch (Exception ex)
        {
            if (version == _requestVersion)
            {
                Items.Clear();
                _totalCount = 0;
                _hasLoaded = true;
                IsMessageError = true;
                Message = DesktopText.AuditLogLoadFailed(_language, ex.Message);
            }
        }
        finally
        {
            if (version == _requestVersion)
            {
                IsLoading = false;
                NotifyPageState();
            }
        }
    }

    private async Task ExportAsync(AuditExportFormat format)
    {
        if (_exportPathPicker is null || IsLoading || IsExporting)
        {
            return;
        }

        string extension = format == AuditExportFormat.Json ? "json" : "csv";
        string suggestedName = $"gitguard-audit-{_utcNow().ToLocalTime():yyyyMMdd-HHmmss}.{extension}";

        IsExporting = true;
        Message = null;
        IsMessageError = false;
        try
        {
            string? path = await _exportPathPicker
                .PickSaveFileAsync(
                    DesktopText.AuditLogExportPickerTitle(format, _language),
                    suggestedName,
                    extension)
                .ConfigureAwait(true);
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            AuditExportReport report = await _operations.ExportAsync(path, format).ConfigureAwait(true);
            Message = DesktopText.AuditLogExportSucceeded(report, _language);
        }
        catch (Exception ex)
        {
            IsMessageError = true;
            Message = DesktopText.AuditLogExportFailed(_language, ex.Message);
        }
        finally
        {
            IsExporting = false;
        }
    }

    private void NotifyInteractionState()
    {
        OnPropertyChanged(nameof(CanInteract));
        OnPropertyChanged(nameof(CanExport));
        OnPropertyChanged(nameof(CanGoPrevious));
        OnPropertyChanged(nameof(CanGoNext));
    }

    private void NotifyPageState()
    {
        OnPropertyChanged(nameof(CurrentPage));
        OnPropertyChanged(nameof(TotalCount));
        OnPropertyChanged(nameof(TotalPages));
        OnPropertyChanged(nameof(PageText));
        OnPropertyChanged(nameof(SummaryText));
        OnPropertyChanged(nameof(HasItems));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(CanExport));
        OnPropertyChanged(nameof(CanGoPrevious));
        OnPropertyChanged(nameof(CanGoNext));
    }
}
