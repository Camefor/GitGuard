using GitGuard.Core.Model;

namespace GitGuard.Core.Abstractions;

/// <summary>
/// 待提交扫描标记存储（SRS FR-050）：在 pre-commit 与 post-commit 之间传递「本次待提交内容的身份」。
/// 标记属于仓库本地临时状态（不进入审计库、不包含敏感原文），实现位于
/// <c>&lt;git-dir&gt;/gitguard/pending-scan.json</c>。
/// </summary>
public interface IPendingScanStore
{
    /// <summary>读取当前标记。</summary>
    /// <returns>标记存在且可解析时返回；不存在或内容损坏时返回 <see langword="null"/>（损坏内容由实现尽力清理）。</returns>
    PendingScan? Read();

    /// <summary>写入标记（覆盖既有标记）。</summary>
    /// <param name="pendingScan">待写入的标记。</param>
    /// <exception cref="ArgumentNullException"><paramref name="pendingScan"/> 为 <see langword="null"/>。</exception>
    /// <exception cref="IOException">写入失败（调用方在扫描路径降级为警告，不阻断提交）。</exception>
    /// <exception cref="UnauthorizedAccessException">无写权限（同上）。</exception>
    void Write(PendingScan pendingScan);

    /// <summary>清除标记（不存在时静默返回）。</summary>
    /// <exception cref="IOException">删除失败。</exception>
    /// <exception cref="UnauthorizedAccessException">无删除权限。</exception>
    void Clear();
}
