using FluentAssertions;
using GitGuard.Application.Hooks;
using GitGuard.Core.Model;
using GitGuard.Infrastructure.Git;
using GitGuard.Infrastructure.Sqlite;
using GitGuard.Infrastructure.Time;
using Microsoft.Data.Sqlite;

namespace GitGuard.Application.Tests;

/// <summary>
/// Hook 安装 / 卸载端到端测试（TC-FR-010-01~05、TC-DR-05-01）：真实 git 仓库 + 真实 SQLite 审计库 +
/// 真实 POSIX shell 执行分发脚本（16-测试方案：集成测试禁止 mock，禁止以文本断言替代执行结果）。
/// </summary>
public sealed class HookEndToEndTests
{
    private const string SAMPLE_VERSION = "1.2.3-e2e";

    // TC-FR-010-01：全新仓库安装 → 分发脚本落盘 + hook_installations 行 + HookInstalled 事件
    [Fact]
    public void Install_OnRealRepository_WritesScriptAndPersistsHookInstalledEvent()
    {
        using var repo = new TempGitRepository();
        var git = new GitAccess(repo.Root);
        string databasePath = Path.Combine(repo.Root, "audit", "gitguard.db");
        var store = new SqliteAuditStore(databasePath);
        string stub = CreateStub(repo);

        HookInstallReport report = new InstallHookUseCase(git, store, new SystemClock(), SAMPLE_VERSION)
            .Execute(stub, hooks: new[] { HookType.PreCommit });

        report.RepoPath.Should().Be(git.GetRepositoryRoot());
        report.Results.Should().ContainSingle().Which.Disposition.Should().Be(HookInstallDisposition.Installed);
        File.ReadAllText(Path.Combine(repo.Root, ".git", "hooks", "pre-commit"))
            .Should().StartWith($"#!/bin/sh\n# gitguard-managed v{SAMPLE_VERSION}\n");

        using SqliteConnection connection = SqliteTestHelpers.OpenRawConnection(databasePath);
        SqliteTestHelpers.ReadScalarString(connection, "SELECT repo_path FROM hook_installations;")
            .Should().Be(git.GetRepositoryRoot());
        SqliteTestHelpers.ReadScalarString(connection, "SELECT hook_type FROM hook_installations;").Should().Be("pre-commit");
        SqliteTestHelpers.ReadScalarString(connection, "SELECT version FROM hook_installations;").Should().Be(SAMPLE_VERSION);
        SqliteTestHelpers.ReadScalarString(connection, "SELECT status FROM hook_installations;").Should().Be("Active");
        SqliteTestHelpers.ReadScalarString(connection, "SELECT category FROM audit_logs ORDER BY seq DESC LIMIT 1;").Should().Be("Hook");
        SqliteTestHelpers.ReadScalarString(connection, "SELECT event_type FROM audit_logs ORDER BY seq DESC LIMIT 1;")
            .Should().Be("HookInstalled");
    }

    // TC-FR-010-02 / TC-FR-010-04：安装包裹原 Hook，卸载还原原 Hook 并翻转审计行状态
    [Fact]
    public void InstallOverLegacyHook_ThenUninstall_RestoresOriginalScriptAndMarksRowUninstalled()
    {
        using var repo = new TempGitRepository();
        var git = new GitAccess(repo.Root);
        string databasePath = Path.Combine(repo.Root, "audit", "gitguard.db");
        var store = new SqliteAuditStore(databasePath);
        string hookPath = Path.Combine(repo.Root, ".git", "hooks", "pre-commit");
        const string legacyContent = "#!/bin/sh\necho husky-like hook\n";
        File.WriteAllText(hookPath, legacyContent);

        HookInstallReport installReport = new InstallHookUseCase(git, store, new SystemClock(), SAMPLE_VERSION)
            .Execute(CreateStub(repo), hooks: new[] { HookType.PreCommit });
        installReport.Results.Should().ContainSingle()
            .Which.Disposition.Should().Be(HookInstallDisposition.WrappedLegacy);

        HookUninstallReport uninstallReport = new UninstallHookUseCase(git, store, new SystemClock(), SAMPLE_VERSION)
            .Execute(force: true, hooks: new[] { HookType.PreCommit });

        uninstallReport.Results.Should().ContainSingle()
            .Which.Disposition.Should().Be(HookUninstallDisposition.RestoredLegacy);
        File.ReadAllText(hookPath).Should().Be(legacyContent);
        File.Exists(hookPath + HookTypeExtensions.BACKUP_FILE_SUFFIX).Should().BeFalse();

        using SqliteConnection connection = SqliteTestHelpers.OpenRawConnection(databasePath);
        SqliteTestHelpers.ReadScalarLong(connection, "SELECT COUNT(*) FROM hook_installations;").Should().Be(1);
        SqliteTestHelpers.ReadScalarString(connection, "SELECT status FROM hook_installations;").Should().Be("Uninstalled");

        // 卸载仅翻转状态：首次安装的 version / 备份路径保留（05-数据库设计 §7）
        SqliteTestHelpers.ReadScalarString(connection, "SELECT version FROM hook_installations;").Should().Be(SAMPLE_VERSION);
        SqliteTestHelpers.ReadScalarString(connection, "SELECT original_hook_backup_path FROM hook_installations;")
            .Should().Be("pre-commit.local.bak");
        SqliteTestHelpers.ReadScalarString(connection, "SELECT event_type FROM audit_logs ORDER BY seq DESC LIMIT 1;")
            .Should().Be("HookUninstalled");
    }

    // TC-DR-05-01：安装后原 Hook（Husky 形态）仍被 git 真实执行，且 GitGuard 先行运行
    [Fact]
    public void RealGitCommit_WithInstalledHook_RunsGuardThenLegacyHook()
    {
        using var repo = new TempGitRepository();
        var git = new GitAccess(repo.Root);
        var store = new SqliteAuditStore(Path.Combine(repo.Root, "audit", "gitguard.db"));
        string guardLog = Path.Combine(repo.Root, "guard.log");
        string legacyLog = Path.Combine(repo.Root, "legacy.log");
        string stub = CreateStub(repo, guardLog);
        WriteLegacyHookWithLog(repo, legacyLog);
        EnableRepoHooks(repo);
        repo.WriteFile("a.txt", "hello\n");
        repo.Stage("a.txt");

        new InstallHookUseCase(git, store, new SystemClock(), SAMPLE_VERSION)
            .Execute(stub, hooks: new[] { HookType.PreCommit });
        string commitSha = repo.Commit("init");

        commitSha.Should().HaveLength(40);
        File.ReadAllText(guardLog).Trim().Should().Be("scan --staged --record-pending --hook-mode pre-commit");
        File.ReadAllText(legacyLog).Trim().Should().Be("LEGACY");
    }

    // FR-010 阻断语义：GitGuard 拒绝时提交失败，但原 Hook 仍执行（分发脚本无 set -e）
    [Fact]
    public void RealGitCommit_GuardBlocks_AbortsCommitWhileLegacyStillRuns()
    {
        using var repo = new TempGitRepository();
        var git = new GitAccess(repo.Root);
        var store = new SqliteAuditStore(Path.Combine(repo.Root, "audit", "gitguard.db"));
        string guardLog = Path.Combine(repo.Root, "guard.log");
        string legacyLog = Path.Combine(repo.Root, "legacy.log");
        string stub = CreateStub(repo, guardLog, guardExitCode: 1);
        WriteLegacyHookWithLog(repo, legacyLog);
        EnableRepoHooks(repo);
        repo.WriteFile("a.txt", "hello\n");
        repo.Stage("a.txt");
        new InstallHookUseCase(git, store, new SystemClock(), SAMPLE_VERSION)
            .Execute(stub, hooks: new[] { HookType.PreCommit });

        Action act = () => repo.Commit("init");

        act.Should().Throw<InvalidOperationException>();
        File.ReadAllText(guardLog).Trim().Should().Be("scan --staged --record-pending --hook-mode pre-commit");
        File.ReadAllText(legacyLog).Trim().Should().Be("LEGACY");
    }

    // TC-DR-05-01：真实 POSIX shell 执行分发脚本的退出码矩阵（GitGuard 非零优先，否则沿用原 Hook 退出码）
    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(3, 0, 3)]
    [InlineData(0, 7, 7)]
    [InlineData(3, 7, 3)]
    public void RunInstalledHook_ExitCodeFollowsGuardPriorityThenLegacy(int guardExitCode, int legacyExitCode, int expectedExitCode)
    {
        string? shPath = HookTestSupport.ResolveShPath();
        if (shPath is null)
        {
            Assert.Skip("未找到 POSIX shell（Git for Windows 或 PATH 上的 sh），本用例需真实执行分发脚本");
            return;
        }

        using var repo = new TempGitRepository();
        var git = new GitAccess(repo.Root);
        var store = new SqliteAuditStore(Path.Combine(repo.Root, "audit", "gitguard.db"));
        string guardLog = Path.Combine(repo.Root, "guard.log");
        string legacyLog = Path.Combine(repo.Root, "legacy.log");
        string stub = CreateStub(repo, guardLog, guardExitCode);
        string hookPath = WriteLegacyHookWithLog(repo, legacyLog, legacyExitCode);
        new InstallHookUseCase(git, store, new SystemClock(), SAMPLE_VERSION)
            .Execute(stub, hooks: new[] { HookType.PreCommit });

        int exitCode = HookTestSupport.RunScript(shPath, hookPath, repo.Root, "alpha", "beta");

        exitCode.Should().Be(expectedExitCode);
        File.ReadAllText(guardLog).Trim().Should().Be("scan --staged --record-pending --hook-mode pre-commit");
        File.ReadAllText(legacyLog).Trim().Should().Be("LEGACY alpha beta");
    }

    // 分发脚本固化路径失效（如卸载残留 / 移动安装目录）时给出可辨识退出码，而不是让提交静默通过
    [Fact]
    public void RunInstalledHook_MissingExecutable_ExitsTenWithoutLegacyHook()
    {
        string? shPath = HookTestSupport.ResolveShPath();
        if (shPath is null)
        {
            Assert.Skip("未找到 POSIX shell（Git for Windows 或 PATH 上的 sh），本用例需真实执行分发脚本");
            return;
        }

        using var repo = new TempGitRepository();
        var git = new GitAccess(repo.Root);
        var store = new SqliteAuditStore(Path.Combine(repo.Root, "audit", "gitguard.db"));
        string legacyLog = Path.Combine(repo.Root, "legacy.log");
        string stub = CreateStub(repo, Path.Combine(repo.Root, "guard.log"));
        string hookPath = WriteLegacyHookWithLog(repo, legacyLog);
        new InstallHookUseCase(git, store, new SystemClock(), SAMPLE_VERSION)
            .Execute(stub, hooks: new[] { HookType.PreCommit });
        File.Delete(stub);

        int exitCode = HookTestSupport.RunScript(shPath, hookPath, repo.Root);

        exitCode.Should().Be(10);
        File.Exists(legacyLog).Should().BeFalse();
    }

    // TempGitRepository 置空 core.hooksPath 以中和本机全局配置；本用例需 git 真实执行 hook，
    // 显式指回 .git/hooks（绝对路径：避免相对路径在 File 操作中相对测试进程工作目录解析）
    private static void EnableRepoHooks(TempGitRepository repo) =>
        repo.RunGit("config", "core.hooksPath", HookTestSupport.ToUnixPath(Path.Combine(repo.Root, ".git", "hooks")));

    private static string CreateStub(TempGitRepository repo, string? guardLog = null, int guardExitCode = 0)
    {
        string toolsDirectory = Path.Combine(repo.Root, "tools");
        Directory.CreateDirectory(toolsDirectory);
        string exitCodeFilePath = Path.Combine(toolsDirectory, "guard.exit");
        File.WriteAllText(exitCodeFilePath, guardExitCode.ToString(System.Globalization.CultureInfo.InvariantCulture));
        return HookTestSupport.CreateStubExecutable(
            Path.Combine(toolsDirectory, "gitguard"),
            guardLog ?? Path.Combine(toolsDirectory, "guard.log"),
            exitCodeFilePath);
    }

    private static string WriteLegacyHookWithLog(TempGitRepository repo, string logPath, int legacyExitCode = 0)
    {
        string hookPath = Path.Combine(repo.Root, ".git", "hooks", "pre-commit");
        string body =
            $"printf 'LEGACY %s\\n' \"$*\" >> \"{HookTestSupport.ToUnixPath(logPath)}\"\n" +
            $"exit {legacyExitCode}\n";
        HookTestSupport.WritePosixScript(hookPath, body);
        return hookPath;
    }
}
