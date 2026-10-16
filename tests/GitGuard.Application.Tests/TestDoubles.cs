using System.Runtime.CompilerServices;
using GitGuard.Core;
using GitGuard.Core.Abstractions;
using GitGuard.Core.Model;
using GitGuard.Core.Policy;

namespace GitGuard.Application.Tests;

/// <summary>测试用 Git 访问替身：差异与提交数据由委托注入，并记录调用序列（单元测试专用，集成测试一律使用真实 git）。</summary>
internal sealed class FakeGitAccess : IGitAccess
{
    private readonly Func<string> _stagedDiff;
    private readonly Func<string, IReadOnlyList<string>> _listCommitShas;
    private readonly Func<string, string> _commitDiff;
    private readonly string? _hooksPath;

    internal FakeGitAccess(
        string repositoryRoot,
        Func<string>? stagedDiff = null,
        Func<string, IReadOnlyList<string>>? listCommitShas = null,
        Func<string, string>? commitDiff = null,
        string? hooksPath = null)
    {
        RepositoryRoot = repositoryRoot;
        _stagedDiff = stagedDiff ?? (() => string.Empty);
        _listCommitShas = listCommitShas ?? (_ => Array.Empty<string>());
        _commitDiff = commitDiff ?? (_ => string.Empty);
        _hooksPath = hooksPath;
    }

    internal string RepositoryRoot { get; }

    internal List<string> RequestedRanges { get; } = new List<string>();

    internal List<string> RequestedCommitShas { get; } = new List<string>();

    internal int StagedDiffCalls { get; private set; }

    public string GetRepositoryRoot() => RepositoryRoot;

    public string GetGitCommonDirectory() => Path.Combine(RepositoryRoot, ".git");

    public string? GetConfiguredHooksPath() => _hooksPath;

    public string GetVersion() => "git version 0.0.0-fake";

    public string GetHeadCommitSha() => new string('0', 40);

    public string GetStagedDiff()
    {
        StagedDiffCalls++;
        return _stagedDiff();
    }

    public IReadOnlyList<string> ListCommitShas(string revisionRange)
    {
        RequestedRanges.Add(revisionRange);
        return _listCommitShas(revisionRange);
    }

    public string GetCommitDiff(string commitSha)
    {
        RequestedCommitShas.Add(commitSha);
        return _commitDiff(commitSha);
    }
}

/// <summary>测试用检测器：行为由委托注入（返回发现项序列）；委托内也可注入异常验证 FailClosed 分支。</summary>
internal sealed class StubDetector : IDetector
{
    private readonly Func<FileChange, IReadOnlyList<Finding>> _behavior;

    internal StubDetector(string name, Func<FileChange, IReadOnlyList<Finding>> behavior)
    {
        Name = name;
        _behavior = behavior;
    }

    public string Name { get; }

    internal List<FileChange> SeenChanges { get; } = new List<FileChange>();

    public async IAsyncEnumerable<Finding> DetectAsync(
        FileChange change,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        SeenChanges.Add(change);

        // 与生产检测器一致：主体为同步逻辑，等待已完成任务仅为保持 async 迭代器形态
        await Task.CompletedTask.ConfigureAwait(false);

        foreach (Finding finding in _behavior(change))
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return finding;
        }
    }
}

/// <summary>测试用阻塞检测器：等待至令牌取消，用于验证扫描级超时（12）与外部取消原样上抛。</summary>
internal sealed class BlockingDetector : IDetector
{
    public string Name => "blocking";

    public async IAsyncEnumerable<Finding> DetectAsync(
        FileChange change,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
        yield break;
    }
}

/// <summary>测试用固定时钟：扫描起点可断言，避免依赖真实时间。</summary>
internal sealed class FixedClock : IClock
{
    internal FixedClock(DateTimeOffset utcNow) => UtcNow = utcNow;

    public DateTimeOffset UtcNow { get; }
}

/// <summary>测试用审计存储：记录每次写入的 ScanRun 与判定映射。</summary>
internal sealed class RecordingAuditStore : IAuditStore
{
    internal List<ScanRun> SavedRuns { get; } = new List<ScanRun>();

    internal IReadOnlyDictionary<Finding, PolicyDecision>? LastDecisions { get; private set; }

    internal List<HookInstallation> SavedHookInstallations { get; } = new List<HookInstallation>();

    internal List<(HookInstallation Installation, bool RestoredLegacyHook)> SavedHookUninstallations { get; } =
        new List<(HookInstallation, bool)>();

    public void SaveScanRun(ScanRun run, IReadOnlyDictionary<Finding, PolicyDecision> findingDecisions)
    {
        SavedRuns.Add(run);
        LastDecisions = findingDecisions;
    }

    public ScanRun? FindScanRun(Guid runId) => SavedRuns.FirstOrDefault(run => run.Id == runId);

    public IReadOnlyList<ScanRun> ListRecentScanRuns(string repoPath, int limit) =>
        SavedRuns.Where(run => run.RepoPath == repoPath).Take(limit).ToList();

    public void SaveHookInstallation(HookInstallation installation) => SavedHookInstallations.Add(installation);

    public void SaveHookUninstallation(HookInstallation installation, bool restoredLegacyHook) =>
        SavedHookUninstallations.Add((installation, restoredLegacyHook));
}

/// <summary>测试用审计存储：写入必败，用于验证 AuditWriteFailed 降级路径（不改变扫描判定）。</summary>
internal sealed class FailingAuditStore : IAuditStore
{
    public void SaveScanRun(ScanRun run, IReadOnlyDictionary<Finding, PolicyDecision> findingDecisions) =>
        throw new SqliteException("审计写入失败（测试注入）。");

    public ScanRun? FindScanRun(Guid runId) => null;

    public IReadOnlyList<ScanRun> ListRecentScanRuns(string repoPath, int limit) => Array.Empty<ScanRun>();

    public void SaveHookInstallation(HookInstallation installation) =>
        throw new SqliteException("审计写入失败（测试注入）。");

    public void SaveHookUninstallation(HookInstallation installation, bool restoredLegacyHook) =>
        throw new SqliteException("审计写入失败（测试注入）。");
}

/// <summary>测试用发现项构造：固定指纹盐与脱敏片段，只关注严重度 / 路径 / 提交归属。</summary>
internal static class FindingTestData
{
    /// <summary>满足 SnippetMasked 不变式（'*' 覆盖率 ≥ 50%）的脱敏片段。</summary>
    internal const string MASKED_SNIPPET = "AK********************";

    internal static Finding Create(
        string ruleId,
        Severity severity,
        string path = "config/app.env",
        string? commitSha = null,
        int lineNumber = 1) =>
        Finding.Create(
            ruleId,
            path,
            lineNumber,
            severity,
            Fingerprint.FromHex(new string('a', 64)),
            SnippetMasked.Create(MASKED_SNIPPET),
            commitSha);
}

/// <summary>
/// 测试共用 unified diff 文本（与 <c>git diff --cached -U0</c> 输出形态一致）。
/// </summary>
internal static class DiffSamples
{
    /// <summary>单文件修改：新增一行 <c>added line</c>（新侧行号 1）。</summary>
    internal const string SingleAddedLine =
        "diff --git a/app.txt b/app.txt\n" +
        "index 1111111..2222222 100644\n" +
        "--- a/app.txt\n" +
        "+++ b/app.txt\n" +
        "@@ -1 +1 @@\n" +
        "-old line\n" +
        "+added line\n";
}
