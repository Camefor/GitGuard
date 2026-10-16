using GitGuard.Core;

namespace GitGuard.Application.Hooks;

/// <summary>
/// 安装并发锁（<c>06-API接口设计</c> §8）：以独占句柄持有 <c>&lt;git-common-dir&gt;/gitguard.install.lock</c>，
/// 冲突 → <see cref="HookException"/>(<c>INSTALL_IN_PROGRESS</c> → 退出码 50)。
/// 锁由 OS 在进程退出（含崩溃）时自动释放，无陈旧锁问题；锁文件本体保留在磁盘（空文件，不写入任何信息）。
/// </summary>
internal sealed class InstallLock : IDisposable
{
    /// <summary>锁文件名（相对 Git 公共目录）。</summary>
    internal const string LOCK_FILE_NAME = "gitguard.install.lock";

    private readonly FileStream _stream;

    /// <summary>获取锁；已被其他进程持有时抛 <see cref="HookException"/>(<c>INSTALL_IN_PROGRESS</c>)。</summary>
    /// <param name="gitCommonDirectory">Git 公共目录（<c>git rev-parse --git-common-dir</c>）。</param>
    /// <exception cref="HookException">锁冲突（退出码 50）或锁文件不可创建（退出码 3）。</exception>
    internal InstallLock(string gitCommonDirectory)
    {
        string lockPath = Path.Combine(gitCommonDirectory, LOCK_FILE_NAME);
        try
        {
            _stream = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        }
        catch (IOException ex)
        {
            throw new HookException(
                HookException.INSTALL_IN_PROGRESS,
                $"另一个 GitGuard 安装 / 卸载正在进行（锁文件：{lockPath}），请稍后重试。",
                ex);
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new HookException(
                HookException.WRITE_PERMISSION_DENIED,
                $"无法创建安装锁文件（08-权限设计 §5）：{lockPath}",
                ex);
        }
    }

    /// <inheritdoc />
    public void Dispose() => _stream.Dispose();
}
