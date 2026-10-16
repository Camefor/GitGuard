using System.Security.Cryptography;
using GitGuard.Core.Abstractions;

namespace GitGuard.Infrastructure.Security;

/// <summary>
/// <see cref="ISecretRandom"/> 的生产实现：包装 <see cref="RandomNumberGenerator"/>（操作系统 CSPRNG），
/// 供指纹盐生成使用（DR-02 / 07-缓存设计 §3）。
/// </summary>
public sealed class SystemSecretRandom : ISecretRandom
{
    /// <summary>共享实例（无状态、线程安全）。</summary>
    public static SystemSecretRandom Instance { get; } = new SystemSecretRandom();

    /// <inheritdoc />
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="count"/> 不为正数。</exception>
    public byte[] GetBytes(int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(count);
        return RandomNumberGenerator.GetBytes(count);
    }
}
