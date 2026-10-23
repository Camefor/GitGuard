using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using GitGuard.Application.Rules;
using GitGuard.Core.Rules;
using GitGuard.Desktop.Localization;
using GitGuard.Desktop.Services;

namespace GitGuard.Desktop.ViewModels;

/// <summary>
/// 规则库 ViewModel（GGD-0045 / <c>11-前端UI设计</c> §2、<c>12-前端组件设计</c> §3.7）：
/// 展示与 <c>gitguard rules list</c> 同源的规则目录，支持来源 / 类别 / 有效启用筛选、200ms 搜索节流，
/// 并默认隐藏正则原文，仅在用户显式切换后展示选中规则的模式。
/// Gitleaks 规则由外部引擎在扫描期动态产生，不进入静态目录，页面在 Gitleaks 分类下给出解释与适配器状态。
/// </summary>
public sealed class RulesViewModel : SectionViewModel
{
    /// <summary>搜索输入节流窗口（毫秒；12-前端组件设计 §6）。</summary>
    public const int SEARCH_DEBOUNCE_MILLISECONDS = 200;

    /// <summary>来源筛选：全部。</summary>
    public const string SOURCE_ALL = "ALL";

    /// <summary>来源筛选：内置规则。</summary>
    public const string SOURCE_BUILTIN = "BUILTIN";

    /// <summary>来源筛选：自定义规则。</summary>
    public const string SOURCE_USER = "USER";

    /// <summary>来源筛选：Gitleaks（动态规则，无静态条目）。</summary>
    public const string SOURCE_GITLEAKS = "GITLEAKS";

    /// <summary>类别筛选：全部。</summary>
    public const string CATEGORY_ALL = "ALL";

    private readonly IRuleOperations _operations;
    private readonly TimeSpan _searchDebounce;
    private readonly List<RuleItemViewModel> _allRules = new List<RuleItemViewModel>();
    private readonly Dictionary<string, string> _patterns = new Dictionary<string, string>(StringComparer.Ordinal);

    private DesktopLanguage _language;
    private RuleFilterOption _selectedSourceFilter;
    private RuleFilterOption _selectedCategoryFilter;
    private RuleItemViewModel? _selectedRule;
    private string _searchText = string.Empty;
    private bool _enabledOnly;
    private bool _isLoading;
    private bool _isPatternVisible;
    private bool _hasLoaded;
    private bool _initialized;
    private bool _gitleaksEnabled;
    private string? _gitleaksPath;
    private string? _loadMessage;
    private CancellationTokenSource? _searchCts;
    private int _searchVersion;

    /// <summary>初始化规则库 ViewModel。</summary>
    /// <param name="operations">规则目录数据访问服务。</param>
    /// <param name="language">初始界面语言。</param>
    /// <param name="searchDebounce">
    /// 搜索节流窗口；<see langword="null"/> 使用 <see cref="SEARCH_DEBOUNCE_MILLISECONDS"/>（单测可传零）。
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="operations"/> 为 <see langword="null"/>。</exception>
    public RulesViewModel(
        IRuleOperations operations,
        DesktopLanguage language,
        TimeSpan? searchDebounce = null)
        : base(DesktopSection.Rules, language)
    {
        ArgumentNullException.ThrowIfNull(operations);

        _operations = operations;
        _language = language;
        _searchDebounce = searchDebounce ?? TimeSpan.FromMilliseconds(SEARCH_DEBOUNCE_MILLISECONDS);

        SourceFilters = CreateSourceFilters(language);
        CategoryFilters = CreateCategoryFilters(language);
        _selectedSourceFilter = SourceFilters[0];
        _selectedCategoryFilter = CategoryFilters[0];

        VisibleRules = new ObservableCollection<RuleItemViewModel>();
        RefreshCommand = new AsyncRelayCommand(RefreshAsync);
        TogglePatternCommand = new RelayCommand(TogglePattern);
    }

    /// <summary>当前筛选下可见的规则（保持目录原始顺序）。</summary>
    public ObservableCollection<RuleItemViewModel> VisibleRules { get; }

    /// <summary>来源筛选选项（全部 / 内置 / 自定义 / Gitleaks）。</summary>
    public IReadOnlyList<RuleFilterOption> SourceFilters { get; }

    /// <summary>类别筛选选项（全部 / Secret / Privacy / Local path / URL）。</summary>
    public IReadOnlyList<RuleFilterOption> CategoryFilters { get; }

    /// <summary>刷新规则目录命令。</summary>
    public IAsyncRelayCommand RefreshCommand { get; }

    /// <summary>切换选中规则的正则显示状态命令。</summary>
    public IRelayCommand TogglePatternCommand { get; }

    /// <summary>搜索输入内容（变更后按 <see cref="SEARCH_DEBOUNCE_MILLISECONDS"/> 节流应用）。</summary>
    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value ?? string.Empty))
            {
                ScheduleSearch();
            }
        }
    }

    /// <summary>当前来源筛选。</summary>
    public RuleFilterOption SelectedSourceFilter
    {
        get => _selectedSourceFilter;
        set
        {
            if (value is null || !SetProperty(ref _selectedSourceFilter, value))
            {
                return;
            }

            ApplyFilter();
            OnPropertyChanged(nameof(IsGitleaksInfoVisible));
            OnPropertyChanged(nameof(GitleaksInfoText));
        }
    }

    /// <summary>当前类别筛选。</summary>
    public RuleFilterOption SelectedCategoryFilter
    {
        get => _selectedCategoryFilter;
        set
        {
            if (value is null || !SetProperty(ref _selectedCategoryFilter, value))
            {
                return;
            }

            ApplyFilter();
        }
    }

    /// <summary>是否仅显示有效启用的规则。</summary>
    public bool EnabledOnly
    {
        get => _enabledOnly;
        set
        {
            if (SetProperty(ref _enabledOnly, value))
            {
                ApplyFilter();
            }
        }
    }

    /// <summary>当前选中的规则；切换时自动隐藏正则。</summary>
    public RuleItemViewModel? SelectedRule
    {
        get => _selectedRule;
        set
        {
            if (!SetProperty(ref _selectedRule, value))
            {
                return;
            }

            IsPatternVisible = false;
            OnPropertyChanged(nameof(HasSelectedRule));
            OnPropertyChanged(nameof(CanTogglePattern));
            OnPropertyChanged(nameof(SelectedPatternText));
            OnPropertyChanged(nameof(PatternToggleText));
        }
    }

    /// <summary>正则原文是否对用户可见（默认 <see langword="false"/>）。</summary>
    public bool IsPatternVisible
    {
        get => _isPatternVisible;
        private set
        {
            if (SetProperty(ref _isPatternVisible, value))
            {
                OnPropertyChanged(nameof(SelectedPatternText));
                OnPropertyChanged(nameof(PatternToggleText));
            }
        }
    }

    /// <summary>规则目录是否正在加载。</summary>
    public bool IsLoading
    {
        get => _isLoading;
        private set
        {
            if (SetProperty(ref _isLoading, value))
            {
                OnPropertyChanged(nameof(IsEmpty));
                OnPropertyChanged(nameof(CanInteract));
            }
        }
    }

    /// <summary>加载失败消息（空目录仍可展示空状态）。</summary>
    public string? LoadMessage
    {
        get => _loadMessage;
        private set
        {
            if (SetProperty(ref _loadMessage, value))
            {
                OnPropertyChanged(nameof(HasLoadMessage));
                OnPropertyChanged(nameof(IsEmpty));
            }
        }
    }

    /// <summary>全部规则数量。</summary>
    public int TotalRuleCount => _allRules.Count;

    /// <summary>有效启用的规则数量。</summary>
    public int EnabledRuleCount => _allRules.Count(rule => rule.Enabled);

    /// <summary>当前筛选后可见的规则数量。</summary>
    public int VisibleRuleCount => VisibleRules.Count;

    /// <summary>是否存在加载失败消息。</summary>
    public bool HasLoadMessage => !string.IsNullOrWhiteSpace(LoadMessage);

    /// <summary>是否展示空状态（已加载完成、无匹配且无错误）。</summary>
    public bool IsEmpty => _hasLoaded && !IsLoading && VisibleRules.Count == 0 && !HasLoadMessage;

    /// <summary>是否选中了规则。</summary>
    public bool HasSelectedRule => SelectedRule is not null;

    /// <summary>是否可以切换正则显示。</summary>
    public bool CanTogglePattern => SelectedRule is not null;

    /// <summary>刷新按钮是否可用。</summary>
    public bool CanInteract => !IsLoading;

    /// <summary>是否处于 Gitleaks 分类（用于展示动态规则说明）。</summary>
    public bool IsGitleaksInfoVisible =>
        string.Equals(SelectedSourceFilter.Key, SOURCE_GITLEAKS, StringComparison.Ordinal);

    /// <summary>选中规则的正则展示文本：默认隐藏，显式切换后展示原文。</summary>
    public string SelectedPatternText
    {
        get
        {
            if (SelectedRule is null)
            {
                return string.Empty;
            }

            if (!IsPatternVisible)
            {
                return DesktopText.RulesPatternHidden(_language);
            }

            return _patterns.TryGetValue(SelectedRule.RuleId, out string? pattern)
                ? pattern
                : string.Empty;
        }
    }

    /// <summary>正则切换按钮文案。</summary>
    public string PatternToggleText => IsPatternVisible
        ? DesktopText.RulesPatternHide(_language)
        : DesktopText.RulesPatternShow(_language);

    /// <summary>规则数量摘要。</summary>
    public string SummaryText => DesktopText.RulesSummary(
        TotalRuleCount,
        EnabledRuleCount,
        VisibleRuleCount,
        _language);

    /// <summary>Gitleaks 分类说明（含适配器开关与可执行文件）。</summary>
    public string GitleaksInfoText => _gitleaksEnabled
        ? DesktopText.RulesGitleaksEnabledInfo(
            string.IsNullOrWhiteSpace(_gitleaksPath)
                ? DesktopText.RulesGitleaksSearchPath(_language)
                : _gitleaksPath,
            _language)
        : DesktopText.RulesGitleaksDisabledInfo(_language);

    /// <summary>空状态文案（Gitleaks 分类使用动态规则说明）。</summary>
    public string EmptyStateText => IsGitleaksInfoVisible
        ? DesktopText.RulesGitleaksEmpty(_language)
        : DesktopText.RulesEmpty(_language);

    /// <summary>搜索框水印。</summary>
    public string SearchWatermark => DesktopText.RulesSearchWatermark(_language);

    /// <summary>来源筛选标签。</summary>
    public string SourceFilterLabel => DesktopText.RulesSourceFilterLabel(_language);

    /// <summary>类别筛选标签。</summary>
    public string CategoryFilterLabel => DesktopText.RulesCategoryFilterLabel(_language);

    /// <summary>仅启用复选框文案。</summary>
    public string EnabledOnlyText => DesktopText.RulesEnabledOnly(_language);

    /// <summary>刷新按钮文案。</summary>
    public string RefreshButtonText => DesktopText.RulesRefreshButton(_language);

    /// <summary>加载提示。</summary>
    public string LoadingText => DesktopText.RulesLoading(_language);

    /// <summary>规则列表标题。</summary>
    public string RulesListTitle => DesktopText.RulesListTitle(_language);

    /// <summary>规则详情标题。</summary>
    public string DetailTitle => DesktopText.RulesDetailTitle(_language);

    /// <summary>未选择规则时的提示。</summary>
    public string SelectRulePrompt => DesktopText.RulesSelectPrompt(_language);

    /// <summary>规则 ID 字段标签。</summary>
    public string RuleIdLabel => DesktopText.RulesRuleIdLabel(_language);

    /// <summary>规则名称字段标签。</summary>
    public string RuleNameLabel => DesktopText.RulesRuleNameLabel(_language);

    /// <summary>类别字段标签。</summary>
    public string CategoryFieldLabel => DesktopText.RulesCategoryFieldLabel(_language);

    /// <summary>严重度字段标签。</summary>
    public string SeverityFieldLabel => DesktopText.RulesSeverityFieldLabel(_language);

    /// <summary>来源字段标签。</summary>
    public string SourceFieldLabel => DesktopText.RulesSourceFieldLabel(_language);

    /// <summary>启用状态字段标签。</summary>
    public string StatusFieldLabel => DesktopText.RulesStatusFieldLabel(_language);

    /// <summary>正则字段标签。</summary>
    public string PatternLabel => DesktopText.RulesPatternLabel(_language);

    /// <summary>正则默认隐藏提示。</summary>
    public string PatternHint => DesktopText.RulesPatternHint(_language);

    /// <summary>页面首次可见时加载规则目录（只执行一次）；失败已转为 <see cref="LoadMessage"/>。</summary>
    /// <param name="cancellationToken">取消标记。</param>
    /// <returns>加载任务。</returns>
    public Task EnsureLoadedAsync(CancellationToken cancellationToken = default)
    {
        if (_initialized)
        {
            return Task.CompletedTask;
        }

        _initialized = true;
        return LoadAsync(cancellationToken);
    }

    /// <inheritdoc />
    public override void Relocalize(DesktopLanguage language)
    {
        _language = language;
        base.Relocalize(language);

        foreach (RuleItemViewModel rule in _allRules)
        {
            rule.Relocalize(language);
        }

        foreach (RuleFilterOption option in SourceFilters)
        {
            option.Label = DesktopText.RuleSourceFilterLabel(option.Key, language);
        }

        foreach (RuleFilterOption option in CategoryFilters)
        {
            option.Label = DesktopText.RuleCategoryFilterLabel(option.Key, language);
        }

        OnPropertyChanged(nameof(SelectedPatternText));
        OnPropertyChanged(nameof(PatternToggleText));
        OnPropertyChanged(nameof(SummaryText));
        OnPropertyChanged(nameof(GitleaksInfoText));
        OnPropertyChanged(nameof(EmptyStateText));
        OnPropertyChanged(nameof(SearchWatermark));
        OnPropertyChanged(nameof(SourceFilterLabel));
        OnPropertyChanged(nameof(CategoryFilterLabel));
        OnPropertyChanged(nameof(EnabledOnlyText));
        OnPropertyChanged(nameof(RefreshButtonText));
        OnPropertyChanged(nameof(LoadingText));
        OnPropertyChanged(nameof(RulesListTitle));
        OnPropertyChanged(nameof(DetailTitle));
        OnPropertyChanged(nameof(SelectRulePrompt));
        OnPropertyChanged(nameof(RuleIdLabel));
        OnPropertyChanged(nameof(RuleNameLabel));
        OnPropertyChanged(nameof(CategoryFieldLabel));
        OnPropertyChanged(nameof(SeverityFieldLabel));
        OnPropertyChanged(nameof(SourceFieldLabel));
        OnPropertyChanged(nameof(StatusFieldLabel));
        OnPropertyChanged(nameof(PatternLabel));
        OnPropertyChanged(nameof(PatternHint));
    }

    private static RuleFilterOption[] CreateSourceFilters(DesktopLanguage language) =>
        new[]
        {
            new RuleFilterOption(SOURCE_ALL, DesktopText.RuleSourceFilterLabel(SOURCE_ALL, language)),
            new RuleFilterOption(SOURCE_BUILTIN, DesktopText.RuleSourceFilterLabel(SOURCE_BUILTIN, language)),
            new RuleFilterOption(SOURCE_USER, DesktopText.RuleSourceFilterLabel(SOURCE_USER, language)),
            new RuleFilterOption(SOURCE_GITLEAKS, DesktopText.RuleSourceFilterLabel(SOURCE_GITLEAKS, language)),
        };

    private static RuleFilterOption[] CreateCategoryFilters(DesktopLanguage language) =>
        new[]
        {
            new RuleFilterOption(CATEGORY_ALL, DesktopText.RuleCategoryFilterLabel(CATEGORY_ALL, language)),
            new RuleFilterOption("SECRET", DesktopText.RuleCategoryFilterLabel("SECRET", language)),
            new RuleFilterOption("PRIVACY", DesktopText.RuleCategoryFilterLabel("PRIVACY", language)),
            new RuleFilterOption("LOCALPATH", DesktopText.RuleCategoryFilterLabel("LOCALPATH", language)),
            new RuleFilterOption("URL", DesktopText.RuleCategoryFilterLabel("URL", language)),
        };

    private static string SourceKey(RuleSource source) => source switch
    {
        RuleSource.Builtin => SOURCE_BUILTIN,
        RuleSource.User => SOURCE_USER,
        RuleSource.Gitleaks => SOURCE_GITLEAKS,
        _ => string.Empty,
    };

    private async Task RefreshAsync()
    {
        if (IsLoading)
        {
            return;
        }

        await LoadAsync(CancellationToken.None).ConfigureAwait(true);
    }

    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        if (IsLoading)
        {
            return;
        }

        IsLoading = true;
        LoadMessage = null;
        try
        {
            RuleCatalogSnapshot snapshot = await _operations
                .LoadCatalogAsync(cancellationToken)
                .ConfigureAwait(true);
            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            _allRules.Clear();
            _patterns.Clear();
            foreach (RuleCatalogEntry entry in snapshot.Rules)
            {
                _allRules.Add(new RuleItemViewModel(entry, _language));
                _patterns[entry.RuleId] = entry.Pattern.Regex.ToString();
            }

            _gitleaksEnabled = snapshot.GitleaksEnabled;
            _gitleaksPath = snapshot.GitleaksPath;
            _hasLoaded = true;
            ApplyFilter();
            OnPropertyChanged(nameof(GitleaksInfoText));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // 页面关闭 / 宿主取消：不视为失败
        }
        catch (Exception ex)
        {
            if (!cancellationToken.IsCancellationRequested)
            {
                LoadMessage = DesktopText.RulesLoadFailed(ex.Message, _language);
            }
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void ScheduleSearch()
    {
        _searchCts?.Cancel();
        _searchCts?.Dispose();
        var cts = new CancellationTokenSource();
        _searchCts = cts;
        int version = ++_searchVersion;
        _ = DebounceSearchAsync(version, cts.Token);
    }

    private async Task DebounceSearchAsync(int version, CancellationToken cancellationToken)
    {
        try
        {
            if (_searchDebounce > TimeSpan.Zero)
            {
                await Task.Delay(_searchDebounce, cancellationToken).ConfigureAwait(true);
            }
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (cancellationToken.IsCancellationRequested || version != _searchVersion)
        {
            return;
        }

        ApplyFilter();
    }

    private void ApplyFilter()
    {
        if (!_hasLoaded)
        {
            return;
        }

        string search = SearchText.Trim();
        var filtered = new List<RuleItemViewModel>(_allRules.Count);
        foreach (RuleItemViewModel rule in _allRules)
        {
            if (EnabledOnly && !rule.Enabled)
            {
                continue;
            }

            if (!string.Equals(SelectedSourceFilter.Key, SOURCE_ALL, StringComparison.Ordinal)
                && !string.Equals(SourceKey(rule.Source), SelectedSourceFilter.Key, StringComparison.Ordinal))
            {
                continue;
            }

            if (!string.Equals(SelectedCategoryFilter.Key, CATEGORY_ALL, StringComparison.Ordinal)
                && !string.Equals(rule.Category, SelectedCategoryFilter.Key, StringComparison.Ordinal))
            {
                continue;
            }

            if (search.Length > 0
                && !rule.RuleId.Contains(search, StringComparison.OrdinalIgnoreCase)
                && !rule.Name.Contains(search, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            filtered.Add(rule);
        }

        RuleItemViewModel? previousSelection = SelectedRule;
        VisibleRules.Clear();
        foreach (RuleItemViewModel rule in filtered)
        {
            VisibleRules.Add(rule);
        }

        SelectedRule = null;
        if (previousSelection is not null && filtered.Contains(previousSelection))
        {
            SelectedRule = previousSelection;
        }
        else if (filtered.Count > 0)
        {
            SelectedRule = filtered[0];
        }

        NotifyFilterState();
    }

    private void TogglePattern()
    {
        if (SelectedRule is null)
        {
            return;
        }

        IsPatternVisible = !IsPatternVisible;
    }

    private void NotifyFilterState()
    {
        OnPropertyChanged(nameof(TotalRuleCount));
        OnPropertyChanged(nameof(EnabledRuleCount));
        OnPropertyChanged(nameof(VisibleRuleCount));
        OnPropertyChanged(nameof(SummaryText));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(EmptyStateText));
    }
}
