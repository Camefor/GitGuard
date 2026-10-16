using FluentAssertions;
using GitGuard.Application.Scanning;
using GitGuard.Core.Model;
using GitGuard.Infrastructure.Git;
using GitGuard.Infrastructure.Sqlite;
using GitGuard.Infrastructure.Time;
using Microsoft.Data.Sqlite;

namespace GitGuard.Application.Tests;

/// <summary>
/// 扫描用例端到端测试（TC-FR-020-01~05 / TC-FR-030-01~02）：真实 git 仓库 + 真实检测器 + 真实 SQLite 审计库，
/// 覆盖 DR-02（脱敏落库）与 DR-04（未暂存内容不参与扫描）。
/// </summary>
public sealed class ScanEndToEndTests
{
    private const string ZERO_SHA = "0000000000000000000000000000000000000000";

    // TC-FR-020-01：暂存金丝雀 → Blocked / 退出码 1 / 审计落库且脱敏
    [Fact]
    public async Task StagedCanary_BlocksWithExitCodeOneAndPersistsMaskedAudit()
    {
        using var repo = new TempGitRepository();
        string canary = TestSupport.CreateCanaryToken();
        repo.WriteFile("config/app.env", $"aws_key={canary}\n");
        repo.Stage("config/app.env");
        var git = new GitAccess(repo.Root);
        string databasePath = CreateDatabasePath(repo);
        var store = new SqliteAuditStore(databasePath);
        var useCase = new ScanStagedUseCase(git, TestSupport.CreateDetectors(), store, new SystemClock());

        ScanOutcome outcome = await useCase.ExecuteAsync(TestContext.Current.CancellationToken);

        outcome.Result.Should().Be(ScanResult.Blocked);
        outcome.ExitCode.Should().Be(1);
        outcome.FilesScanned.Should().Be(1);
        Finding finding = outcome.Findings.Should().ContainSingle().Subject;
        finding.RuleId.Should().Be("GGD/SECRET/AWS-AKID");
        finding.FilePath.Should().Be("config/app.env");
        finding.LineNumber.Should().Be(1);
        finding.CommitSha.Should().BeNull();
        finding.SnippetMasked.Value.Should().NotContain(canary);

        // 审计落库（05-数据库设计 §4）：run + findings 单事务写入，action 记录策略判定
        ScanRun? persisted = store.FindScanRun(outcome.Run.Id);
        persisted.Should().NotBeNull();
        persisted!.Result.Should().Be(ScanResult.Blocked);
        using (SqliteConnection connection = SqliteTestHelpers.OpenRawConnection(databasePath))
        {
            SqliteTestHelpers.ReadStringColumn(connection, "SELECT action FROM findings")
                .Should().ContainSingle().Which.Should().Be("Block");
            SqliteTestHelpers.ReadStringColumn(connection, "SELECT snippet_masked FROM findings")
                .Should().NotContain(value => value.Contains(canary, StringComparison.Ordinal));
            SqliteTestHelpers.ReadScalarString(connection, "SELECT result FROM scan_runs").Should().Be("Blocked");
        }

        // DR-02：金丝雀原文不得落入任何落盘形态（主库 / WAL / SHM）；连接已释放，可安全读取文件字节
        SqliteTestHelpers.ReadDatabaseFilesText(databasePath).Should().NotContain(canary);
    }

    // TC-FR-020-02 / DR-04：未暂存内容不参与扫描
    [Fact]
    public async Task UnstagedOnlyChange_ScansNothing()
    {
        using var repo = new TempGitRepository();
        repo.WriteFile("config/app.env", "placeholder\n");
        repo.Stage("config/app.env");
        repo.Commit("init");
        string canary = TestSupport.CreateCanaryToken();
        repo.WriteFile("config/app.env", $"aws_key={canary}\n");
        var git = new GitAccess(repo.Root);
        var store = new SqliteAuditStore(CreateDatabasePath(repo));
        var useCase = new ScanStagedUseCase(git, TestSupport.CreateDetectors(), store, new SystemClock());

        ScanOutcome outcome = await useCase.ExecuteAsync(TestContext.Current.CancellationToken);

        outcome.Result.Should().Be(ScanResult.Clean);
        outcome.ExitCode.Should().Be(0);
        outcome.FilesScanned.Should().Be(0);
        outcome.Findings.Should().BeEmpty();
    }

    // TC-FR-020-03：超过 1 MiB（默认上限）的文件跳过并记为 skipped
    [Fact]
    public async Task OversizeStagedFile_IsSkippedAsTooLarge()
    {
        using var repo = new TempGitRepository();
        repo.WriteFile("assets/big.txt", new string('a', (1024 * 1024) + 8) + "\n");
        repo.Stage("assets/big.txt");
        var git = new GitAccess(repo.Root);
        var store = new SqliteAuditStore(CreateDatabasePath(repo));
        var useCase = new ScanStagedUseCase(git, TestSupport.CreateDetectors(), store, new SystemClock());

        ScanOutcome outcome = await useCase.ExecuteAsync(TestContext.Current.CancellationToken);

        outcome.Result.Should().Be(ScanResult.Clean);
        outcome.FilesScanned.Should().Be(0);
        SkippedFile skipped = outcome.SkippedFiles.Should().ContainSingle().Subject;
        skipped.Path.Should().Be("assets/big.txt");
        skipped.Reason.Should().Be(SkipReason.TooLarge);
    }

    // TC-FR-020-04：二进制文件跳过并记为 skipped
    [Fact]
    public async Task BinaryStagedFile_IsSkippedAsBinary()
    {
        using var repo = new TempGitRepository();
        repo.WriteBytes("assets/logo.png", new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x00, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x01, 0x02 });
        repo.Stage("assets/logo.png");
        var git = new GitAccess(repo.Root);
        var store = new SqliteAuditStore(CreateDatabasePath(repo));
        var useCase = new ScanStagedUseCase(git, TestSupport.CreateDetectors(), store, new SystemClock());

        ScanOutcome outcome = await useCase.ExecuteAsync(TestContext.Current.CancellationToken);

        outcome.Result.Should().Be(ScanResult.Clean);
        outcome.FilesScanned.Should().Be(0);
        SkippedFile skipped = outcome.SkippedFiles.Should().ContainSingle().Subject;
        skipped.Path.Should().Be("assets/logo.png");
        skipped.Reason.Should().Be(SkipReason.Binary);
    }

    // TC-FR-020-05：公开 URL 不触发告警（Clean / 退出码 0）
    [Fact]
    public async Task PublicUrlOnlyContent_StaysClean()
    {
        using var repo = new TempGitRepository();
        repo.WriteFile("docs/readme.md", "See https://example.com/docs/index.html for details.\n");
        repo.Stage("docs/readme.md");
        var git = new GitAccess(repo.Root);
        var store = new SqliteAuditStore(CreateDatabasePath(repo));
        var useCase = new ScanStagedUseCase(git, TestSupport.CreateDetectors(), store, new SystemClock());

        ScanOutcome outcome = await useCase.ExecuteAsync(TestContext.Current.CancellationToken);

        outcome.Result.Should().Be(ScanResult.Clean);
        outcome.ExitCode.Should().Be(0);
        outcome.FilesScanned.Should().Be(1);
        outcome.Findings.Should().BeEmpty();
        outcome.SkippedFiles.Should().BeEmpty();
    }

    // TC-FR-030-01：推送区间内历史提交引入的凭据（当前提交已删除）仍阻断，发现项携带所属提交 SHA
    [Fact]
    public async Task PushedRangeWithDeletedSecret_StillBlocksWithOwningCommitSha()
    {
        using var repo = new TempGitRepository();
        repo.WriteFile("README.md", "init\n");
        repo.Stage("README.md");
        string baseSha = repo.Commit("init");
        string canary = TestSupport.CreateCanaryToken();
        repo.WriteFile("config/leak.env", $"aws_key={canary}\n");
        repo.Stage("config/leak.env");
        string leakSha = repo.Commit("add leak");
        repo.RunGit("rm", "-q", "config/leak.env");
        string headSha = repo.Commit("remove leak");
        var git = new GitAccess(repo.Root);
        var store = new SqliteAuditStore(CreateDatabasePath(repo));
        var useCase = new ScanPushUseCase(git, TestSupport.CreateDetectors(), store, new SystemClock());
        string stdin = $"refs/heads/main {headSha} refs/heads/main {baseSha}\n";

        ScanOutcome outcome = await useCase.ExecuteAsync(stdin, TestContext.Current.CancellationToken);

        outcome.Run.ScanType.Should().Be(ScanType.PrePush);
        outcome.Result.Should().Be(ScanResult.Blocked);
        outcome.ExitCode.Should().Be(1);
        Finding finding = outcome.Findings.Should().ContainSingle().Subject;
        finding.FilePath.Should().Be("config/leak.env");
        finding.CommitSha.Should().Be(leakSha);
        finding.SnippetMasked.Value.Should().NotContain(canary);
    }

    // TC-FR-030-02：新分支（remote-sha 全零）扫描 local-sha 全部可达提交
    [Fact]
    public async Task NewBranchZeroRemoteSha_ScansAllReachableCommits()
    {
        using var repo = new TempGitRepository();
        repo.WriteFile("README.md", "init\n");
        repo.Stage("README.md");
        repo.Commit("init");
        string canary = TestSupport.CreateCanaryToken();
        repo.WriteFile("config/leak.env", $"aws_key={canary}\n");
        repo.Stage("config/leak.env");
        string leakSha = repo.Commit("add leak");
        var git = new GitAccess(repo.Root);
        var store = new SqliteAuditStore(CreateDatabasePath(repo));
        var useCase = new ScanPushUseCase(git, TestSupport.CreateDetectors(), store, new SystemClock());
        string stdin = $"refs/heads/feature {leakSha} refs/heads/feature {ZERO_SHA}\n";

        ScanOutcome outcome = await useCase.ExecuteAsync(stdin, TestContext.Current.CancellationToken);

        outcome.Result.Should().Be(ScanResult.Blocked);
        outcome.ExitCode.Should().Be(1);
        outcome.Findings.Should().ContainSingle().Which.CommitSha.Should().Be(leakSha);
    }

    // FR-030：多提交推送逐提交扫描，发现项分别归属各自提交
    [Fact]
    public async Task MultiplePushedCommits_ProduceFindingsPerOwningCommit()
    {
        using var repo = new TempGitRepository();
        repo.WriteFile("README.md", "init\n");
        repo.Stage("README.md");
        string baseSha = repo.Commit("init");
        repo.WriteFile("config/one.env", $"aws_key={TestSupport.CreateCanaryToken()}\n");
        repo.Stage("config/one.env");
        string firstSha = repo.Commit("add first secret");
        repo.WriteFile("config/two.env", $"aws_key={TestSupport.CreateCanaryToken()}\n");
        repo.Stage("config/two.env");
        string secondSha = repo.Commit("add second secret");
        var git = new GitAccess(repo.Root);
        var store = new SqliteAuditStore(CreateDatabasePath(repo));
        var useCase = new ScanPushUseCase(git, TestSupport.CreateDetectors(), store, new SystemClock());
        string stdin = $"refs/heads/main {secondSha} refs/heads/main {baseSha}\n";

        ScanOutcome outcome = await useCase.ExecuteAsync(stdin, TestContext.Current.CancellationToken);

        outcome.Result.Should().Be(ScanResult.Blocked);
        outcome.Findings.Should().HaveCount(2);
        outcome.Findings.Select(finding => finding.CommitSha).Should().BeEquivalentTo(new[] { firstSha, secondSha });
    }

    // FR-030：删除分支推送（local-sha 全零）无提交可扫描
    [Fact]
    public async Task BranchDeletionPush_ScansNothing()
    {
        using var repo = new TempGitRepository();
        repo.WriteFile("README.md", "init\n");
        repo.Stage("README.md");
        string headSha = repo.Commit("init");
        var git = new GitAccess(repo.Root);
        var store = new SqliteAuditStore(CreateDatabasePath(repo));
        var useCase = new ScanPushUseCase(git, TestSupport.CreateDetectors(), store, new SystemClock());
        string stdin = $"refs/heads/main {ZERO_SHA} refs/heads/main {headSha}\n";

        ScanOutcome outcome = await useCase.ExecuteAsync(stdin, TestContext.Current.CancellationToken);

        outcome.Result.Should().Be(ScanResult.Clean);
        outcome.ExitCode.Should().Be(0);
        outcome.FilesScanned.Should().Be(0);
    }

    // FR-020：正常暂存变更（无命中）→ Clean，Run 聚合字段符合不变式
    [Fact]
    public async Task StagedBenignChange_ReturnsCleanWithNormalizedRunAggregate()
    {
        using var repo = new TempGitRepository();
        repo.WriteFile("src/service.cs", "public sealed class Service { }\n");
        repo.Stage("src/service.cs");
        var git = new GitAccess(repo.Root);
        var store = new SqliteAuditStore(CreateDatabasePath(repo));
        var useCase = new ScanStagedUseCase(git, TestSupport.CreateDetectors(), store, new SystemClock());

        ScanOutcome outcome = await useCase.ExecuteAsync(TestContext.Current.CancellationToken);

        outcome.Result.Should().Be(ScanResult.Clean);
        outcome.Run.RepoPath.Should().Be(git.GetRepositoryRoot());
        outcome.Run.ScanType.Should().Be(ScanType.PreCommit);
        outcome.Run.StartedAt.Offset.Should().Be(TimeSpan.Zero);
        outcome.Run.DurationMs.Should().BeGreaterThanOrEqualTo(0);
    }

    private static string CreateDatabasePath(TempGitRepository repo) =>
        Path.Combine(repo.Root, "audit", "gitguard.db");
}
