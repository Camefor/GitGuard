using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GitGuard.Core;
using GitGuard.Core.Domain;
using GitGuard.Core.Model;
using GitGuard.Core.Policy;
using GitGuard.Desktop.Localization;
using GitGuard.Desktop.Services;

namespace GitGuard.Desktop.ViewModels;

/// <summary>
/// 白名单管理 ViewModel（GGD-0044 / <c>11-前端UI设计</c> §2、<c>12-前端组件设计</c> §3.3）：
/// 分页读取全局白名单，按规则 / 路径 / 指纹与原因新增条目，删除前二次确认。
/// 白名单存于本机审计库并对所有已登记仓库生效；所有数据访问经 <see cref="IRepositoryOperations"/>
/// 在后台线程执行，请求版本号用于丢弃过期分页响应。
/// </summary>
public sealed class AllowlistViewModel : ObservableObject
{
    /// <summary>每页白名单条目数量（与 <see cref="AllowlistQuery.DEFAULT_LIMIT"/> 一致）。</summary>
    public const int PAGE_SIZE = AllowlistQuery.DEFAULT_LIMIT;

    private readonly IRepositoryOperations _operations;
    private readonly Action _closeRequested;

    private DesktopLanguage _language;
    private string _repositoryName = string.Empty;
    private string _repositoryPath = string.Empty;
    private int _currentPage = 1;
    private int _totalCount;
    private int _pageRequestVersion;
    private bool _isActive;
    private bool _isLoadingPage;
    private bool _isBusy;
    private bool _hasLoadedPage;
    private string? _pageMessage;
    private bool _isPageMessageError;
    private string? _formMessage;
    private string _newRuleId = string.Empty;
    private string _newPathPattern = string.Empty;
    private string _newFingerprint = string.Empty;
    private string _newReason = string.Empty;
    private AllowlistEntryItemViewModel? _pendingDelete;

    /// <summary>初始化白名单管理 ViewModel。</summary>
    /// <param name="operations">仓库 / 白名单数据访问服务。</param>
    /// <param name="closeRequested">返回仓库详情时回调（由仓库页控制可见性）。</param>
    /// <param name="language">初始界面语言。</param>
    /// <exception cref="ArgumentNullException">任一依赖为 <see langword="null"/>。</exception>
    public AllowlistViewModel(
        IRepositoryOperations operations,
        Action closeRequested,
        DesktopLanguage language)
    {
        ArgumentNullException.ThrowIfNull(operations);
        ArgumentNullException.ThrowIfNull(closeRequested);

        _operations = operations;
        _closeRequested = closeRequested;
        _language = language;

        Entries = new ObservableCollection<AllowlistEntryItemViewModel>();
        BackCommand = new RelayCommand(_closeRequested);
        RefreshCommand = new AsyncRelayCommand(RefreshAsync);
        PreviousPageCommand = new AsyncRelayCommand(PreviousPageAsync);
        NextPageCommand = new AsyncRelayCommand(NextPageAsync);
        AddCommand = new AsyncRelayCommand(AddAsync);
        RequestDeleteCommand = new RelayCommand<AllowlistEntryItemViewModel>(RequestDelete);
        ConfirmDeleteCommand = new AsyncRelayCommand(ConfirmDeleteAsync);
        CancelDeleteCommand = new RelayCommand(CancelDelete);
    }

    /// <summary>当前页白名单条目。</summary>
    public ObservableCollection<AllowlistEntryItemViewModel> Entries { get; }

    /// <summary>返回仓库详情命令。</summary>
    public IRelayCommand BackCommand { get; }

    /// <summary>刷新当前页命令。</summary>
    public IAsyncRelayCommand RefreshCommand { get; }

    /// <summary>上一页命令。</summary>
    public IAsyncRelayCommand PreviousPageCommand { get; }

    /// <summary>下一页命令。</summary>
    public IAsyncRelayCommand NextPageCommand { get; }

    /// <summary>新增白名单条目命令。</summary>
    public IAsyncRelayCommand AddCommand { get; }

    /// <summary>请求删除指定条目（显示二次确认）命令。</summary>
    public IRelayCommand<AllowlistEntryItemViewModel> RequestDeleteCommand { get; }

    /// <summary>确认删除命令。</summary>
    public IAsyncRelayCommand ConfirmDeleteCommand { get; }

    /// <summary>取消删除命令。</summary>
    public IRelayCommand CancelDeleteCommand { get; }

    /// <summary>仓库显示名（仅用于页面上下文提示）。</summary>
    public string RepositoryName
    {
        get => _repositoryName;
        private set => SetProperty(ref _repositoryName, value);
    }

    /// <summary>仓库工作区绝对路径（仅用于页面上下文提示）。</summary>
    public string RepositoryPath
    {
        get => _repositoryPath;
        private set => SetProperty(ref _repositoryPath, value);
    }

    /// <summary>当前页码（1-based）。</summary>
    public int CurrentPage => _currentPage;

    /// <summary>白名单条目总数（分页计数）。</summary>
    public int TotalCount => _totalCount;

    /// <summary>总页数（无记录时为 1，便于分页文案显示）。</summary>
    public int TotalPages => Math.Max(1, (_totalCount + PAGE_SIZE - 1) / PAGE_SIZE);

    /// <summary>白名单列表是否正在加载。</summary>
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

    /// <summary>是否有新增或删除操作正在执行。</summary>
    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                NotifyPageState();
            }
        }
    }

    /// <summary>页面级消息（查询 / 新增 / 删除结果）。</summary>
    public string? PageMessage
    {
        get => _pageMessage;
        private set
        {
            if (SetProperty(ref _pageMessage, value))
            {
                OnPropertyChanged(nameof(HasPageMessage));
                OnPropertyChanged(nameof(IsEmpty));
            }
        }
    }

    /// <summary>页面级消息是否为错误（用于错误色显示）。</summary>
    public bool IsPageMessageError
    {
        get => _isPageMessageError;
        private set => SetProperty(ref _isPageMessageError, value);
    }

    /// <summary>表单级消息（校验失败或新增失败）。</summary>
    public string? FormMessage
    {
        get => _formMessage;
        private set
        {
            if (SetProperty(ref _formMessage, value))
            {
                OnPropertyChanged(nameof(HasFormMessage));
            }
        }
    }

    /// <summary>规则 ID 输入内容。</summary>
    public string NewRuleId
    {
        get => _newRuleId;
        set => SetProperty(ref _newRuleId, value ?? string.Empty);
    }

    /// <summary>路径 glob 输入内容。</summary>
    public string NewPathPattern
    {
        get => _newPathPattern;
        set => SetProperty(ref _newPathPattern, value ?? string.Empty);
    }

    /// <summary>指纹输入内容。</summary>
    public string NewFingerprint
    {
        get => _newFingerprint;
        set => SetProperty(ref _newFingerprint, value ?? string.Empty);
    }

    /// <summary>登记原因输入内容。</summary>
    public string NewReason
    {
        get => _newReason;
        set => SetProperty(ref _newReason, value ?? string.Empty);
    }

    /// <summary>当前待删除条目；非空时展示二次确认。</summary>
    public AllowlistEntryItemViewModel? PendingDelete
    {
        get => _pendingDelete;
        private set
        {
            if (SetProperty(ref _pendingDelete, value))
            {
                OnPropertyChanged(nameof(HasPendingDelete));
                OnPropertyChanged(nameof(PendingDeleteSummary));
            }
        }
    }

    /// <summary>是否存在页面级消息。</summary>
    public bool HasPageMessage => !string.IsNullOrWhiteSpace(PageMessage);

    /// <summary>是否存在表单级消息。</summary>
    public bool HasFormMessage => !string.IsNullOrWhiteSpace(FormMessage);

    /// <summary>当前页是否有条目。</summary>
    public bool HasEntries => Entries.Count > 0;

    /// <summary>是否展示白名单空状态。</summary>
    public bool IsEmpty => _hasLoadedPage && !IsLoadingPage && !HasEntries && !HasPageMessage;

    /// <summary>是否展示删除二次确认。</summary>
    public bool HasPendingDelete => PendingDelete is not null;

    /// <summary>待删除条目的匹配条件摘要。</summary>
    public string PendingDeleteSummary => PendingDelete?.ConditionSummary ?? string.Empty;

    /// <summary>上一页是否可用。</summary>
    public bool CanGoPrevious => CurrentPage > 1 && !IsLoadingPage && !IsBusy;

    /// <summary>下一页是否可用。</summary>
    public bool CanGoNext => CurrentPage < TotalPages && !IsLoadingPage && !IsBusy;

    /// <summary>新增按钮是否可用。</summary>
    public bool CanSubmit => !IsLoadingPage && !IsBusy;

    /// <summary>白名单分页文案。</summary>
    public string PageText => DesktopText.AllowlistPage(CurrentPage, TotalPages, TotalCount, _language);

    /// <summary>白名单页标题。</summary>
    public string Title => DesktopText.AllowlistTitle(_language);

    /// <summary>返回按钮文案。</summary>
    public string BackButtonText => DesktopText.AllowlistBackButton(_language);

    /// <summary>刷新按钮文案。</summary>
    public string RefreshButtonText => DesktopText.AllowlistRefreshButton(_language);

    /// <summary>条目列表标题。</summary>
    public string EntriesTitle => DesktopText.AllowlistEntriesTitle(_language);

    /// <summary>新增表单标题。</summary>
    public string FormTitle => DesktopText.AllowlistFormTitle(_language);

    /// <summary>全局作用域与匹配条件提示。</summary>
    public string GlobalHint => DesktopText.AllowlistGlobalHint(_language);

    /// <summary>规则 ID 字段标签。</summary>
    public string RuleLabel => DesktopText.AllowlistRuleLabel(_language);

    /// <summary>路径 glob 字段标签。</summary>
    public string PathLabel => DesktopText.AllowlistPathLabel(_language);

    /// <summary>指纹字段标签。</summary>
    public string FingerprintLabel => DesktopText.AllowlistFingerprintLabel(_language);

    /// <summary>原因字段标签。</summary>
    public string ReasonLabel => DesktopText.AllowlistReasonLabel(_language);

    /// <summary>规则 ID 输入水印。</summary>
    public string RuleWatermark => DesktopText.AllowlistRuleWatermark(_language);

    /// <summary>路径 glob 输入水印。</summary>
    public string PathWatermark => DesktopText.AllowlistPathWatermark(_language);

    /// <summary>指纹输入水印。</summary>
    public string FingerprintWatermark => DesktopText.AllowlistFingerprintWatermark(_language);

    /// <summary>原因输入水印。</summary>
    public string ReasonWatermark => DesktopText.AllowlistReasonWatermark(_language);

    /// <summary>添加按钮文案。</summary>
    public string AddButtonText => DesktopText.AllowlistAddButton(_language);

    /// <summary>空状态文案。</summary>
    public string EmptyText => DesktopText.AllowlistEmpty(_language);

    /// <summary>加载提示。</summary>
    public string LoadingText => DesktopText.AllowlistLoading(_language);

    /// <summary>上一页按钮文案。</summary>
    public string PreviousPageText => DesktopText.AllowlistPreviousPage(_language);

    /// <summary>下一页按钮文案。</summary>
    public string NextPageText => DesktopText.AllowlistNextPage(_language);

    /// <summary>删除按钮文案。</summary>
    public string DeleteButtonText => DesktopText.AllowlistDeleteButton(_language);

    /// <summary>删除二次确认提示。</summary>
    public string DeleteConfirmPrompt => DesktopText.AllowlistDeleteConfirmPrompt(_language);

    /// <summary>确认删除按钮文案。</summary>
    public string ConfirmDeleteButtonText => DesktopText.AllowlistConfirmDeleteButton(_language);

    /// <summary>取消删除按钮文案。</summary>
    public string CancelButtonText => DesktopText.AllowlistCancelButton(_language);

    /// <summary>打开指定仓库上下文的白名单页；重复调用会重置分页与表单状态。</summary>
    /// <param name="repositoryName">仓库显示名（仅用于页面上下文）。</param>
    /// <param name="repositoryPath">仓库工作区绝对路径（仅用于页面上下文）。</param>
    /// <param name="cancellationToken">取消标记。</param>
    /// <returns>首页加载完成时结束。</returns>
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
        Entries.Clear();
        ResetForm();
        CloseDeleteConfirmation();
        ClearPageMessage();
        NotifyPageState();

        await LoadPageAsync(1, cancellationToken).ConfigureAwait(true);
    }

    /// <summary>关闭时失效所有进行中的请求，并清空页面状态。</summary>
    public void Deactivate()
    {
        _isActive = false;
        _pageRequestVersion++;
        _currentPage = 1;
        _totalCount = 0;
        _hasLoadedPage = false;
        Entries.Clear();
        ResetForm();
        CloseDeleteConfirmation();
        ClearPageMessage();
        IsLoadingPage = false;
        IsBusy = false;
        RepositoryName = string.Empty;
        RepositoryPath = string.Empty;
        NotifyPageState();
    }

    /// <summary>按新界面语言刷新全部派生文案。</summary>
    /// <param name="language">目标界面语言。</param>
    public void Relocalize(DesktopLanguage language)
    {
        _language = language;
        foreach (AllowlistEntryItemViewModel entry in Entries)
        {
            entry.Relocalize(language);
        }

        OnPropertyChanged(nameof(PendingDeleteSummary));
        NotifyLocalizedLabels();
    }

    private static string? NormalizeOptional(string value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static bool IsSha256Hex(string value) =>
        value.Length == 64 && value.All(char.IsAsciiHexDigit);

    private async Task RefreshAsync()
    {
        if (!_isActive || IsLoadingPage || IsBusy)
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

    private async Task AddAsync()
    {
        if (!_isActive || IsLoadingPage || IsBusy)
        {
            return;
        }

        FormMessage = null;
        if (!TryBuildDraft(out string? ruleId, out string? pathPattern, out string? fingerprint, out string reason))
        {
            return;
        }

        IsBusy = true;
        ClearPageMessage();
        try
        {
            await _operations
                .AddAllowlistAsync(ruleId, pathPattern, fingerprint, reason)
                .ConfigureAwait(true);
            if (!_isActive)
            {
                return;
            }

            ResetForm();
            _currentPage = 1;
            bool loaded = await LoadPageAsync(1, CancellationToken.None).ConfigureAwait(true);
            if (loaded)
            {
                SetPageMessage(DesktopText.AllowlistAddedMessage(_language), isError: false);
            }
        }
        catch (Exception ex)
        {
            if (_isActive)
            {
                FormMessage = FormatError(ex);
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void RequestDelete(AllowlistEntryItemViewModel? entry)
    {
        if (!_isActive || entry is null || IsBusy)
        {
            return;
        }

        FormMessage = null;
        PendingDelete = entry;
    }

    private void CancelDelete() => PendingDelete = null;

    private async Task ConfirmDeleteAsync()
    {
        AllowlistEntryItemViewModel? entry = PendingDelete;
        if (!_isActive || entry is null || IsBusy)
        {
            return;
        }

        IsBusy = true;
        ClearPageMessage();
        try
        {
            await _operations.RemoveAllowlistAsync(entry.Id).ConfigureAwait(true);
            if (!_isActive)
            {
                return;
            }

            CloseDeleteConfirmation();
            if (Entries.Count == 1 && CurrentPage > 1)
            {
                _currentPage--;
            }

            bool loaded = await LoadPageAsync(CurrentPage, CancellationToken.None).ConfigureAwait(true);
            if (loaded)
            {
                SetPageMessage(DesktopText.AllowlistDeletedMessage(_language), isError: false);
            }
        }
        catch (Exception ex)
        {
            if (_isActive)
            {
                SetPageMessage(FormatError(ex), isError: true);
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task<bool> LoadPageAsync(int page, CancellationToken cancellationToken)
    {
        if (!_isActive)
        {
            return false;
        }

        int requestVersion = ++_pageRequestVersion;
        IsLoadingPage = true;
        ClearPageMessage();

        try
        {
            AllowlistPage result = await _operations
                .QueryAllowlistAsync(PAGE_SIZE, (page - 1) * PAGE_SIZE, cancellationToken)
                .ConfigureAwait(true);
            if (requestVersion != _pageRequestVersion || !_isActive)
            {
                return false;
            }

            _currentPage = page;
            _totalCount = result.TotalCount;
            _hasLoadedPage = true;

            Entries.Clear();
            foreach (AllowlistEntry entry in result.Entries)
            {
                Entries.Add(new AllowlistEntryItemViewModel(entry, _language));
            }

            CloseDeleteConfirmation();
            NotifyPageState();
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return false;
        }
        catch (Exception ex)
        {
            if (requestVersion == _pageRequestVersion && _isActive)
            {
                SetPageMessage(FormatError(ex), isError: true);
            }

            return false;
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

    private bool TryBuildDraft(
        out string? ruleId,
        out string? pathPattern,
        out string? fingerprint,
        out string reason)
    {
        ruleId = NormalizeOptional(NewRuleId);
        pathPattern = NormalizeOptional(NewPathPattern);
        fingerprint = NormalizeOptional(NewFingerprint);
        reason = NewReason.Trim();

        if (ruleId is null && pathPattern is null && fingerprint is null)
        {
            FormMessage = DesktopText.AllowlistValidationAtLeastOne(_language);
            return false;
        }

        if (reason.Length < 5)
        {
            FormMessage = DesktopText.AllowlistValidationReasonTooShort(_language);
            return false;
        }

        if (reason.Length > 256)
        {
            FormMessage = DesktopText.AllowlistValidationReasonTooLong(_language);
            return false;
        }

        if (ruleId is not null && !RuleIdFormat.IsValid(ruleId))
        {
            FormMessage = DesktopText.AllowlistValidationRuleId(_language);
            return false;
        }

        if (fingerprint is not null && !IsSha256Hex(fingerprint))
        {
            FormMessage = DesktopText.AllowlistValidationFingerprint(_language);
            return false;
        }

        return true;
    }

    private void ResetForm()
    {
        NewRuleId = string.Empty;
        NewPathPattern = string.Empty;
        NewFingerprint = string.Empty;
        NewReason = string.Empty;
        FormMessage = null;
    }

    private void CloseDeleteConfirmation() => PendingDelete = null;

    private void ClearPageMessage() => SetPageMessage(null, isError: false);

    private void SetPageMessage(string? message, bool isError)
    {
        PageMessage = message;
        IsPageMessageError = isError && !string.IsNullOrWhiteSpace(message);
    }

    private string FormatError(Exception exception) =>
        exception is AllowlistException allowlistException
            ? DesktopText.AllowlistError(allowlistException.ErrorCode, allowlistException.Message, _language)
            : exception.Message;

    private void NotifyPageState()
    {
        OnPropertyChanged(nameof(CurrentPage));
        OnPropertyChanged(nameof(TotalCount));
        OnPropertyChanged(nameof(TotalPages));
        OnPropertyChanged(nameof(PageText));
        OnPropertyChanged(nameof(HasEntries));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(CanGoPrevious));
        OnPropertyChanged(nameof(CanGoNext));
        OnPropertyChanged(nameof(CanSubmit));
    }

    private void NotifyLocalizedLabels()
    {
        OnPropertyChanged(nameof(PageText));
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(BackButtonText));
        OnPropertyChanged(nameof(RefreshButtonText));
        OnPropertyChanged(nameof(EntriesTitle));
        OnPropertyChanged(nameof(FormTitle));
        OnPropertyChanged(nameof(GlobalHint));
        OnPropertyChanged(nameof(RuleLabel));
        OnPropertyChanged(nameof(PathLabel));
        OnPropertyChanged(nameof(FingerprintLabel));
        OnPropertyChanged(nameof(ReasonLabel));
        OnPropertyChanged(nameof(RuleWatermark));
        OnPropertyChanged(nameof(PathWatermark));
        OnPropertyChanged(nameof(FingerprintWatermark));
        OnPropertyChanged(nameof(ReasonWatermark));
        OnPropertyChanged(nameof(AddButtonText));
        OnPropertyChanged(nameof(EmptyText));
        OnPropertyChanged(nameof(LoadingText));
        OnPropertyChanged(nameof(PreviousPageText));
        OnPropertyChanged(nameof(NextPageText));
        OnPropertyChanged(nameof(DeleteButtonText));
        OnPropertyChanged(nameof(DeleteConfirmPrompt));
        OnPropertyChanged(nameof(ConfirmDeleteButtonText));
        OnPropertyChanged(nameof(CancelButtonText));
    }
}
