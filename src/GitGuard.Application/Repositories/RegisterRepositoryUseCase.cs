using GitGuard.Application.Composition;
using GitGuard.Core;
using GitGuard.Core.Abstractions;
using GitGuard.Core.Domain;
using GitGuard.Core.Model;

namespace GitGuard.Application.Repositories;

/// <summary>
/// 仓库登记用例（GGD-0042 / <c>11-前端UI设计</c> §2 <c>/repos</c>）：校验路径存在、解析 Git 工作区根目录、
/// 防止重复登记并写入本机仓库注册表。展示名缺省取仓库目录名。
/// 失败统一映射为 <see cref="RepositoryException"/>，桌面端按错误码展示双语提示。
/// </summary>
public sealed class RegisterRepositoryUseCase
{
    private readonly IRepositoryStore _store;
    private readonly IGitAccessFactory _gitAccessFactory;
    private readonly IClock _clock;

    /// <summary>初始化仓库登记用例。</summary>
    /// <param name="store">仓库注册表抽象。</param>
    /// <param name="gitAccessFactory">按路径构造 Git 访问实例的工厂（构造时预检工作区）。</param>
    /// <param name="clock">时钟（登记时间取 UTC）。</param>
    /// <exception cref="ArgumentNullException">任一参数为 <see langword="null"/>。</exception>
    public RegisterRepositoryUseCase(IRepositoryStore store, IGitAccessFactory gitAccessFactory, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(gitAccessFactory);
        ArgumentNullException.ThrowIfNull(clock);

        _store = store;
        _gitAccessFactory = gitAccessFactory;
        _clock = clock;
    }

    /// <summary>登记一个本地 Git 仓库。</summary>
    /// <param name="path">用户输入的路径（支持 <c>~</c> 前缀；可为仓库内任意子目录）。</param>
    /// <param name="displayName">可选展示名；空白时取仓库目录名。</param>
    /// <returns>已持久化的仓库登记记录（<see cref="RegisteredRepository.RepoPath"/> 为工作区根目录）。</returns>
    /// <exception cref="RepositoryException">
    /// 路径为空 / 不存在 / 不是目录（<see cref="RepositoryException.REPOSITORY_VALIDATION_FAILED"/>）、
    /// 不是 Git 工作区（<see cref="RepositoryException.REPOSITORY_NOT_A_REPOSITORY"/>）或已登记
    /// （<see cref="RepositoryException.REPOSITORY_ALREADY_REGISTERED"/>）。
    /// </exception>
    /// <exception cref="SqliteException">注册表读写失败。</exception>
    public RegisteredRepository Execute(string path, string? displayName = null)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new RepositoryException(
                RepositoryException.REPOSITORY_VALIDATION_FAILED,
                "仓库路径不能为空。");
        }

        string fullPath = ResolveFullPath(path);
        if (!Directory.Exists(fullPath))
        {
            throw new RepositoryException(
                RepositoryException.REPOSITORY_VALIDATION_FAILED,
                $"目录不存在：{fullPath}");
        }

        string repositoryRoot = ResolveRepositoryRoot(_gitAccessFactory, fullPath);
        if (_store.FindByPath(repositoryRoot) is not null)
        {
            throw new RepositoryException(
                RepositoryException.REPOSITORY_ALREADY_REGISTERED,
                $"仓库已登记：{repositoryRoot}");
        }

        try
        {
            var repository = RegisteredRepository.Create(
                repositoryRoot,
                ResolveDisplayName(displayName, repositoryRoot),
                _clock.UtcNow);
            if (!_store.TryAdd(repository))
            {
                // 校验与写入之间被另一入口抢先登记：保持唯一性并以可展示错误返回
                throw new RepositoryException(
                    RepositoryException.REPOSITORY_ALREADY_REGISTERED,
                    $"仓库已登记：{repositoryRoot}");
            }

            return repository;
        }
        catch (InvariantViolationException ex)
        {
            throw new RepositoryException(RepositoryException.REPOSITORY_VALIDATION_FAILED, ex.Message, ex);
        }
    }

    private static string ResolveFullPath(string path)
    {
        try
        {
            return Path.GetFullPath(GitGuardSession.ExpandUserPath(path.Trim()));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw new RepositoryException(
                RepositoryException.REPOSITORY_VALIDATION_FAILED,
                $"仓库路径无效：{path}",
                ex);
        }
    }

    private static string ResolveDisplayName(string? displayName, string repositoryRoot)
    {
        if (!string.IsNullOrWhiteSpace(displayName))
        {
            return displayName.Trim();
        }

        string directoryName = Path.GetFileName(repositoryRoot);
        return string.IsNullOrWhiteSpace(directoryName) ? repositoryRoot : directoryName;
    }

    private static string ResolveRepositoryRoot(IGitAccessFactory gitAccessFactory, string fullPath)
    {
        try
        {
            return Path.TrimEndingDirectorySeparator(Path.GetFullPath(gitAccessFactory.Create(fullPath).GetRepositoryRoot()));
        }
        catch (GitException ex)
        {
            throw new RepositoryException(
                RepositoryException.REPOSITORY_NOT_A_REPOSITORY,
                $"所选目录不是 Git 工作区：{fullPath}",
                ex);
        }
    }
}
