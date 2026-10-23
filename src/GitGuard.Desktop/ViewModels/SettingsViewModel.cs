using CommunityToolkit.Mvvm.Input;
using GitGuard.Desktop.Localization;
using GitGuard.Desktop.Services;

namespace GitGuard.Desktop.ViewModels;

/// <summary>
/// 全局设置 ViewModel（GGD-0046 / <c>11-前端UI设计</c> §2、§5、§7）：语言与主题即时生效并写入
/// <c>desktop-settings.json</c>；Gitleaks 路径以桌面覆盖形式保存，不修改用户 <c>config.yml</c>。
/// 覆盖仅影响桌面端扫描与规则页展示，CLI 仍完全遵循配置文件。
/// </summary>
public sealed class SettingsViewModel : SectionViewModel
{
    private readonly AppState _appState;
    private readonly ISettingsOperations _settingsOperations;

    private DesktopLanguage _language;
    private SettingsLanguageOption _selectedLanguage;
    private SettingsThemeOption _selectedTheme;
    private bool _gitleaksEnabled;
    private string _gitleaksPath = string.Empty;
    private bool _hasOverride;
    private bool _isLoading;
    private bool _isSaving;
    private bool _hasLoaded;
    private string? _message;
    private bool _isMessageError;

    /// <summary>初始化设置页 ViewModel。</summary>
    /// <param name="appState">应用级状态（语言与主题的唯一真源）。</param>
    /// <param name="settingsOperations">设置持久化服务。</param>
    /// <param name="language">初始界面语言。</param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="appState"/> 或 <paramref name="settingsOperations"/> 为 <see langword="null"/>。
    /// </exception>
    public SettingsViewModel(
        AppState appState,
        ISettingsOperations settingsOperations,
        DesktopLanguage language)
        : base(DesktopSection.Settings, language)
    {
        ArgumentNullException.ThrowIfNull(appState);
        ArgumentNullException.ThrowIfNull(settingsOperations);

        _appState = appState;
        _settingsOperations = settingsOperations;
        _language = language;

        // 语言名称使用原生写法，不随界面语言变化
        LanguageOptions = new[]
        {
            new SettingsLanguageOption(DesktopLanguage.Chinese, "简体中文"),
            new SettingsLanguageOption(DesktopLanguage.English, "English"),
        };
        ThemeOptions = new[]
        {
            new SettingsThemeOption(DesktopThemeMode.System, language),
            new SettingsThemeOption(DesktopThemeMode.Light, language),
            new SettingsThemeOption(DesktopThemeMode.Dark, language),
        };
        _selectedLanguage = FindLanguageOption(appState.Language);
        _selectedTheme = FindThemeOption(appState.ThemeMode);

        SaveGitleaksCommand = new RelayCommand(SaveGitleaks);
        ResetGitleaksCommand = new AsyncRelayCommand(ResetGitleaksAsync);
    }

    /// <summary>界面语言选项（中文 / English）。</summary>
    public IReadOnlyList<SettingsLanguageOption> LanguageOptions { get; }

    /// <summary>主题选项（跟随系统 / 浅色 / 深色）。</summary>
    public IReadOnlyList<SettingsThemeOption> ThemeOptions { get; }

    /// <summary>保存 Gitleaks 覆盖命令。</summary>
    public IRelayCommand SaveGitleaksCommand { get; }

    /// <summary>清除桌面 Gitleaks 覆盖并恢复配置文件值命令。</summary>
    public IAsyncRelayCommand ResetGitleaksCommand { get; }

    /// <summary>当前界面语言；修改后立即应用到 <see cref="AppState"/> 并持久化。</summary>
    public SettingsLanguageOption SelectedLanguage
    {
        get => _selectedLanguage;
        set
        {
            if (value is null || !SetProperty(ref _selectedLanguage, value))
            {
                return;
            }

            _appState.Language = value.Value;
            PersistAppearance();
        }
    }

    /// <summary>当前主题模式；修改后立即应用到 <see cref="AppState"/> 并持久化。</summary>
    public SettingsThemeOption SelectedTheme
    {
        get => _selectedTheme;
        set
        {
            if (value is null || !SetProperty(ref _selectedTheme, value))
            {
                return;
            }

            _appState.ThemeMode = value.Value;
            PersistAppearance();
        }
    }

    /// <summary>Gitleaks 适配器在桌面端是否启用。</summary>
    public bool GitleaksEnabled
    {
        get => _gitleaksEnabled;
        set => SetProperty(ref _gitleaksEnabled, value);
    }

    /// <summary>Gitleaks 可执行文件路径；空表示从 PATH 查找。</summary>
    public string GitleaksPath
    {
        get => _gitleaksPath;
        set => SetProperty(ref _gitleaksPath, value ?? string.Empty);
    }

    /// <summary>是否存在桌面端 Gitleaks 覆盖。</summary>
    public bool HasOverride
    {
        get => _hasOverride;
        private set
        {
            if (SetProperty(ref _hasOverride, value))
            {
                OnPropertyChanged(nameof(OverrideStatusText));
                OnPropertyChanged(nameof(CanResetGitleaks));
            }
        }
    }

    /// <summary>是否正在读取有效配置。</summary>
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

    /// <summary>是否正在保存 Gitleaks 覆盖。</summary>
    public bool IsSaving
    {
        get => _isSaving;
        private set
        {
            if (SetProperty(ref _isSaving, value))
            {
                NotifyInteractionState();
            }
        }
    }

    /// <summary>页面级消息（保存结果或加载失败）。</summary>
    public string? Message
    {
        get => _message;
        private set
        {
            if (SetProperty(ref _message, value))
            {
                OnPropertyChanged(nameof(HasMessage));
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

    /// <summary>设置控件是否可交互。</summary>
    public bool CanInteract => !IsLoading && !IsSaving;

    /// <summary>是否可清除桌面覆盖。</summary>
    public bool CanResetGitleaks => _hasOverride && CanInteract;

    /// <summary>加载提示。</summary>
    public string LoadingText => DesktopText.SettingsLoading(_language);

    /// <summary>语言区块标题。</summary>
    public string LanguageLabel => DesktopText.SettingsLanguageLabel(_language);

    /// <summary>语言区块说明。</summary>
    public string LanguageHint => DesktopText.SettingsLanguageHint(_language);

    /// <summary>主题区块标题。</summary>
    public string ThemeLabel => DesktopText.SettingsThemeLabel(_language);

    /// <summary>主题区块说明。</summary>
    public string ThemeHint => DesktopText.SettingsThemeHint(_language);

    /// <summary>Gitleaks 区块标题。</summary>
    public string GitleaksTitle => DesktopText.SettingsGitleaksTitle(_language);

    /// <summary>Gitleaks 启用开关标签。</summary>
    public string GitleaksEnabledLabel => DesktopText.SettingsGitleaksEnabledLabel(_language);

    /// <summary>Gitleaks 启用开关说明。</summary>
    public string GitleaksEnabledHint => DesktopText.SettingsGitleaksEnabledHint(_language);

    /// <summary>Gitleaks 路径字段标签。</summary>
    public string GitleaksPathLabel => DesktopText.SettingsGitleaksPathLabel(_language);

    /// <summary>Gitleaks 路径输入水印。</summary>
    public string GitleaksPathWatermark => DesktopText.SettingsGitleaksPathWatermark(_language);

    /// <summary>Gitleaks 路径说明。</summary>
    public string GitleaksPathHint => DesktopText.SettingsGitleaksPathHint(_language);

    /// <summary>保存 Gitleaks 按钮文案。</summary>
    public string SaveGitleaksText => DesktopText.SettingsSaveGitleaksButton(_language);

    /// <summary>清除覆盖按钮文案。</summary>
    public string ResetGitleaksText => DesktopText.SettingsResetGitleaksButton(_language);

    /// <summary>当前覆盖状态说明。</summary>
    public string OverrideStatusText => DesktopText.SettingsOverrideStatus(_hasOverride, _language);

    /// <summary>首次进入设置页时读取有效 Gitleaks 设置（只执行一次）。</summary>
    /// <param name="cancellationToken">取消标记。</param>
    /// <returns>加载完成的任务。</returns>
    public async Task EnsureLoadedAsync(CancellationToken cancellationToken = default)
    {
        if (_hasLoaded || IsLoading)
        {
            return;
        }

        IsLoading = true;
        Message = null;
        IsMessageError = false;

        try
        {
            DesktopGitleaksSettings effective = await _settingsOperations
                .LoadGitleaksAsync(cancellationToken)
                .ConfigureAwait(true);
            DesktopGitleaksSettings? currentOverride = _settingsOperations.Current.Gitleaks;

            GitleaksEnabled = currentOverride?.Enabled ?? effective.Enabled;
            GitleaksPath = currentOverride is not null
                ? currentOverride.Path ?? string.Empty
                : effective.Path ?? string.Empty;
            HasOverride = currentOverride is not null;
            _hasLoaded = true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // 页面离开或调用方取消：保持初始状态
        }
        catch (Exception ex)
        {
            IsMessageError = true;
            Message = DesktopText.SettingsLoadFailed(_language, ex.Message);
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <inheritdoc />
    public override void Relocalize(DesktopLanguage language)
    {
        base.Relocalize(language);
        _language = language;

        foreach (SettingsThemeOption option in ThemeOptions)
        {
            option.Relocalize(language);
        }

        // 语言可能由侧边栏切换：同步下拉选择但不触发再次保存
        _selectedLanguage = FindLanguageOption(_appState.Language);
        OnPropertyChanged(nameof(SelectedLanguage));

        OnPropertyChanged(nameof(LoadingText));
        OnPropertyChanged(nameof(LanguageLabel));
        OnPropertyChanged(nameof(LanguageHint));
        OnPropertyChanged(nameof(ThemeLabel));
        OnPropertyChanged(nameof(ThemeHint));
        OnPropertyChanged(nameof(GitleaksTitle));
        OnPropertyChanged(nameof(GitleaksEnabledLabel));
        OnPropertyChanged(nameof(GitleaksEnabledHint));
        OnPropertyChanged(nameof(GitleaksPathLabel));
        OnPropertyChanged(nameof(GitleaksPathWatermark));
        OnPropertyChanged(nameof(GitleaksPathHint));
        OnPropertyChanged(nameof(SaveGitleaksText));
        OnPropertyChanged(nameof(ResetGitleaksText));
        OnPropertyChanged(nameof(OverrideStatusText));
    }

    private static string? NormalizePath(string path) =>
        string.IsNullOrWhiteSpace(path) ? null : path.Trim();

    private void SaveGitleaks()
    {
        if (IsLoading || IsSaving)
        {
            return;
        }

        IsSaving = true;
        Message = null;
        IsMessageError = false;
        try
        {
            _settingsOperations.Save(
                _settingsOperations.Current with
                {
                    Language = _appState.Language,
                    ThemeMode = _appState.ThemeMode,
                    Gitleaks = new DesktopGitleaksSettings(GitleaksEnabled, NormalizePath(GitleaksPath)),
                });
            HasOverride = true;
            Message = DesktopText.SettingsGitleaksSaved(_language);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            IsMessageError = true;
            Message = DesktopText.SettingsSaveFailed(_language, ex.Message);
        }
        finally
        {
            IsSaving = false;
        }
    }

    private async Task ResetGitleaksAsync()
    {
        if (IsLoading || IsSaving)
        {
            return;
        }

        IsSaving = true;
        Message = null;
        IsMessageError = false;
        try
        {
            _settingsOperations.Save(
                _settingsOperations.Current with
                {
                    Language = _appState.Language,
                    ThemeMode = _appState.ThemeMode,
                    Gitleaks = null,
                });

            DesktopGitleaksSettings effective = await _settingsOperations
                .LoadGitleaksAsync()
                .ConfigureAwait(true);
            GitleaksEnabled = effective.Enabled;
            GitleaksPath = effective.Path ?? string.Empty;
            HasOverride = false;
            Message = DesktopText.SettingsOverrideCleared(_language);
        }
        catch (Exception ex)
        {
            IsMessageError = true;
            Message = DesktopText.SettingsSaveFailed(_language, ex.Message);
        }
        finally
        {
            IsSaving = false;
        }
    }

    private void PersistAppearance()
    {
        try
        {
            _settingsOperations.Save(
                _settingsOperations.Current with
                {
                    Language = _appState.Language,
                    ThemeMode = _appState.ThemeMode,
                });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            IsMessageError = true;
            Message = DesktopText.SettingsSaveFailed(_language, ex.Message);
        }
    }

    private SettingsLanguageOption FindLanguageOption(DesktopLanguage language) =>
        LanguageOptions.First(option => option.Value == language);

    private SettingsThemeOption FindThemeOption(DesktopThemeMode mode) =>
        ThemeOptions.First(option => option.Value == mode);

    private void NotifyInteractionState()
    {
        OnPropertyChanged(nameof(CanInteract));
        OnPropertyChanged(nameof(CanResetGitleaks));
    }
}
