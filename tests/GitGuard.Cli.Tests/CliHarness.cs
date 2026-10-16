using System.Diagnostics;

namespace GitGuard.Cli.Tests;

/// <summary>
/// CLI 端到端测试夹具：真实临时 Git 仓库 + 隔离的数据目录 / 用户配置目录 + 内存标准流，
/// 经 <see cref="CliApplication.RunAsync(string[], CliHost, CancellationToken)"/> 驱动完整命令链
/// （16-测试方案 §2：集成测试调用真实 git 二进制，禁止 mock）。
/// </summary>
internal sealed class CliHarness : IDisposable
{
    /// <summary>注入 <c>version</c> 输出与 Hook 标记行的固定版本（可断言，不随程序集版本漂移）。</summary>
    internal const string TEST_VERSION = "9.9.9-test";

    /// <summary>注入 Hook 分发脚本的可执行文件路径（占位值：本机 gitguard 真实路径不参与断言）。</summary>
    internal const string TEST_EXECUTABLE = "/opt/gitguard/gitguard";

    private readonly StringWriter _output = new StringWriter();
    private readonly StringWriter _error = new StringWriter();

    /// <summary>初始化夹具。</summary>
    /// <param name="isRepository">是否把工作目录初始化为 Git 仓库（<see langword="false"/> 用于非仓库场景）。</param>
    internal CliHarness(bool isRepository = true)
    {
        Root = Path.Combine(Path.GetTempPath(), "gitguard-cli-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);

        DataDirectory = Path.Combine(Root, "data");
        UserConfigDirectory = Path.Combine(Root, "config");
        Directory.CreateDirectory(DataDirectory);
        Directory.CreateDirectory(UserConfigDirectory);

        if (!isRepository)
        {
            return;
        }

        RunGit("init", "--quiet");
        RunGit("config", "user.name", "GitGuard Test");
        RunGit("config", "user.email", "gitguard-test@example.com");
        RunGit("config", "commit.gpgsign", "false");
        RunGit("config", "core.autocrlf", "false");

        // 显式置空：中和本机全局 core.hooksPath
        RunGit("config", "core.hooksPath", string.Empty);
    }

    /// <summary>工作目录（仓库根目录；非仓库场景时即普通临时目录）。</summary>
    internal string Root { get; }

    /// <summary>隔离的用户数据目录根（audit.db / cache / logs / crash）。</summary>
    internal string DataDirectory { get; }

    /// <summary>隔离的用户配置目录（<c>config.yml</c> 的父目录）。</summary>
    internal string UserConfigDirectory { get; }

    /// <summary>已捕获的标准输出（<c>--format json</c> 的机器可读输出）。</summary>
    internal string Output => _output.ToString();

    /// <summary>已捕获的标准错误（文本报告、日志与用法错误）。</summary>
    internal string Error => _error.ToString();

    /// <summary>清理临时目录（还原只读属性后递归删除）。</summary>
    public void Dispose()
    {
        ClearReadOnlyAttributes();
        try
        {
            Directory.Delete(Root, recursive: true);
        }
        catch (IOException)
        {
            // 句柄偶发被系统占用：清理失败不影响测试结论，留待系统清理临时目录
        }
        catch (UnauthorizedAccessException)
        {
            // 权限竞态：同上
        }
    }

    /// <summary>生成金丝雀 AWS AKID：十六位随机大写十六进制，仅测试内构造（不上传、不入库）。</summary>
    /// <returns>金丝雀 Token 文本。</returns>
    internal static string CreateCanaryToken() =>
        "AKIA" + Guid.NewGuid().ToString("N").Substring(0, 16).ToUpperInvariant();

    /// <summary>写入文本文件（相对仓库根目录；自动创建父目录）。</summary>
    /// <param name="relativePath">相对路径。</param>
    /// <param name="content">文件内容。</param>
    internal void WriteFile(string relativePath, string content) =>
        File.WriteAllText(ResolvePath(relativePath), content);

    /// <summary>写入项目级配置 <c>.gitguard.yml</c>。</summary>
    /// <param name="yaml">YAML 文本。</param>
    internal void WriteProjectConfig(string yaml) => WriteFile(".gitguard.yml", yaml);

    /// <summary>暂存指定路径（相对仓库根目录）。</summary>
    /// <param name="relativePaths">待暂存路径。</param>
    internal void Stage(params string[] relativePaths)
    {
        string[] arguments = new string[relativePaths.Length + 2];
        arguments[0] = "add";
        arguments[1] = "--";
        relativePaths.CopyTo(arguments, 2);
        RunGit(arguments);
    }

    /// <summary>提交当前已暂存内容，返回新提交 SHA。</summary>
    /// <param name="message">提交信息。</param>
    /// <returns>提交 SHA。</returns>
    internal string Commit(string message)
    {
        RunGit("commit", "--quiet", "-m", message);
        return RunGit("rev-parse", "HEAD");
    }

    /// <summary>执行任意 git 命令并返回裁剪后的 stdout；非零退出抛 <see cref="InvalidOperationException"/>（测试装配错误）。</summary>
    /// <param name="arguments">git 参数。</param>
    /// <returns>stdout（已裁剪）。</returns>
    internal string RunGit(params string[] arguments)
    {
        var startInfo = new ProcessStartInfo("git")
        {
            WorkingDirectory = Root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using Process process = Process.Start(startInfo) ?? throw new InvalidOperationException("无法启动 git 进程。");
        Task<string> standardOutput = process.StandardOutput.ReadToEndAsync();
        Task<string> standardError = process.StandardError.ReadToEndAsync();
        process.WaitForExit();

        string output = standardOutput.GetAwaiter().GetResult();
        string error = standardError.GetAwaiter().GetResult();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"git {string.Join(' ', arguments)} 失败（退出码 {process.ExitCode}）：{error}");
        }

        return output.Trim();
    }

    /// <summary>按给定参数运行 CLI 并返回退出码（标准输入为空）。</summary>
    /// <param name="args">命令行参数（不含可执行文件名）。</param>
    /// <returns>进程退出码。</returns>
    internal Task<int> RunAsync(params string[] args) => RunAsync(args, string.Empty);

    /// <summary>按给定参数运行 CLI 并返回退出码。</summary>
    /// <param name="args">命令行参数（不含可执行文件名）。</param>
    /// <param name="standardInput">标准输入内容（<c>scan --push</c> 推送信息行）。</param>
    /// <returns>进程退出码。</returns>
    internal Task<int> RunAsync(string[] args, string standardInput)
    {
        var host = new CliHost
        {
            WorkingDirectory = Root,
            Output = _output,
            Error = _error,
            Input = new StringReader(standardInput),
            DataDirectory = DataDirectory,
            UserConfigDirectory = UserConfigDirectory,
            GitGuardVersion = TEST_VERSION,
            ExecutablePath = TEST_EXECUTABLE,
            IsErrorTerminal = false,
            NoColorEnvironment = true,
        };
        return CliApplication.RunAsync(args, host, CancellationToken.None);
    }

    /// <summary>仓库内相对路径的绝对路径（如 <c>.git/hooks/pre-commit</c>）。</summary>
    /// <param name="relativePath">相对仓库根目录的路径。</param>
    /// <returns>绝对路径。</returns>
    internal string ResolvePath(string relativePath) => Path.Combine(Root, relativePath);

    /// <summary>git 对象文件为只读（mode 444），删除前恢复普通属性。</summary>
    private void ClearReadOnlyAttributes()
    {
        foreach (string file in Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories))
        {
            File.SetAttributes(file, FileAttributes.Normal);
        }
    }
}
