using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using GitGuard.Application.Hooks;
using GitGuard.Core;
using GitGuard.Core.Model;
using GitGuard.Desktop.Localization;
using GitGuard.Desktop.Services;

namespace GitGuard.Desktop.ViewModels;

/// <summary>
/// 仓库页 ViewModel（GGD-0042 / <c>11-前端UI设计</c> §2-§3）：登记 / 移除本地仓库、展示 Git 与 Hook 状态、
/// 触发 HEAD 全量扫描并刷新最近扫描摘要。所有数据访问经 <see cref="IRepositoryOperations"/> 在后台线程执行；
/// 失败以可展示消息呈现，不让未观察任务终止进程。
/// </summary>
public sealed class RepositoriesViewModel : SectionViewModel
{
    private readonly IRepositoryOperations _operations;
    private DesktopLanguage _language;
    private RepositoryItemViewModel? _selectedRepository;
    private string _newRepositoryPath = string.Empty;
    private bool _isBusy;
    private bool _isDetailLoading;
    private bool _isDetailVisible;
    private bool _isHistoryVisible;
    private bool _isScanning;
    private bool _isRemoveConfirmationVisible;
    private string? _listMessage;
    private string? _detailMessage;
    private bool _hasLatestScan;
    private bool _hasHookAttention;
    private string _gitGuardVersionText = string.Empty;
    private string _gitVersionText = string.Empty;
    private string _hooksDirectoryText = string.Empty;
    private string _latestScanResultText = string.Empty;
    private string _latestScanText = string.Empty;
    private RepositoryDetail? _currentDetail;
    private int _detailRequestVersion;
    private bool _initialized;

    /// <summary>初始化仓库页 ViewModel。</summary>
    /// <param name="operations">仓库操作服务（ViewModel 不直接访问 SQLite / Git）。</param>
    /// <param name="language">初始界面语言。</param>
    /// <param name="editorLauncher">
    /// 外部编辑器启动器；<see langword="null"/> 时使用默认的 <see cref="ShellEditorLauncher"/>
    /// （ViewModel 单测可注入假对象，避免真实启动进程）。
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="operations"/> 为 <see langword="null"/>。</exception>
    public RepositoriesViewModel(
        IRepositoryOperations operations,
        DesktopLanguage language,
        IEditorLauncher? editorLauncher = null)
        : base(DesktopSection.Repositories, language)
    {
        ArgumentNullException.ThrowIfNull(operations);

        _operations = operations;
        _language = language;
        Repositories = new ObservableCollection<RepositoryItemViewModel>();
        HookRows = new ObservableCollection<HookStatusRowViewModel>();
        History = new ScanHistoryViewModel(
            operations,
            editorLauncher ?? new ShellEditorLauncher(),
            CloseHistory,
            language);
        RefreshCommand = new AsyncRelayCommand(RefreshAsync);
        AddCommand = new AsyncRelayCommand(AddAsync);
        ScanCommand = new AsyncRelayCommand(ScanAsync);
        OpenHistoryCommand = new AsyncRelayCommand(OpenHistoryAsync);
        RequestRemoveCommand = new RelayCommand(RequestRemove);
        ConfirmRemoveCommand = new AsyncRelayCommand(ConfirmRemoveAsync);
        CancelRemoveCommand = new RelayCommand(CancelRemove);
    }

    /// <summary>已登记仓库列表（登记时间倒序）。</summary>
    public ObservableCollection<RepositoryItemViewModel> Repositories { get; }

    /// <summary>当前仓库的 Hook 状态行（pre-commit / pre-push / post-commit）。</summary>
    public ObservableCollection<HookStatusRowViewModel> HookRows { get; }

    /// <summary>当前仓库的扫描历史子页面（GGD-0043）。</summary>
    public ScanHistoryViewModel History { get; }

    /// <summary>刷新仓库列表命令。</summary>
    public IAsyncRelayCommand RefreshCommand { get; }

    /// <summary>登记仓库命令。</summary>
    public IAsyncRelayCommand AddCommand { get; }

    /// <summary>立即扫描命令。</summary>
    public IAsyncRelayCommand ScanCommand { get; }

    /// <summary>打开当前仓库的扫描历史命令。</summary>
    public IAsyncRelayCommand OpenHistoryCommand { get; }

    /// <summary>请求移除（显示二次确认）命令。</summary>
    public IRelayCommand RequestRemoveCommand { get; }

    /// <summary>确认移除命令。</summary>
    public IAsyncRelayCommand ConfirmRemoveCommand { get; }

    /// <summary>取消移除命令。</summary>
    public IRelayCommand CancelRemoveCommand { get; }

    /// <summary>仓库路径输入内容。</summary>
    public string NewRepositoryPath
    {
        get => _newRepositoryPath;
        set => SetProperty(ref _newRepositoryPath, value);
    }

    /// <summary>当前选中的仓库；切换时异步加载详情。</summary>
    public RepositoryItemViewModel? SelectedRepository
    {
        get => _selectedRepository;
        set
        {
            if (!SetProperty(ref _selectedRepository, value))
            {
                return;
            }

            CloseHistory();
            IsRemoveConfirmationVisible = false;
            _ = LoadSelectedDetailAsync(value);
        }
    }

    /// <summary>是否存在任何后台操作（列表刷新 / 登记 / 移除 / 扫描）。</summary>
    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                OnPropertyChanged(nameof(IsEmpty));
                OnPropertyChanged(nameof(CanInteract));
            }
        }
    }

    /// <summary>详情是否正在加载。</summary>
    public bool IsDetailLoading
    {
        get => _isDetailLoading;
        private set
        {
            if (SetProperty(ref _isDetailLoading, value))
            {
                OnPropertyChanged(nameof(CanInteract));
            }
        }
    }

    /// <summary>是否展示右侧详情面板。</summary>
    public bool IsDetailVisible
    {
        get => _isDetailVisible;
        private set
        {
            if (SetProperty(ref _isDetailVisible, value))
            {
                OnPropertyChanged(nameof(IsSelectPromptVisible));
                OnPropertyChanged(nameof(CanInteract));
            }
        }
    }

    /// <summary>是否展示扫描历史子页面（覆盖仓库详情）。</summary>
    public bool IsHistoryVisible
    {
        get => _isHistoryVisible;
        private set
        {
            if (SetProperty(ref _isHistoryVisible, value))
            {
                OnPropertyChanged(nameof(CanInteract));
            }
        }
    }

    /// <summary>是否正在扫描。</summary>
    public bool IsScanning
    {
        get => _isScanning;
        private set
        {
            if (SetProperty(ref _isScanning, value))
            {
                OnPropertyChanged(nameof(ScanButtonText));
            }
        }
    }

    /// <summary>是否展示移除二次确认。</summary>
    public bool IsRemoveConfirmationVisible
    {
        get => _isRemoveConfirmationVisible;
        private set => SetProperty(ref _isRemoveConfirmationVisible, value);
    }

    /// <summary>列表级消息（登记 / 刷新错误）。</summary>
    public string? ListMessage
    {
        get => _listMessage;
        private set
        {
            if (SetProperty(ref _listMessage, value))
            {
                OnPropertyChanged(nameof(HasListMessage));
            }
        }
    }

    /// <summary>详情级消息（加载 / 扫描 / 移除结果）。</summary>
    public string? DetailMessage
    {
        get => _detailMessage;
        private set
        {
            if (SetProperty(ref _detailMessage, value))
            {
                OnPropertyChanged(nameof(HasDetailMessage));
            }
        }
    }

    /// <summary>列表是否为空（不含加载中状态）。</summary>
    public bool IsEmpty => Repositories.Count == 0 && !IsBusy;

    /// <summary>是否展示「请选择仓库」占位。</summary>
    public bool IsSelectPromptVisible => !IsDetailVisible && Repositories.Count > 0;

    /// <summary>详情操作（扫描 / 移除 / 扫描历史）当前是否可用。</summary>
    public bool CanInteract => IsDetailVisible && !IsBusy && !IsDetailLoading && !IsHistoryVisible;

    /// <summary>列表级消息是否可见。</summary>
    public bool HasListMessage => !string.IsNullOrWhiteSpace(ListMessage);

    /// <summary>详情级消息是否可见。</summary>
    public bool HasDetailMessage => !string.IsNullOrWhiteSpace(DetailMessage);

    /// <summary>是否存在需要接管的 Hook。</summary>
    public bool HasHookAttention
    {
        get => _hasHookAttention;
        private set => SetProperty(ref _hasHookAttention, value);
    }

    /// <summary>是否存在最近扫描记录。</summary>
    public bool HasLatestScan
    {
        get => _hasLatestScan;
        private set => SetProperty(ref _hasLatestScan, value);
    }

    /// <summary>当前仓库的 GitGuard 版本。</summary>
    public string GitGuardVersionText
    {
        get => _gitGuardVersionText;
        private set => SetProperty(ref _gitGuardVersionText, value);
    }

    /// <summary>当前仓库的 Git 版本行。</summary>
    public string GitVersionText
    {
        get => _gitVersionText;
        private set => SetProperty(ref _gitVersionText, value);
    }

    /// <summary>实际检查的 Hooks 目录。</summary>
    public string HooksDirectoryText
    {
        get => _hooksDirectoryText;
        private set => SetProperty(ref _hooksDirectoryText, value);
    }

    /// <summary>最近扫描结果标签。</summary>
    public string LatestScanResultText
    {
        get => _latestScanResultText;
        private set => SetProperty(ref _latestScanResultText, value);
    }

    /// <summary>最近扫描摘要（时间 / 类型 / 发现数 / 耗时）。</summary>
    public string LatestScanText
    {
        get => _latestScanText;
        private set => SetProperty(ref _latestScanText, value);
    }

    /// <summary>列表标题。</summary>
    public string ListTitle => DesktopText.RepositoriesListTitle(_language);

    /// <summary>路径输入框标题。</summary>
    public string PathLabel => DesktopText.RepositoriesPathLabel(_language);

    /// <summary>路径输入框水印。</summary>
    public string PathWatermark => DesktopText.RepositoriesPathWatermark(_language);

    /// <summary>登记按钮文案。</summary>
    public string AddButtonText => DesktopText.RepositoriesAddButton(_language);

    /// <summary>刷新按钮文案。</summary>
    public string RefreshButtonText => DesktopText.RepositoriesRefreshButton(_language);

    /// <summary>列表空状态文案。</summary>
    public string EmptyStateText => DesktopText.RepositoriesEmptyState(_language);

    /// <summary>未选择仓库占位文案。</summary>
    public string SelectPromptText => DesktopText.RepositoriesSelectPrompt(_language);

    /// <summary>详情加载提示。</summary>
    public string LoadingText => DesktopText.RepositoryLoading(_language);

    /// <summary>Hook 区块标题。</summary>
    public string HooksTitle => DesktopText.RepositoryHooksTitle(_language);

    /// <summary>GitGuard 版本标签。</summary>
    public string GitGuardVersionLabel => DesktopText.RepositoryGitGuardVersionLabel(_language);

    /// <summary>Git 版本标签。</summary>
    public string GitVersionLabel => DesktopText.RepositoryGitVersionLabel(_language);

    /// <summary>Hooks 目录标签。</summary>
    public string HooksDirectoryLabel => DesktopText.RepositoryHooksDirectoryLabel(_language);

    /// <summary>最近扫描区块标题。</summary>
    public string LatestScanLabel => DesktopText.RepositoryLatestScanLabel(_language);

    /// <summary>无扫描记录文案。</summary>
    public string NoScanText => DesktopText.RepositoryNoScan(_language);

    /// <summary>扫描按钮文案（扫描中显示进行态）。</summary>
    public string ScanButtonText => IsScanning
        ? DesktopText.RepositoryScanningButton(_language)
        : DesktopText.RepositoryScanButton(_language);

    /// <summary>扫描历史按钮文案。</summary>
    public string ScanHistoryButtonText => DesktopText.ScanHistoryButton(_language);

    /// <summary>移除登记按钮文案。</summary>
    public string RemoveButtonText => DesktopText.RepositoryRemoveButton(_language);

    /// <summary>移除二次确认提示。</summary>
    public string RemoveConfirmPrompt => DesktopText.RepositoryRemoveConfirmPrompt(_language);

    /// <summary>确认移除按钮文案。</summary>
    public string ConfirmRemoveButtonText => DesktopText.RepositoryRemoveConfirmButton(_language);

    /// <summary>取消按钮文案。</summary>
    public string CancelRemoveButtonText => DesktopText.RepositoryCancelButton(_language);

    /// <summary>未托管 Hook 接管提示。</summary>
    public string HookAttentionText => DesktopText.HookAttentionHint(_language);

    /// <summary>页面首次可见时加载仓库列表（只执行一次）。</summary>
    /// <returns>加载任务；失败已转为 <see cref="ListMessage"/>，不会抛出。</returns>
    public Task EnsureLoadedAsync()
    {
        if (_initialized)
        {
            return Task.CompletedTask;
        }

        _initialized = true;
        return RefreshAsync();
    }

    /// <inheritdoc />
    public override void Relocalize(DesktopLanguage language)
    {
        _language = language;
        base.Relocalize(language);

        foreach (RepositoryItemViewModel repository in Repositories)
        {
            repository.Relocalize(language);
        }

        foreach (HookStatusRowViewModel hook in HookRows)
        {
            hook.Relocalize(language);
        }

        History.Relocalize(language);
        NotifyLocalizedLabels();
        if (_currentDetail is not null)
        {
            ApplyDetailTexts(_currentDetail);
        }
    }

    private async Task RefreshAsync()
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        ListMessage = null;
        try
        {
            await LoadRepositoriesAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            ListMessage = FormatError(ex);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task LoadRepositoriesAsync()
    {
        IReadOnlyList<RegisteredRepository> repositories = await _operations.ListAsync().ConfigureAwait(true);
        Guid? selectedId = SelectedRepository?.Id;

        Repositories.Clear();
        foreach (RegisteredRepository repository in repositories)
        {
            Repositories.Add(new RepositoryItemViewModel(repository, _language));
        }

        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(IsSelectPromptVisible));

        RepositoryItemViewModel? selected = selectedId is { } id
            ? Repositories.FirstOrDefault(item => item.Id == id)
            : null;
        SelectedRepository = selected ?? Repositories.FirstOrDefault();
        if (SelectedRepository is null)
        {
            IsDetailVisible = false;
        }
    }

    private async Task AddAsync()
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        ListMessage = null;
        try
        {
            RegisteredRepository repository = await _operations.RegisterAsync(NewRepositoryPath).ConfigureAwait(true);
            NewRepositoryPath = string.Empty;
            await LoadRepositoriesAsync().ConfigureAwait(true);
            SelectedRepository = Repositories.FirstOrDefault(item => item.Id == repository.Id) ?? SelectedRepository;
        }
        catch (Exception ex)
        {
            ListMessage = FormatError(ex);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ScanAsync()
    {
        RepositoryItemViewModel? item = SelectedRepository;
        if (item is null || IsBusy || IsDetailLoading)
        {
            return;
        }

        IsBusy = true;
        IsScanning = true;
        DetailMessage = null;
        try
        {
            RepositoryDetail detail = await _operations.ScanAsync(item.RepoPath).ConfigureAwait(true);
            if (!ReferenceEquals(SelectedRepository, item))
            {
                return;
            }

            ApplyDetail(detail);
            DetailMessage = detail.AuditWriteFailed
                ? DesktopText.AuditWriteFailedWarning(_language)
                : detail.LatestScan is { } latest
                    ? DesktopText.ScanCompletedMessage(latest.Result, latest.FindingCount, _language)
                    : null;
        }
        catch (Exception ex)
        {
            if (ReferenceEquals(SelectedRepository, item))
            {
                DetailMessage = FormatError(ex);
            }
        }
        finally
        {
            IsScanning = false;
            IsBusy = false;
        }
    }

    private async Task OpenHistoryAsync()
    {
        RepositoryItemViewModel? item = SelectedRepository;
        if (item is null || IsBusy || IsDetailLoading)
        {
            return;
        }

        IsHistoryVisible = true;
        await History.OpenAsync(item.DisplayName, item.RepoPath).ConfigureAwait(true);
    }

    /// <summary>关闭扫描历史并释放其进行中的请求（返回仓库详情）。</summary>
    private void CloseHistory()
    {
        if (!_isHistoryVisible)
        {
            return;
        }

        History.Deactivate();
        IsHistoryVisible = false;
    }

    private void RequestRemove()
    {
        if (SelectedRepository is null || IsBusy)
        {
            return;
        }

        DetailMessage = null;
        IsRemoveConfirmationVisible = true;
    }

    private void CancelRemove() => IsRemoveConfirmationVisible = false;

    private async Task ConfirmRemoveAsync()
    {
        RepositoryItemViewModel? item = SelectedRepository;
        if (item is null || IsBusy)
        {
            return;
        }

        IsBusy = true;
        ListMessage = null;
        try
        {
            await _operations.RemoveAsync(item.Id).ConfigureAwait(true);
            IsRemoveConfirmationVisible = false;
            await LoadRepositoriesAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            DetailMessage = FormatError(ex);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task LoadSelectedDetailAsync(RepositoryItemViewModel? item)
    {
        int requestVersion = ++_detailRequestVersion;
        ResetDetail();

        if (item is null)
        {
            IsDetailVisible = false;
            return;
        }

        IsDetailVisible = true;
        IsDetailLoading = true;
        try
        {
            RepositoryDetail detail = await _operations.LoadDetailAsync(item.RepoPath).ConfigureAwait(true);
            if (requestVersion != _detailRequestVersion)
            {
                return;
            }

            ApplyDetail(detail);
            DetailMessage = detail.AuditWriteFailed ? DesktopText.AuditWriteFailedWarning(_language) : null;
        }
        catch (Exception ex)
        {
            if (requestVersion == _detailRequestVersion)
            {
                DetailMessage = FormatError(ex);
            }
        }
        finally
        {
            if (requestVersion == _detailRequestVersion)
            {
                IsDetailLoading = false;
            }
        }
    }

    private void ApplyDetail(RepositoryDetail detail)
    {
        _currentDetail = detail;
        HookRows.Clear();
        foreach (HookStatus hook in detail.Status.Hooks)
        {
            HookRows.Add(new HookStatusRowViewModel(hook, _language));
        }

        HasHookAttention = detail.Status.NeedsAttention;
        GitGuardVersionText = detail.Status.GitGuardVersion;
        GitVersionText = detail.Status.GitVersion;
        HooksDirectoryText = detail.Status.HooksDirectory;
        ApplyDetailTexts(detail);
    }

    private void ApplyDetailTexts(RepositoryDetail detail)
    {
        HasLatestScan = detail.LatestScan is not null;
        if (detail.LatestScan is { } latest)
        {
            LatestScanResultText = DesktopText.ScanResultLabel(latest.Result, _language);
            LatestScanText = DesktopText.LatestScanSummary(latest, _language);
        }
        else
        {
            LatestScanResultText = string.Empty;
            LatestScanText = string.Empty;
        }
    }

    private void ResetDetail()
    {
        _currentDetail = null;
        HookRows.Clear();
        GitGuardVersionText = string.Empty;
        GitVersionText = string.Empty;
        HooksDirectoryText = string.Empty;
        LatestScanResultText = string.Empty;
        LatestScanText = string.Empty;
        HasLatestScan = false;
        HasHookAttention = false;
        DetailMessage = null;
    }

    private void NotifyLocalizedLabels()
    {
        OnPropertyChanged(nameof(ListTitle));
        OnPropertyChanged(nameof(PathLabel));
        OnPropertyChanged(nameof(PathWatermark));
        OnPropertyChanged(nameof(AddButtonText));
        OnPropertyChanged(nameof(RefreshButtonText));
        OnPropertyChanged(nameof(EmptyStateText));
        OnPropertyChanged(nameof(SelectPromptText));
        OnPropertyChanged(nameof(LoadingText));
        OnPropertyChanged(nameof(HooksTitle));
        OnPropertyChanged(nameof(GitGuardVersionLabel));
        OnPropertyChanged(nameof(GitVersionLabel));
        OnPropertyChanged(nameof(HooksDirectoryLabel));
        OnPropertyChanged(nameof(LatestScanLabel));
        OnPropertyChanged(nameof(NoScanText));
        OnPropertyChanged(nameof(ScanButtonText));
        OnPropertyChanged(nameof(ScanHistoryButtonText));
        OnPropertyChanged(nameof(RemoveButtonText));
        OnPropertyChanged(nameof(RemoveConfirmPrompt));
        OnPropertyChanged(nameof(ConfirmRemoveButtonText));
        OnPropertyChanged(nameof(CancelRemoveButtonText));
        OnPropertyChanged(nameof(HookAttentionText));
    }

    private string FormatError(Exception exception) =>
        exception is RepositoryException repositoryException
            ? DesktopText.RepositoryError(repositoryException.ErrorCode, repositoryException.Message, _language)
            : exception.Message;
}
