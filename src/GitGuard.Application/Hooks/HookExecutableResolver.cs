using GitGuard.Application.Composition;
using GitGuard.Core;

namespace GitGuard.Application.Hooks;

/// <summary>
/// GitGuard CLI 可执行文件解析器（GGD-0054）：统一 CLI 与桌面端的定位顺序，避免把桌面端自身进程
/// 误写为 Hook 执行入口。解析顺序为：显式配置 <c>hooks.gitguard_executable_path</c> → 宿主提供的当前进程路径（CLI）
/// → 系统 <c>PATH</c> 中的 <c>gitguard</c> / <c>gitguard.exe</c> → 已知安装位置 → <see langword="null"/>（由桌面端提示用户选择）。
/// 显式配置指向的文件不存在时 FailClosed（<see cref="ConfigurationException"/>，CLI 退出码 13）。
/// </summary>
public sealed class HookExecutableResolver
{
    private static readonly string[] _executableNames = OperatingSystem.IsWindows()
        ? new[] { "gitguard.exe", "gitguard.cmd", "gitguard.bat", "gitguard" }
        : new[] { "gitguard" };

    private readonly string _workingDirectory;
    private readonly string? _pathEnvironment;
    private readonly IReadOnlyList<string> _knownInstallLocations;

    /// <summary>初始化解析器。</summary>
    /// <param name="workingDirectory">相对配置路径的解析基准（通常为会话工作目录 / 目标仓库根）。</param>
    /// <param name="pathEnvironment">用于测试注入的 <c>PATH</c> 值；<see langword="null"/> 读取当前进程环境。</param>
    /// <param name="knownInstallLocations">已知安装位置；<see langword="null"/> 使用平台缺省候选列表。</param>
    /// <exception cref="ArgumentException"><paramref name="workingDirectory"/> 为空或全空白。</exception>
    public HookExecutableResolver(
        string workingDirectory,
        string? pathEnvironment = null,
        IReadOnlyList<string>? knownInstallLocations = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);

        _workingDirectory = Path.GetFullPath(workingDirectory);
        _pathEnvironment = pathEnvironment;
        _knownInstallLocations = knownInstallLocations ?? GetDefaultKnownInstallLocations();
    }

    /// <summary>按统一优先级解析 GitGuard CLI 可执行文件。</summary>
    /// <param name="configuredPath">用户配置的路径（支持 <c>~</c> 与相对路径）；<see langword="null"/> 表示自动探测。</param>
    /// <param name="preferredPath">
    /// 宿主提供的优先路径：CLI 传入当前进程可执行文件；桌面端应为 <see langword="null"/>，避免把 GUI 进程写入 Hook。
    /// </param>
    /// <returns>可执行文件绝对路径；自动探测均未命中时为 <see langword="null"/>。</returns>
    /// <exception cref="ConfigurationException">显式配置的路径不存在。</exception>
    public string? Resolve(string? configuredPath, string? preferredPath = null)
    {
        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            return ResolveConfiguredPath(configuredPath);
        }

        if (!string.IsNullOrWhiteSpace(preferredPath))
        {
            return Path.GetFullPath(preferredPath);
        }

        return FindOnPath() ?? FindKnownInstallation();
    }

    private static List<string> GetDefaultKnownInstallLocations()
    {
        var locations = new List<string>();
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrWhiteSpace(home))
        {
            if (OperatingSystem.IsWindows())
            {
                locations.Add(Path.Combine(home, ".dotnet", "tools", "gitguard.exe"));
            }
            else
            {
                locations.Add(Path.Combine(home, ".dotnet", "tools", "gitguard"));
                locations.Add(Path.Combine(home, ".local", "bin", "gitguard"));
                locations.Add(Path.Combine(home, "bin", "gitguard"));
            }
        }

        if (OperatingSystem.IsWindows())
        {
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (!string.IsNullOrWhiteSpace(localAppData))
            {
                locations.Add(Path.Combine(localAppData, "Programs", "GitGuard", "gitguard.exe"));
                locations.Add(Path.Combine(localAppData, "GitGuard", "gitguard.exe"));
            }

            string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            if (!string.IsNullOrWhiteSpace(programFiles))
            {
                locations.Add(Path.Combine(programFiles, "GitGuard", "gitguard.exe"));
            }
        }
        else
        {
            locations.Add("/usr/local/bin/gitguard");
            locations.Add("/opt/homebrew/bin/gitguard");
            locations.Add("/opt/gitguard/gitguard");
        }

        return locations;
    }

    private string ResolveConfiguredPath(string configuredPath)
    {
        string expanded = GitGuardSession.ExpandUserPath(configuredPath.Trim());
        string fullPath = Path.IsPathRooted(expanded)
            ? Path.GetFullPath(expanded)
            : Path.GetFullPath(Path.Combine(_workingDirectory, expanded));
        if (!File.Exists(fullPath))
        {
            throw new ConfigurationException($"hooks.gitguard_executable_path 指向的文件不存在：{fullPath}");
        }

        return fullPath;
    }

    private string? FindOnPath()
    {
        string? pathValue = _pathEnvironment ?? Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(pathValue))
        {
            return null;
        }

        foreach (string rawDirectory in pathValue.Split(
                     Path.PathSeparator,
                     StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            string directory = rawDirectory.Trim('"');
            if (directory.Length == 0)
            {
                continue;
            }

            foreach (string executableName in _executableNames)
            {
                string candidate = Path.Combine(directory, executableName);
                if (File.Exists(candidate))
                {
                    return Path.GetFullPath(candidate);
                }
            }
        }

        return null;
    }

    private string? FindKnownInstallation()
    {
        foreach (string candidate in _knownInstallLocations)
        {
            if (!string.IsNullOrWhiteSpace(candidate) && File.Exists(candidate))
            {
                return Path.GetFullPath(candidate);
            }
        }

        return null;
    }
}
