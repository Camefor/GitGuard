using GitGuard.Core.Model;

namespace GitGuard.Core.Abstractions;

/// <summary>
/// 可上报规则级耗时的检测器（NFR-M-002）。未实现本接口的检测器（如未来的外部适配器）
/// 仍按 <see cref="IDetector"/> 正常工作，只是不参与耗时摘要。
/// </summary>
public interface IRuleTimingAwareDetector : IDetector
{
    /// <summary>扫描单个文件并上报每次规则求值耗时。</summary>
    /// <param name="change">文件变更。</param>
    /// <param name="timingSink">耗时接收器（本次扫描内聚合）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>发现项流；检测失败时抛 <see cref="DetectionException"/>（FailClosed）。</returns>
    /// <exception cref="ArgumentNullException"><paramref name="change"/> 或 <paramref name="timingSink"/> 为 <see langword="null"/>。</exception>
    IAsyncEnumerable<Finding> DetectWithTimingAsync(
        FileChange change,
        IRuleTimingSink timingSink,
        CancellationToken cancellationToken = default);
}
