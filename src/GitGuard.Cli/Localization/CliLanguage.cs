using System.Globalization;

namespace GitGuard.Cli.Localization;

/// <summary>CLI 输出语言（SRS <c>NFR-U-002</c> / <c>模块设计/Cli.md</c> §7）：默认中文，可切换英文。</summary>
internal enum CliLanguage
{
    /// <summary>中文（默认）。</summary>
    Chinese,

    /// <summary>English.</summary>
    English,
}

/// <summary>
/// 输出语言解析：<c>--language</c> 优先于 <c>GITGUARD_LANG</c>，均未提供或无法识别时回退中文。
/// 环境变量接受 <c>zh</c> / <c>zh-CN</c> / <c>en</c> / <c>en-US</c>（大小写不敏感）；命令行取值由
/// <c>System.CommandLine</c> 在解析期限定为 <c>zh-CN</c> / <c>en-US</c>。
/// </summary>
internal static class CliLanguageResolver
{
    /// <summary><c>--language</c> 选项名。</summary>
    internal const string LANGUAGE_OPTION = "--language";

    /// <summary>中文取值。</summary>
    internal const string ZH_CN = "zh-CN";

    /// <summary>英文取值。</summary>
    internal const string EN_US = "en-US";

    private const string LANGUAGE_OPTION_PREFIX = "--language=";
    private const string ZH_LANGUAGE = "zh";
    private const string ZH_HANS = "zh-Hans";
    private const string EN_LANGUAGE = "en";
    private const string EN_GB = "en-GB";

    /// <summary>解析本次调用的输出语言。</summary>
    /// <param name="arguments">命令行参数（不含可执行文件名）。</param>
    /// <param name="environmentValue"><c>GITGUARD_LANG</c> 取值；未设置为 <see langword="null"/>。</param>
    /// <returns>输出语言（无法识别时回退中文）。</returns>
    /// <exception cref="ArgumentNullException"><paramref name="arguments"/> 为 <see langword="null"/>。</exception>
    internal static CliLanguage Resolve(IReadOnlyList<string> arguments, string? environmentValue)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        CliLanguage fallback = Parse(environmentValue) ?? CliLanguage.Chinese;
        for (int index = arguments.Count - 1; index >= 0; index--)
        {
            string argument = arguments[index];
            if (argument.StartsWith(LANGUAGE_OPTION_PREFIX, StringComparison.Ordinal))
            {
                return Parse(argument[LANGUAGE_OPTION_PREFIX.Length..]) ?? fallback;
            }

            if (string.Equals(argument, LANGUAGE_OPTION, StringComparison.Ordinal))
            {
                // 缺值时由解析器报用法错误；此处仅为帮助文案选择语言
                return index + 1 < arguments.Count ? Parse(arguments[index + 1]) ?? fallback : fallback;
            }
        }

        return fallback;
    }

    /// <summary>解析语言取值（大小写不敏感）；无法识别时返回 <see langword="null"/>。</summary>
    /// <param name="value">语言标识（如 <c>zh-CN</c> / <c>en</c>）。</param>
    /// <returns>输出语言；无法识别为 <see langword="null"/>。</returns>
    internal static CliLanguage? Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        string normalized = value.Trim();
        if (string.Equals(normalized, ZH_LANGUAGE, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(normalized, ZH_CN, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(normalized, ZH_HANS, StringComparison.OrdinalIgnoreCase))
        {
            return CliLanguage.Chinese;
        }

        if (string.Equals(normalized, EN_LANGUAGE, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(normalized, EN_US, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(normalized, EN_GB, StringComparison.OrdinalIgnoreCase))
        {
            return CliLanguage.English;
        }

        return null;
    }
}

/// <summary>输出语言与 .NET 区域设置的映射。</summary>
internal static class CliLanguageExtensions
{
    private const string ZH_CN = "zh-CN";
    private const string EN_US = "en-US";

    /// <summary>取语言对应的 <see cref="CultureInfo"/>（框架卫星资源按该区域选择）。</summary>
    /// <param name="language">输出语言。</param>
    /// <returns>只读的 <see cref="CultureInfo"/>。</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="language"/> 不是已登记取值。</exception>
    internal static CultureInfo ToCulture(this CliLanguage language) => language switch
    {
        CliLanguage.Chinese => CultureInfo.GetCultureInfo(ZH_CN),
        CliLanguage.English => CultureInfo.GetCultureInfo(EN_US),
        _ => throw new ArgumentOutOfRangeException(nameof(language), language, "未知的输出语言"),
    };
}
