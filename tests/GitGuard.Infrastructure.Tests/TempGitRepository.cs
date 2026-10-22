using System.Diagnostics;

namespace GitGuard.Infrastructure.Tests;

/// <summary>
/// 集成测试用真实临时 Git 仓库（16-测试方案：集成测试调用真实 git 二进制，禁止 mock）。
/// 每个实例使用独立目录；构造时写入本地配置，中和本机全局配置（身份 / 签名 / core.autocrlf / core.hooksPath），保证测试确定性。
/// </summary>
internal sealed class TempGitRepository : IDisposable
{
    public TempGitRepository()
    {
        Root = Path.Combine(Path.GetTempPath(), "gitguard-infra-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);

        RunGit("init", "--quiet");
        RunGit("config", "user.name", "GitGuard Test");
        RunGit("config", "user.email", "gitguard-test@example.com");
        RunGit("config", "commit.gpgsign", "false");
        RunGit("config", "core.autocrlf", "false");

        // 显式置空：中和本机全局 core.hooksPath，使「未配置 → null」断言确定成立
        RunGit("config", "core.hooksPath", string.Empty);
    }

    /// <summary>仓库根目录绝对路径。</summary>
    public string Root { get; }

    /// <summary>写入文件（相对仓库根目录；自动创建父目录）。</summary>
    public void WriteFile(string relativePath, string content)
    {
        string fullPath = Path.Combine(Root, relativePath);
        string? directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(fullPath, content);
    }

    /// <summary>暂存指定路径（相对仓库根目录）。</summary>
    public void Stage(params string[] relativePaths)
    {
        RunGit(BuildAddArguments(relativePaths));
    }

    /// <summary>提交当前已暂存内容，返回新提交 SHA。</summary>
    public string Commit(string message)
    {
        RunGit("commit", "--quiet", "-m", message);
        return RunGit("rev-parse", "HEAD");
    }

    /// <summary>执行任意 git 命令并返回裁剪后的 stdout；非零退出抛 <see cref="InvalidOperationException"/>（测试装配错误）。</summary>
    public string RunGit(params string[] arguments)
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

    private static string[] BuildAddArguments(string[] relativePaths)
    {
        string[] arguments = new string[relativePaths.Length + 2];
        arguments[0] = "add";
        arguments[1] = "--";
        relativePaths.CopyTo(arguments, 2);
        return arguments;
    }

    /// <summary>git 对象文件为只读（mode 444），删除前恢复普通属性。</summary>
    private void ClearReadOnlyAttributes()
    {
        foreach (string file in Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories))
        {
            File.SetAttributes(file, FileAttributes.Normal);
        }
    }
}

/// <summary>集成测试用临时空目录（非 Git 仓库场景）。</summary>
internal sealed class TempDirectory : IDisposable
{
    public TempDirectory()
    {
        Root = Path.Combine(Path.GetTempPath(), "gitguard-infra-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);
    }

    /// <summary>目录绝对路径。</summary>
    public string Root { get; }

    public void Dispose()
    {
        try
        {
            Directory.Delete(Root, recursive: true);
        }
        catch (IOException)
        {
            // 清理失败不影响测试结论，留待系统清理临时目录
        }
        catch (UnauthorizedAccessException)
        {
            // 权限竞态：同上
        }
    }
}
