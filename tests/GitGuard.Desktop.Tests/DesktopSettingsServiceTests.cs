using System.Text;
using FluentAssertions;
using GitGuard.Application.Configuration;
using GitGuard.Desktop.Localization;
using GitGuard.Desktop.Services;

namespace GitGuard.Desktop.Tests;

/// <summary>
/// 桌面设置持久化测试（GGD-0046 / 11-前端UI设计 §2、§5）：JSON 往返、损坏与版本不匹配回退、
/// 原子写入不留临时文件，以及有效 Gitleaks 设置读取。全部使用临时目录，不触碰真实用户配置。
/// </summary>
public sealed class DesktopSettingsServiceTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "gitguard-desktop-settings",
        Guid.NewGuid().ToString("N"));

    private string SettingsPath => Path.Combine(_directory, DesktopSettingsService.FILE_NAME);

    [Fact]
    public void Constructor_MissingFile_UsesDefaults()
    {
        DesktopSettingsService service = CreateService();

        service.Current.Language.Should().Be(DesktopLanguage.Chinese);
        service.Current.ThemeMode.Should().Be(DesktopThemeMode.System);
        service.Current.Gitleaks.Should().BeNull();
    }

    [Fact]
    public void Save_ThenReload_RoundTripsLanguageThemeAndGitleaksOverride()
    {
        DesktopSettingsService service = CreateService();

        service.Save(
            new DesktopSettings
            {
                Language = DesktopLanguage.English,
                ThemeMode = DesktopThemeMode.Dark,
                Gitleaks = new DesktopGitleaksSettings(true, @"C:\tools\gitleaks.exe"),
            });

        DesktopSettingsService reloaded = CreateService();
        reloaded.Current.Language.Should().Be(DesktopLanguage.English);
        reloaded.Current.ThemeMode.Should().Be(DesktopThemeMode.Dark);
        reloaded.Current.Gitleaks.Should().NotBeNull();
        reloaded.Current.Gitleaks!.Enabled.Should().BeTrue();
        reloaded.Current.Gitleaks.Path.Should().Be(@"C:\tools\gitleaks.exe");
    }

    [Fact]
    public void Save_WritesUtf8JsonWithoutBomAndLeavesNoTemporaryFile()
    {
        DesktopSettingsService service = CreateService();

        service.Save(new DesktopSettings { Language = DesktopLanguage.English });

        byte[] bytes = File.ReadAllBytes(SettingsPath);
        bytes[0].Should().Be((byte)'{', "JSON 文件应以 UTF-8 无 BOM 写入");
        Encoding.UTF8.GetString(bytes).Should().Contain("\"language\": \"English\"");
        Directory.GetFiles(_directory, DesktopSettingsService.FILE_NAME + ".tmp-*").Should().BeEmpty();
    }

    [Fact]
    public void Save_WithoutGitleaks_ClearsOverrideOnReload()
    {
        DesktopSettingsService service = CreateService();
        service.Save(new DesktopSettings { Gitleaks = new DesktopGitleaksSettings(true, "gitleaks") });

        service.Save(new DesktopSettings());
        DesktopSettingsService reloaded = CreateService();

        reloaded.Current.Gitleaks.Should().BeNull();
    }

    [Fact]
    public void Constructor_CorruptJson_FallsBackToDefaults()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(SettingsPath, "{ this is not valid json", new UTF8Encoding(false));

        DesktopSettingsService service = CreateService();

        service.Current.Language.Should().Be(DesktopLanguage.Chinese);
        service.Current.ThemeMode.Should().Be(DesktopThemeMode.System);
    }

    [Fact]
    public void Constructor_UnsupportedSchemaVersion_FallsBackToDefaults()
    {
        Directory.CreateDirectory(_directory);
        string json =
            """
            {
              "version": 99,
              "language": "English",
              "theme": "Dark",
              "hasGitleaks": true,
              "useGitleaks": true,
              "gitleaksPath": "gitleaks"
            }
            """;
        File.WriteAllText(SettingsPath, json, new UTF8Encoding(false));

        DesktopSettingsService service = CreateService();

        service.Current.Language.Should().Be(DesktopLanguage.Chinese);
        service.Current.Gitleaks.Should().BeNull();
    }

    [Fact]
    public async Task LoadGitleaksAsync_ReturnsEffectiveConfigSnapshot()
    {
        var service = new DesktopSettingsService(
            SettingsPath,
            () => GitGuardConfig.Default with
            {
                Secrets = SecretRulesSettings.Default with
                {
                    UseGitleaks = true,
                    GitleaksPath = "/opt/gitleaks",
                },
            });

        DesktopGitleaksSettings effective = await service.LoadGitleaksAsync(
            TestContext.Current.CancellationToken);

        effective.Enabled.Should().BeTrue();
        effective.Path.Should().Be("/opt/gitleaks");
    }

    [Fact]
    public void ResolvePath_CombinesUserDataDirectoryAndFileName()
    {
        string resolved = DesktopSettingsService.ResolvePath(_directory);

        resolved.Should().Be(Path.Combine(_directory, DesktopSettingsService.FILE_NAME));
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, recursive: true);
            }
        }
        catch (IOException)
        {
            // 临时目录清理失败不影响测试结论
        }
    }

    private DesktopSettingsService CreateService() =>
        new DesktopSettingsService(SettingsPath, () => GitGuardConfig.Default);
}
