using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace GitGuard.Infrastructure.FileSystem;

/// <summary>
/// 通知 Windows 资源管理器环境变量已变更（<c>WM_SETTINGCHANGE</c> + <c>"Environment"</c>），
/// 使新启动的进程（含新终端）读取更新后的用户 PATH；通知失败不影响安装结果。
/// </summary>
[SupportedOSPlatform("windows")]
internal static partial class WindowsEnvironmentBroadcast
{
    private const int HWND_BROADCAST = 0xffff;
    private const uint WM_SETTINGCHANGE = 0x001A;
    private const uint SMTO_ABORTIFHUNG = 0x0002;
    private const uint TIMEOUT_MILLISECONDS = 1000;

    /// <summary>广播环境变量变更（尽力而为，不抛异常）。</summary>
    internal static void Notify()
    {
        try
        {
            _ = SendMessageTimeout(
                new IntPtr(HWND_BROADCAST),
                WM_SETTINGCHANGE,
                IntPtr.Zero,
                "Environment",
                SMTO_ABORTIFHUNG,
                TIMEOUT_MILLISECONDS,
                out _);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            // user32.dll 不可用（极简 Windows 容器等）：仅影响已运行进程的即时刷新，忽略
        }
    }

    [LibraryImport("user32.dll", EntryPoint = "SendMessageTimeoutW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    private static partial IntPtr SendMessageTimeout(
        IntPtr hWnd,
        uint msg,
        IntPtr wParam,
        string lParam,
        uint fuFlags,
        uint uTimeout,
        out IntPtr lpdwResult);
}
