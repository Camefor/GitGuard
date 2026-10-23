namespace GitGuard.Core.Abstractions;

/// <summary>
/// 针对任意路径创建 <see cref="IGitAccess"/> 的工厂（GGD-0042）：桌面端需要在单个进程内为多个已登记仓库
/// 构造独立的 Git 访问实例，而组合根默认只绑定一个工作目录。实现负责在构造时预检仓库有效性（FailClosed）。
/// </summary>
public interface IGitAccessFactory
{
    /// <summary>为指定路径创建 Git 访问实例。</summary>
    /// <param name="repositoryPath">仓库内任意目录（通常为工作区根目录）；实现统一切换到工作区根目录执行命令。</param>
    /// <returns>构造时已通过 <c>git rev-parse --show-toplevel</c> 预检的 Git 访问实例。</returns>
    /// <exception cref="ArgumentException"><paramref name="repositoryPath"/> 为空或全空白。</exception>
    /// <exception cref="GitException">路径不构成 Git 工作区、git 缺失或命令失败时抛出（FailClosed）。</exception>
    IGitAccess Create(string repositoryPath);
}
