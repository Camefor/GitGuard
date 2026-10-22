using GitGuard.Core;
using GitGuard.Core.Abstractions;
using GitGuard.Core.Domain;
using GitGuard.Core.Model;
using GitGuard.Core.Policy;

namespace GitGuard.Application.Allowlist;

/// <summary>
/// 白名单新增用例（<c>06-API接口设计</c> §5.5 / FR-040）：校验匹配条件与登记原因，生成白名单条目并写入
/// <c>allowlist</c> 表，同时由存储层登记 <c>AllowlistChanged</c> 审计事件。
/// 参数校验失败统一映射为 <see cref="AllowlistException.ALLOWLIST_VALIDATION_FAILED"/>（CLI 退出码 20）。
/// </summary>
public sealed class AllowlistAddUseCase
{
    private readonly IAllowlistStore _store;
    private readonly IClock _clock;

    /// <summary>初始化白名单新增用例。</summary>
    /// <param name="store">白名单存储抽象。</param>
    /// <param name="clock">时钟（登记时间取 UTC）。</param>
    /// <exception cref="ArgumentNullException">任一参数为 <see langword="null"/>。</exception>
    public AllowlistAddUseCase(IAllowlistStore store, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(clock);

        _store = store;
        _clock = clock;
    }

    /// <summary>新增一条白名单。</summary>
    /// <param name="ruleId">可选：规则 ID 匹配条件。</param>
    /// <param name="pathPattern">可选：文件路径 glob 匹配条件。</param>
    /// <param name="fingerprintHex">可选：64 字符 SHA-256 十六进制指纹。</param>
    /// <param name="reason">登记原因（5 ~ 256 字符）。</param>
    /// <param name="createdBy">登记人（CLI 传入操作系统用户名）。</param>
    /// <returns>已持久化的白名单条目。</returns>
    /// <exception cref="ArgumentNullException"><paramref name="reason"/> 或 <paramref name="createdBy"/> 为 <see langword="null"/>。</exception>
    /// <exception cref="AllowlistException">匹配条件或指纹不合法（错误码 <see cref="AllowlistException.ALLOWLIST_VALIDATION_FAILED"/>）。</exception>
    /// <exception cref="SqliteException">写入失败（CLI 映射退出码 14，FailClosed）。</exception>
    public AllowlistEntry Execute(
        string? ruleId,
        string? pathPattern,
        string? fingerprintHex,
        string reason,
        string createdBy)
    {
        ArgumentNullException.ThrowIfNull(reason);
        ArgumentNullException.ThrowIfNull(createdBy);

        try
        {
            string? normalizedFingerprint = NormalizeOptional(fingerprintHex);
            Fingerprint? fingerprint = normalizedFingerprint is null
                ? null
                : Fingerprint.FromHex(normalizedFingerprint);

            var entry = AllowlistEntry.Create(
                NormalizeOptional(ruleId),
                NormalizeOptional(pathPattern),
                fingerprint,
                reason,
                _clock.UtcNow,
                createdBy);

            _store.AddAllowlistEntry(entry);
            return entry;
        }
        catch (InvariantViolationException ex)
        {
            // 领域不变式是唯一的参数校验来源；CLI 只负责退出码映射，不再重复实现校验
            throw new AllowlistException(AllowlistException.ALLOWLIST_VALIDATION_FAILED, ex.Message, ex);
        }
    }

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
