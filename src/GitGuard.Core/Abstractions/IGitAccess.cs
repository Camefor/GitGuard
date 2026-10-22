using GitGuard.Core.Model;

namespace GitGuard.Core.Abstractions;

/// <summary>
/// Git 访问抽象（SAD §5.1）：与本机 Git 仓库的一切交互必须经由本接口，上层禁止直接调用 git 或读取仓库内部文件。
/// 实现（Infrastructure/Git）统一以 Process 调用系统 git CLI（13-开发规范 §5：禁止 LibGit2Sharp）。
/// 失败契约（DR-03 / <c>10-异常处理设计.md</c> §1、§6）：非零退出、超时、进程无法启动一律抛 <see cref="GitException"/>，
/// 不重试、不吞异常；差异类输出逐字返回 git 原始文本，供检测器解析。
/// </summary>
public interface IGitAccess
{
    /// <summary>获取仓库工作区根目录（<c>git rev-parse --show-toplevel</c>）。</summary>
    /// <returns>规范化后的绝对路径（平台分隔符、无尾部分隔符）。</returns>
    /// <exception cref="GitException">路径不构成 Git 工作区或 git 命令失败时抛出（FailClosed）。</exception>
    string GetRepositoryRoot();

    /// <summary>获取仓库公共 Git 目录（<c>git rev-parse --git-common-dir</c>）。</summary>
    /// <returns>基于仓库工作区根目录解析后的绝对路径；worktree 场景下指向主仓库的 .git。</returns>
    /// <exception cref="GitException">git 命令失败时抛出（FailClosed）。</exception>
    string GetGitCommonDirectory();

    /// <summary>获取当前工作树专属的 Git 目录（<c>git rev-parse --git-dir</c>）。</summary>
    /// <remarks>linked worktree 场景下与 <see cref="GetGitCommonDirectory"/> 不同；FR-050 的待提交标记按工作树隔离。</remarks>
    /// <returns>绝对路径（普通仓库即工作区根目录下的 <c>.git</c>）。</returns>
    /// <exception cref="GitException">git 命令失败时抛出（FailClosed）。</exception>
    string GetGitDirectory();

    /// <summary>读取配置的 hooks 路径（<c>git config --get core.hooksPath</c>）。</summary>
    /// <returns>配置存在时为基于仓库工作区根目录解析后的绝对路径；未配置或显式置空时为 <see langword="null"/>（调用方回退 .git/hooks）。</returns>
    /// <exception cref="GitException">git 命令失败时抛出（FailClosed）。</exception>
    string? GetConfiguredHooksPath();

    /// <summary>获取本机 git 版本（<c>git --version</c>），用于 FR-012 状态展示与安装环境诊断。</summary>
    /// <returns>形如 <c>git version 2.43.0</c> 的完整版本行。</returns>
    /// <exception cref="GitException">git 命令失败时抛出（FailClosed）。</exception>
    string GetVersion();

    /// <summary>获取当前 HEAD 指向的提交 SHA（<c>git rev-parse HEAD</c>）。</summary>
    /// <returns>十六进制提交 SHA。</returns>
    /// <exception cref="GitException">HEAD 缺失（如尚无任何提交）或 git 命令失败时抛出（FailClosed）。</exception>
    string GetHeadCommitSha();

    /// <summary>尝试获取当前 HEAD 指向的提交 SHA（<c>git rev-parse --verify --quiet HEAD</c>）。</summary>
    /// <remarks>FR-050：pre-commit 记录父提交时需要区分「尚无提交」与 git 失败。</remarks>
    /// <returns>十六进制提交 SHA；HEAD 缺失（尚无提交）时为 <see langword="null"/>。</returns>
    /// <exception cref="GitException">git 命令失败（除 HEAD 缺失外）时抛出（FailClosed）。</exception>
    string? TryGetHeadCommitSha();

    /// <summary>获取暂存区差异（<c>git diff --cached -U0</c>）。</summary>
    /// <remarks>DR-04：PreCommit 扫描的唯一差异入口；调用方禁止改读工作区文件替代。</remarks>
    /// <returns>unified-0 格式的差异文本；暂存区无变化时为空字符串。</returns>
    /// <exception cref="GitException">git 命令失败时抛出（FailClosed）。</exception>
    string GetStagedDiff();

    /// <summary>获取暂存区的树 SHA（<c>git write-tree</c>），用于 FR-050 的待提交身份预测。</summary>
    /// <remarks>该命令仅写入索引对应的 tree 对象，不创建提交；索引存在未合并条目时 git 失败。</remarks>
    /// <returns>十六进制树 SHA。</returns>
    /// <exception cref="GitException">索引无法写出或 git 命令失败时抛出（FailClosed）。</exception>
    string GetStagedTreeSha();

    /// <summary>读取提交对象的树与父提交（SRS FR-050 绕过检测比对依据）。</summary>
    /// <param name="commitSha">提交 SHA（通常为当前 HEAD）。</param>
    /// <returns>提交元数据（树 SHA + 父提交列表）；不包含提交信息等敏感内容。</returns>
    /// <exception cref="ArgumentException"><paramref name="commitSha"/> 为空、空白或以 <c>-</c> 开头。</exception>
    /// <exception cref="GitException">提交不存在或 git 命令失败时抛出（FailClosed）。</exception>
    CommitMetadata GetCommitMetadata(string commitSha);

    /// <summary>枚举版本范围内的提交 SHA（<c>git rev-list</c>），用于 FR-030 推送扫描。</summary>
    /// <param name="revisionRange">版本区间：新分支为 <c>&lt;local-sha&gt;</c>，否则 <c>&lt;remote-sha&gt;..&lt;local-sha&gt;</c>。</param>
    /// <returns>提交 SHA 列表（git 原始顺序：新 → 旧）。</returns>
    /// <exception cref="ArgumentException"><paramref name="revisionRange"/> 为空、空白或以 <c>-</c> 开头。</exception>
    /// <exception cref="GitException">区间无法解析或 git 命令失败时抛出（FailClosed）。</exception>
    IReadOnlyList<string> ListCommitShas(string revisionRange);

    /// <summary>
    /// 批量获取多个提交的差异（一次 <c>git log --no-walk=unsorted --stdin -p -U0 --root</c>），
    /// 用于 FR-030 推送扫描；替代逐提交子进程以降低 NFR-P-003 的进程创建开销。
    /// </summary>
    /// <param name="commitShas">去重后的提交 SHA 列表（通常来自 <see cref="ListCommitShas"/>）。</param>
    /// <returns>与输入顺序一致的提交差异列表；空提交的差异文本为空字符串。</returns>
    /// <exception cref="ArgumentNullException"><paramref name="commitShas"/> 为 <see langword="null"/>。</exception>
    /// <exception cref="ArgumentException">任一 SHA 为空、空白、以 <c>-</c> 开头、非 40 / 64 位十六进制，或列表含重复项。</exception>
    /// <exception cref="GitException">
    /// 提交不存在、git 命令失败，或输出无法完整归属到请求提交时抛出（FailClosed，禁止静默漏扫）。
    /// </exception>
    IReadOnlyList<CommitDiff> GetCommitDiffs(IReadOnlyList<string> commitShas);
}
