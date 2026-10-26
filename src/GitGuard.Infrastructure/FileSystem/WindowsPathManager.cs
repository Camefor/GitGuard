using System.Runtime.Versioning;
using GitGuard.Core;
using GitGuard.Core.Abstractions;
using GitGuard.Core.Model;

namespace GitGuard.Infrastructure.FileSystem;

/// <summary>
/// Windows 用户级 PATH 管理（GGD-0061）：把 GitGuard 可执行文件所在目录追加到 <c>HKCU\Environment\Path</c>，
/// 使用 <c>REG_EXPAND_SZ</c> 保留既有 <c>%VAR%</c> 引用、大小写不敏感去重、写入后广播 <c>WM_SETTINGCHANGE</c>。
/// 只修改当前用户环境，不需要管理员权限，也不触碰系统级 <c>HKLM</c> PATH。
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class WindowsPathManager : IPathManager
{
    /// <summary>平台标识。</summary>
    internal const string PLATFORM = "windows";

    private readonly WindowsRegistryPathStore _store;
    private readonly PathInstallStateStore _stateStore;
    private readonly Func<string?> _currentPathProvider;

    /// <summary>初始化 Windows PATH 管理器。</summary>
    /// <param name="paths">用户数据路径（状态文件位置）。</param>
    internal WindowsPathManager(UserDataPaths paths)
        : this(
            new WindowsRegistryPathStore(),
            paths.PathInstallStatePath,
            static () => Environment.GetEnvironmentVariable("PATH"))
    {
    }

    /// <summary>测试注入构造：使用临时注册表键与显式状态文件，不触碰真实用户 PATH。</summary>
    /// <param name="store">注册表路径存储。</param>
    /// <param name="statePath">状态文件路径。</param>
    /// <param name="currentPathProvider">当前进程 PATH 读取器。</param>
    internal WindowsPathManager(
        WindowsRegistryPathStore store,
        string statePath,
        Func<string?> currentPathProvider)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(currentPathProvider);

        _store = store;
        _stateStore = new PathInstallStateStore(statePath);
        _currentPathProvider = currentPathProvider;
    }

    /// <inheritdoc />
    public PathInstallReport Install(string executablePath, bool force, bool updateProfile)
    {
        _ = force; // Windows 仅修改 PATH 值，无文件接管语义
        _ = updateProfile; // Windows 不涉及 Shell profile

        string targetDirectory = ResolveTargetDirectory(executablePath);
        string? pathValue = _store.Read();
        IReadOnlyList<string> entries = PathEnvironmentVariable.Split(pathValue, ';');
        bool alreadyPresent = PathEnvironmentVariable.Contains(entries, targetDirectory, StringComparison.OrdinalIgnoreCase);
        bool pathUpdated = false;
        if (!alreadyPresent)
        {
            string updated = PathEnvironmentVariable.Append(pathValue, ';', targetDirectory, StringComparison.OrdinalIgnoreCase);
            if (updated.Length > PathEnvironmentVariable.WINDOWS_MAX_LENGTH)
            {
                throw new PathException(
                    PathException.TOO_LONG,
                    $"用户 PATH 追加后将超过 {PathEnvironmentVariable.WINDOWS_MAX_LENGTH} 个字符；请先清理不再使用的条目。");
            }

            _store.Write(updated);
            pathUpdated = true;
            WindowsEnvironmentBroadcast.Notify();
        }

        _stateStore.Save(new PathInstallState(
            PLATFORM,
            targetDirectory,
            Path.GetFullPath(executablePath),
            profilePath: null,
            DateTimeOffset.UtcNow));

        bool effective = IsEffective(targetDirectory);
        return new PathInstallReport(
            PLATFORM,
            targetDirectory,
            Path.GetFullPath(executablePath),
            alreadyPresent,
            pathUpdated,
            profilePath: null,
            profileUpdated: false,
            backupPath: null,
            effective);
    }

    /// <inheritdoc />
    public PathUninstallReport Uninstall(string executablePath)
    {
        PathInstallState? state = _stateStore.Load();
        string targetDirectory = state?.TargetDirectory ?? ResolveTargetDirectory(executablePath);
        string? pathValue = _store.Read();
        string updated = PathEnvironmentVariable.Remove(
            pathValue,
            ';',
            targetDirectory,
            StringComparison.OrdinalIgnoreCase,
            out bool removed);
        if (removed)
        {
            _store.Write(updated);
            WindowsEnvironmentBroadcast.Notify();
        }

        _stateStore.Delete();
        bool notInstalled = !removed && state is null;
        return new PathUninstallReport(
            PLATFORM,
            targetDirectory,
            pathUpdated: removed,
            linkRemoved: false,
            profileBlockRemoved: false,
            notInstalled);
    }

    /// <inheritdoc />
    public PathStatusReport GetStatus(string executablePath)
    {
        PathInstallState? state = _stateStore.Load();
        string targetDirectory = state?.TargetDirectory ?? ResolveTargetDirectory(executablePath);
        IReadOnlyList<string> entries = PathEnvironmentVariable.Split(_store.Read(), ';');
        bool pathEntryPresent = PathEnvironmentVariable.Contains(entries, targetDirectory, StringComparison.OrdinalIgnoreCase);
        return new PathStatusReport(
            PLATFORM,
            targetDirectory,
            state?.ExecutablePath ?? Path.GetFullPath(executablePath),
            installed: state is not null || pathEntryPresent,
            pathEntryPresent,
            effective: IsEffective(targetDirectory),
            profilePath: null,
            profileConfigured: false);
    }

    private static string ResolveTargetDirectory(string executablePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);

        string fullPath = Path.GetFullPath(executablePath);
        string? directory = Path.GetDirectoryName(fullPath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new PathException(
                PathException.UNSUPPORTED,
                $"无法从可执行文件路径解析目录：{executablePath}");
        }

        return Path.TrimEndingDirectorySeparator(directory);
    }

    private bool IsEffective(string targetDirectory) =>
        PathEnvironmentVariable.Contains(
            PathEnvironmentVariable.Split(_currentPathProvider(), ';'),
            targetDirectory,
            StringComparison.OrdinalIgnoreCase);
}
