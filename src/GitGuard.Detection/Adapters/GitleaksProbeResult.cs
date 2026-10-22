namespace GitGuard.Detection.Adapters;

/// <summary>
/// Gitleaks 启动期探测结果（FR-090）：可用时携带可执行文件路径与版本行，
/// 不可用时携带降级警告；版本超出已验证区间时仍可用，仅通过 <see cref="Warning"/> 提示。
/// </summary>
public sealed class GitleaksProbeResult
{
    private GitleaksProbeResult(
        bool isAvailable,
        string? executablePath,
        string? versionLine,
        string? warning)
    {
        IsAvailable = isAvailable;
        ExecutablePath = executablePath;
        VersionLine = versionLine;
        Warning = warning;
    }

    /// <summary>适配器是否可用（可执行文件存在且版本命令执行成功）。</summary>
    public bool IsAvailable { get; }

    /// <summary>可执行文件绝对路径；不可用时为 <see langword="null"/>。</summary>
    public string? ExecutablePath { get; }

    /// <summary>版本命令输出的版本行；不可用或无法识别时为 <see langword="null"/> 或原始文本。</summary>
    public string? VersionLine { get; }

    /// <summary>降级 / 兼容性警告；无警告为 <see langword="null"/>。由组合根写入扫描警告列表。</summary>
    public string? Warning { get; }

    /// <summary>构造可用结果。</summary>
    /// <param name="executablePath">可执行文件绝对路径。</param>
    /// <param name="versionLine">版本行。</param>
    /// <param name="warning">兼容性警告（可选）。</param>
    /// <returns>可用结果。</returns>
    internal static GitleaksProbeResult Available(string executablePath, string? versionLine, string? warning) =>
        new GitleaksProbeResult(isAvailable: true, executablePath, versionLine, warning);

    /// <summary>构造不可用结果（适配器降级，不阻断扫描）。</summary>
    /// <param name="warning">降级原因。</param>
    /// <returns>不可用结果。</returns>
    internal static GitleaksProbeResult Unavailable(string warning) =>
        new GitleaksProbeResult(isAvailable: false, executablePath: null, versionLine: null, warning);
}
