namespace GitGuard.Core.Abstractions;

/// <summary>
/// 时钟抽象（DDD 变更记录 0.1.2 遗留项「IClock 待使用方引入」，首个使用方为扫描用例）：
/// 用例层禁止直接调用 <see cref="DateTimeOffset.UtcNow"/>（13-开发规范 §4.1 时间与时区），一律经本接口取时，
/// 使 <c>ScanRun.StartedAt</c> 在测试中可确定复现。
/// </summary>
public interface IClock
{
    /// <summary>当前 UTC 时间（偏移量恒为零，满足 <c>ScanRun</c> 不变式）。</summary>
    DateTimeOffset UtcNow { get; }
}
