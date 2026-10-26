using GitGuard.Core;
using GitGuard.Core.Abstractions;
using GitGuard.Core.Model;

namespace GitGuard.Infrastructure.FileSystem;

/// <summary>
/// Unix（Linux / macOS）用户级 PATH 管理（GGD-0061）：在 <c>~/.local/bin</c> 创建指向 GitGuard 可执行文件的
/// <c>gitguard</c> 符号链接；<c>--update-profile</c> 时按 <c>SHELL</c> 向 bash / zsh / sh / fish 的 profile
/// 写入带标记块（可逆）的 PATH 追加语句。不修改系统级 PATH，也不需要 <c>sudo</c>。
/// </summary>
internal sealed class UnixPathManager : IPathManager
{
    private readonly string _homeDirectory;
    private readonly string? _shellPath;
    private readonly PathInstallStateStore _stateStore;
    private readonly Func<string?> _currentPathProvider;

    /// <summary>初始化 Unix PATH 管理器。</summary>
    /// <param name="paths">用户数据路径（状态文件位置）。</param>
    /// <exception cref="PathException">无法解析用户主目录。</exception>
    internal UnixPathManager(UserDataPaths paths)
        : this(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            Environment.GetEnvironmentVariable("SHELL"),
            paths.PathInstallStatePath,
            static () => Environment.GetEnvironmentVariable("PATH"))
    {
    }

    /// <summary>测试注入构造：使用显式 HOME / SHELL / 状态文件与 PATH 读取器。</summary>
    /// <param name="homeDirectory">用户主目录。</param>
    /// <param name="shellPath"><c>SHELL</c> 环境变量值。</param>
    /// <param name="statePath">状态文件路径。</param>
    /// <param name="currentPathProvider">当前进程 PATH 读取器。</param>
    /// <exception cref="PathException">主目录为空。</exception>
    internal UnixPathManager(
        string homeDirectory,
        string? shellPath,
        string statePath,
        Func<string?> currentPathProvider)
    {
        ArgumentNullException.ThrowIfNull(currentPathProvider);
        if (string.IsNullOrWhiteSpace(homeDirectory))
        {
            throw new PathException(
                PathException.UNSUPPORTED,
                "无法解析用户主目录，无法管理 ~/.local/bin 符号链接。");
        }

        _homeDirectory = Path.GetFullPath(homeDirectory);
        _shellPath = shellPath;
        _stateStore = new PathInstallStateStore(statePath);
        _currentPathProvider = currentPathProvider;
    }

    /// <summary>平台标识：<c>macos</c> / <c>linux</c>。</summary>
    internal static string PlatformName => OperatingSystem.IsMacOS() ? "macos" : "linux";

    /// <summary>用户 PATH 目录（<c>~/.local/bin</c>）。</summary>
    internal string TargetDirectory => Path.Combine(_homeDirectory, ".local", "bin");

    /// <summary>GitGuard 符号链接路径（<c>~/.local/bin/gitguard</c>）。</summary>
    internal string LinkPath => Path.Combine(TargetDirectory, "gitguard");

    /// <inheritdoc />
    public PathInstallReport Install(string executablePath, bool force, bool updateProfile)
    {
        EnsureSupported();
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);

        string executableFullPath = Path.GetFullPath(executablePath);
        string linkPath = LinkPath;
        bool alreadyPresent = false;
        bool pathUpdated = false;
        string? backupPath = null;

        try
        {
            Directory.CreateDirectory(TargetDirectory);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new PathException(
                PathException.WRITE_PERMISSION_DENIED,
                $"无法创建目录 {TargetDirectory}：{ex.Message}",
                ex);
        }

        if (PathExists(linkPath))
        {
            string? linkTarget = TryResolveLinkTarget(linkPath);
            if (linkTarget is not null && PathsEqual(linkTarget, executableFullPath))
            {
                alreadyPresent = true;
            }
            else
            {
                if (!force)
                {
                    throw new PathException(
                        PathException.ENTRY_CONFLICT,
                        $"{linkPath} 已存在且不是指向当前 GitGuard 的符号链接；确认来源后使用 --force 备份接管。");
                }

                if (Directory.Exists(linkPath))
                {
                    throw new PathException(
                        PathException.ENTRY_CONFLICT,
                        $"{linkPath} 是目录，无法作为符号链接接管；请先手工清理。");
                }

                backupPath = linkPath + ".local.bak";
                if (PathExists(backupPath))
                {
                    throw new PathException(
                        PathException.ENTRY_CONFLICT,
                        $"备份文件 {backupPath} 已存在，拒绝覆盖；请先手工清理。");
                }

                try
                {
                    File.Move(linkPath, backupPath);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    throw new PathException(
                        PathException.WRITE_PERMISSION_DENIED,
                        $"无法备份既有符号链接 {linkPath}：{ex.Message}",
                        ex);
                }
            }
        }

        if (!alreadyPresent)
        {
            try
            {
                File.CreateSymbolicLink(linkPath, executableFullPath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                throw new PathException(
                    PathException.WRITE_PERMISSION_DENIED,
                    $"无法创建符号链接 {linkPath}：{ex.Message}",
                    ex);
            }

            pathUpdated = true;
        }

        PathInstallState? previous = _stateStore.Load();
        string? profilePath = previous?.ProfilePath;
        bool profileUpdated = false;
        if (updateProfile)
        {
            (profilePath, profileUpdated) = UpdateProfile();
        }

        _stateStore.Save(new PathInstallState(
            PlatformName,
            TargetDirectory,
            executableFullPath,
            profilePath,
            DateTimeOffset.UtcNow));

        return new PathInstallReport(
            PlatformName,
            TargetDirectory,
            executableFullPath,
            alreadyPresent,
            pathUpdated,
            profilePath,
            profileUpdated,
            backupPath,
            IsEffective());
    }

    /// <inheritdoc />
    public PathUninstallReport Uninstall(string executablePath)
    {
        EnsureSupported();
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);

        PathInstallState? state = _stateStore.Load();
        string targetDirectory = state?.TargetDirectory ?? TargetDirectory;
        string linkPath = Path.Combine(targetDirectory, "gitguard");
        string executableFullPath = Path.GetFullPath(executablePath);
        bool linkRemoved = false;

        if (PathExists(linkPath))
        {
            string? linkTarget = TryResolveLinkTarget(linkPath);
            bool managed = state is not null
                ? linkTarget is not null && PathsEqual(linkTarget, state.ExecutablePath)
                : linkTarget is not null && PathsEqual(linkTarget, executableFullPath);
            if (managed)
            {
                try
                {
                    File.Delete(linkPath);
                    linkRemoved = true;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    throw new PathException(
                        PathException.WRITE_PERMISSION_DENIED,
                        $"无法删除符号链接 {linkPath}：{ex.Message}",
                        ex);
                }
            }
        }

        bool profileBlockRemoved = false;
        if (state?.ProfilePath is { } profilePath && File.Exists(profilePath))
        {
            try
            {
                string content = File.ReadAllText(profilePath);
                (string updated, bool changed) = ShellProfileEditor.RemoveBlock(content);
                if (changed)
                {
                    File.WriteAllText(profilePath, updated);
                    profileBlockRemoved = true;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                throw new PathException(
                    PathException.WRITE_PERMISSION_DENIED,
                    $"无法更新 Shell profile {profilePath}：{ex.Message}",
                    ex);
            }
        }

        _stateStore.Delete();
        bool notInstalled = state is null && !linkRemoved && !profileBlockRemoved;
        return new PathUninstallReport(
            PlatformName,
            targetDirectory,
            pathUpdated: false,
            linkRemoved,
            profileBlockRemoved,
            notInstalled);
    }

    /// <inheritdoc />
    public PathStatusReport GetStatus(string executablePath)
    {
        EnsureSupported();
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);

        PathInstallState? state = _stateStore.Load();
        string targetDirectory = state?.TargetDirectory ?? TargetDirectory;
        string linkPath = Path.Combine(targetDirectory, "gitguard");
        string executableFullPath = Path.GetFullPath(executablePath);
        string? linkTarget = TryResolveLinkTarget(linkPath);
        bool installed = linkTarget is not null
            && PathsEqual(linkTarget, state?.ExecutablePath ?? executableFullPath);
        string? profilePath = state?.ProfilePath;
        bool profileConfigured = IsProfileConfigured(profilePath);

        return new PathStatusReport(
            PlatformName,
            targetDirectory,
            state?.ExecutablePath ?? executableFullPath,
            installed,
            pathEntryPresent: profileConfigured,
            effective: IsEffective(),
            profilePath,
            profileConfigured);
    }

    /// <summary>只读探测 profile 标记块：读取失败（权限 / IO）按未配置处理，不阻塞状态查询。</summary>
    private static bool IsProfileConfigured(string? profilePath)
    {
        if (profilePath is null || !File.Exists(profilePath))
        {
            return false;
        }

        try
        {
            return ShellProfileEditor.ContainsBlock(File.ReadAllText(profilePath));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static void EnsureSupported()
    {
        if (OperatingSystem.IsWindows())
        {
            throw new PathException(
                PathException.UNSUPPORTED,
                "Unix PATH 管理器不能在 Windows 上使用。");
        }
    }

    private static bool PathExists(string path) =>
        File.Exists(path)
        || Directory.Exists(path)
        || new FileInfo(path).LinkTarget is not null;

    private static string? TryResolveLinkTarget(string path)
    {
        try
        {
            FileSystemInfo? target = File.ResolveLinkTarget(path, returnFinalTarget: false);
            return target?.FullName;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static bool PathsEqual(string left, string right) =>
        string.Equals(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(left)),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)),
            StringComparison.Ordinal);

    private (string ProfilePath, bool Updated) UpdateProfile()
    {
        ShellKind kind = ShellProfileEditor.ResolveShellKind(_shellPath);
        string profilePath = ShellProfileEditor.ResolveProfilePath(_homeDirectory, _shellPath);
        try
        {
            string? content = File.Exists(profilePath) ? File.ReadAllText(profilePath) : null;
            (string updated, bool changed) = ShellProfileEditor.AddBlock(content, kind);
            if (changed)
            {
                string? directory = Path.GetDirectoryName(profilePath);
                if (directory is not null)
                {
                    Directory.CreateDirectory(directory);
                }

                File.WriteAllText(profilePath, updated);
            }

            return (profilePath, changed);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new PathException(
                PathException.WRITE_PERMISSION_DENIED,
                $"无法写入 Shell profile {profilePath}：{ex.Message}",
                ex);
        }
    }

    private bool IsEffective() =>
        PathEnvironmentVariable.Contains(
            PathEnvironmentVariable.Split(_currentPathProvider(), ':'),
            TargetDirectory,
            StringComparison.Ordinal);
}
