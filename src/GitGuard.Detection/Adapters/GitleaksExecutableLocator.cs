namespace GitGuard.Detection.Adapters;

/// <summary>
/// Gitleaks 可执行文件定位（FR-090 / 模块设计 Secret §7）：
/// 优先使用 <c>rules.secrets.gitleaks_path</c>（支持 <c>~</c> 前缀），未配置时在 <c>PATH</c> 中查找。
/// 仅做文件存在性判断，不执行任何内容；实际可执行性由版本探测确认。
/// </summary>
public static class GitleaksExecutableLocator
{
    /// <summary>默认可执行文件名（PATH 查找使用）。</summary>
    public const string EXECUTABLE_NAME = "gitleaks";

    /// <summary>尝试定位 Gitleaks 可执行文件。</summary>
    /// <param name="configuredPath"><c>rules.secrets.gitleaks_path</c> 的原始值；空表示 PATH 查找。</param>
    /// <param name="executablePath">定位成功时的绝对路径；失败为 <see langword="null"/>。</param>
    /// <param name="failureReason">定位失败原因（面向用户的警告文案）；成功为 <see langword="null"/>。</param>
    /// <returns>找到可执行文件返回 <see langword="true"/>。</returns>
    public static bool TryLocate(
        string? configuredPath,
        out string? executablePath,
        out string? failureReason)
    {
        executablePath = null;
        failureReason = null;

        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            string expanded = ExpandUserPath(configuredPath.Trim());
            if (ContainsDirectorySeparator(expanded))
            {
                string fullPath = Path.GetFullPath(expanded);
                if (File.Exists(fullPath))
                {
                    executablePath = fullPath;
                    return true;
                }

                failureReason = $"配置的 rules.secrets.gitleaks_path 不存在：{fullPath}";
                return false;
            }

            string? foundOnPath = SearchPath(expanded);
            if (foundOnPath is not null)
            {
                executablePath = foundOnPath;
                return true;
            }

            failureReason = $"配置的 rules.secrets.gitleaks_path 无法解析：{expanded}（既不是现存文件，也不在 PATH 中）";
            return false;
        }

        string? found = SearchPath(EXECUTABLE_NAME);
        if (found is not null)
        {
            executablePath = found;
            return true;
        }

        failureReason = "未找到 gitleaks 可执行文件：rules.secrets.gitleaks_path 未配置，且 PATH 中不存在 gitleaks。";
        return false;
    }

    /// <summary>展开 <c>~</c> / <c>~/</c> 前缀为用户主目录；其他形式原样返回。</summary>
    /// <param name="path">原始路径。</param>
    /// <returns>展开后的路径。</returns>
    private static string ExpandUserPath(string path)
    {
        if (path[0] != '~' || (path.Length > 1 && path[1] != '/' && path[1] != '\\'))
        {
            return path;
        }

        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrWhiteSpace(home))
        {
            return path;
        }

        return path.Length == 1 ? home : Path.Combine(home, path.Substring(2));
    }

    /// <summary>判断路径是否含目录分隔符（含 Windows 两种写法）。</summary>
    /// <param name="path">待判断路径。</param>
    /// <returns>含分隔符返回 <see langword="true"/>。</returns>
    private static bool ContainsDirectorySeparator(string path) =>
        path.Contains('/', StringComparison.Ordinal) || path.Contains('\\', StringComparison.Ordinal);

    /// <summary>在 PATH 的各目录中查找候选可执行文件（Windows 额外尝试 <c>.exe</c>）。</summary>
    /// <param name="fileName">文件名（可含扩展名）。</param>
    /// <returns>找到的绝对路径；未找到为 <see langword="null"/>。</returns>
    private static string? SearchPath(string fileName)
    {
        string? pathVariable = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(pathVariable))
        {
            return null;
        }

        StringComparer comparer = OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;
        var seenDirectories = new HashSet<string>(comparer);
        string[] candidateNames = BuildCandidateNames(fileName);

        foreach (string rawDirectory in pathVariable.Split(Path.PathSeparator))
        {
            if (string.IsNullOrWhiteSpace(rawDirectory))
            {
                continue;
            }

            // PATH 条目可能被引号包裹（Windows 用户在含空格目录上手工配置时常见）
            string directory = rawDirectory.Trim().Trim('"');
            if (directory.Length == 0 || !seenDirectories.Add(directory))
            {
                continue;
            }

            foreach (string candidateName in candidateNames)
            {
                string candidate = Path.Combine(directory, candidateName);
                if (File.Exists(candidate))
                {
                    return Path.GetFullPath(candidate);
                }
            }
        }

        return null;
    }

    /// <summary>构造候选文件名列表（Windows 对无扩展名输入追加 <c>.exe</c>）。</summary>
    /// <param name="fileName">输入文件名。</param>
    /// <returns>候选文件名。</returns>
    private static string[] BuildCandidateNames(string fileName)
    {
        if (Path.HasExtension(fileName) || !OperatingSystem.IsWindows())
        {
            return new[] { fileName };
        }

        return new[] { fileName + ".exe", fileName };
    }
}
