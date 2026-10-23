using System.Collections.ObjectModel;
using System.ComponentModel;
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
    private readonly Dictionary<DesktopSection, SectionViewModel> _pages;
    private NavigationItemViewModel? _selectedNavigation;
    private SectionViewModel _currentPage;
    private string _windowTitle;
    private string _tagline;
    private string _languageToggleText;
    private string _versionText;

    /// <summary>初始化主窗口 ViewModel。</summary>
    /// <param name="appState">应用级状态。</param>
    /// <param name="repositoryOperations">
    /// 仓库页操作服务；<see langword="null"/> 时仓库页保持骨架（ViewModel 单测与设计预览不访问真实数据）。
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="appState"/> 为 <see langword="null"/>。</exception>
    public MainViewModel(AppState appState, IRepositoryOperations? repositoryOperations = null)
    {
        _appState = appState ?? throw new ArgumentNullException(nameof(appState));

        NavigationItems = new ObservableCollection<NavigationItemViewModel>(
            Enum.GetValues<DesktopSection>().Select(section => new NavigationItemViewModel(section, appState.Language)));
        _pages = Enum.GetValues<DesktopSection>().ToDictionary(
            section => section,
            section => CreatePage(section, appState.Language, repositoryOperations));

        _windowTitle = DesktopText.WindowTitle(appState.Language);
        _tagline = DesktopText.Tagline(appState.Language);
        _languageToggleText = DesktopText.LanguageToggle(appState.Language);
        _versionText = DesktopText.VersionText(appState.Version);
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

    private static SectionViewModel CreatePage(
        DesktopSection section,
        DesktopLanguage language,
        IRepositoryOperations? repositoryOperations) =>
        section == DesktopSection.Repositories && repositoryOperations is not null
            ? new RepositoriesViewModel(repositoryOperations, language)
            : new SectionViewModel(section, language);

    private static void EnsurePageLoaded(SectionViewModel page)
    {
        if (page is RepositoriesViewModel repositoriesPage)
        {
            // 首次进入仓库页时异步加载列表；失败由 ViewModel 转为可展示消息
            _ = repositoriesPage.EnsureLoadedAsync();
        }
    }

    private void ToggleLanguage() => _appState.ToggleLanguage();

    private void OnAppStatePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AppState.Language))
        {
            RefreshLocalizedText();
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
