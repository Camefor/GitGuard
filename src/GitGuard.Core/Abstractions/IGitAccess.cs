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

    /// <summary>读取配置的 hooks 路径（<c>git config --get core.hooksPath</c>）。</summary>
    /// <returns>配置存在时为基于仓库工作区根目录解析后的绝对路径；未配置或显式置空时为 <see langword="null"/>（调用方回退 .git/hooks）。</returns>
    /// <exception cref="GitException">git 命令失败时抛出（FailClosed）。</exception>
    string? GetConfiguredHooksPath();

    /// <summary>获取本机 git 版本（<c>git --version</c>），用于 FR-012 状态展示与安装环境诊断。</summary>
    /// <returns>形如 <c>git version 2.43.0</c> 的完整版本行。</returns>
    /// <exception cref="GitException">git 命令失败时抛出（FailClosed）。</exception>
    string GetVersion();

    /// <summary>获取当前 HEAD 指向的提交 SHA（<c>git rev-parse HEAD</c>），用于 DR-06 绕过检测对比。</summary>
    /// <returns>十六进制提交 SHA。</returns>
    /// <exception cref="GitException">HEAD 缺失（如尚无任何提交）或 git 命令失败时抛出（FailClosed）。</exception>
    string GetHeadCommitSha();

    /// <summary>获取暂存区差异（<c>git diff --cached -U0</c>）。</summary>
    /// <remarks>DR-04：PreCommit 扫描的唯一差异入口；调用方禁止改读工作区文件替代。</remarks>
    /// <returns>unified-0 格式的差异文本；暂存区无变化时为空字符串。</returns>
    /// <exception cref="GitException">git 命令失败时抛出（FailClosed）。</exception>
    string GetStagedDiff();

    /// <summary>枚举版本范围内的提交 SHA（<c>git rev-list</c>），用于 FR-030 推送扫描。</summary>
    /// <param name="revisionRange">版本区间：新分支为 <c>&lt;local-sha&gt;</c>，否则 <c>&lt;remote-sha&gt;..&lt;local-sha&gt;</c>。</param>
    /// <returns>提交 SHA 列表（git 原始顺序：新 → 旧）。</returns>
    /// <exception cref="ArgumentException"><paramref name="revisionRange"/> 为空、空白或以 <c>-</c> 开头。</exception>
    /// <exception cref="GitException">区间无法解析或 git 命令失败时抛出（FailClosed）。</exception>
    IReadOnlyList<string> ListCommitShas(string revisionRange);

    /// <summary>获取单个提交的差异（<c>git diff-tree -p -U0 --root &lt;sha&gt;</c>），用于 FR-030 逐提交扫描。</summary>
    /// <param name="commitSha">提交 SHA（通常来自 <see cref="ListCommitShas"/>）。</param>
    /// <returns>unified-0 格式的差异文本（不含提交头行）；提交无差异时为空字符串。</returns>
    /// <exception cref="ArgumentException"><paramref name="commitSha"/> 为空、空白或以 <c>-</c> 开头。</exception>
    /// <exception cref="GitException">提交不存在或 git 命令失败时抛出（FailClosed）。</exception>
    string GetCommitDiff(string commitSha);
}
