using System.Globalization;
using System.Text.RegularExpressions;
using GitGuard.Core;

namespace GitGuard.Detection.Adapters;

/// <summary>
/// Gitleaks 启动期探测（FR-090 / ADR-0003）：定位可执行文件并执行 <c>gitleaks version</c>。
/// 缺失或探测失败 → 适配器禁用但不阻断扫描；版本不在已验证的 8.x 区间 → 仍启用并给出警告。
/// </summary>
public static class GitleaksProbe
{
    /// <summary>已验证的 Gitleaks 主版本区间（ADR-0003「版本锁定」）。</summary>
    public const int SUPPORTED_MAJOR_VERSION = 8;

    /// <summary>版本探测超时（启动期短探测，避免拖慢扫描）。</summary>
    public static readonly TimeSpan DEFAULT_TIMEOUT = TimeSpan.FromSeconds(5);

    private static readonly Regex _versionPattern = new Regex(
        @"(?<!\d)(\d+)\.(\d+)\.(\d+)",
        RegexOptions.CultureInvariant);

    private static readonly string[] _versionArguments = new[] { "version" };

    /// <summary>探测 Gitleaks 可用性。</summary>
    /// <param name="configuredPath"><c>rules.secrets.gitleaks_path</c>；空表示 PATH 查找。</param>
    /// <param name="processRunner">子进程执行器（测试注入替身）。</param>
    /// <param name="timeout">版本命令超时；<see langword="null"/> 取 <see cref="DEFAULT_TIMEOUT"/>。</param>
    /// <returns>探测结果；任何失败均以不可用 + 警告返回（不抛出）。</returns>
    /// <exception cref="ArgumentNullException"><paramref name="processRunner"/> 为 <see langword="null"/>。</exception>
    public static GitleaksProbeResult Probe(
        string? configuredPath,
        IGitleaksProcessRunner processRunner,
        TimeSpan? timeout = null)
    {
        ArgumentNullException.ThrowIfNull(processRunner);

        if (!GitleaksExecutableLocator.TryLocate(configuredPath, out string? executablePath, out string? failureReason))
        {
            return GitleaksProbeResult.Unavailable($"Gitleaks 适配器已禁用：{failureReason}");
        }

        try
        {
            GitleaksProcessResult result = processRunner
                .RunAsync(executablePath!, _versionArguments, timeout ?? DEFAULT_TIMEOUT)
                .GetAwaiter()
                .GetResult();

            if (result.ExitCode != 0)
            {
                return GitleaksProbeResult.Unavailable(
                    $"Gitleaks 适配器已禁用：版本探测命令退出码 {result.ExitCode}（可执行文件可能损坏或不兼容）。");
            }

            string versionLine = FirstNonEmptyLine(result.StandardOutput)
                ?? FirstNonEmptyLine(result.StandardError)
                ?? string.Empty;

            Match match = _versionPattern.Match(versionLine);
            if (!match.Success)
            {
                return GitleaksProbeResult.Available(
                    executablePath!,
                    versionLine,
                    $"无法识别 Gitleaks 版本号（原始输出：{versionLine}），适配器继续启用但行为可能不兼容。");
            }

            int major = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
            string? warning = major == SUPPORTED_MAJOR_VERSION
                ? null
                : $"Gitleaks 版本 {versionLine} 不在已验证的 {SUPPORTED_MAJOR_VERSION}.x 区间，适配器继续启用但行为可能不兼容。";

            return GitleaksProbeResult.Available(executablePath!, versionLine, warning);
        }
        catch (Exception ex) when (ex is DetectionException or InvalidOperationException)
        {
            return GitleaksProbeResult.Unavailable($"Gitleaks 适配器已禁用：版本探测失败——{ex.Message}");
        }
    }

    /// <summary>取首个非空行（去除首尾空白与 <c>\r</c>）。</summary>
    /// <param name="text">原始文本。</param>
    /// <returns>首个非空行；无内容为 <see langword="null"/>。</returns>
    private static string? FirstNonEmptyLine(string text)
    {
        foreach (string line in text.Split('\n'))
        {
            string trimmed = line.Trim();
            if (trimmed.Length > 0)
            {
                return trimmed;
            }
        }

        return null;
    }
}
