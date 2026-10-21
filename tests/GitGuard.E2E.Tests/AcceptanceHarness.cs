using System.Diagnostics;
using System.Globalization;
using System.Text;
using GitGuard.Cli;
using Microsoft.Data.Sqlite;

namespace GitGuard.E2E.Tests;

/// <summary>
/// MVP 验收场景夹具（16-测试方案 §12）：每个实例是一个隔离的临时工作区 —— 真实 Git 仓库 + 真实 bare 远端 +
/// 隔离的用户数据 / 配置目录 + 构建产物中的真实 <c>gitguard</c> 可执行文件；Hook 分发脚本固化该可执行文件，
/// 因此 <c>git commit</c> / <c>git push</c> 会拉起真实扫描器子进程（16-测试方案 §2：集成测试禁止 mock）。
/// <para>
/// 隔离机制（探针实证）：数据目录经子进程环境变量重定向 —— Windows <c>LOCALAPPDATA</c>、Unix <c>XDG_DATA_HOME</c>、
/// macOS 经 <c>HOME</c>；用户配置目录 Unix 经 <c>HOME</c> 完全隔离，Windows 的主目录由 shell API 解析、
/// 无法被环境变量重定向，故真实 <c>~/.config/gitguard/config.yml</c> 存在时跳过整套场景（见构造函数）。
/// </para>
/// </summary>
internal sealed class AcceptanceHarness : IDisposable
{
    /// <summary>进程内模式的注入版本（仅保证确定性，不参与断言）。</summary>
    internal const string TEST_VERSION = "9.9.9-test";

    private const string REPOSITORY_DIRECTORY_NAME = "repo";
    private const string HOME_DIRECTORY_NAME = "home";
    private const string LOCAL_APP_DATA_DIRECTORY_NAME = "localappdata";
    private const string XDG_DATA_DIRECTORY_NAME = "xdgdata";
    private const string REMOTES_DIRECTORY_NAME = "remotes";
    private const string GITGUARD_EXECUTABLE_NAME = "gitguard";

    private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    private readonly string _homeDirectory;
    private readonly string _localAppDataDirectory;
    private readonly string _xdgDataDirectory;

    /// <summary>初始化隔离工作区：目录树、平台数据目录推导、Git 仓库基线配置（含 hooks 目录钉住）。</summary>
    internal AcceptanceHarness()
    {
        // 先于任何落盘检查：跳过时（Assert.Skip 抛异常）不留下半成品目录
        EnsureHermeticUserConfig();

        Root = Path.Combine(Path.GetTempPath(), "gitguard-e2e", Guid.NewGuid().ToString("N"));
        _homeDirectory = Path.Combine(Root, HOME_DIRECTORY_NAME);
        _localAppDataDirectory = Path.Combine(Root, LOCAL_APP_DATA_DIRECTORY_NAME);
        _xdgDataDirectory = Path.Combine(Root, XDG_DATA_DIRECTORY_NAME);
        RepositoryPath = Path.Combine(Root, REPOSITORY_DIRECTORY_NAME);

        Directory.CreateDirectory(_homeDirectory);
        Directory.CreateDirectory(_localAppDataDirectory);
        Directory.CreateDirectory(_xdgDataDirectory);
        Directory.CreateDirectory(RepositoryPath);

        DataDirectory = ResolveDataDirectory();
        UserConfigDirectory = Path.Combine(_homeDirectory, ".config", "gitguard");
        AuditDatabasePath = Path.Combine(DataDirectory, "audit.db");

        RunGitOrThrow("init", "--quiet", "--initial-branch=master");
        RunGitOrThrow("config", "user.name", "GitGuard E2E");
        RunGitOrThrow("config", "user.email", "gitguard-e2e@example.com");
        RunGitOrThrow("config", "commit.gpgsign", "false");
        RunGitOrThrow("config", "core.autocrlf", "false");

        // 钉住 hooks 目录（绝对路径）：中和本机全局 core.hooksPath 对安装目标与触发链的干扰
        RunGitOrThrow("config", "core.hooksPath", ToPosixPath(Path.Combine(RepositoryPath, ".git", "hooks")));
    }

    /// <summary>测试输出目录中的真实 GitGuard 可执行文件（Hook 分发脚本的固化目标）。</summary>
    internal static string GitGuardExecutablePath
    {
        get
        {
            string fileName = OperatingSystem.IsWindows() ? GITGUARD_EXECUTABLE_NAME + ".exe" : GITGUARD_EXECUTABLE_NAME;
            string path = Path.Combine(AppContext.BaseDirectory, fileName);
            return File.Exists(path)
                ? path
                : throw new InvalidOperationException($"未找到构建产物中的 GitGuard 可执行文件：{path}");
        }
    }

    /// <summary>隔离工作区根目录（home / localappdata / xdgdata / repo / remotes 的父目录）。</summary>
    internal string Root { get; }

    /// <summary>Git 仓库路径（夹具内子进程的工作目录）。</summary>
    internal string RepositoryPath { get; }

    /// <summary>隔离的用户数据目录根（audit.db / cache / logs / crash）。</summary>
    internal string DataDirectory { get; }

    /// <summary>隔离的用户配置目录（Unix 经 HOME 生效；Windows 见 <see cref="EnsureHermeticUserConfig"/>）。</summary>
    internal string UserConfigDirectory { get; }

    /// <summary>审计数据库路径（05-数据库设计 §1）。</summary>
    internal string AuditDatabasePath { get; }

    /// <summary>递归清理工作区（先清 git 对象文件只读位）。</summary>
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

    /// <summary>写入文本文件（相对仓库根目录；自动创建父目录；UTF-8 无 BOM）。</summary>
    /// <param name="relativePath">相对仓库根目录的路径。</param>
    /// <param name="content">文件内容。</param>
    internal void WriteFile(string relativePath, string content)
    {
        string path = ResolvePath(relativePath);
        string? directory = Path.GetDirectoryName(path);
        if (directory is not null)
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(path, content, Utf8NoBom);
    }

    /// <summary>写入 POSIX sh 脚本（无 BOM、LF 行尾保证 shebang 生效；非 Windows 置 0755 可执行位）。</summary>
    /// <param name="relativePath">相对仓库根目录的脚本路径（Windows 上必须无扩展名，MSYS <c>[ -x ]</c> 以 shebang 为判据）。</param>
    /// <param name="body">脚本正文（不含 shebang 行）。</param>
    /// <returns>脚本绝对路径。</returns>
    internal string WritePosixScript(string relativePath, string body)
    {
        string filePath = ResolvePath(relativePath);
        string? directory = Path.GetDirectoryName(filePath);
        if (directory is not null)
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(filePath, "#!/bin/sh\n" + body, Utf8NoBom);
        if (!OperatingSystem.IsWindows())
        {
            UnixFileMode mode =
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                UnixFileMode.OtherRead | UnixFileMode.OtherExecute;
            File.SetUnixFileMode(filePath, mode);
        }

        return filePath;
    }

    /// <summary>仓库内相对路径的绝对路径。</summary>
    /// <param name="relativePath">相对仓库根目录的路径。</param>
    /// <returns>绝对路径。</returns>
    internal string ResolvePath(string relativePath) => Path.Combine(RepositoryPath, relativePath);

    /// <summary>暂存指定路径（相对仓库根目录）。</summary>
    /// <param name="relativePaths">待暂存路径。</param>
    internal void Stage(params string[] relativePaths)
    {
        string[] arguments = new string[relativePaths.Length + 2];
        arguments[0] = "add";
        arguments[1] = "--";
        relativePaths.CopyTo(arguments, 2);
        RunGitOrThrow(arguments);
    }

    /// <summary>提交当前已暂存内容（要求成功），返回新提交 SHA。</summary>
    /// <param name="message">提交信息。</param>
    /// <returns>提交 SHA。</returns>
    internal string CommitOrThrow(string message)
    {
        RunGitOrThrow("commit", "--quiet", "-m", message);
        return RunGitOrThrow("rev-parse", "HEAD");
    }

    /// <summary>HEAD 所在提交数（<c>rev-list --count HEAD</c>）。</summary>
    /// <returns>提交总数。</returns>
    internal int CommitCount() => int.Parse(RunGitOrThrow("rev-list", "--count", "HEAD"), CultureInfo.InvariantCulture);

    /// <summary>执行 git 命令（工作目录 = 仓库根），返回完整结果（非零退出不抛异常）。</summary>
    /// <param name="arguments">git 参数。</param>
    /// <returns>进程结果快照。</returns>
    internal ProcessResult RunGit(params string[] arguments) => RunProcess("git", arguments, string.Empty);

    /// <summary>在附加单个环境变量的情况下执行 git 命令（变量经 Hook 子进程继承，如 <c>GITGUARD_SKIP=1</c>）。</summary>
    /// <param name="environmentName">环境变量名。</param>
    /// <param name="environmentValue">环境变量值。</param>
    /// <param name="arguments">git 参数。</param>
    /// <returns>进程结果快照。</returns>
    internal ProcessResult RunGitWithEnvironment(string environmentName, string environmentValue, params string[] arguments) =>
        RunProcess(
            "git",
            arguments,
            string.Empty,
            new Dictionary<string, string>(StringComparer.Ordinal) { [environmentName] = environmentValue });

    /// <summary>执行 git 命令并要求成功；失败抛 <see cref="InvalidOperationException"/>（夹具装配错误）。</summary>
    /// <param name="arguments">git 参数。</param>
    /// <returns>stdout（已裁剪）。</returns>
    internal string RunGitOrThrow(params string[] arguments)
    {
        ProcessResult result = RunGit(arguments);
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException($"git {string.Join(' ', arguments)} 失败（退出码 {result.ExitCode}）：{result.StandardError}");
        }

        return result.StandardOutput.Trim();
    }

    /// <summary>创建 bare 远端并注册为指定名称的 remote，返回远端路径。</summary>
    /// <param name="name">远端名称（如 <c>origin</c>）。</param>
    /// <returns>bare 仓库绝对路径。</returns>
    internal string CreateBareRemote(string name)
    {
        string remotePath = Path.Combine(Root, REMOTES_DIRECTORY_NAME, name + ".git");
        string? directory = Path.GetDirectoryName(remotePath);
        if (directory is not null)
        {
            Directory.CreateDirectory(directory);
        }

        RunGitOrThrow("init", "--bare", "--quiet", ToPosixPath(remotePath));
        RunGitOrThrow("remote", "add", name, ToPosixPath(remotePath));
        return remotePath;
    }

    /// <summary>读取 bare 远端 master 分支当前指向的提交 SHA；分支不存在时返回空串。</summary>
    /// <param name="remotePath">bare 仓库绝对路径。</param>
    /// <returns>提交 SHA 或空串。</returns>
    internal string RemoteMasterSha(string remotePath)
    {
        ProcessResult result = RunGit("-C", ToPosixPath(remotePath), "rev-parse", "master");
        return result.ExitCode == 0 ? result.StandardOutput.Trim() : string.Empty;
    }

    /// <summary>运行真实 gitguard 可执行文件（子进程模式：真实进程环境 + Hook 固化路径同源）。</summary>
    /// <param name="arguments">命令行参数（不含可执行文件名）。</param>
    /// <returns>进程结果快照。</returns>
    internal ProcessResult RunCli(params string[] arguments) => RunProcess(GitGuardExecutablePath, arguments, string.Empty);

    /// <summary>
    /// 进程内驱动 CLI（<see cref="CliApplication.RunAsync"/> + 注入 <see cref="CliHost"/>）：
    /// 仅用于 S7 注入缺失 / 替身扫描器可执行文件路径（子进程模式的可执行文件路径恒为真实进程路径，无法注入）。
    /// </summary>
    /// <param name="arguments">命令行参数（不含可执行文件名）。</param>
    /// <param name="standardInput">标准输入内容（<c>scan --push</c> 推送信息行；无则传 <see langword="null"/>）。</param>
    /// <param name="executablePath">注入的可执行文件路径（写入 Hook 分发脚本）。</param>
    /// <returns>进程结果快照（内存标准流）。</returns>
    internal ProcessResult RunCliInProcess(string[] arguments, string? standardInput, string? executablePath)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var host = new CliHost
        {
            WorkingDirectory = RepositoryPath,
            Output = output,
            Error = error,
            Input = new StringReader(standardInput ?? string.Empty),
            DataDirectory = DataDirectory,
            UserConfigDirectory = UserConfigDirectory,
            GitGuardVersion = TEST_VERSION,
            ExecutablePath = executablePath,
            IsErrorTerminal = false,
            NoColorEnvironment = true,
        };

        int exitCode = CliApplication.RunAsync(arguments, host, CancellationToken.None).GetAwaiter().GetResult();
        return new ProcessResult(exitCode, output.ToString(), error.ToString());
    }

    /// <summary>审计库直读：单行单列文本值（16-测试方案 §2：集成测试禁止 mock，直接校验落库真实状态）。</summary>
    /// <param name="commandText">SQL 查询。</param>
    /// <returns>首行首列文本值。</returns>
    internal string ReadAuditScalarString(string commandText)
    {
        using SqliteConnection connection = OpenAuditConnection();
        using var command = connection.CreateCommand();
        command.CommandText = commandText;
        object? value = command.ExecuteScalar();
        return value is string text ? text : throw new InvalidOperationException($"查询未返回文本：{commandText}");
    }

    /// <summary>审计库直读：单行单列整数值。</summary>
    /// <param name="commandText">SQL 查询。</param>
    /// <returns>首行首列整数值。</returns>
    internal long ReadAuditScalarLong(string commandText)
    {
        using SqliteConnection connection = OpenAuditConnection();
        using var command = connection.CreateCommand();
        command.CommandText = commandText;
        object? value = command.ExecuteScalar();
        return value is long number ? number : throw new InvalidOperationException($"查询未返回整数：{commandText}");
    }

    /// <summary>拼接审计库主文件与 WAL/SHM 文件的文本内容（金丝雀缺席断言需覆盖全部落盘形态）。</summary>
    /// <returns>落盘字节的 UTF-8 文本拼接。</returns>
    internal string ReadAuditFilesText()
    {
        var builder = new StringBuilder();
        foreach (string suffix in new[] { string.Empty, "-wal", "-shm" })
        {
            string path = AuditDatabasePath + suffix;
            if (File.Exists(path))
            {
                builder.Append(Encoding.UTF8.GetString(File.ReadAllBytes(path)));
            }
        }

        return builder.ToString();
    }

    /// <summary>路径转正斜杠形态（MSYS sh 把反斜杠当转义字符，git 参数在 Windows 上同样接受正斜杠）。</summary>
    private static string ToPosixPath(string path) =>
        OperatingSystem.IsWindows() ? path.Replace('\\', '/') : path;

    /// <summary>真实用户级配置路径（Windows 上主目录由 shell API 解析，环境变量重定向对其无效）。</summary>
    private static string ResolveRealUserConfigPath() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config", "gitguard", "config.yml");

    private static void EnsureHermeticUserConfig()
    {
        if (OperatingSystem.IsWindows() && File.Exists(ResolveRealUserConfigPath()))
        {
            Assert.Skip("Windows 无法隔离用户配置目录：真实 ~/.config/gitguard/config.yml 存在，跳过 E2E 场景以避免配置渗入。");
        }
    }

    /// <summary>打开绕过审计存储的原始连接（Pooling=false 确保句柄随即释放，不阻塞临时目录清理）。</summary>
    private SqliteConnection OpenAuditConnection()
    {
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = AuditDatabasePath,
            Pooling = false,
        };
        var connection = new SqliteConnection(builder.ToString());
        connection.Open();
        return connection;
    }

    private string ResolveDataDirectory()
    {
        if (OperatingSystem.IsWindows())
        {
            return Path.Combine(_localAppDataDirectory, "GitGuard");
        }

        if (OperatingSystem.IsMacOS())
        {
            return Path.Combine(_homeDirectory, "Library", "Application Support", "GitGuard");
        }

        return Path.Combine(_xdgDataDirectory, "gitguard");
    }

    private ProcessResult RunProcess(
        string executable,
        IReadOnlyList<string> arguments,
        string standardInput,
        IReadOnlyDictionary<string, string>? additionalEnvironment = null)
    {
        var startInfo = new ProcessStartInfo(executable)
        {
            WorkingDirectory = RepositoryPath,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        ApplyIsolatedEnvironment(startInfo);
        if (additionalEnvironment is not null)
        {
            foreach ((string name, string value) in additionalEnvironment)
            {
                startInfo.Environment[name] = value;
            }
        }

        using Process process = Process.Start(startInfo) ?? throw new InvalidOperationException($"无法启动进程：{executable}");
        process.StandardInput.Write(standardInput);
        process.StandardInput.Close();

        Task<string> standardOutput = process.StandardOutput.ReadToEndAsync();
        Task<string> standardError = process.StandardError.ReadToEndAsync();
        process.WaitForExit();

        return new ProcessResult(process.ExitCode, standardOutput.GetAwaiter().GetResult(), standardError.GetAwaiter().GetResult());
    }

    private void ApplyIsolatedEnvironment(ProcessStartInfo startInfo)
    {
        startInfo.Environment["HOME"] = _homeDirectory;
        startInfo.Environment["USERPROFILE"] = _homeDirectory;
        startInfo.Environment["NO_COLOR"] = "1";
        if (OperatingSystem.IsWindows())
        {
            startInfo.Environment["LOCALAPPDATA"] = _localAppDataDirectory;
        }
        else
        {
            startInfo.Environment["XDG_DATA_HOME"] = _xdgDataDirectory;
        }
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
