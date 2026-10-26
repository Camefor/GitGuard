using System.Collections.ObjectModel;
using System.ComponentModel;
using Avalonia.Styling;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GitGuard.Desktop.Localization;
using GitGuard.Desktop.Services;

namespace GitGuard.Desktop.ViewModels;

/// <summary>
/// 主窗口 ViewModel：维护一级导航（仓库 / 规则 / 日志 / 设置）、当前页面与界面语言切换
/// （11-前端UI设计 §1 / §7，12-前端组件设计 §4）。页面实例在会话内保持，切换语言只刷新文案。
/// </summary>
public sealed class MainViewModel : ObservableObject
{
    private readonly AppState _appState;
    private readonly DesktopServices _services;
    private readonly Dictionary<DesktopSection, SectionViewModel> _pages;
    private NavigationItemViewModel? _selectedNavigation;
    private SectionViewModel _currentPage;
    private string _windowTitle;
    private string _tagline;
    private string _languageToggleText;
    private string _versionText;
    private ThemeVariant _requestedThemeVariant;

    /// <summary>初始化主窗口 ViewModel。</summary>
    /// <param name="appState">应用级状态。</param>
    /// <param name="services">
    /// 页面服务集合；<see langword="null"/> 时全部页面保持骨架（ViewModel 单测与设计预览不访问真实数据）。
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="appState"/> 为 <see langword="null"/>。</exception>
    public MainViewModel(AppState appState, DesktopServices? services = null)
    {
        _appState = appState ?? throw new ArgumentNullException(nameof(appState));
        _services = services ?? new DesktopServices();

        NavigationItems = new ObservableCollection<NavigationItemViewModel>(
            Enum.GetValues<DesktopSection>().Select(section => new NavigationItemViewModel(section, appState.Language)));
        _pages = Enum.GetValues<DesktopSection>().ToDictionary(section => section, CreatePage);

        _windowTitle = DesktopText.WindowTitle(appState.Language);
        _tagline = DesktopText.Tagline(appState.Language);
        _languageToggleText = DesktopText.LanguageToggle(appState.Language);
        _versionText = DesktopText.VersionText(appState.Version);
        _requestedThemeVariant = appState.ThemeMode.ToThemeVariant();
        _selectedNavigation = NavigationItems[0];
        _currentPage = _pages[_selectedNavigation.Section];

        ToggleLanguageCommand = new RelayCommand(ToggleLanguage);
        _appState.PropertyChanged += OnAppStatePropertyChanged;
        EnsurePageLoaded(_currentPage);
    }

    /// <summary>一级导航项（顺序与 <see cref="DesktopSection"/> 声明一致）。</summary>
    public ObservableCollection<NavigationItemViewModel> NavigationItems { get; }

    /// <summary>语言切换命令（中 / 英）。</summary>
    public IRelayCommand ToggleLanguageCommand { get; }

    /// <summary>窗口标题。</summary>
    public string WindowTitle
    {
        get => _windowTitle;
        private set => SetProperty(ref _windowTitle, value);
    }

    /// <summary>侧边栏产品副标题。</summary>
    public string Tagline
    {
        get => _tagline;
        private set => SetProperty(ref _tagline, value);
    }

    /// <summary>语言切换按钮文案（显示目标语言）。</summary>
    public string LanguageToggleText
    {
        get => _languageToggleText;
        private set => SetProperty(ref _languageToggleText, value);
    }

    /// <summary>侧边栏版本行。</summary>
    public string VersionText
    {
        get => _versionText;
        private set => SetProperty(ref _versionText, value);
    }

    /// <summary>窗口主题变体（跟随系统 / 浅色 / 深色），由 <see cref="AppState.ThemeMode"/> 驱动。</summary>
    public ThemeVariant RequestedThemeVariant
    {
        get => _requestedThemeVariant;
        private set => SetProperty(ref _requestedThemeVariant, value);
    }

    /// <summary>当前选中的导航项；置空时保持当前页面不变。</summary>
    public NavigationItemViewModel? SelectedNavigation
    {
        get => _selectedNavigation;
        set
        {
            if (SetProperty(ref _selectedNavigation, value) && value is not null)
            {
                CurrentPage = _pages[value.Section];
                EnsurePageLoaded(CurrentPage);
            }
        }
    }

    /// <summary>当前页面骨架。</summary>
    public SectionViewModel CurrentPage
    {
        get => _currentPage;
        private set => SetProperty(ref _currentPage, value);
    }

    private static void EnsurePageLoaded(SectionViewModel page)
    {
        // 首次进入页面时异步加载数据；失败由各 ViewModel 转为可展示消息
        switch (page)
        {
            case RepositoriesViewModel repositoriesPage:
                _ = repositoriesPage.EnsureLoadedAsync();
                break;
            case RulesViewModel rulesPage:
                _ = rulesPage.EnsureLoadedAsync();
                break;
            case LogsViewModel logsPage:
                _ = logsPage.EnsureLoadedAsync();
                break;
            case SettingsViewModel settingsPage:
                _ = settingsPage.EnsureLoadedAsync();
                break;
        }
    }

    private SectionViewModel CreatePage(DesktopSection section) =>
        section switch
        {
            DesktopSection.Repositories when _services.RepositoryOperations is not null =>
                new RepositoriesViewModel(
                    _services.RepositoryOperations,
                    _appState.Language,
                    _services.EditorLauncher,
                    _services.SettingsOperations,
                    _services.CliPathPicker),
            DesktopSection.Rules when _services.RuleOperations is not null =>
                new RulesViewModel(_services.RuleOperations, _appState.Language),
            DesktopSection.Logs when _services.AuditOperations is not null =>
                new LogsViewModel(_services.AuditOperations, _services.ExportPathPicker, _appState.Language),
            DesktopSection.Settings when _services.SettingsOperations is not null =>
                new SettingsViewModel(_appState, _services.SettingsOperations, _appState.Language),
            _ => new SectionViewModel(section, _appState.Language),
        };

    private void ToggleLanguage()
    {
        _appState.ToggleLanguage();

        // 侧边栏切换同样写入桌面设置，保证重启后语言一致；写盘失败不阻断界面切换
        if (_services.SettingsOperations is { } settingsOperations)
        {
            try
            {
                settingsOperations.Save(
                    settingsOperations.Current with
                    {
                        Language = _appState.Language,
                        ThemeMode = _appState.ThemeMode,
                    });
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
            {
                // 设置页在显式保存时会展示错误；此处静默保留当前会话语言
            }
        }
    }

    private void OnAppStatePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AppState.Language))
        {
            RefreshLocalizedText();
        }
        else if (e.PropertyName == nameof(AppState.ThemeMode))
        {
            RequestedThemeVariant = _appState.ThemeMode.ToThemeVariant();
        }
    }

    private void RefreshLocalizedText()
    {
        DesktopLanguage language = _appState.Language;
        WindowTitle = DesktopText.WindowTitle(language);
        Tagline = DesktopText.Tagline(language);
        LanguageToggleText = DesktopText.LanguageToggle(language);

        foreach (NavigationItemViewModel item in NavigationItems)
        {
            item.Relocalize(language);
        }

        // 页面实例会话内缓存：语言切换必须刷新全部缓存页面，避免切回旧页面时残留旧语言文案
        foreach (SectionViewModel page in _pages.Values)
        {
            page.Relocalize(language);
        }
    }
}
