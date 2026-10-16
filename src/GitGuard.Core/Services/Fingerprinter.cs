using System.Security.Cryptography;
using System.Text;
using GitGuard.Core.Abstractions;
using GitGuard.Core.Domain;
using GitGuard.Core.Model;

namespace GitGuard.Core.Services;

/// <summary>
/// 指纹器（DDD §6 / DR-02）：对敏感原文做 SHA-256 + 盐生成 <see cref="Fingerprint"/>。
/// 采用 HMAC-SHA256（盐为密钥、UTF-8 原文为消息），原始值不可从指纹反推；
/// 盐由调用方注入且跨 run 稳定（07-缓存设计 §3），保证同一原文在同一机器上指纹恒定，
/// 供白名单匹配与审计关联使用。
/// </summary>
public sealed class Fingerprinter
{
    /// <summary>指纹盐长度（字节）；07-缓存设计 §3：<c>fingerprint.salt</c> 为 32 字节随机数据。</summary>
    public const int SALT_BYTES = 32;

    private readonly byte[] _salt;

    /// <summary>使用机器本地的指纹盐初始化指纹器。</summary>
    /// <param name="salt">32 字节盐；由调用方首次运行生成或以 AtomicWrite 读入（07-缓存设计 §3、§5）。</param>
    /// <exception cref="InvariantViolationException">盐长度不为 32 字节时抛出（DR-02）。</exception>
    public Fingerprinter(ReadOnlySpan<byte> salt)
    {
        DomainGuard.Require(salt.Length == SALT_BYTES, "DR-02", $"指纹盐必须为 {SALT_BYTES} 字节（当前 {salt.Length} 字节）");
        _salt = salt.ToArray();
    }

    /// <summary>生成首次运行使用的随机盐；调用方负责原子写入缓存文件（07-缓存设计 §5）。</summary>
    /// <param name="random">密码学安全随机源。</param>
    /// <returns>32 字节随机盐。</returns>
    /// <exception cref="ArgumentNullException"><paramref name="random"/> 为 <see langword="null"/>。</exception>
    /// <exception cref="InvariantViolationException">随机源返回的长度不足 32 字节时抛出（DR-02）。</exception>
    public static byte[] CreateSalt(ISecretRandom random)
    {
        ArgumentNullException.ThrowIfNull(random);

        byte[] salt = random.GetBytes(SALT_BYTES);
        DomainGuard.Require(salt.Length == SALT_BYTES, "DR-02", $"随机源返回的盐必须为 {SALT_BYTES} 字节（当前 {salt.Length} 字节）");
        return salt;
    }

    /// <summary>计算敏感原文的指纹；替代原文进入白名单比对与持久化。</summary>
    /// <param name="rawSecret">检测器命中的敏感原文；本方法调用后不得在别处保留。</param>
    /// <returns>64 字符小写十六进制的指纹。</returns>
    /// <exception cref="ArgumentNullException"><paramref name="rawSecret"/> 为 <see langword="null"/>。</exception>
    /// <exception cref="InvariantViolationException">原文为空时抛出（DR-02）。</exception>
    public Fingerprint Compute(string rawSecret)
    {
        ArgumentNullException.ThrowIfNull(rawSecret);
        DomainGuard.Require(rawSecret.Length > 0, "DR-02", "指纹计算的原文不能为空");

        byte[] digest = HMACSHA256.HashData(_salt, Encoding.UTF8.GetBytes(rawSecret));
        return Fingerprint.FromHex(Convert.ToHexStringLower(digest));
    }
}
