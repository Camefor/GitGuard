using GitGuard.Core.Abstractions;

namespace GitGuard.Infrastructure.Git;

/// <summary>
/// <see cref="IGitAccessFactory"/> 默认实现（GGD-0042）：为桌面端登记的多个仓库分别创建
/// <see cref="GitAccess"/>，共享同一 git 可执行文件与命令超时配置。
/// </summary>
public sealed class GitAccessFactory : IGitAccessFactory
{
    private readonly string _gitExecutable;
    private readonly TimeSpan? _commandTimeout;

    /// <summary>初始化工厂。</summary>
    /// <param name="gitExecutable">git 可执行文件；默认从 PATH 解析 <c>git</c>。</param>
    /// <param name="commandTimeout">单条 git 命令超时；<see langword="null"/> 取 <see cref="GitAccess"/> 默认值（30s）。</param>
    /// <exception cref="ArgumentException"><paramref name="gitExecutable"/> 为空或全空白。</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="commandTimeout"/> 不为正数。</exception>
    public GitAccessFactory(string gitExecutable = "git", TimeSpan? commandTimeout = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gitExecutable);
        if (commandTimeout is { } timeout)
        {
            ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(timeout, TimeSpan.Zero, nameof(commandTimeout));
        }

        _gitExecutable = gitExecutable;
        _commandTimeout = commandTimeout;
    }

    /// <inheritdoc />
    public IGitAccess Create(string repositoryPath) => new GitAccess(repositoryPath, _gitExecutable, _commandTimeout);
}
