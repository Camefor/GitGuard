using System.Text.Json;
using FluentAssertions;
using GitGuard.Cli;

namespace GitGuard.E2E.Tests;

/// <summary>
/// MVP 必过验收场景（16-测试方案 §12 S1~S8）：真实 <c>git commit</c> / <c>git push</c> 触发 Hook 分发脚本 →
/// 拉起真实 <c>gitguard</c> 子进程扫描 → 阻断 / 放行与审计落库的全链路断言。
/// S8 覆盖正常提交、<c>--no-verify</c> 与 <c>GITGUARD_SKIP=1</c> 三类绕过判定（FR-050 / DR-06）。
/// </summary>
public sealed class MvpScenarioTests
{
    private static readonly string[] INSTALL_COMMAND = new[] { "install" };
    private static readonly string[] INSTALL_PRE_PUSH_COMMAND = new[] { "install", "--hook", "pre-push" };
    private static readonly string[] INSTALL_PRE_COMMIT_COMMAND = new[] { "install", "--hook", "pre-commit" };

    /// <summary>S1（16-测试方案 §12）：已暂存密钥 → 阻断 commit，且阻断判定落库审计。</summary>
    [Fact]
    public void S1_StagedSecret_BlocksCommit()
    {
        using var harness = new AcceptanceHarness();
        string canary = AcceptanceHarness.CreateCanaryToken();

        harness.WriteFile("README.md", "# Sample\n");
        harness.Stage("README.md");
        harness.CommitOrThrow("baseline");

        harness.RunCli(INSTALL_COMMAND).ExitCode.Should().Be(ExitCodes.SUCCESS, "install 应成功写入 Hook 分发脚本");

        harness.WriteFile("config/leak.env", $"AWS_ACCESS_KEY_ID={canary}\n");
        harness.Stage("config/leak.env");

        ProcessResult commit = harness.RunGit("commit", "--quiet", "-m", "add config");

        commit.ExitCode.Should().NotBe(0, "暂存区含密钥必须阻断提交");
        commit.StandardError.Should().Contain("exit_code=1", "阻断报告应经 Hook 透传到 git 标准错误");
        harness.CommitCount().Should().Be(1, "阻断后不得产生新提交");

        harness.ReadAuditScalarString("SELECT scan_type FROM scan_runs ORDER BY rowid DESC LIMIT 1").Should().Be("PreCommit");
        harness.ReadAuditScalarString("SELECT result FROM scan_runs ORDER BY rowid DESC LIMIT 1").Should().Be("Blocked");
        harness.ReadAuditScalarLong("SELECT exit_code FROM scan_runs ORDER BY rowid DESC LIMIT 1").Should().Be(ExitCodes.BLOCKED);
        harness.ReadAuditScalarString("SELECT action FROM findings ORDER BY rowid DESC LIMIT 1").Should().Be("Block");
        harness.ReadAuditFilesText().Should().NotContain(canary, "金丝雀原文不得出现在审计库任何落盘文件中");
    }

    /// <summary>S2（16-测试方案 §12）：密钥仅存在于工作区（未暂存）→ 不误拦 commit。</summary>
    [Fact]
    public void S2_UnstagedSecret_DoesNotBlockCommit()
    {
        using var harness = new AcceptanceHarness();
        string canary = AcceptanceHarness.CreateCanaryToken();

        harness.WriteFile("docs/notes.md", "# Notes\n");
        harness.Stage("docs/notes.md");
        harness.CommitOrThrow("baseline");

        harness.RunCli(INSTALL_COMMAND).ExitCode.Should().Be(ExitCodes.SUCCESS);

        // 已跟踪文件的工作区修改：不暂存，扫描器（--staged）不得看到该内容
        harness.WriteFile("docs/notes.md", $"# Notes\nAWS_ACCESS_KEY_ID={canary}\n");
        harness.WriteFile("src/app.txt", "hello\n");
        harness.Stage("src/app.txt");

        ProcessResult commit = harness.RunGit("commit", "--quiet", "-m", "clean change");

        commit.ExitCode.Should().Be(ExitCodes.SUCCESS, "未暂存内容不参与扫描，不得误拦");
        harness.CommitCount().Should().Be(2);
        harness.ReadAuditScalarString("SELECT result FROM scan_runs ORDER BY rowid DESC LIMIT 1").Should().Be("Clean");
        harness.ReadAuditFilesText().Should().NotContain(canary);
    }

    /// <summary>S3（16-测试方案 §12）：推送区间内的本地提交含密钥 → 阻断 push 且远端不前进。</summary>
    [Fact]
    public void S3_CommittedSecretInPushRange_BlocksPush()
    {
        using var harness = new AcceptanceHarness();
        string canary = AcceptanceHarness.CreateCanaryToken();

        harness.WriteFile("README.md", "# Sample\n");
        harness.Stage("README.md");
        string baseSha = harness.CommitOrThrow("baseline");
        string remotePath = harness.CreateBareRemote("origin");
        harness.RunGitOrThrow("push", "origin", "master");
        harness.RemoteMasterSha(remotePath).Should().Be(baseSha);

        harness.RunCli(INSTALL_PRE_PUSH_COMMAND).ExitCode.Should().Be(ExitCodes.SUCCESS);

        harness.WriteFile("src/settings.env", $"AWS_ACCESS_KEY_ID={canary}\n");
        harness.Stage("src/settings.env");
        string leakSha = harness.CommitOrThrow("add settings");

        ProcessResult push = harness.RunGit("push", "origin", "master");

        push.ExitCode.Should().NotBe(0, "推送区间含密钥必须阻断");
        push.StandardError.Should().Contain("exit_code=1");
        harness.RemoteMasterSha(remotePath).Should().Be(baseSha, "阻断后远端不得前进");

        harness.ReadAuditScalarString("SELECT scan_type FROM scan_runs ORDER BY rowid DESC LIMIT 1").Should().Be("PrePush");
        harness.ReadAuditScalarString("SELECT result FROM scan_runs ORDER BY rowid DESC LIMIT 1").Should().Be("Blocked");
        harness.ReadAuditScalarString("SELECT commit_sha FROM findings ORDER BY rowid DESC LIMIT 1").Should().Be(leakSha);
    }

    /// <summary>S4（16-测试方案 §12）：推送区间内「加入又删除」的密钥仍阻断，findings 精确指向泄漏提交。</summary>
    [Fact]
    public void S4_SecretAddedThenRemovedInRange_StillBlocksPush()
    {
        using var harness = new AcceptanceHarness();
        string canary = AcceptanceHarness.CreateCanaryToken();

        harness.WriteFile("README.md", "# Sample\n");
        harness.Stage("README.md");
        string baseSha = harness.CommitOrThrow("baseline");
        string remotePath = harness.CreateBareRemote("origin");
        harness.RunGitOrThrow("push", "origin", "master");

        harness.RunCli(INSTALL_PRE_PUSH_COMMAND).ExitCode.Should().Be(ExitCodes.SUCCESS);

        harness.WriteFile("config/leak.env", $"AWS_ACCESS_KEY_ID={canary}\n");
        harness.Stage("config/leak.env");
        string leakSha = harness.CommitOrThrow("add leak");

        harness.RunGitOrThrow("rm", "config/leak.env");
        harness.CommitOrThrow("remove leak");

        ProcessResult push = harness.RunGit("push", "origin", "master");

        push.ExitCode.Should().NotBe(0, "区间内加入又删除的密钥仍须阻断（历史泄漏不可推送）");
        harness.RemoteMasterSha(remotePath).Should().Be(baseSha);
        harness.ReadAuditScalarString("SELECT commit_sha FROM findings ORDER BY rowid DESC LIMIT 1").Should().Be(leakSha);
        harness.ReadAuditScalarString("SELECT file_path FROM findings ORDER BY rowid DESC LIMIT 1").Should().Be("config/leak.env");
    }

    /// <summary>S5（16-测试方案 §12）：公开 URL 与示例代码（非真实密钥）→ 零 Finding、scan_runs 为 Clean。</summary>
    [Fact]
    public void S5_PublicDocsAndSamples_ProduceNoFindings()
    {
        using var harness = new AcceptanceHarness();

        harness.WriteFile("README.md", "# Sample\n");
        harness.Stage("README.md");
        harness.CommitOrThrow("baseline");

        harness.RunCli(INSTALL_COMMAND).ExitCode.Should().Be(ExitCodes.SUCCESS);

        string guide =
            "# 文档示例\n\n" +
            "公共文档：https://example.com/docs/index.html 与 https://api.github.com/repos/example/project 。\n\n" +
            "```js\n" +
            "const endpoint = \"https://example.com/api/v1/items\";\n" +
            "const response = await fetch(endpoint);\n" +
            "```\n\n" +
            "占位符示例：将 YOUR_API_KEY_HERE 替换为控制台申请的值。\n";
        harness.WriteFile("docs/guide.md", guide);
        harness.Stage("docs/guide.md");

        ProcessResult commit = harness.RunGit("commit", "--quiet", "-m", "docs");

        commit.ExitCode.Should().Be(ExitCodes.SUCCESS);
        harness.ReadAuditScalarString("SELECT result FROM scan_runs ORDER BY rowid DESC LIMIT 1").Should().Be("Clean");
        harness.ReadAuditScalarLong("SELECT COUNT(*) FROM findings").Should().Be(0, "公开 URL 与示例代码不得产生任何 Finding");
    }

    /// <summary>S6（16-测试方案 §12）：已有 Hook（如 Husky 生成的 pre-commit）被包裹而非破坏，原脚本仍执行。</summary>
    [Fact]
    public void S6_ExistingLegacyHook_WrappedAndStillExecuted()
    {
        using var harness = new AcceptanceHarness();

        harness.WriteFile("README.md", "# Sample\n");
        harness.Stage("README.md");
        harness.CommitOrThrow("baseline");

        harness.WritePosixScript(".git/hooks/pre-commit", "printf 'LEGACY\\n' >> legacy.log\nexit 0\n");

        harness.RunCli(INSTALL_COMMAND).ExitCode.Should().Be(ExitCodes.SUCCESS);

        File.Exists(harness.ResolvePath(".git/hooks/pre-commit.local.bak")).Should().BeTrue("原有 Hook 必须备份为 .local.bak");
        File.ReadAllText(harness.ResolvePath(".git/hooks/pre-commit")).Should().Contain("gitguard-managed");

        harness.WriteFile("src/app.txt", "hello\n");
        harness.Stage("src/app.txt");
        ProcessResult commit = harness.RunGit("commit", "--quiet", "-m", "with legacy hook");

        commit.ExitCode.Should().Be(ExitCodes.SUCCESS, "旧 Hook 与扫描器均通过时提交必须成功");
        File.ReadAllText(harness.ResolvePath("legacy.log")).Should().Contain("LEGACY", "旧 Hook 必须仍被分发脚本链式执行");
        harness.ReadAuditScalarString("SELECT result FROM scan_runs ORDER BY rowid DESC LIMIT 1").Should().Be("Clean");
    }

    /// <summary>S7（16-测试方案 §12）：扫描器可执行文件缺失 → Hook 以 FailClosed（exit 10）阻断提交。</summary>
    [Fact]
    public void S7a_MissingScannerExecutable_BlocksCommitFailClosed()
    {
        using var harness = new AcceptanceHarness();

        harness.WriteFile("README.md", "# Sample\n");
        harness.Stage("README.md");
        harness.CommitOrThrow("baseline");

        // 注入不存在的扫描器路径：install 照常写入分发脚本，但脚本执行时 [ ! -x ] 分支阻断
        string missingExecutable = Path.Combine(harness.Root, "missing", "gitguard");
        harness.RunCliInProcess(INSTALL_PRE_COMMIT_COMMAND, standardInput: null, executablePath: missingExecutable)
            .ExitCode.Should().Be(ExitCodes.SUCCESS);

        harness.WriteFile("src/app.txt", "hello\n");
        harness.Stage("src/app.txt");
        ProcessResult commit = harness.RunGit("commit", "--quiet", "-m", "fail closed");

        commit.ExitCode.Should().NotBe(0, "扫描器不可用时必须 FailClosed 阻断");
        commit.StandardError.Should().Contain("executable not found");
        harness.CommitCount().Should().Be(1);
        harness.ReadAuditScalarLong("SELECT COUNT(*) FROM scan_runs").Should().Be(0, "扫描器未运行，不应产生扫描记录");
    }

    /// <summary>
    /// S7（16-测试方案 §12）：扫描器以 FailClosed 退出码（11 检测器异常 / 12 超时）失败 → 阻断提交。
    /// 扫描器自身产生 10/11/12 的用例见 TC-DR-03（本用例只验证分发链的退出码透传）。</summary>
    /// <param name="exitCode">替身扫描器退出码。</param>
    [Theory]
    [InlineData(11)]
    [InlineData(12)]
    public void S7b_ScannerFailClosedExitCode_BlocksCommit(int exitCode)
    {
        using var harness = new AcceptanceHarness();

        harness.WriteFile("README.md", "# Sample\n");
        harness.Stage("README.md");
        harness.CommitOrThrow("baseline");

        // 替身扫描器放在 .git/ 内（工作树外，不会被当成待提交内容）；写入 stub.log 用于证明分发脚本真实执行
        string stubPath = harness.WritePosixScript(".git/stub-gitguard", $"printf 'invoked\\n' >> stub.log\nexit {exitCode}\n");
        harness.RunCliInProcess(INSTALL_PRE_COMMIT_COMMAND, standardInput: null, executablePath: stubPath)
            .ExitCode.Should().Be(ExitCodes.SUCCESS);

        harness.WriteFile("src/app.txt", "hello\n");
        harness.Stage("src/app.txt");
        ProcessResult commit = harness.RunGit("commit", "--quiet", "-m", "scanner fails");

        commit.ExitCode.Should().NotBe(0, $"扫描器以 {exitCode} 退出必须阻断提交");
        harness.CommitCount().Should().Be(1);
        File.ReadAllText(harness.ResolvePath("stub.log")).Should().Contain("invoked", "分发脚本必须真实执行了扫描器");
    }

    /// <summary>
    /// S8（16-测试方案 §12 / TC-FR-050-01 / TC-DR-06-01）：正常提交不留绕过记录；
    /// <c>--no-verify</c> 与 <c>GITGUARD_SKIP=1</c> 分别登记为 <c>NoVerify</c> / <c>GitGuardSkip</c>，
    /// 两者叠加时只登记一条 <c>NoVerify</c>。
    /// </summary>
    [Fact]
    public void S8_BypassDetection_LeavesAuditTrailForNoVerifyAndGitGuardSkip()
    {
        using var harness = new AcceptanceHarness();

        harness.WriteFile("README.md", "# Sample\n");
        harness.Stage("README.md");
        harness.CommitOrThrow("baseline");
        harness.RunCli(INSTALL_COMMAND).ExitCode.Should().Be(ExitCodes.SUCCESS, "install 应写入 pre-commit 与 post-commit 分发脚本");
        File.Exists(harness.ResolvePath(".git/hooks/post-commit")).Should().BeTrue();

        // 1) 正常提交：pre-commit 记录预测 → post-commit 消费标记，不产生绕过事件
        harness.WriteFile("src/normal.txt", "normal\n");
        harness.Stage("src/normal.txt");
        ProcessResult normal = harness.RunGit("commit", "--quiet", "-m", "normal commit");
        normal.ExitCode.Should().Be(ExitCodes.SUCCESS);
        harness.ReadAuditScalarLong("SELECT COUNT(*) FROM scan_runs WHERE scan_type = 'Bypass'")
            .Should().Be(0, "正常提交不得登记绕过事件");
        harness.ReadAuditScalarString("SELECT scan_type FROM scan_runs ORDER BY rowid DESC LIMIT 1").Should().Be("PreCommit");
        File.Exists(harness.ResolvePath(".git/gitguard/pending-scan.json"))
            .Should().BeFalse("正常提交必须消费待提交预测标记");

        // 2) --no-verify：pre-commit 未运行，post-commit 无标记可比对 → NoVerify
        harness.WriteFile("src/bypassed.txt", "bypassed\n");
        harness.Stage("src/bypassed.txt");
        ProcessResult bypassed = harness.RunGit("commit", "--quiet", "--no-verify", "-m", "bypass hooks");
        bypassed.ExitCode.Should().Be(ExitCodes.SUCCESS);
        string bypassedSha = harness.RunGitOrThrow("rev-parse", "HEAD");
        harness.ReadAuditScalarLong("SELECT COUNT(*) FROM scan_runs WHERE scan_type = 'Bypass'").Should().Be(1);
        harness.ReadAuditScalarString("SELECT result FROM scan_runs WHERE scan_type = 'Bypass'").Should().Be("Clean");
        harness.ReadAuditScalarLong("SELECT exit_code FROM scan_runs WHERE scan_type = 'Bypass'").Should().Be(0);
        ReadLatestBypassReason(harness).Should().Be("NoVerify");
        ReadLatestBypassCommitSha(harness).Should().Be(bypassedSha);

        // 3) GITGUARD_SKIP=1：pre-commit 跳过扫描并记录 skipped 标记 → GitGuardSkip，且不产生 PreCommit 扫描记录
        harness.WriteFile("src/skipped.txt", "skipped\n");
        harness.Stage("src/skipped.txt");
        ProcessResult skipped = harness.RunGitWithEnvironment("GITGUARD_SKIP", "1", "commit", "--quiet", "-m", "explicit skip");
        skipped.ExitCode.Should().Be(ExitCodes.SUCCESS);
        string skippedSha = harness.RunGitOrThrow("rev-parse", "HEAD");
        harness.ReadAuditScalarLong("SELECT COUNT(*) FROM scan_runs WHERE scan_type = 'Bypass'").Should().Be(2);
        harness.ReadAuditScalarLong("SELECT COUNT(*) FROM scan_runs WHERE scan_type = 'PreCommit'")
            .Should().Be(1, "GITGUARD_SKIP=1 必须跳过暂存区扫描");
        ReadLatestBypassReason(harness).Should().Be("GitGuardSkip");
        ReadLatestBypassCommitSha(harness).Should().Be(skippedSha);

        // 4) --no-verify 与 GITGUARD_SKIP=1 叠加：pre-commit 未运行，仍归因为 NoVerify 且只登记一条
        harness.WriteFile("src/double.txt", "double\n");
        harness.Stage("src/double.txt");
        ProcessResult doubled = harness.RunGitWithEnvironment(
            "GITGUARD_SKIP", "1", "commit", "--quiet", "--no-verify", "-m", "double bypass");
        doubled.ExitCode.Should().Be(ExitCodes.SUCCESS);
        harness.ReadAuditScalarLong("SELECT COUNT(*) FROM scan_runs WHERE scan_type = 'Bypass'").Should().Be(3);
        ReadLatestBypassReason(harness).Should().Be("NoVerify");
        File.Exists(harness.ResolvePath(".git/gitguard/pending-scan.json")).Should().BeFalse();
    }

    private static string ReadLatestBypassReason(AcceptanceHarness harness) =>
        ReadLatestBypassPayload(harness).GetProperty("Reason").GetString()
        ?? throw new InvalidOperationException("BypassDetected 载荷缺少 Reason。");

    private static string ReadLatestBypassCommitSha(AcceptanceHarness harness) =>
        ReadLatestBypassPayload(harness).GetProperty("CommitSha").GetString()
        ?? throw new InvalidOperationException("BypassDetected 载荷缺少 CommitSha。");

    private static JsonElement ReadLatestBypassPayload(AcceptanceHarness harness)
    {
        using var payload = JsonDocument.Parse(harness.ReadAuditScalarString(
            "SELECT payload_json FROM audit_logs WHERE category = 'Bypass' AND event_type = 'BypassDetected' ORDER BY seq DESC LIMIT 1"));
        return payload.RootElement.Clone();
    }
}
