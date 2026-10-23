namespace GitGuard.Application.Scanning;

/// <summary>
/// 扫描运行时设置（<c>模块设计/Policy.md</c> §7 的 <c>scan</c> 段落子集）。
/// 本任务（GGD-0007）先以内置默认值交付；<c>.gitguard.yml</c> 加载与合并（FR-070）随 CLI 任务引入，
/// 届时由组合根把配置映射为不可变的本记录后注入用例。
/// </summary>
public sealed record ScanSettings
{
    /// <summary>单文件大小上限默认值：1 MiB（SRS FR-020）。</summary>
    public const int DEFAULT_MAX_FILE_BYTES = 1024 * 1024;

    /// <summary>扫描级超时默认值：30s（<c>06-API接口设计</c> §7 <c>scan.timeout_ms</c>）。</summary>
    public static readonly TimeSpan DEFAULT_TIMEOUT = TimeSpan.FromSeconds(30);

    /// <summary>全默认设置（1 MiB / 30s，Hook 模式两类扫描均启用）；不可变，可安全共享。</summary>
    public static ScanSettings Default { get; } = new ScanSettings();

    /// <summary>
    /// 是否在 <c>pre-commit</c> Hook 模式（<c>--hook-mode pre-commit</c>）执行暂存区扫描；
    /// 手动 <c>scan --staged</c> 不受该开关影响。
    /// </summary>
    public bool PreCommit { get; init; } = true;

    /// <summary>
    /// 是否在 <c>pre-push</c> Hook 模式（<c>--hook-mode pre-push</c>）执行推送范围扫描；
    /// 手动 <c>scan --push</c> 不受该开关影响。
    /// </summary>
    public bool PrePush { get; init; } = true;

    /// <summary>单文件跳过阈值（字节）；超过的文件跳过并记为 skipped（SRS FR-020）。</summary>
    public int MaxFileBytes { get; init; } = DEFAULT_MAX_FILE_BYTES;

    /// <summary>扫描级超时；超时以 FailClosed 结束（退出码 12）。</summary>
    public TimeSpan Timeout { get; init; } = DEFAULT_TIMEOUT;
}
