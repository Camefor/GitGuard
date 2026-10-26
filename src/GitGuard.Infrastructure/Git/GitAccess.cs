using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using GitGuard.Core;
using GitGuard.Core.Abstractions;
using GitGuard.Core.Model;

namespace GitGuard.Infrastructure.Git;

/// <summary>
/// <see cref="IGitAccess"/> 默认实现：以 <see cref="Process"/> 调用系统 git CLI（13-开发规范 §5：禁止 LibGit2Sharp）。
/// 所有命令统一在仓库工作区根目录下启动，参数以参数列表传递（无 shell 展开），输出按 UTF-8 解码；
/// 失败契约遵循 DR-03（FailClosed）与 <c>10-异常处理设计.md</c> §6：非零退出、超时、进程无法启动
/// 一律抛 <see cref="GitException"/> 且不重试。
/// </summary>
public sealed class GitAccess : IGitAccess
{
    /// <summary>单条 git 命令超时（模块设计/Hook.md §4：30s）；超时即终止子进程并直接失败。</summary>
    private static readonly TimeSpan _defaultCommandTimeout = TimeSpan.FromSeconds(30);

    private readonly string _repositoryRoot;
    private readonly string _gitExecutable;
    private readonly TimeSpan _commandTimeout;

    // 07-缓存设计 §4：Git 元数据（hooks 路径 / HEAD）在单次进程内只查询一次，缓存于 Lazy
    private readonly Lazy<string> _gitCommonDirectory;
    private readonly Lazy<string> _gitDirectory;
    private readonly Lazy<string?> _configuredHooksPath;
    private readonly Lazy<string> _headCommitSha;
    private readonly Lazy<string> _emptyTreeSha;

    /// <summary>在指定路径下创建 Git 访问实例；构造时预检仓库有效性（非 Git 工作区立即 FailClosed）。</summary>
    /// <param name="repositoryPath">仓库内任意目录（通常为当前工作目录）；命令统一切换到工作区根目录执行。</param>
    /// <param name="gitExecutable">git 可执行文件；默认从 PATH 解析 <c>git</c>。</param>
    /// <param name="commandTimeout">单条命令超时；默认 30s（模块设计/Hook.md §4）。</param>
    /// <exception cref="ArgumentException"><paramref name="repositoryPath"/> 或 <paramref name="gitExecutable"/> 为空或空白。</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="commandTimeout"/> 不为正数。</exception>
    /// <exception cref="GitException">
    /// 路径所在（或其父级）不构成 Git 工作区时抛 <c>NOT_A_GIT_REPOSITORY</c>；
    /// git 进程无法启动或预检超时时抛 <c>GIT_COMMAND_FAILED</c>。
    /// </exception>
    public GitAccess(string repositoryPath, string gitExecutable = "git", TimeSpan? commandTimeout = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(gitExecutable);

        _commandTimeout = commandTimeout ?? _defaultCommandTimeout;
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(_commandTimeout, TimeSpan.Zero, nameof(commandTimeout));
        _gitExecutable = gitExecutable;
        _repositoryRoot = ResolveRepositoryRoot(Path.GetFullPath(repositoryPath));
        _gitCommonDirectory = new Lazy<string>(ResolveGitCommonDirectory);
        _gitDirectory = new Lazy<string>(ResolveGitDirectory);
        _configuredHooksPath = new Lazy<string?>(ResolveConfiguredHooksPath);
        _headCommitSha = new Lazy<string>(ResolveHeadCommitSha);
        _emptyTreeSha = new Lazy<string>(ResolveEmptyTreeSha);
    }

    /// <inheritdoc />
    public string GetRepositoryRoot() => _repositoryRoot;

    /// <inheritdoc />
    public string GetGitCommonDirectory() => _gitCommonDirectory.Value;

    /// <inheritdoc />
    public string GetGitDirectory() => _gitDirectory.Value;

    /// <inheritdoc />
    public string? GetConfiguredHooksPath() => _configuredHooksPath.Value;

    /// <inheritdoc />
    public string GetVersion()
    {
        var result = RunGit("--version");
        EnsureGitSucceeded(result, "--version");
        return result.StandardOutput.Trim();
    }

    /// <inheritdoc />
    public string GetHeadCommitSha() => _headCommitSha.Value;

    /// <inheritdoc />
    public string? TryGetHeadCommitSha()
    {
        // --verify --quiet：HEAD 缺失（尚无提交）时退出码 1 且无输出，区别于真正的 git 失败
        var result = RunGit("rev-parse", "--verify", "--quiet", "HEAD");
        if (result.ExitCode == 0)
        {
            return result.StandardOutput.Trim();
        }

        if (result.ExitCode == 1)
        {
            return null;
        }

        throw new GitException(
            GitException.GIT_COMMAND_FAILED,
            $"git rev-parse --verify --quiet HEAD 失败（退出码 {result.ExitCode}）。",
            result.ExitCode,
            result.StandardError);
    }

    /// <inheritdoc />
    public string GetStagedTreeSha()
    {
        var result = RunGit("write-tree");
        EnsureGitSucceeded(result, "write-tree");
        return result.StandardOutput.Trim();
    }

    /// <inheritdoc />
    public CommitMetadata GetCommitMetadata(string commitSha)
    {
        ValidateRevisionArgument(commitSha, nameof(commitSha));

        var result = RunGit("cat-file", "-p", commitSha);
        EnsureGitSucceeded(result, $"cat-file -p {commitSha}");
        return ParseCommitMetadata(commitSha, result.StandardOutput);
    }

    /// <inheritdoc />
    public string GetStagedDiff()
    {
        // DR-04：PreCommit 的唯一差异入口；--no-color/--no-ext-diff/--no-textconv 中性化用户 diff 配置，保证输出恒为标准 unified-0 格式；
        // -c core.quotepath=false 让非 ASCII 路径以原始 UTF-8 输出（默认会转成 \NNN 八进制转义，破坏解析层路径与 glob 匹配）
        var result = RunGit("-c", "core.quotepath=false", "diff", "--cached", "-U0", "--no-color", "--no-ext-diff", "--no-textconv");
        EnsureGitSucceeded(result, "diff --cached -U0");
        return result.StandardOutput;
    }

    /// <inheritdoc />
    public string GetFullTreeDiff(string revision)
    {
        ValidateRevisionArgument(revision, nameof(revision));

        // 空树 → 目标修订：所有跟踪文件均以新增形式出现，等效读取整棵 HEAD 树但不触碰工作区（DR-04）；
        // 选项与暂存区差异保持一致，保证输出恒为标准 unified-0 格式且路径不被 quote。
        var result = RunGit(
            "-c",
            "core.quotepath=false",
            "diff",
            _emptyTreeSha.Value,
            revision,
            "-U0",
            "--no-color",
            "--no-ext-diff",
            "--no-textconv");
        EnsureGitSucceeded(result, $"diff <empty-tree> {revision} -U0");
        return result.StandardOutput;
    }

    /// <inheritdoc />
    public IReadOnlyList<string> ListCommitShas(string revisionRange)
    {
        ValidateRevisionArgument(revisionRange, nameof(revisionRange));

        var result = RunGit("rev-list", revisionRange);
        EnsureGitSucceeded(result, $"rev-list {revisionRange}");
        return result.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    /// <inheritdoc />
    public IReadOnlyList<CommitDiff> GetCommitDiffs(IReadOnlyList<string> commitShas)
    {
        ArgumentNullException.ThrowIfNull(commitShas);
        if (commitShas.Count == 0)
        {
            return Array.Empty<CommitDiff>();
        }

        ValidateCommitShas(commitShas);

        // --no-walk=unsorted：只输出 stdin 列出的提交且保持输入顺序；
        // --format=%H：每个提交都输出完整 SHA 头（空提交也有），未知提交直接失败而非静默跳过；
        // --root 将初始提交视为全量新增（否则新分支仅含根提交时漏扫）；
        // -c log.showSignature=false 中和用户配置，保证输出以 SHA 头开始。
        string standardInput = string.Join('\n', commitShas) + "\n";
        var result = RunGitAt(
            _repositoryRoot,
            standardInput,
            "-c",
            "core.quotepath=false",
            "-c",
            "log.showSignature=false",
            "log",
            "--no-walk=unsorted",
            "--stdin",
            "-p",
            "-U0",
            "--root",
            "--format=%H",
            "--no-color",
            "--no-ext-diff",
            "--no-textconv");
        EnsureGitSucceeded(result, "log --no-walk=unsorted --stdin");
        return ParseCommitDiffs(commitShas, result.StandardOutput);
    }

    /// <summary>终止超时的 git 子进程；进程恰好已退出或无权限终止时静默兜底。</summary>
    private static void KillProcess(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);

            // 等待进程完全退出，确保管道句柄回收后再向上抛错
            process.WaitForExit();
        }
        catch (InvalidOperationException)
        {
            // 进程在 Kill 前已自行退出
            return;
        }
        catch (Win32Exception)
        {
            // 无权终止子进程（罕见）：放弃等待，仍沿异常路径 FailClosed
            return;
        }
    }

    /// <summary>退出码非 0 时抛出携带 stderr 的 <see cref="GitException"/>（FailClosed）。</summary>
    private static void EnsureGitSucceeded((int ExitCode, string StandardOutput, string StandardError) result, string commandDescription)
    {
        if (result.ExitCode == 0)
        {
            return;
        }

        throw new GitException(
            GitException.GIT_COMMAND_FAILED,
            $"git {commandDescription} 失败（退出码 {result.ExitCode}）。",
            result.ExitCode,
            result.StandardError);
    }

    /// <summary>校验提交标识/版本区间参数，拒绝空值与前导 <c>-</c>（防止外部输入被当作 git 选项）。</summary>
    private static void ValidateRevisionArgument(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);

        if (value.StartsWith('-'))
        {
            throw new ArgumentException("修订标识不得以 '-' 开头。", parameterName);
        }
    }

    /// <summary>校验批量提交 SHA：格式合法且无重复，避免输出归属歧义。</summary>
    private static void ValidateCommitShas(IReadOnlyList<string> commitShas)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (string sha in commitShas)
        {
            ValidateRevisionArgument(sha, nameof(commitShas));

            if (sha is not { Length: 40 or 64 } || !sha.All(char.IsAsciiHexDigit))
            {
                throw new ArgumentException($"提交 SHA 必须为 40 / 64 位十六进制：{sha}", nameof(commitShas));
            }

            if (!seen.Add(sha))
            {
                throw new ArgumentException($"提交 SHA 列表包含重复项：{sha}", nameof(commitShas));
            }
        }
    }

    /// <summary>构造批量差异输出解析失败的 FailClosed 异常。</summary>
    private static GitException CreateBatchParseException(string message) =>
        new GitException(GitException.GIT_COMMAND_FAILED, $"git log --no-walk=unsorted --stdin 输出解析失败：{message}");

    /// <summary>
    /// 严格解析 <c>git log --format=%H -p</c> 的批量输出：按 SHA 头切分提交段，
    /// 每个请求提交必须恰好出现一次，输出存在任何无法归属的内容即 FailClosed。
    /// </summary>
    private static List<CommitDiff> ParseCommitDiffs(IReadOnlyList<string> commitShas, string output)
    {
        var requested = new HashSet<string>(commitShas, StringComparer.Ordinal);
        var builders = new Dictionary<string, StringBuilder>(commitShas.Count, StringComparer.Ordinal);
        StringBuilder? current = null;
        bool diffStarted = false;
        bool separatorSkipped = false;

        string[] lines = output.Split('\n');
        for (int index = 0; index < lines.Length; index++)
        {
            string line = lines[index];
            if (index == lines.Length - 1 && line.Length == 0)
            {
                // 末尾换行产生的空段：不追加，保持与原单提交差异一致的收尾
                break;
            }

            string candidate = line.EndsWith('\r') ? line.Substring(0, line.Length - 1) : line;
            if (requested.Contains(candidate))
            {
                if (!builders.TryAdd(candidate, new StringBuilder()))
                {
                    throw CreateBatchParseException("同一提交头重复出现");
                }

                current = builders[candidate];
                diffStarted = false;
                separatorSkipped = false;
                continue;
            }

            if (current is null)
            {
                if (candidate.Length == 0)
                {
                    continue;
                }

                throw CreateBatchParseException("首个提交头之前存在意外内容");
            }

            if (!diffStarted)
            {
                // git log 在 SHA 头与补丁之间插入一个空行；空提交没有补丁
                if (candidate.Length == 0 && !separatorSkipped)
                {
                    separatorSkipped = true;
                    continue;
                }

                if (!candidate.StartsWith("diff --git ", StringComparison.Ordinal))
                {
                    throw CreateBatchParseException("提交段缺少 diff --git 起始行");
                }

                diffStarted = true;
            }

            current.Append(line).Append('\n');
        }

        if (builders.Count != commitShas.Count)
        {
            throw CreateBatchParseException("未覆盖全部请求提交");
        }

        var diffs = new List<CommitDiff>(commitShas.Count);
        foreach (string sha in commitShas)
        {
            diffs.Add(new CommitDiff(sha, builders[sha].ToString()));
        }

        return diffs;
    }

    /// <summary>解析 commit 对象头部：只提取 tree 与 parent 行（跳过 author / committer / 签名等字段）。</summary>
    private static CommitMetadata ParseCommitMetadata(string commitSha, string commitObject)
    {
        string? treeSha = null;
        var parents = new List<string>();
        foreach (string line in commitObject.Split('\n'))
        {
            if (line.Length == 0)
            {
                break;
            }

            if (line.StartsWith("tree ", StringComparison.Ordinal))
            {
                treeSha = line.Substring("tree ".Length).Trim();
            }
            else if (line.StartsWith("parent ", StringComparison.Ordinal))
            {
                parents.Add(line.Substring("parent ".Length).Trim());
            }
        }

        if (treeSha is null)
        {
            throw new GitException(
                GitException.GIT_COMMAND_FAILED,
                $"git cat-file -p {commitSha} 输出缺少 tree 行，无法解析提交元数据。");
        }

        try
        {
            return new CommitMetadata(commitSha, treeSha, parents);
        }
        catch (GitGuard.Core.Domain.InvariantViolationException ex)
        {
            throw new GitException(
                GitException.GIT_COMMAND_FAILED,
                $"git cat-file -p {commitSha} 输出格式非法：{ex.Message}",
                ex);
        }
    }

    /// <summary>预检仓库有效性并解析工作区根目录：非 Git 工作区抛 <c>NOT_A_GIT_REPOSITORY</c>（CLI 映射退出码 2）。</summary>
    private string ResolveRepositoryRoot(string workingDirectory)
    {
        var result = RunGitAt(workingDirectory, standardInput: null, "rev-parse", "--show-toplevel");
        if (result.ExitCode != 0)
        {
            throw new GitException(
                GitException.NOT_A_GIT_REPOSITORY,
                "当前路径不是 Git 工作区（或位于 bare 仓库中），请在普通 Git 仓库内执行。",
                result.ExitCode,
                result.StandardError);
        }

        return Path.GetFullPath(result.StandardOutput.Trim());
    }

    /// <summary>解析公共 Git 目录；git 可能返回相对路径（如 .git 或 worktree 下的 ../.git），统一绝对化。</summary>
    private string ResolveGitCommonDirectory()
    {
        var result = RunGit("rev-parse", "--git-common-dir");
        EnsureGitSucceeded(result, "rev-parse --git-common-dir");
        return Path.GetFullPath(Path.Combine(_repositoryRoot, result.StandardOutput.Trim()));
    }

    /// <summary>解析当前工作树专属 Git 目录（linked worktree 下为 .git/worktrees/&lt;name&gt;）。</summary>
    private string ResolveGitDirectory()
    {
        // 不用 --absolute-git-dir（Git ≥ 2.13）：--git-dir 在 Git 2.9 起可用，相对路径统一基于工作区根目录绝对化
        var result = RunGit("rev-parse", "--git-dir");
        EnsureGitSucceeded(result, "rev-parse --git-dir");
        return Path.GetFullPath(Path.Combine(_repositoryRoot, result.StandardOutput.Trim()));
    }

    /// <summary>读取 core.hooksPath；未设置（git 退出码 1）或显式置空时返回 null（调用方回退 .git/hooks）。</summary>
    private string? ResolveConfiguredHooksPath()
    {
        var result = RunGit("config", "--get", "core.hooksPath");
        if (result.ExitCode == 1)
        {
            return null;
        }

        EnsureGitSucceeded(result, "config --get core.hooksPath");

        string hooksPath = result.StandardOutput.Trim();
        if (hooksPath.Length == 0)
        {
            return null;
        }

        // core.hooksPath 为相对路径时相对工作区根目录解析（git 在 hooks 执行前会切换 cwd 到工作区根目录）
        return Path.GetFullPath(Path.Combine(_repositoryRoot, hooksPath));
    }

    /// <summary>解析 HEAD 指向的提交 SHA；HEAD 缺失（尚无提交）时按 git 失败处理（FailClosed）。</summary>
    private string ResolveHeadCommitSha()
    {
        var result = RunGit("rev-parse", "HEAD");
        EnsureGitSucceeded(result, "rev-parse HEAD");
        return result.StandardOutput.Trim();
    }

    /// <summary>计算当前仓库对象格式下的空树 SHA（空输入经 stdin 哈希，不写入对象库）。</summary>
    private string ResolveEmptyTreeSha()
    {
        var result = RunGitAt(_repositoryRoot, standardInput: string.Empty, "hash-object", "-t", "tree", "--stdin");
        EnsureGitSucceeded(result, "hash-object -t tree --stdin");
        return result.StandardOutput.Trim();
    }

    /// <summary>在工作区根目录执行 git 命令。</summary>
    private (int ExitCode, string StandardOutput, string StandardError) RunGit(params string[] arguments)
    {
        return RunGitAt(_repositoryRoot, standardInput: null, arguments);
    }

    /// <summary>
    /// 在指定目录启动 git 子进程并同步等待其结束；
    /// <paramref name="standardInput"/> 非 <see langword="null"/> 时写入子进程 stdin 并关闭；
    /// 超时（DR-03）终止进程树后抛 <see cref="GitException"/>，绝不重试。
    /// </summary>
    private (int ExitCode, string StandardOutput, string StandardError) RunGitAt(
        string workingDirectory,
        string? standardInput,
        params string[] arguments)
    {
        // 桌面端 WinExe 没有控制台；统一经工厂创建，避免每条 git 命令闪现控制台窗口
        var startInfo = GitProcessStartInfoFactory.Create(_gitExecutable);
        startInfo.WorkingDirectory = workingDirectory;
        startInfo.RedirectStandardInput = standardInput is not null;
        startInfo.RedirectStandardOutput = true;
        startInfo.RedirectStandardError = true;
        startInfo.StandardOutputEncoding = Encoding.UTF8;
        startInfo.StandardErrorEncoding = Encoding.UTF8;
        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using Process process = StartGitProcess(startInfo);
        Task<string> standardOutput = process.StandardOutput.ReadToEndAsync();
        Task<string> standardError = process.StandardError.ReadToEndAsync();

        if (standardInput is not null)
        {
            try
            {
                process.StandardInput.Write(standardInput);
                process.StandardInput.Close();
            }
            catch (IOException)
            {
                // git 提前退出（如未知提交）时管道可能已关闭；退出码与 stderr 由下方统一处理
            }
        }

        if (!process.WaitForExit((int)_commandTimeout.TotalMilliseconds))
        {
            KillProcess(process);

            throw new GitException(
                GitException.GIT_COMMAND_FAILED,
                $"git {string.Join(' ', arguments)} 超时，已终止子进程；按 FailClosed 直接失败且不重试。");
        }

        return (process.ExitCode, standardOutput.GetAwaiter().GetResult(), standardError.GetAwaiter().GetResult());
    }

    /// <summary>启动 git 子进程；可执行文件缺失或无权限执行时抛 <c>GIT_COMMAND_FAILED</c>。</summary>
    private Process StartGitProcess(ProcessStartInfo startInfo)
    {
        try
        {
            return Process.Start(startInfo)
                ?? throw new GitException(GitException.GIT_COMMAND_FAILED, $"启动 git 进程（{_gitExecutable}）失败：未返回进程句柄。");
        }
        catch (Win32Exception ex)
        {
            throw new GitException(
                GitException.GIT_COMMAND_FAILED,
                $"无法启动 git 进程（{_gitExecutable}）：请确认已安装 Git 且可执行文件可被解析。",
                ex);
        }
    }
}
