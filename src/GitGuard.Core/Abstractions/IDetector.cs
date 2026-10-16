using GitGuard.Core.Model;

namespace GitGuard.Core.Abstractions;

/// <summary>
/// 检测器契约（ADR-0003）：四类内置检测器与未来 <c>GitleaksAdapter</c> / <c>UserRegexDetector</c>
/// 共享统一接口，使检测引擎可独立启用 / 禁用 / 替换（SAD §1）。
/// </summary>
public interface IDetector
{
    /// <summary>检测器名称（<c>secret</c> / <c>privacy</c> / <c>local_path</c> / <c>url</c>）。</summary>
    string Name { get; }

    /// <summary>
    /// 扫描单个文件的变更行并流式产出发现项；调用方按 FR-021 固定顺序（Secret → Privacy → LocalPath → Url）驱动。
    /// </summary>
    /// <param name="change">文件变更（仓库相对路径 + 新增 / 修改行）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>发现项流；检测失败（正则超时等）时抛 <see cref="DetectionException"/>（FailClosed）。</returns>
    IAsyncEnumerable<Finding> DetectAsync(FileChange change, CancellationToken cancellationToken = default);
}
