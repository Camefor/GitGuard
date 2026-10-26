using System.Reflection;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using GitGuard.Application.Composition;
using GitGuard.Desktop.Services;
using GitGuard.Desktop.ViewModels;

namespace GitGuard.Desktop;

/// <summary>桌面端应用组合根：加载 XAML 资源，并在桌面生命周期内装配 <see cref="AppState"/> 与主窗口。</summary>
public sealed partial class App : Avalonia.Application
{
    /// <summary>加载应用级 XAML 资源（主题、画刷与全局样式）。</summary>
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    /// <inheritdoc />
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // 主会话仅用于仓库注册表与共享用户数据目录：以应用基目录为工作目录，避免把启动目录当作项目配置来源
            var session = new GitGuardSession(
                new GitGuardSessionOptions { WorkingDirectory = AppContext.BaseDirectory });

            // GGD-0046：桌面设置独立于 config.yml 持久化，启动时恢复语言与主题
            var settingsService = new DesktopSettingsService(
                DesktopSettingsService.ResolvePath(session.UserDataDirectory),
                () => session.Config);
            DesktopSettings settings = settingsService.Current;
            var appState = new AppState(ResolveVersion(), settings.Language, settings.ThemeMode);
            RequestedThemeVariant = appState.ThemeMode.ToThemeVariant();

            desktop.MainWindow = new MainWindow
            {
                DataContext = new MainViewModel(
                    appState,
                    new DesktopServices
                    {
                        RepositoryOperations = new RepositoryOperations(session, settingsService),
                        EditorLauncher = new ShellEditorLauncher(),
                        RuleOperations = new RuleOperations(session, settingsService),
                        AuditOperations = new AuditOperations(session),
                        SettingsOperations = settingsService,
                        ExportPathPicker = new StorageProviderExportPathPicker(() => desktop.MainWindow),
                        CliPathPicker = new StorageProviderCliPathPicker(() => desktop.MainWindow),
                    }),
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>解析本程序集版本（保留 SemVer 主体，剥离 <c>+build</c> 元数据）；不可得时回退 <c>0.0.0</c>。</summary>
    private static string ResolveVersion()
    {
        Assembly assembly = typeof(App).Assembly;
        string? informational = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(informational))
        {
            int metadataIndex = informational.IndexOf('+', StringComparison.Ordinal);
            return metadataIndex > 0 ? informational.Substring(0, metadataIndex) : informational;
        }

        Version? version = assembly.GetName().Version;
        return version is null ? "0.0.0" : version.ToString(3);
    }
}
