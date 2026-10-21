using FluentAssertions;
using GitGuard.Cli;

namespace GitGuard.E2E.Tests;

/// <summary>
/// Hook 框架真实兼容矩阵（SRS NFR-A-001 / 16-测试方案 §2）：在临时仓库中真实安装
/// Husky / lefthook / pre-commit，验证 GitGuard 安装包裹、真实 <c>git commit</c> 链式执行、
/// 阻断语义与卸载还原。
/// <para>
/// 用例依赖 npm / Python 工具链与网络访问，仅当设置 <c>GITGUARD_HOOK_COMPAT=1</c> 时执行；
/// CI 的 <c>hook-compat</c> 作业负责安装工具链并设置该环境变量。
/// </para>
/// </summary>
public sealed class HookFrameworkCompatibilityTests
{
    private const string COMPAT_GATE_ENVIRONMENT = "GITGUARD_HOOK_COMPAT";
    private const string PRE_COMMIT_EXECUTABLE_ENVIRONMENT = "GITGUARD_PRE_COMMIT";

    private static readonly string[] INSTALL_COMMAND = new[] { "install" };
    private static readonly string[] UNINSTALL_COMMAND = new[] { "uninstall" };

    /// <summary>
    /// TC-COMPAT-HUSKY / TC-COMPAT-LEFTHOOK / TC-COMPAT-PRECOMMIT：真实框架 Hook 被备份并包裹后，
    /// 正常提交同时触发 GitGuard 与框架；GitGuard 阻断时框架仍执行且提交失败；卸载后恢复原框架 Hook。
    /// </summary>
    /// <param name="framework">框架名称（<c>husky</c> / <c>lefthook</c> / <c>pre-commit</c>）。</param>
    [Theory]
    [InlineData("husky")]
    [InlineData("lefthook")]
    [InlineData("pre-commit")]
    public void RealFrameworkInstall_CoexistsWithGitGuard(string framework)
    {
        RequireCompatibilityGate();

        using var harness = new AcceptanceHarness(pinHooksPath: false);

        harness.WriteFile("README.md", "# Compatibility\n");
        harness.Stage("README.md");
        harness.CommitOrThrow("baseline");

        FrameworkFixture fixture = InstallFramework(harness, framework);
        string originalHook = File.ReadAllText(harness.ResolvePath(fixture.HookPath));
        long scansBeforeInstall = ReadPreCommitScanCount(harness);

        // TC-COMPAT-01：安装不得覆盖原框架 Hook —— 先备份，再写入 gitguard 分发脚本
        harness.RunCli(INSTALL_COMMAND).ExitCode.Should().Be(ExitCodes.SUCCESS, $"{framework} 仓库中 install 应成功");
        File.Exists(harness.ResolvePath(fixture.HookPath + ".local.bak"))
            .Should().BeTrue($"{framework} 生成的 Hook 必须备份为 .local.bak");
        File.ReadAllText(harness.ResolvePath(fixture.HookPath))
            .Should().Contain("gitguard-managed", $"{fixture.HookPath} 应替换为 GitGuard 分发脚本");

        if (framework == "husky")
        {
            harness.RunGitOrThrow("config", "--get", "core.hooksPath")
                .Should().Be(".husky/_", "真实 Husky 通过相对 core.hooksPath 分发 Hook");
            File.Exists(harness.ResolvePath(".git/hooks/pre-commit"))
                .Should().BeFalse("GitGuard 应安装到 Husky 的 core.hooksPath，而不是默认 .git/hooks");
        }

        // TC-COMPAT-02：正常提交——GitGuard 扫描与框架 Hook 均执行
        harness.WriteFile("src/app.txt", "hello\n");
        harness.Stage("src/app.txt");
        ProcessResult cleanCommit = harness.RunGit("commit", "--quiet", "-m", "coexist");
        cleanCommit.ExitCode.Should().Be(ExitCodes.SUCCESS, $"{framework} Hook 与 GitGuard 均通过时提交必须成功");
        CountEvidence(harness, fixture).Should().Be(1, $"{framework} 的 Hook 必须在正常提交时执行");
        ReadLatestPreCommitResult(harness).Should().Be("Clean");
        ReadPreCommitScanCount(harness).Should().Be(scansBeforeInstall + 1, "正常提交应产生一次 PreCommit 扫描");

        // TC-COMPAT-03：暂存密钥——GitGuard 阻断，但原框架 Hook 仍执行（不静默跳过既有流程）
        string canary = AcceptanceHarness.CreateCanaryToken();
        harness.WriteFile("src/leak.env", $"AWS_ACCESS_KEY_ID={canary}\n");
        harness.Stage("src/leak.env");
        int commitsBeforeBlock = harness.CommitCount();

        ProcessResult blockedCommit = harness.RunGit("commit", "--quiet", "-m", "leak");
        blockedCommit.ExitCode.Should().NotBe(0, "暂存区含密钥必须阻断提交");
        blockedCommit.StandardError.Should().Contain("exit_code=1", "阻断报告应经 Hook 链透传到 git 标准错误");
        harness.CommitCount().Should().Be(commitsBeforeBlock, "阻断后不得产生新提交");
        CountEvidence(harness, fixture).Should().Be(2, $"GitGuard 阻断时 {framework} 的 Hook 仍须执行");
        ReadLatestPreCommitResult(harness).Should().Be("Blocked");
        harness.ReadAuditFilesText().Should().NotContain(canary, "金丝雀原文不得出现在审计库任何落盘文件中");

        harness.RunGitOrThrow("reset", "--quiet", "HEAD", "--", "src/leak.env");
        File.Delete(harness.ResolvePath("src/leak.env"));

        // TC-COMPAT-04：卸载——原框架 Hook 逐字节还原，后续提交不再触发 GitGuard
        harness.RunCli(UNINSTALL_COMMAND).ExitCode.Should().Be(ExitCodes.SUCCESS, "uninstall 应成功恢复原框架 Hook");
        File.Exists(harness.ResolvePath(fixture.HookPath + ".local.bak"))
            .Should().BeFalse("恢复后备份文件必须被消费");
        File.ReadAllText(harness.ResolvePath(fixture.HookPath))
            .Should().Be(originalHook, $"{framework} 生成的 Hook 必须逐字节还原");

        long scansAfterUninstall = ReadPreCommitScanCount(harness);
        harness.WriteFile("src/after.txt", "after\n");
        harness.Stage("src/after.txt");
        ProcessResult standaloneCommit = harness.RunGit("commit", "--quiet", "-m", "standalone");
        standaloneCommit.ExitCode.Should().Be(ExitCodes.SUCCESS, "卸载后原框架 Hook 不应影响提交");
        CountEvidence(harness, fixture).Should().Be(3, "卸载后原框架 Hook 仍须正常执行");
        ReadPreCommitScanCount(harness)
            .Should().Be(scansAfterUninstall, "卸载后不得再运行 GitGuard 扫描");
    }

    private static void RequireCompatibilityGate()
    {
        if (!string.Equals(
                Environment.GetEnvironmentVariable(COMPAT_GATE_ENVIRONMENT),
                "1",
                StringComparison.Ordinal))
        {
            Assert.Skip(
                $"未设置 {COMPAT_GATE_ENVIRONMENT}=1：跳过需要 npm / Python 工具链与网络访问的真实 Hook 框架兼容性用例。");
        }
    }

    private static FrameworkFixture InstallFramework(AcceptanceHarness harness, string framework)
    {
        return framework switch
        {
            "husky" => InstallHusky(harness),
            "lefthook" => InstallLefthook(harness),
            "pre-commit" => InstallPreCommit(harness),
            _ => throw new ArgumentOutOfRangeException(nameof(framework), framework, "未知的 Hook 框架"),
        };
    }

    private static FrameworkFixture InstallHusky(AcceptanceHarness harness)
    {
        WriteNodePackageManifest(harness);
        RunNpmOrThrow(harness, "install", "--no-audit", "--no-fund", "--no-package-lock", "--no-save", "husky@9");
        RunNodeToolOrThrow(harness, "npx", "--no-install", "husky", "init");

        harness.WriteFile(".husky/pre-commit", "node compat-husky.js\n");
        harness.WriteFile("compat-husky.js", "require('fs').appendFileSync('husky.log', 'HUSKY\\n');\n");

        harness.RunGitOrThrow("config", "--get", "core.hooksPath")
            .Should().Be(".husky/_", "Husky v9 初始化后应设置相对 core.hooksPath");
        File.Exists(harness.ResolvePath(".husky/_/pre-commit"))
            .Should().BeTrue("Husky v9 应在 .husky/_ 生成真实分发脚本");

        return new FrameworkFixture(".husky/_/pre-commit", "husky.log", "HUSKY");
    }

    private static FrameworkFixture InstallLefthook(AcceptanceHarness harness)
    {
        WriteNodePackageManifest(harness);
        RunNpmOrThrow(harness, "install", "--no-audit", "--no-fund", "--no-package-lock", "--no-save", "lefthook@2");

        string lefthookConfig =
            "pre-commit:\n" +
            "  commands:\n" +
            "    compat:\n" +
            "      run: node compat-lefthook.js\n";
        harness.WriteFile("lefthook.yml", lefthookConfig);
        harness.WriteFile("compat-lefthook.js", "require('fs').appendFileSync('lefthook.log', 'LEFTHOOK\\n');\n");
        RunNodeToolOrThrow(harness, "npx", "--no-install", "lefthook", "install");

        File.Exists(harness.ResolvePath(".git/hooks/pre-commit"))
            .Should().BeTrue("lefthook install 应写入 .git/hooks/pre-commit");

        return new FrameworkFixture(".git/hooks/pre-commit", "lefthook.log", "LEFTHOOK");
    }

    private static FrameworkFixture InstallPreCommit(AcceptanceHarness harness)
    {
        string preCommitConfig =
            "repos:\n" +
            "  - repo: local\n" +
            "    hooks:\n" +
            "      - id: compat\n" +
            "        name: compat\n" +
            "        entry: node compat-precommit.js\n" +
            "        language: system\n" +
            "        always_run: true\n" +
            "        pass_filenames: false\n";
        harness.WriteFile(".pre-commit-config.yaml", preCommitConfig);
        harness.WriteFile(
            "compat-precommit.js",
            "require('fs').appendFileSync('precommit.log', 'PRECOMMIT\\n');\n");

        ProcessResult install = harness.RunTool(ResolvePreCommitExecutable(), "install");
        install.ExitCode.Should().Be(
            0,
            $"pre-commit install 应成功（stdout: {install.StandardOutput} stderr: {install.StandardError}）");
        File.Exists(harness.ResolvePath(".git/hooks/pre-commit"))
            .Should().BeTrue("pre-commit install 应写入 .git/hooks/pre-commit");

        return new FrameworkFixture(".git/hooks/pre-commit", "precommit.log", "PRECOMMIT");
    }

    private static void WriteNodePackageManifest(AcceptanceHarness harness) =>
        harness.WriteFile("package.json", "{\n  \"name\": \"gitguard-hook-compat\",\n  \"private\": true\n}\n");

    private static void RunNpmOrThrow(AcceptanceHarness harness, params string[] arguments)
    {
        ProcessResult result = harness.RunNodeTool("npm", arguments);
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"npm {string.Join(' ', arguments)} 失败（退出码 {result.ExitCode}）：" +
                $"{result.StandardError}{result.StandardOutput}");
        }
    }

    private static void RunNodeToolOrThrow(AcceptanceHarness harness, string tool, params string[] arguments)
    {
        ProcessResult result = harness.RunNodeTool(tool, arguments);
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"{tool} {string.Join(' ', arguments)} 失败（退出码 {result.ExitCode}）：" +
                $"{result.StandardError}{result.StandardOutput}");
        }
    }

    private static string ResolvePreCommitExecutable()
    {
        string? configured = Environment.GetEnvironmentVariable(PRE_COMMIT_EXECUTABLE_ENVIRONMENT);
        return string.IsNullOrWhiteSpace(configured) ? "pre-commit" : configured;
    }

    private static int CountEvidence(AcceptanceHarness harness, FrameworkFixture fixture)
    {
        string path = harness.ResolvePath(fixture.EvidenceLogPath);
        return File.Exists(path)
            ? File.ReadAllText(path).Split(fixture.EvidenceMarker, StringSplitOptions.None).Length - 1
            : 0;
    }

    private static string ReadLatestPreCommitResult(AcceptanceHarness harness) =>
        harness.ReadAuditScalarString(
            "SELECT result FROM scan_runs WHERE scan_type = 'PreCommit' ORDER BY rowid DESC LIMIT 1");

    private static long ReadPreCommitScanCount(AcceptanceHarness harness) =>
        File.Exists(harness.AuditDatabasePath)
            ? harness.ReadAuditScalarLong("SELECT COUNT(*) FROM scan_runs WHERE scan_type = 'PreCommit'")
            : 0;

    private sealed class FrameworkFixture
    {
        internal FrameworkFixture(string hookPath, string evidenceLogPath, string evidenceMarker)
        {
            HookPath = hookPath;
            EvidenceLogPath = evidenceLogPath;
            EvidenceMarker = evidenceMarker;
        }

        internal string HookPath { get; }

        internal string EvidenceLogPath { get; }

        internal string EvidenceMarker { get; }
    }
}
