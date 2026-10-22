using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using GitGuard.Application.Composition;
using GitGuard.Core.Abstractions;
using Microsoft.Data.Sqlite;

namespace GitGuard.Application.Tests;

/// <summary>
/// 组合根审计事件接线测试（08-权限设计 §7）：配置降级 → <c>PolicyOverride</c>（同进程只写一次）、
/// FailClosed → <c>Error/FailClosed</c>（仅存消息摘要）、审计库不可用时返回 false 而不递归失败。
/// </summary>
public sealed class AuditEventWiringTests
{
    [Fact]
    public void CreateScans_WithPolicyDowngrades_RecordsPolicyOverrideEventsOnce()
    {
        using var repo = new TempGitRepository();
        using var data = new TempDirectory();
        const string config =
            "version: 1\n" +
            "policy:\n" +
            "  high: warn\n" +
            "rules:\n" +
            "  secrets:\n" +
            "    disabled_rules:\n" +
            "      - GGD/SECRET/AWS-AKID\n";
        repo.WriteFile(".gitguard.yml", config);
        GitGuardSession session = CreateSession(repo.Root, data.Root);

        _ = session.CreateStagedScan();
        _ = session.CreatePushScan();

        using SqliteConnection connection = SqliteTestHelpers.OpenRawConnection(Path.Combine(data.Root, "audit.db"));
        List<string> payloads = SqliteTestHelpers.ReadStringColumn(
            connection,
            "SELECT payload_json FROM audit_logs WHERE category = 'Policy' AND event_type = 'PolicyOverride' ORDER BY seq;");
        payloads.Should().HaveCount(2, "Severity/High 与禁用规则各记一条，且同一 Session 不重复写入；实际：" + string.Join(" | ", payloads));
        payloads.Should().Contain(payload => HasOverride(payload, "SEVERITY/High", "Block", "Warn"));
        payloads.Should().Contain(payload => HasOverride(payload, "GGD/SECRET/AWS-AKID", "Block", "Allow"));
    }

    [Fact]
    public void TryRecordFailClosed_PersistsHashedMessageInErrorCategory()
    {
        using var repo = new TempGitRepository();
        using var data = new TempDirectory();
        GitGuardSession session = CreateSession(repo.Root, data.Root);
        const string message = "配置解析失败：token=super-secret-value";

        bool recorded = session.TryRecordFailClosed(13, "CONFIG_INVALID", message);

        recorded.Should().BeTrue();
        string dbPath = Path.Combine(data.Root, "audit.db");
        using SqliteConnection connection = SqliteTestHelpers.OpenRawConnection(dbPath);
        SqliteTestHelpers.ReadScalarString(connection, "SELECT category FROM audit_logs WHERE event_type = 'FailClosed';")
            .Should().Be("Error");
        using var payload = JsonDocument.Parse(
            SqliteTestHelpers.ReadScalarString(connection, "SELECT payload_json FROM audit_logs WHERE event_type = 'FailClosed';"));
        payload.RootElement.GetProperty("ExitCode").GetInt32().Should().Be(13);
        payload.RootElement.GetProperty("ErrorCode").GetString().Should().Be("CONFIG_INVALID");
        payload.RootElement.GetProperty("MessageHash").GetString()
            .Should().Be(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(message))));

        SqliteTestHelpers.ReadDatabaseFilesText(dbPath)
            .Should().NotContain("super-secret-value", "FailClosed 事件只允许保存消息摘要");
    }

    [Fact]
    public void TryRecordFailClosed_InvalidArguments_Throw()
    {
        using var repo = new TempGitRepository();
        using var data = new TempDirectory();
        GitGuardSession session = CreateSession(repo.Root, data.Root);

        Action lowExitCode = () => session.TryRecordFailClosed(9, "CONFIG_INVALID", "message");
        Action emptyErrorCode = () => session.TryRecordFailClosed(10, "   ", "message");
        Action nullMessage = () => session.TryRecordFailClosed(10, "CONFIG_INVALID", null!);

        lowExitCode.Should().Throw<ArgumentOutOfRangeException>();
        emptyErrorCode.Should().Throw<ArgumentException>();
        nullMessage.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void TryRecordFailClosed_AuditStoreUnavailable_ReturnsFalseWithoutThrowing()
    {
        using var repo = new TempGitRepository();
        using var data = new TempDirectory();
        string blockedDataPath = Path.Combine(data.Root, "blocked");
        File.WriteAllText(blockedDataPath, "not a directory");
        GitGuardSession session = CreateSession(repo.Root, blockedDataPath);

        bool recorded = session.TryRecordFailClosed(14, "AUDIT_STORE_FAILED", "audit database unavailable");

        recorded.Should().BeFalse("审计库不可用时只返回失败，由 CLI 输出警告，不递归触发新的 FailClosed");
    }

    private static bool HasOverride(string payload, string ruleId, string from, string to)
    {
        using var document = JsonDocument.Parse(payload);
        JsonElement root = document.RootElement;
        return root.GetProperty("RuleId").GetString() == ruleId
            && root.GetProperty("From").GetString() == from
            && root.GetProperty("To").GetString() == to;
    }

    private static GitGuardSession CreateSession(string workingDirectory, string dataDirectory) =>
        new GitGuardSession(
            new GitGuardSessionOptions
            {
                WorkingDirectory = workingDirectory,
                DataDirectory = dataDirectory,
                UserConfigDirectory = Path.Combine(dataDirectory, "config"),
                GitGuardVersion = "0.0.0-test",
            },
            new FixedClock(new DateTimeOffset(2026, 10, 22, 9, 0, 0, TimeSpan.Zero)),
            new FixedSecretRandom());

    /// <summary>确定性随机源：盐内容可复现，避免测试间指纹串扰。</summary>
    private sealed class FixedSecretRandom : ISecretRandom
    {
        public byte[] GetBytes(int count) => new byte[count];
    }

    /// <summary>真实临时目录夹具。</summary>
    private sealed class TempDirectory : IDisposable
    {
        internal TempDirectory()
        {
            Root = Path.Combine(Path.GetTempPath(), "gitguard-audit-wiring-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
        }

        internal string Root { get; }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Root, recursive: true);
            }
            catch (IOException)
            {
                // 句柄偶发被系统占用：清理失败不影响测试结论
            }
        }
    }
}
