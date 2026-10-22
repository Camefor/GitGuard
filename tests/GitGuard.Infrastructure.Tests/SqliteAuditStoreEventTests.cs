using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
using FluentAssertions;
using GitGuard.Core.Policy;
using GitGuard.Infrastructure.Sqlite;
using Microsoft.Data.Sqlite;

namespace GitGuard.Infrastructure.Tests;

/// <summary>
/// 审计事件补全与文件权限加固测试（GGD-0015 / 08-权限设计 §5、§7）：
/// <c>PolicyOverride</c> / <c>FilePermissionFixed</c> / <c>FailClosed</c> 事件落库，
/// 以及 Unix mode / Windows ACL 的启动期修复与审计留痕。
/// </summary>
public sealed class SqliteAuditStoreEventTests
{
    private const string MessageHash = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    [Fact]
    public void SavePolicyOverrideEvent_PersistsPolicyCategoryAndPayload()
    {
        using var directory = new TempDirectory();
        var store = new SqliteAuditStore(Path.Combine(directory.Root, "audit.db"));

        store.SavePolicyOverrideEvent("GGD/SECRET/AWS-AKID", PolicyAction.Block, PolicyAction.Warn);

        using SqliteConnection connection = SqliteTestHelpers.OpenRawConnection(Path.Combine(directory.Root, "audit.db"));
        SqliteTestHelpers.ReadScalarString(connection, "SELECT category FROM audit_logs WHERE event_type = 'PolicyOverride';")
            .Should().Be("Policy");
        using var payload = JsonDocument.Parse(
            SqliteTestHelpers.ReadScalarString(connection, "SELECT payload_json FROM audit_logs WHERE event_type = 'PolicyOverride';"));
        payload.RootElement.GetProperty("RuleId").GetString().Should().Be("GGD/SECRET/AWS-AKID");
        payload.RootElement.GetProperty("From").GetString().Should().Be("Block");
        payload.RootElement.GetProperty("To").GetString().Should().Be("Warn");
    }

    [Fact]
    public void SaveFilePermissionFixedEvent_PersistsSecurityCategoryAndPayload()
    {
        using var directory = new TempDirectory();
        string dbPath = Path.Combine(directory.Root, "audit.db");
        var store = new SqliteAuditStore(dbPath);

        store.SaveFilePermissionFixedEvent(dbPath, "0644", "0600");

        using SqliteConnection connection = SqliteTestHelpers.OpenRawConnection(dbPath);
        SqliteTestHelpers.ReadScalarString(connection, "SELECT category FROM audit_logs WHERE event_type = 'FilePermissionFixed';")
            .Should().Be("Security");
        using var payload = JsonDocument.Parse(
            SqliteTestHelpers.ReadScalarString(connection, "SELECT payload_json FROM audit_logs WHERE event_type = 'FilePermissionFixed';"));
        payload.RootElement.GetProperty("FilePath").GetString().Should().Be(dbPath);
        payload.RootElement.GetProperty("PrevMode").GetString().Should().Be("0644");
        payload.RootElement.GetProperty("NewMode").GetString().Should().Be("0600");
    }

    [Fact]
    public void SaveFailClosedEvent_PersistsErrorCategoryAndPayloadWithoutRawMessage()
    {
        using var directory = new TempDirectory();
        string dbPath = Path.Combine(directory.Root, "audit.db");
        var store = new SqliteAuditStore(dbPath);

        store.SaveFailClosedEvent(14, "AUDIT_STORE_FAILED", MessageHash);

        using SqliteConnection connection = SqliteTestHelpers.OpenRawConnection(dbPath);
        SqliteTestHelpers.ReadScalarString(connection, "SELECT category FROM audit_logs WHERE event_type = 'FailClosed';")
            .Should().Be("Error");
        using var payload = JsonDocument.Parse(
            SqliteTestHelpers.ReadScalarString(connection, "SELECT payload_json FROM audit_logs WHERE event_type = 'FailClosed';"));
        payload.RootElement.GetProperty("ExitCode").GetInt32().Should().Be(14);
        payload.RootElement.GetProperty("ErrorCode").GetString().Should().Be("AUDIT_STORE_FAILED");
        payload.RootElement.GetProperty("MessageHash").GetString().Should().Be(MessageHash);
    }

    [Theory]
    [InlineData("", "AUDIT_STORE_FAILED")]
    [InlineData("   ", "AUDIT_STORE_FAILED")]
    [InlineData("GGD/SECRET/AWS-AKID", "")]
    public void SaveFailClosedEvent_InvalidArguments_Throw(string errorCode, string messageHash)
    {
        using var directory = new TempDirectory();
        var store = new SqliteAuditStore(Path.Combine(directory.Root, "audit.db"));

        Action act = () => store.SaveFailClosedEvent(10, errorCode, messageHash);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void SaveFailClosedEvent_ExitCodeBelowTen_ThrowsArgumentOutOfRangeException()
    {
        using var directory = new TempDirectory();
        var store = new SqliteAuditStore(Path.Combine(directory.Root, "audit.db"));

        Action act = () => store.SaveFailClosedEvent(1, "AUDIT_STORE_FAILED", MessageHash);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    [UnsupportedOSPlatform("windows")]
    public void Ctor_OnUnix_FixesBroadDatabaseModeAndRecordsEvent()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Skip("Unix mode 断言仅在 Unix 运行（08-权限设计 §5.1）");
        }

        using var directory = new TempDirectory();
        string dbPath = Path.Combine(directory.Root, "audit.db");
        File.WriteAllText(dbPath, string.Empty);
        File.SetUnixFileMode(dbPath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead);

        _ = new SqliteAuditStore(dbPath);

        File.GetUnixFileMode(dbPath)
            .Should().Be(UnixFileMode.UserRead | UnixFileMode.UserWrite, "08-权限设计 §5.1：audit.db 必须为 0600");

        using SqliteConnection connection = SqliteTestHelpers.OpenRawConnection(dbPath);
        SqliteTestHelpers.ReadStringColumn(
                connection,
                "SELECT payload_json FROM audit_logs WHERE event_type = 'FilePermissionFixed' ORDER BY seq;")
            .Should().Contain(payload => payload.Contains(dbPath, StringComparison.Ordinal)
                && payload.Contains("\"NewMode\":\"0600\"", StringComparison.Ordinal));
    }

    [Fact]
    [SupportedOSPlatform("windows")]
    public void Ctor_OnWindows_RemovesEveryoneAllowRuleAndRecordsEvent()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Skip("Windows ACL 断言仅在 Windows 运行（08-权限设计 §5.2）");
        }

        using var directory = new TempDirectory();
        string dataDirectory = Path.Combine(directory.Root, "GitGuard");
        Directory.CreateDirectory(dataDirectory);
        AddEveryoneReadRule(dataDirectory);

        string dbPath = Path.Combine(dataDirectory, "audit.db");
        _ = new SqliteAuditStore(dbPath);

        DirectorySecurity security = new DirectoryInfo(dataDirectory).GetAccessControl(AccessControlSections.Access);
        var broadRules = security
            .GetAccessRules(includeExplicit: true, includeInherited: true, targetType: typeof(SecurityIdentifier))
            .Cast<FileSystemAccessRule>()
            .Where(rule => rule.AccessControlType == AccessControlType.Allow)
            .Where(rule => rule.IdentityReference is SecurityIdentifier sid
                && (sid.Value == "S-1-1-0" || sid.Value == "S-1-5-11"));
        broadRules.Should().BeEmpty("08-权限设计 §5.2：目录 ACL 必须重置为仅当前用户");

        using SqliteConnection connection = SqliteTestHelpers.OpenRawConnection(dbPath);
        SqliteTestHelpers.ReadScalarLong(
                connection,
                "SELECT COUNT(*) FROM audit_logs WHERE category = 'Security' AND event_type = 'FilePermissionFixed';")
            .Should().BePositive();
    }

    [SupportedOSPlatform("windows")]
    private static void AddEveryoneReadRule(string directoryPath)
    {
        var directoryInfo = new DirectoryInfo(directoryPath);
        DirectorySecurity security = directoryInfo.GetAccessControl(AccessControlSections.Access);
        security.AddAccessRule(new FileSystemAccessRule(
            new SecurityIdentifier("S-1-1-0"),
            FileSystemRights.Read,
            AccessControlType.Allow));
        directoryInfo.SetAccessControl(security);
    }
}
