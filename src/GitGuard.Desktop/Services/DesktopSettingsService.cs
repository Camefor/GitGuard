using System.Text.Json;
using System.Text.Json.Serialization;
using GitGuard.Application.Configuration;
using GitGuard.Desktop.Localization;

namespace GitGuard.Desktop.Services;

/// <summary>
/// <see cref="ISettingsOperations"/> 的默认实现（GGD-0046）：把设置写入用户数据目录的
/// <c>desktop-settings.json</c>（UTF-8 无 BOM、原子替换），并读取配置文件中的有效 Gitleaks 设置。
/// 文件缺失或损坏时回退默认设置，不阻断桌面端启动。
/// </summary>
public sealed class DesktopSettingsService : ISettingsOperations
{
    /// <summary>设置文件名（位于 GitGuard 用户数据目录）。</summary>
    public const string FILE_NAME = "desktop-settings.json";

    /// <summary>当前设置文件 Schema 版本。</summary>
    public const int SUPPORTED_VERSION = 1;

    private readonly string _filePath;
    private readonly Func<GitGuardConfig> _configProvider;

    /// <summary>初始化设置服务并加载已有设置。</summary>
    /// <param name="filePath">设置文件绝对路径（通常为 <c>&lt;data&gt;/desktop-settings.json</c>）。</param>
    /// <param name="configProvider">读取配置文件合并结果（用于展示有效 Gitleaks 设置）。</param>
    /// <exception cref="ArgumentException"><paramref name="filePath"/> 为空或全空白。</exception>
    /// <exception cref="ArgumentNullException"><paramref name="configProvider"/> 为 <see langword="null"/>。</exception>
    public DesktopSettingsService(string filePath, Func<GitGuardConfig> configProvider)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentNullException.ThrowIfNull(configProvider);

        _filePath = Path.GetFullPath(filePath);
        _configProvider = configProvider;
        Current = Load(_filePath);
    }

    /// <inheritdoc />
    public DesktopSettings Current { get; private set; }

    /// <summary>按用户数据目录解析默认设置文件路径。</summary>
    /// <param name="userDataDirectory">GitGuard 用户数据目录。</param>
    /// <returns><c>&lt;data&gt;/desktop-settings.json</c>。</returns>
    /// <exception cref="ArgumentException"><paramref name="userDataDirectory"/> 为空或全空白。</exception>
    public static string ResolvePath(string userDataDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userDataDirectory);
        return Path.Combine(userDataDirectory, FILE_NAME);
    }

    /// <inheritdoc />
    public Task<DesktopGitleaksSettings> LoadGitleaksAsync(CancellationToken cancellationToken = default) =>
        Task.Run(
            () =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                SecretRulesSettings secrets = _configProvider().Secrets;
                return new DesktopGitleaksSettings(secrets.UseGitleaks, secrets.GitleaksPath);
            },
            cancellationToken);

    /// <inheritdoc />
    public void Save(DesktopSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        string? directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        string temporaryPath = _filePath + ".tmp-" + Guid.CreateVersion7().ToString("N");
        try
        {
            using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, ToDocument(settings), DesktopSettingsJsonContext.Default.DesktopSettingsDocument);
            }

            File.Move(temporaryPath, _filePath, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            TryDelete(temporaryPath);
            throw;
        }

        Current = settings;
    }

    private static DesktopSettings Load(string filePath)
    {
        if (!File.Exists(filePath))
        {
            return new DesktopSettings();
        }

        try
        {
            using FileStream stream = File.OpenRead(filePath);
            DesktopSettingsDocument? document = JsonSerializer.Deserialize(
                stream,
                DesktopSettingsJsonContext.Default.DesktopSettingsDocument);
            return document is { Version: SUPPORTED_VERSION } ? FromDocument(document) : new DesktopSettings();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            // 设置文件损坏不应阻断启动：回退默认值，下次保存时覆盖
            return new DesktopSettings();
        }
    }

    private static DesktopSettings FromDocument(DesktopSettingsDocument document)
    {
        DesktopLanguage language = Enum.TryParse(document.Language, ignoreCase: true, out DesktopLanguage parsedLanguage)
            && Enum.IsDefined(parsedLanguage)
                ? parsedLanguage
                : DesktopLanguage.Chinese;

        DesktopThemeMode themeMode = Enum.TryParse(document.Theme, ignoreCase: true, out DesktopThemeMode parsedTheme)
            && Enum.IsDefined(parsedTheme)
                ? parsedTheme
                : DesktopThemeMode.System;

        DesktopGitleaksSettings? gitleaks = document.HasGitleaks
            ? new DesktopGitleaksSettings(document.UseGitleaks, NormalizePath(document.GitleaksPath))
            : null;

        return new DesktopSettings
        {
            Language = language,
            ThemeMode = themeMode,
            Gitleaks = gitleaks,
        };
    }

    private static DesktopSettingsDocument ToDocument(DesktopSettings settings) => new DesktopSettingsDocument
    {
        Version = SUPPORTED_VERSION,
        Language = settings.Language.ToString(),
        Theme = settings.ThemeMode.ToString(),
        HasGitleaks = settings.Gitleaks is not null,
        UseGitleaks = settings.Gitleaks?.Enabled ?? false,
        GitleaksPath = NormalizePath(settings.Gitleaks?.Path),
    };

    private static string? NormalizePath(string? path) =>
        string.IsNullOrWhiteSpace(path) ? null : path.Trim();

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 临时文件清理失败不改变保存失败语义
        }
    }

    /// <summary>设置文件 DTO（显式字符串枚举，避免 AOT 反射式枚举转换）。</summary>
    internal sealed class DesktopSettingsDocument
    {
        /// <summary>Schema 版本。</summary>
        public int Version { get; set; }

        /// <summary>界面语言（<see cref="DesktopLanguage"/> 名称）。</summary>
        public string? Language { get; set; }

        /// <summary>主题模式（<see cref="DesktopThemeMode"/> 名称）。</summary>
        public string? Theme { get; set; }

        /// <summary>是否包含桌面端 Gitleaks 覆盖。</summary>
        public bool HasGitleaks { get; set; }

        /// <summary>Gitleaks 适配器开关。</summary>
        public bool UseGitleaks { get; set; }

        /// <summary>Gitleaks 可执行文件路径；空表示 PATH 查找。</summary>
        public string? GitleaksPath { get; set; }
    }
}

/// <summary>设置文件的 System.Text.Json 源生成上下文（Native AOT 安全）。</summary>
[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(DesktopSettingsService.DesktopSettingsDocument))]
internal sealed partial class DesktopSettingsJsonContext : JsonSerializerContext
{
}
