using System.Diagnostics;

namespace GitGuard.Infrastructure.Git;

/// <summary>
/// git 子进程启动配置工厂：桌面端宿主为 WinExe（无控制台），必须显式禁用子进程控制台窗口，
/// 否则每次状态刷新、扫描或 Hook 操作都会转瞬弹出 git.exe 的控制台窗口。
/// </summary>
internal static class GitProcessStartInfoFactory
{
    /// <summary>创建统一的隐藏控制台窗口启动配置。</summary>
    /// <param name="gitExecutable">git 可执行文件；默认从 PATH 解析 <c>git</c>。</param>
    /// <returns>已设置 <see cref="ProcessStartInfo.UseShellExecute"/> 为 <see langword="false"/> 且
    /// <see cref="ProcessStartInfo.CreateNoWindow"/> 为 <see langword="true"/> 的启动配置。</returns>
    /// <exception cref="ArgumentException"><paramref name="gitExecutable"/> 为空或全空白。</exception>
    internal static ProcessStartInfo Create(string gitExecutable)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gitExecutable);

        return new ProcessStartInfo(gitExecutable)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
        };
    }
}
