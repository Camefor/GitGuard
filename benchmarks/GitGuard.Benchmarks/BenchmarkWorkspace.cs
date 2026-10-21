using System.Diagnostics;

namespace GitGuard.Benchmarks;

/// <summary>
/// 基准临时工作区：样本仓库、隔离的 LOCALAPPDATA / XDG / HOME（避免用户配置与审计库影响结果），
/// 以及驱动样本仓库的 git 辅助方法；结束时可选择保留现场。
/// </summary>
internal sealed class BenchmarkWorkspace : IDisposable
{
    private readonly bool _keep;

    /// <summary>创建临时工作区。</summary>
    /// <param name="keep">是否保留现场（<see langword="false"/> 时释放并删除）。</param>
    internal BenchmarkWorkspace(bool keep)
    {
        _keep = keep;
        Root = Path.Combine(Path.GetTempPath(), "gitguard-bench", Guid.NewGuid().ToString("N"));
        DataRoot = Path.Combine(Root, "data");
        HomeRoot = Path.Combine(Root, "home");
        Directory.CreateDirectory(DataRoot);
        Directory.CreateDirectory(HomeRoot);
    }

    /// <summary>工作区根目录。</summary>
    internal string Root { get; }

    /// <summary>隔离的数据目录根（Windows 经 LOCALAPPDATA 生效）。</summary>
    internal string DataRoot { get; }

    /// <summary>隔离的主目录（Linux / macOS 的 XDG 路径与用户配置生效）。</summary>
    internal string HomeRoot { get; }

    /// <summary>暂存区样本仓库路径。</summary>
    internal string StagedRepositoryPath => Path.Combine(Root, "repo-staged");

    /// <summary>推送样本仓库路径。</summary>
    internal string PushRepositoryPath => Path.Combine(Root, "repo-push");

    /// <summary>删除临时目录（保留现场时不删除）；git 对象文件为只读需先恢复属性。</summary>
    public void Dispose()
    {
        if (_keep)
        {
            Console.Out.WriteLine($"Workspace kept: {Root}");
            return;
        }

        try
        {
            foreach (string file in Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories))
            {
                File.SetAttributes(file, FileAttributes.Normal);
            }

            Directory.Delete(Root, recursive: true);
        }
        catch (IOException)
        {
            // 句柄偶发被系统占用：清理失败不影响基准结论
        }
        catch (UnauthorizedAccessException)
        {
            // 权限竞态：同上
        }
    }

    /// <summary>执行 git 命令并返回裁剪后的 stdout；非零退出抛 <see cref="InvalidOperationException"/>。</summary>
    /// <param name="workingDirectory">工作目录。</param>
    /// <param name="arguments">git 参数。</param>
    /// <returns>stdout（已裁剪）。</returns>
    internal static string RunGit(string workingDirectory, params string[] arguments)
    {
        var startInfo = new ProcessStartInfo("git")
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using Process process = Process.Start(startInfo) ?? throw new InvalidOperationException("无法启动 git 进程。");
        Task<string> outputTask = process.StandardOutput.ReadToEndAsync();
        Task<string> errorTask = process.StandardError.ReadToEndAsync();
        process.WaitForExit();

        string output = outputTask.GetAwaiter().GetResult();
        string error = errorTask.GetAwaiter().GetResult();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"git {string.Join(' ', arguments)} 失败（退出码 {process.ExitCode}）：{error.Trim()}");
        }

        return output.Trim();
    }

    /// <summary>在指定路径初始化样本仓库并写入确定的本地 git 配置。</summary>
    /// <param name="path">仓库根目录。</param>
    /// <returns>已初始化的仓库根目录。</returns>
    internal static string CreateRepository(string path)
    {
        Directory.CreateDirectory(path);
        RunGit(path, "init", "--quiet", "--initial-branch=master");
        RunGit(path, "config", "user.name", "GitGuard Benchmarks");
        RunGit(path, "config", "user.email", "benchmarks@example.com");
        RunGit(path, "config", "commit.gpgsign", "false");
        RunGit(path, "config", "core.autocrlf", "false");
        RunGit(path, "config", "core.hooksPath", string.Empty);
        return path;
    }

    /// <summary>构造子进程环境增量：禁用颜色 / 固定语言 / 隔离用户目录 / 忽略系统级 git 配置。</summary>
    /// <returns>环境变量增量（值为 <see langword="null"/> 表示移除）。</returns>
    internal IReadOnlyDictionary<string, string?> CreateEnvironment()
    {
        string gitConfig = Path.Combine(Root, "gitconfig");
        File.WriteAllText(gitConfig, string.Empty);

        return new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["NO_COLOR"] = "1",
            ["GITGUARD_LANG"] = "en-US",
            ["GITGUARD_SKIP"] = null,
            ["HOME"] = HomeRoot,
            ["USERPROFILE"] = HomeRoot,
            ["XDG_CONFIG_HOME"] = Path.Combine(HomeRoot, ".config"),
            ["XDG_DATA_HOME"] = Path.Combine(DataRoot, "xdg"),
            ["LOCALAPPDATA"] = DataRoot,
            ["GIT_CONFIG_NOSYSTEM"] = "1",
            ["GIT_CONFIG_GLOBAL"] = gitConfig,
        };
    }
}
