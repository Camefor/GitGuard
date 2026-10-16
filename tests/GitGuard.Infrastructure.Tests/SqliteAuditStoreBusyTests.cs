using System.Diagnostics;
using FluentAssertions;
using GitGuard.Core.Model;
using GitGuard.Core.Policy;
using GitGuard.Infrastructure.Sqlite;
using Microsoft.Data.Sqlite;
using GitGuardSqliteException = GitGuard.Core.SqliteException;
using MsSqliteException = Microsoft.Data.Sqlite.SqliteException;

namespace GitGuard.Infrastructure.Tests;

/// <summary>
/// BUSY 重试集成测试（10-异常处理设计 §6 / Audit.md §4）：持写锁的第二连接制造 SQLITE_BUSY，
/// 验证「有限重试 → 耗尽 FailClosed」与「锁释放后可重试成功」两条路径。
/// </summary>
public sealed class SqliteAuditStoreBusyTests
{
    [Fact]
    public void SaveScanRun_WriteLockHeld_RetriesExhaustedThrowsFailClosed()
    {
        using var directory = new TempDirectory();
        string dbPath = Path.Combine(directory.Root, "audit.db");
        var retryDelays = new[] { TimeSpan.FromMilliseconds(10), TimeSpan.FromMilliseconds(10), TimeSpan.FromMilliseconds(10) };
        var store = new SqliteAuditStore(dbPath, busyRetryDelays: retryDelays);
        var run = ScanRun.Create(directory.Root, ScanType.PreCommit, DateTimeOffset.UnixEpoch, 12, ScanResult.Clean, 0);

        using (var writeLock = new DatabaseWriteLock(dbPath))
        {
            var stopwatch = Stopwatch.StartNew();

            Action act = () => store.SaveScanRun(run, new Dictionary<Finding, PolicyDecision>());

            GitGuardSqliteException exception = act.Should().Throw<GitGuardSqliteException>().Which;
            stopwatch.Stop();

            exception.Message.Should().Contain("写入扫描运行");
            exception.InnerException.Should().BeOfType<MsSqliteException>()
                .Which.SqliteErrorCode.Should().Be(5);

            // 3 次尝试 × 每次约 1s BUSY 等待窗口（连接串 Default Timeout=1）；单次超时不可能到达此处
            stopwatch.Elapsed.Should().BeGreaterThan(TimeSpan.FromSeconds(2));
        }

        // 锁释放后同一实例恢复可用：FailClosed 不污染后续写入
        store.SaveScanRun(run, new Dictionary<Finding, PolicyDecision>());
        store.FindScanRun(run.Id).Should().NotBeNull();
    }

    [Fact]
    public async Task SaveScanRun_LockReleasedDuringRetry_Succeeds()
    {
        using var directory = new TempDirectory();
        string dbPath = Path.Combine(directory.Root, "audit.db");
        var retryDelays = new[] { TimeSpan.FromMilliseconds(200), TimeSpan.FromMilliseconds(200), TimeSpan.FromMilliseconds(200) };
        var store = new SqliteAuditStore(dbPath, busyRetryDelays: retryDelays);
        var run = ScanRun.Create(directory.Root, ScanType.PreCommit, DateTimeOffset.UnixEpoch, 12, ScanResult.Clean, 0);
        var decisions = new Dictionary<Finding, PolicyDecision>();

        var writeLock = new DatabaseWriteLock(dbPath);
        try
        {
            var saveTask = Task.Run(() => store.SaveScanRun(run, decisions), TestContext.Current.CancellationToken);
            await Task.Delay(300, TestContext.Current.CancellationToken);
            writeLock.Dispose();

            Func<Task> wait = () => saveTask;
            await wait.Should().NotThrowAsync();
        }
        finally
        {
            writeLock.Dispose();
        }

        store.FindScanRun(run.Id).Should().NotBeNull();
    }

    [Fact]
    public void FindScanRun_WhileWriteLockHeld_ReadsUnderWalWithoutBlocking()
    {
        using var directory = new TempDirectory();
        string dbPath = Path.Combine(directory.Root, "audit.db");
        var store = new SqliteAuditStore(dbPath);

        using var writeLock = new DatabaseWriteLock(dbPath);
        var stopwatch = Stopwatch.StartNew();
        ScanRun? loaded = store.FindScanRun(Guid.CreateVersion7());
        stopwatch.Stop();

        // WAL 模式下读不阻塞于写事务；若退化为 rollback journal，读取会等到超时后抛错或明显变慢
        loaded.Should().BeNull();
        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(1));
    }
}

/// <summary>持写锁的第二连接（BEGIN IMMEDIATE 立即取得 RESERVED 锁），模拟并发写者。</summary>
internal sealed class DatabaseWriteLock : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly SqliteTransaction _transaction;

    internal DatabaseWriteLock(string databasePath)
    {
        _connection = SqliteTestHelpers.OpenRawConnection(databasePath);
        _transaction = _connection.BeginTransaction(deferred: false);
    }

    public void Dispose()
    {
        _transaction.Dispose();
        _connection.Dispose();
    }
}
