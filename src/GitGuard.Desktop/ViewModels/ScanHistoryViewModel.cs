using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GitGuard.Core.Model;
using GitGuard.Desktop.Localization;
using GitGuard.Desktop.Services;

namespace GitGuard.Desktop.ViewModels;

/// <summary>
/// 扫描历史 ViewModel（GGD-0043 / <c>11-前端UI设计</c> §2-§3）：按仓库分页加载 ScanRun 时间线，
/// 选中运行后加载脱敏 Finding 明细，并通过 <see cref="IEditorLauncher"/> 打开文件与行号。
/// 所有 SQLite 访问经 <see cref="IRepositoryOperations"/> 在后台线程执行；请求版本号用于丢弃过期响应。
/// </summary>
public sealed class ScanHistoryViewModel : ObservableObject
{
    /// <summary>每页扫描运行数量（与 <see cref="AuditQuery.DEFAULT_LIMIT"/> 一致）。</summary>
    public const int PAGE_SIZE = AuditQuery.DEFAULT_LIMIT;

    private readonly IRepositoryOperations _operations;
    private readonly IEditorLauncher _editorLauncher;
    private readonly Action _closeRequested;

    private DesktopLanguage _language;
    private string _repositoryName = string.Empty;
    private string _repositoryPath = string.Empty;
    private int _currentPage = 1;
    private int _totalCount;
    private int _pageRequestVersion;
    private int _runRequestVersion;
    private bool _isActive;
    private bool _isLoadingPage;
    private bool _isLoadingRun;
    private bool _hasLoadedPage;
    private string? _message;
    private ScanRunItemViewModel? _selectedRun;
    private FindingItemViewModel? _selectedFinding;

    /// <summary>初始化扫描历史 ViewModel。</summary>
    /// <param name="operations">仓库操作服务。</param>
    /// <param name="editorLauncher">外部编辑器启动器。</param>
    /// <param name="closeRequested">返回仓库详情时回调（由仓库页控制可见性）。</param>
    /// <param name="language">初始界面语言。</param>
    /// <exception cref="ArgumentNullException">任一依赖为 <see langword="null"/>。</exception>
    public ScanHistoryViewModel(
        IRepositoryOperations operations,
        IEditorLauncher editorLauncher,
        Action closeRequested,
        DesktopLanguage language)
    {
        ArgumentNullException.ThrowIfNull(operations);
        ArgumentNullException.ThrowIfNull(editorLauncher);
        ArgumentNullException.ThrowIfNull(closeRequested);

        _operations = operations;
        _editorLauncher = editorLauncher;
        _closeRequested = closeRequested;
        _language = language;

        Runs = new ObservableCollection<ScanRunItemViewModel>();
        Findings = new ObservableCollection<FindingItemViewModel>();
        BackCommand = new RelayCommand(_closeRequested);
        RefreshCommand = new AsyncRelayCommand(RefreshAsync);
        PreviousPageCommand = new AsyncRelayCommand(PreviousPageAsync);
        NextPageCommand = new AsyncRelayCommand(NextPageAsync);
        OpenInEditorCommand = new AsyncRelayCommand(OpenInEditorAsync);
    }

    /// <summary>当前页扫描时间线。</summary>
    public ObservableCollection<ScanRunItemViewModel> Runs { get; }

    /// <summary>当前选中运行的发现项（只含脱敏片段与指纹）。</summary>
    public ObservableCollection<FindingItemViewModel> Findings { get; }

    /// <summary>返回仓库详情命令。</summary>
    public IRelayCommand BackCommand { get; }

    /// <summary>刷新当前页命令。</summary>
    public IAsyncRelayCommand RefreshCommand { get; }

    /// <summary>上一页命令。</summary>
    public IAsyncRelayCommand PreviousPageCommand { get; }

    /// <summary>下一页命令。</summary>
    public IAsyncRelayCommand NextPageCommand { get; }

    /// <summary>在外部编辑器中打开当前发现项命令。</summary>
    public IAsyncRelayCommand OpenInEditorCommand { get; }

    /// <summary>仓库显示名。</summary>
    public string RepositoryName
    {
        get => _repositoryName;
        private set => SetProperty(ref _repositoryName, value);
    }

    /// <summary>仓库工作区绝对路径。</summary>
    public string RepositoryPath
    {
        get => _repositoryPath;
        private set => SetProperty(ref _repositoryPath, value);
    }

    /// <summary>当前页码（1-based）。</summary>
    public int CurrentPage => _currentPage;

    /// <summary>扫描运行总数（分页计数）。</summary>
    public int TotalCount => _totalCount;

    /// <summary>总页数（无记录时为 1，便于分页文案显示）。</summary>
    public int TotalPages => Math.Max(1, (_totalCount + PAGE_SIZE - 1) / PAGE_SIZE);

    /// <summary>当前选中的扫描运行；切换时异步加载 Finding 明细。</summary>
    public ScanRunItemViewModel? SelectedRun
    {
        get => _selectedRun;
        set
        {
            if (!SetProperty(ref _selectedRun, value))
            {
                return;
            }

            OnPropertyChanged(nameof(HasSelectedRun));
            SelectedFinding = null;
            _ = LoadRunAsync(value);
        }
    }

    /// <summary>当前选中的发现项。</summary>
    public FindingItemViewModel? SelectedFinding
    {
        get => _selectedFinding;
        set
        {
            if (SetProperty(ref _selectedFinding, value))
            {
                OnPropertyChanged(nameof(HasSelectedFinding));
                OnPropertyChanged(nameof(CanOpenInEditor));
            }
        }
    }

    /// <summary>时间线是否正在加载。</summary>
    public bool IsLoadingPage
    {
        get => _isLoadingPage;
        private set
        {
            if (SetProperty(ref _isLoadingPage, value))
            {
                NotifyPageState();
            }
        }
    }

    /// <summary>当前运行的 Finding 是否正在加载。</summary>
    public bool IsLoadingRun
    {
        get => _isLoadingRun;
        private set
        {
            if (SetProperty(ref _isLoadingRun, value))
            {
                OnPropertyChanged(nameof(CanOpenInEditor));
                OnPropertyChanged(nameof(IsNoFindingsVisible));
                NotifyNavigationState();
            }
        }
    }

    /// <summary>页面级消息（查询失败 / 运行不存在 / 编辑器启动失败）。</summary>
    public string? Message
    {
        get => _message;
        private set
        {
            if (SetProperty(ref _message, value))
            {
                OnPropertyChanged(nameof(HasMessage));
                OnPropertyChanged(nameof(IsEmpty));
                OnPropertyChanged(nameof(IsNoFindingsVisible));
                OnPropertyChanged(nameof(IsSelectRunPromptVisible));
            }
        }
    }

    /// <summary>是否存在页面级消息。</summary>
    public bool HasMessage => !string.IsNullOrWhiteSpace(Message);

    /// <summary>时间线是否有记录。</summary>
    public bool HasRuns => Runs.Count > 0;

    /// <summary>是否已选中扫描运行。</summary>
    public bool HasSelectedRun => SelectedRun is not null;

    /// <summary>当前运行是否有发现项。</summary>
    public bool HasFindings => Findings.Count > 0;

    /// <summary>是否已选中发现项。</summary>
    public bool HasSelectedFinding => SelectedFinding is not null;

    /// <summary>是否展示无扫描历史空状态。</summary>
    public bool IsEmpty => _hasLoadedPage && !IsLoadingPage && !HasRuns && !HasMessage;

    /// <summary>是否展示「本次扫描无发现项」。</summary>
    public bool IsNoFindingsVisible => HasSelectedRun && !IsLoadingRun && !HasFindings && !HasMessage;

    /// <summary>是否展示「请选择发现项」。</summary>
    public bool IsSelectFindingPromptVisible =>
        HasSelectedRun && HasFindings && !HasSelectedFinding && !IsLoadingRun;

    /// <summary>是否展示「请选择扫描运行」。</summary>
    public bool IsSelectRunPromptVisible =>
        _hasLoadedPage && !HasSelectedRun && !IsLoadingPage && !IsLoadingRun && !HasMessage;

    /// <summary>编辑器打开按钮是否可用。</summary>
    public bool CanOpenInEditor => HasSelectedFinding && !IsLoadingRun;

    /// <summary>上一页是否可用。</summary>
    public bool CanGoPrevious => CurrentPage > 1 && !IsLoadingPage && !IsLoadingRun;

    /// <summary>下一页是否可用。</summary>
    public bool CanGoNext => CurrentPage < TotalPages && !IsLoadingPage && !IsLoadingRun;

    /// <summary>时线分页文案。</summary>
    public string PageText => DesktopText.ScanHistoryPage(CurrentPage, TotalPages, TotalCount, _language);

    /// <summary>扫描历史标题。</summary>
    public string HistoryTitle => DesktopText.ScanHistoryTitle(_language);

    /// <summary>返回按钮文案。</summary>
    public string BackButtonText => DesktopText.ScanHistoryBackButton(_language);

    /// <summary>刷新按钮文案。</summary>
    public string RefreshButtonText => DesktopText.ScanHistoryRefreshButton(_language);

    /// <summary>时间线标题。</summary>
    public string TimelineTitle => DesktopText.ScanHistoryTimelineTitle(_language);

    /// <summary>发现项列表标题。</summary>
    public string FindingsTitle => DesktopText.ScanHistoryFindingsTitle(_language);

    /// <summary>发现项详情标题。</summary>
    public string FindingDetailTitle => DesktopText.ScanHistoryFindingDetailTitle(_language);

    /// <summary>无扫描历史提示。</summary>
    public string EmptyText => DesktopText.ScanHistoryEmpty(_language);

    /// <summary>选择扫描运行提示。</summary>
    public string SelectRunPromptText => DesktopText.ScanHistorySelectRunPrompt(_language);

    /// <summary>本次扫描无发现项提示。</summary>
    public string NoFindingsText => DesktopText.ScanHistoryNoFindings(_language);

    /// <summary>选择发现项提示。</summary>
    public string SelectFindingPromptText => DesktopText.ScanHistorySelectFindingPrompt(_language);

    /// <summary>页面加载提示。</summary>
    public string LoadingText => DesktopText.ScanHistoryLoading(_language);

    /// <summary>发现项加载提示。</summary>
    public string RunLoadingText => DesktopText.ScanHistoryRunLoading(_language);

    /// <summary>上一页按钮文案。</summary>
    public string PreviousPageText => DesktopText.ScanHistoryPreviousPage(_language);

    /// <summary>下一页按钮文案。</summary>
    public string NextPageText => DesktopText.ScanHistoryNextPage(_language);

    /// <summary>打开编辑器按钮文案。</summary>
    public string OpenInEditorText => DesktopText.ScanHistoryOpenInEditor(_language);

    /// <summary>规则字段标签。</summary>
    public string RuleLabel => DesktopText.ScanHistoryRuleLabel(_language);

    /// <summary>严重度字段标签。</summary>
    public string SeverityLabel => DesktopText.ScanHistorySeverityLabel(_language);

    /// <summary>位置字段标签。</summary>
    public string LocationLabel => DesktopText.ScanHistoryLocationLabel(_language);

    /// <summary>脱敏片段字段标签。</summary>
    public string SnippetLabel => DesktopText.ScanHistorySnippetLabel(_language);

    /// <summary>指纹字段标签。</summary>
    public string FingerprintLabel => DesktopText.ScanHistoryFingerprintLabel(_language);

    /// <summary>提交字段标签。</summary>
    public string CommitLabel => DesktopText.ScanHistoryCommitLabel(_language);

    /// <summary>打开指定仓库的扫描历史首页；重复调用会重置分页与筛选状态。</summary>
    /// <param name="repositoryName">仓库显示名。</param>
    /// <param name="repositoryPath">仓库工作区绝对路径。</param>
    /// <param name="cancellationToken">取消标记。</param>
    /// <returns>首页及首个运行明细加载完成时结束。</returns>
    public async Task OpenAsync(
        string repositoryName,
        string repositoryPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryName);
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryPath);

        RepositoryName = repositoryName;
        RepositoryPath = repositoryPath;
        _isActive = true;
        _currentPage = 1;
        _totalCount = 0;
        _hasLoadedPage = false;
        _pageRequestVersion++;
        _runRequestVersion++;
        Runs.Clear();
        Findings.Clear();
        ApplySelectedRunWithoutLoading(null);
        SelectedFinding = null;
        Message = null;
        NotifyPageState();

        await LoadPageAsync(1, cancellationToken).ConfigureAwait(true);
    }

    /// <summary>关闭时失效所有进行中的请求，并清空页面状态。</summary>
    public void Deactivate()
    {
        _isActive = false;
        _pageRequestVersion++;
        _runRequestVersion++;
        _currentPage = 1;
        _totalCount = 0;
        _hasLoadedPage = false;
        Runs.Clear();
        Findings.Clear();
        ApplySelectedRunWithoutLoading(null);
        SelectedFinding = null;
        Message = null;
        IsLoadingPage = false;
        IsLoadingRun = false;
        RepositoryName = string.Empty;
        RepositoryPath = string.Empty;
        NotifyPageState();
    }

    /// <summary>按新界面语言刷新全部派生文案。</summary>
    /// <param name="language">目标界面语言。</param>
    public void Relocalize(DesktopLanguage language)
    {
        _language = language;
        foreach (ScanRunItemViewModel run in Runs)
        {
            run.Relocalize(language);
        }

        foreach (FindingItemViewModel finding in Findings)
        {
            finding.Relocalize(language);
        }

        NotifyLocalizedLabels();
    }

    private async Task RefreshAsync()
    {
        if (!_isActive || IsLoadingPage || IsLoadingRun)
        {
            return;
        }

        await LoadPageAsync(CurrentPage, CancellationToken.None).ConfigureAwait(true);
    }

    private async Task PreviousPageAsync()
    {
        if (!_isActive || !CanGoPrevious)
        {
            return;
        }

        await LoadPageAsync(CurrentPage - 1, CancellationToken.None).ConfigureAwait(true);
    }

    private async Task NextPageAsync()
    {
        if (!_isActive || !CanGoNext)
        {
            return;
        }

        await LoadPageAsync(CurrentPage + 1, CancellationToken.None).ConfigureAwait(true);
    }

    private async Task OpenInEditorAsync()
    {
        FindingItemViewModel? finding = SelectedFinding;
        if (!_isActive || finding is null)
        {
            return;
        }

        try
        {
            EditorLaunchResult result = await _editorLauncher
                .OpenAsync(RepositoryPath, finding.FilePath, finding.LineNumber)
                .ConfigureAwait(true);
            if (!_isActive || !ReferenceEquals(SelectedFinding, finding))
            {
                return;
            }

            Message = result.Succeeded
                ? null
                : DesktopText.ScanHistoryEditorError(result.Status, _language);
        }
        catch (Exception)
        {
            if (_isActive && ReferenceEquals(SelectedFinding, finding))
            {
                Message = DesktopText.ScanHistoryEditorError(EditorLaunchStatus.LaunchFailed, _language);
            }
        }
    }

    private async Task LoadPageAsync(int page, CancellationToken cancellationToken)
    {
        if (!_isActive)
        {
            return;
        }

        int requestVersion = ++_pageRequestVersion;
        _runRequestVersion++;
        IsLoadingPage = true;
        Message = null;

        try
        {
            AuditRunPage result = await _operations
                .QueryScanHistoryAsync(
                    RepositoryPath,
                    PAGE_SIZE,
                    (page - 1) * PAGE_SIZE,
                    cancellationToken)
                .ConfigureAwait(true);
            if (requestVersion != _pageRequestVersion || !_isActive)
            {
                return;
            }

            _currentPage = page;
            _totalCount = result.TotalCount;
            _hasLoadedPage = true;

            Runs.Clear();
            foreach (AuditRunSummary summary in result.Runs)
            {
                Runs.Add(new ScanRunItemViewModel(summary, _language));
            }

            Findings.Clear();
            SelectedFinding = null;
            ScanRunItemViewModel? firstRun = Runs.FirstOrDefault();
            ApplySelectedRunWithoutLoading(firstRun);
            NotifyPageState();
            await LoadRunAsync(firstRun).ConfigureAwait(true);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            if (requestVersion == _pageRequestVersion && _isActive)
            {
                Message = ex.Message;
            }
        }
        finally
        {
            if (requestVersion == _pageRequestVersion)
            {
                IsLoadingPage = false;
                NotifyPageState();
            }
        }
    }

    private async Task LoadRunAsync(ScanRunItemViewModel? run)
    {
        int requestVersion = ++_runRequestVersion;
        Findings.Clear();
        SelectedFinding = null;
        NotifyFindingsState();
        if (!_isActive)
        {
            return;
        }

        if (run is null)
        {
            IsLoadingRun = false;
            return;
        }

        IsLoadingRun = true;
        Message = null;

        try
        {
            ScanRun? scanRun = await _operations.LoadScanRunAsync(run.RunId).ConfigureAwait(true);
            if (requestVersion != _runRequestVersion || !_isActive)
            {
                return;
            }

            if (scanRun is null)
            {
                Message = DesktopText.ScanHistoryRunNotFound(_language);
                return;
            }

            foreach (Finding finding in scanRun.Findings)
            {
                Findings.Add(new FindingItemViewModel(finding, _language));
            }

            SelectedFinding = Findings.FirstOrDefault();
            NotifyFindingsState();
        }
        catch (Exception ex)
        {
            if (requestVersion == _runRequestVersion && _isActive)
            {
                Message = ex.Message;
            }
        }
        finally
        {
            if (requestVersion == _runRequestVersion)
            {
                IsLoadingRun = false;
                NotifyFindingsState();
            }
        }
    }

    private void ApplySelectedRunWithoutLoading(ScanRunItemViewModel? run)
    {
        _selectedRun = run;
        OnPropertyChanged(nameof(SelectedRun));
        OnPropertyChanged(nameof(HasSelectedRun));
        OnPropertyChanged(nameof(IsNoFindingsVisible));
        OnPropertyChanged(nameof(IsSelectRunPromptVisible));
    }

    private void NotifyPageState()
    {
        OnPropertyChanged(nameof(CurrentPage));
        OnPropertyChanged(nameof(TotalCount));
        OnPropertyChanged(nameof(TotalPages));
        OnPropertyChanged(nameof(PageText));
        OnPropertyChanged(nameof(HasRuns));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(IsNoFindingsVisible));
        OnPropertyChanged(nameof(IsSelectRunPromptVisible));
        NotifyNavigationState();
    }

    private void NotifyNavigationState()
    {
        OnPropertyChanged(nameof(CanGoPrevious));
        OnPropertyChanged(nameof(CanGoNext));
    }

    private void NotifyFindingsState()
    {
        OnPropertyChanged(nameof(HasFindings));
        OnPropertyChanged(nameof(HasSelectedFinding));
        OnPropertyChanged(nameof(IsNoFindingsVisible));
        OnPropertyChanged(nameof(IsSelectRunPromptVisible));
        OnPropertyChanged(nameof(IsSelectFindingPromptVisible));
        OnPropertyChanged(nameof(CanOpenInEditor));
    }

    private void NotifyLocalizedLabels()
    {
        OnPropertyChanged(nameof(PageText));
        OnPropertyChanged(nameof(HistoryTitle));
        OnPropertyChanged(nameof(BackButtonText));
        OnPropertyChanged(nameof(RefreshButtonText));
        OnPropertyChanged(nameof(TimelineTitle));
        OnPropertyChanged(nameof(FindingsTitle));
        OnPropertyChanged(nameof(FindingDetailTitle));
        OnPropertyChanged(nameof(EmptyText));
        OnPropertyChanged(nameof(SelectRunPromptText));
        OnPropertyChanged(nameof(NoFindingsText));
        OnPropertyChanged(nameof(SelectFindingPromptText));
        OnPropertyChanged(nameof(LoadingText));
        OnPropertyChanged(nameof(RunLoadingText));
        OnPropertyChanged(nameof(PreviousPageText));
        OnPropertyChanged(nameof(NextPageText));
        OnPropertyChanged(nameof(OpenInEditorText));
        OnPropertyChanged(nameof(RuleLabel));
        OnPropertyChanged(nameof(SeverityLabel));
        OnPropertyChanged(nameof(LocationLabel));
        OnPropertyChanged(nameof(SnippetLabel));
        OnPropertyChanged(nameof(FingerprintLabel));
        OnPropertyChanged(nameof(CommitLabel));
    }
}
