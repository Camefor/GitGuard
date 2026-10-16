using GitGuard.Core.Abstractions;

namespace GitGuard.Infrastructure.Time;

/// <summary>
/// <see cref="IClock"/> 的系统实现：返回本机 UTC 时间（偏移量零）。无状态，可单例复用。
/// </summary>
public sealed class SystemClock : IClock
{
    /// <inheritdoc />
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
