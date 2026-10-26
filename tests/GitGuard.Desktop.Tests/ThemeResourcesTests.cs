using System.Globalization;
using System.Xml.Linq;
using FluentAssertions;

namespace GitGuard.Desktop.Tests;

/// <summary>
/// 桌面端主题回归测试（GGD-0057）：App.axaml 的 Light / Dark 语义画刷必须成对定义，
/// 关键文字与背景满足 WCAG AA 4.5:1，且视图不得重新引入硬编码前景 / 背景色。
/// 测试只解析 XAML 文本，不启动 Avalonia 运行时。
/// </summary>
public sealed class ThemeResourcesTests
{
    private static readonly XNamespace _avalonia = "https://github.com/avaloniaui";
    private static readonly XNamespace _xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
    private static readonly string _repositoryRoot = ResolveRepositoryRoot();

    /// <summary>Light / Dark 主题必须覆盖同一组 GitGuard 语义画刷。</summary>
    [Fact]
    public void ThemeDictionaries_DefineSameGitGuardBrushes()
    {
        Dictionary<string, string> light = LoadTheme("Light");
        Dictionary<string, string> dark = LoadTheme("Dark");

        light.Keys.Should().BeEquivalentTo(dark.Keys, "Light / Dark 必须成对定义主题画刷");
        light.Should().ContainKeys(
            "GitGuardWarningSurfaceBrush",
            "GitGuardWarningSurfaceHoverBrush",
            "GitGuardWarningBorderBrush",
            "GitGuardWarningTextBrush",
            "GitGuardDangerBrush",
            "GitGuardDangerHoverBrush",
            "GitGuardDangerButtonTextBrush",
            "GitGuardDangerTextBrush",
            "GitGuardDangerSurfaceBrush",
            "GitGuardSuccessTextBrush",
            "GitGuardSuccessBadgeSurfaceBrush",
            "GitGuardSuccessBadgeTextBrush",
            "GitGuardMutedBadgeSurfaceBrush",
            "GitGuardMutedBadgeTextBrush");
    }

    /// <summary>关键文字 / 背景组合在两种主题下均满足 WCAG AA 对比度。</summary>
    [Fact]
    public void KeyTextBrushes_MeetWcagAaContrast()
    {
        (string Foreground, string Background)[] pairs =
        [
            ("GitGuardPrimaryTextBrush", "GitGuardContentBrush"),
            ("GitGuardSecondaryTextBrush", "GitGuardContentBrush"),
            ("GitGuardPrimaryTextBrush", "GitGuardSurfaceBrush"),
            ("GitGuardSecondaryTextBrush", "GitGuardSurfaceBrush"),
            ("GitGuardSidebarTextBrush", "GitGuardSidebarBrush"),
            ("GitGuardSidebarMutedBrush", "GitGuardSidebarBrush"),
            ("GitGuardWarningTextBrush", "GitGuardWarningSurfaceBrush"),
            ("GitGuardWarningTextBrush", "GitGuardWarningSurfaceHoverBrush"),
            ("GitGuardSuccessTextBrush", "GitGuardContentBrush"),
            ("GitGuardDangerTextBrush", "GitGuardContentBrush"),
            ("GitGuardDangerTextBrush", "GitGuardSurfaceBrush"),
            ("GitGuardDangerTextBrush", "GitGuardDangerSurfaceBrush"),
            ("GitGuardDangerButtonTextBrush", "GitGuardDangerBrush"),
            ("GitGuardDangerButtonTextBrush", "GitGuardDangerHoverBrush"),
            ("GitGuardSuccessBadgeTextBrush", "GitGuardSuccessBadgeSurfaceBrush"),
            ("GitGuardMutedBadgeTextBrush", "GitGuardMutedBadgeSurfaceBrush"),
        ];

        foreach (string theme in new[] { "Light", "Dark" })
        {
            Dictionary<string, string> palette = LoadTheme(theme);
            foreach ((string foreground, string background) in pairs)
            {
                double contrast = CalculateContrast(palette[foreground], palette[background]);
                contrast.Should().BeGreaterThanOrEqualTo(
                    4.5,
                    $"{theme} 主题 {foreground} 与 {background} 的对比度为 {contrast:F2}:1");
            }
        }
    }

    /// <summary>视图与窗口壳不得硬编码前景 / 背景色，必须使用主题语义画刷。</summary>
    [Fact]
    public void Views_DoNotHardCodeForegroundOrBackgroundColors()
    {
        string desktopRoot = Path.Combine(_repositoryRoot, "src", "GitGuard.Desktop");
        IEnumerable<string> files = Directory.EnumerateFiles(Path.Combine(desktopRoot, "Views"), "*.axaml")
            .Append(Path.Combine(desktopRoot, "MainWindow.axaml"));

        var offenders = new List<string>();
        foreach (string file in files)
        {
            var document = XDocument.Load(file);
            offenders.AddRange(
                document.Descendants()
                    .SelectMany(element => element.Attributes())
                    .Where(attribute => attribute.Name.LocalName is "Foreground" or "Background" or "BorderBrush")
                    .Where(attribute => attribute.Value.StartsWith('#')
                        || string.Equals(attribute.Value, "White", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(attribute.Value, "Black", StringComparison.OrdinalIgnoreCase))
                    .Select(attribute => $"{Path.GetFileName(file)}: {attribute.Name.LocalName}={attribute.Value}"));
        }

        offenders.Should().BeEmpty("视图颜色必须来自 App.axaml 的 Light / Dark 语义画刷");
    }

    private static Dictionary<string, string> LoadTheme(string themeKey)
    {
        var document = XDocument.Load(Path.Combine(_repositoryRoot, "src", "GitGuard.Desktop", "App.axaml"));
        XElement dictionary = document.Descendants(_avalonia + "ResourceDictionary")
            .Single(element => (string?)element.Attribute(_xaml + "Key") == themeKey);

        return dictionary.Elements(_avalonia + "SolidColorBrush")
            .ToDictionary(
                element => RequiredAttribute(element, _xaml + "Key"),
                element => RequiredAttribute(element, "Color"),
                StringComparer.Ordinal);
    }

    private static string RequiredAttribute(XElement element, XName name) =>
        element.Attribute(name)?.Value
        ?? throw new InvalidOperationException($"主题画刷缺少 {name} 属性");

    private static double CalculateContrast(string foreground, string background)
    {
        double foregroundLuminance = CalculateRelativeLuminance(foreground);
        double backgroundLuminance = CalculateRelativeLuminance(background);
        double lighter = Math.Max(foregroundLuminance, backgroundLuminance);
        double darker = Math.Min(foregroundLuminance, backgroundLuminance);
        return (lighter + 0.05) / (darker + 0.05);
    }

    private static double CalculateRelativeLuminance(string hexColor)
    {
        string value = hexColor.TrimStart('#');
        if (value.Length != 6)
        {
            throw new InvalidOperationException($"不支持的画刷颜色：{hexColor}");
        }

        double red = ToLinearChannel(value, 0);
        double green = ToLinearChannel(value, 2);
        double blue = ToLinearChannel(value, 4);
        return (0.2126 * red) + (0.7152 * green) + (0.0722 * blue);
    }

    private static double ToLinearChannel(string hexColor, int offset)
    {
        double channel = int.Parse(hexColor.AsSpan(offset, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture) / 255d;
        return channel <= 0.04045 ? channel / 12.92 : Math.Pow((channel + 0.055) / 1.055, 2.4);
    }

    private static string ResolveRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (true)
        {
            if (File.Exists(Path.Combine(directory.FullName, "GitGuard.sln")))
            {
                return directory.FullName;
            }

            if (directory.Parent is not { } parent)
            {
                break;
            }

            directory = parent;
        }

        throw new InvalidOperationException("未能从测试输出目录定位 GitGuard.sln");
    }
}
