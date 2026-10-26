using GitGuard.Core;
using GitGuard.Core.Abstractions;

namespace GitGuard.Infrastructure.FileSystem;

/// <summary>按当前平台创建用户级 PATH 管理器（GGD-0061）。</summary>
public static class PathManagerFactory
{
    /// <summary>创建当前平台的 PATH 管理器。</summary>
    /// <param name="paths">用户数据路径（状态文件位置）。</param>
    /// <returns>Windows / Linux / macOS 对应的 <see cref="IPathManager"/> 实现。</returns>
    /// <exception cref="ArgumentNullException"><paramref name="paths"/> 为 <see langword="null"/>。</exception>
    /// <exception cref="PathException">当前平台不支持自动 PATH 管理。</exception>
    public static IPathManager Create(UserDataPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        if (OperatingSystem.IsWindows())
        {
            return new WindowsPathManager(paths);
        }

        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
        {
            return new UnixPathManager(paths);
        }

        throw new PathException(
            PathException.UNSUPPORTED,
            "当前平台不支持自动 PATH 管理；请手工把 GitGuard 可执行文件目录加入 PATH。");
    }
}
