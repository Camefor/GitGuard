using GitGuard.Core;
using GitGuard.Core.Abstractions;
using GitGuard.Core.Model;

namespace GitGuard.Application.Bypass;

/// <summary>
/// 待提交预测记录器（SRS FR-050）：pre-commit 分发脚本以隐藏选项驱动暂存区扫描，
/// 扫描通过（或按 <c>GITGUARD_SKIP=1</c> 跳过）后记录「父提交 + 暂存树」，供 post-commit 比对。
/// 记录动作不触碰审计库：标记写入失败只降级为警告，绝不阻断提交。
/// </summary>
public sealed class PendingScanRecorder
{
    private readonly IGitAccess _gitAccess;
    private readonly IPendingScanStore _pendingScanStore;
    private readonly IClock _clock;

    /// <summary>初始化待提交预测记录器。</summary>
    /// <param name="gitAccess">Git 访问抽象（构造时已指向目标仓库）。</param>
    /// <param name="pendingScanStore">待提交标记存储（工作树级）。</param>
    /// <param name="clock">时钟抽象（记录时间戳）。</param>
    /// <exception cref="ArgumentNullException">任一引用参数为 <see langword="null"/>。</exception>
    public PendingScanRecorder(IGitAccess gitAccess, IPendingScanStore pendingScanStore, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(gitAccess);
        ArgumentNullException.ThrowIfNull(pendingScanStore);
        ArgumentNullException.ThrowIfNull(clock);

        _gitAccess = gitAccess;
        _pendingScanStore = pendingScanStore;
        _clock = clock;
    }

    /// <summary>记录本次待提交内容的预测身份。</summary>
    /// <param name="skipped">是否因 <c>GITGUARD_SKIP=1</c> 跳过扫描。</param>
    /// <exception cref="GitException">git 命令失败（调用方降级为警告，不阻断提交）。</exception>
    /// <exception cref="IOException">标记写入失败（同上）。</exception>
    /// <exception cref="UnauthorizedAccessException">无写权限（同上）。</exception>
    public void Record(bool skipped)
    {
        string? parentSha = _gitAccess.TryGetHeadCommitSha();
        string treeSha = _gitAccess.GetStagedTreeSha();
        _pendingScanStore.Write(new PendingScan(parentSha, treeSha, skipped, _clock.UtcNow));
    }

    /// <summary>清除待提交标记（扫描未通过或绕过事件已处理后调用）。</summary>
    /// <exception cref="IOException">删除失败（调用方降级为警告）。</exception>
    /// <exception cref="UnauthorizedAccessException">无删除权限（同上）。</exception>
    public void Clear() => _pendingScanStore.Clear();
}
