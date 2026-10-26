using System.Runtime.Versioning;
using GitGuard.Core;
using Microsoft.Win32;

namespace GitGuard.Infrastructure.FileSystem;

/// <summary>
/// Windows 用户环境变量注册表访问（GGD-0061）：<c>HKCU\Environment\Path</c>。
/// 读取时使用 <see cref="RegistryValueOptions.DoNotExpandEnvironmentNames"/> 保留 <c>%VAR%</c> 引用；
/// 写入统一为 <see cref="RegistryValueKind.ExpandString"/>，且绝不调用 <c>setx</c>（会截断 PATH）。
/// 测试通过注入临时注册表子键使用同一实现，不触碰真实 PATH。
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class WindowsRegistryPathStore
{
    /// <summary>用户环境变量注册表键（相对 <c>HKEY_CURRENT_USER</c>）。</summary>
    internal const string DEFAULT_KEY_PATH = "Environment";

    /// <summary>PATH 值名。</summary>
    internal const string PATH_VALUE_NAME = "Path";

    private readonly string _keyPath;

    /// <summary>初始化注册表访问器。</summary>
    /// <param name="keyPath">相对 <c>HKEY_CURRENT_USER</c> 的键路径；默认 <c>Environment</c>，测试注入临时子键。</param>
    /// <exception cref="ArgumentException"><paramref name="keyPath"/> 为空或全空白。</exception>
    internal WindowsRegistryPathStore(string? keyPath = null)
    {
        string resolved = keyPath ?? DEFAULT_KEY_PATH;
        ArgumentException.ThrowIfNullOrWhiteSpace(resolved);
        _keyPath = resolved;
    }

    /// <summary>读取用户 PATH 原始值（不展开环境变量）。</summary>
    /// <returns>PATH 值；键或值不存在时返回 <see langword="null"/>。</returns>
    /// <exception cref="PathException">注册表不可读。</exception>
    internal string? Read()
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(_keyPath, writable: false);
            return key?.GetValue(PATH_VALUE_NAME, null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            throw new PathException(
                PathException.WRITE_PERMISSION_DENIED,
                $"无法读取用户环境变量注册表：{ex.Message}",
                ex);
        }
    }

    /// <summary>写入用户 PATH（<c>REG_EXPAND_SZ</c>，不展开变量引用）。</summary>
    /// <param name="value">新的 PATH 值。</param>
    /// <exception cref="PathException">注册表不可写。</exception>
    internal void Write(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        try
        {
            using RegistryKey key = Registry.CurrentUser.CreateSubKey(_keyPath, writable: true)
                ?? throw new PathException(
                    PathException.WRITE_PERMISSION_DENIED,
                    $"无法打开用户环境变量注册表键 HKCU\\{_keyPath}。");
            key.SetValue(PATH_VALUE_NAME, value, RegistryValueKind.ExpandString);
        }
        catch (PathException)
        {
            throw;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            throw new PathException(
                PathException.WRITE_PERMISSION_DENIED,
                $"无法写入用户环境变量注册表：{ex.Message}",
                ex);
        }
    }
}
