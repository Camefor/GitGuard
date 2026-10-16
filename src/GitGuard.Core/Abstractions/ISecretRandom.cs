namespace GitGuard.Core.Abstractions;

/// <summary>
/// 密码学安全随机源抽象（DDD §6）：<c>Fingerprinter</c> 生成指纹盐时依赖本接口。
/// 生产实现由 Infrastructure 提供（进程级安全随机数发生器）；测试可注入确定性实现。
/// </summary>
public interface ISecretRandom
{
    /// <summary>生成指定长度的随机字节序列。</summary>
    /// <param name="count">需要的字节数；必须为正数。</param>
    /// <returns>长度为 <paramref name="count"/> 的随机字节数组。</returns>
    byte[] GetBytes(int count);
}
