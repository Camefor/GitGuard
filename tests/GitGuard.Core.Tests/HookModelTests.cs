using FluentAssertions;
using GitGuard.Core.Domain;
using GitGuard.Core.Model;

namespace GitGuard.Core.Tests;

/// <summary>Hook 领域模型单元测试（DDD §3.4 / §4.1-§4.2、FR-010 / FR-011、DR-05）。</summary>
public sealed class HookModelTests
{
    private const string SampleVersion = "1.2.3";

    [Theory]
    [InlineData(HookType.PreCommit, "pre-commit")]
    [InlineData(HookType.PrePush, "pre-push")]
    [InlineData(HookType.PostCommit, "post-commit")]
    public void ToHookName_KnownValues_MapToGitHookNames(HookType hookType, string expected)
    {
        hookType.ToHookName().Should().Be(expected);
    }

    [Fact]
    public void ToHookName_UndefinedValue_ThrowsInvariantViolation()
    {
        Action act = () => ((HookType)99).ToHookName();

        act.Should().Throw<InvariantViolationException>().WithMessage("*05-数据库设计 §4.4*");
    }

    [Theory]
    [InlineData(HookType.PreCommit, "pre-commit.local.bak")]
    [InlineData(HookType.PrePush, "pre-push.local.bak")]
    public void ToBackupFileName_KnownValues_EndWithLocalBakSuffix(HookType hookType, string expected)
    {
        hookType.ToBackupFileName().Should().Be(expected);
        HookTypeExtensions.BACKUP_FILE_SUFFIX.Should().Be(".local.bak");
    }

    [Theory]
    [InlineData("pre-commit", HookType.PreCommit)]
    [InlineData("pre-push", HookType.PrePush)]
    [InlineData("post-commit", HookType.PostCommit)]
    public void TryParseHookName_KnownName_ReturnsTrueAndType(string hookName, HookType expected)
    {
        bool parsed = HookTypeExtensions.TryParseHookName(hookName, out HookType hookType);

        parsed.Should().BeTrue();
        hookType.Should().Be(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("commit-msg")]
    [InlineData("Pre-Commit")]
    public void TryParseHookName_UnknownName_ReturnsFalseWithDefault(string? hookName)
    {
        bool parsed = HookTypeExtensions.TryParseHookName(hookName, out HookType hookType);

        parsed.Should().BeFalse();
        hookType.Should().Be(default(HookType));
    }

    [Fact]
    public void Create_WithAllFields_PopulatesActiveRecord()
    {
        var installation = HookInstallation.Create(
            TestData.SampleRepoPath,
            HookType.PreCommit,
            TestData.SampleUtcNow,
            SampleVersion,
            "pre-commit.local.bak");

        installation.RepoPath.Should().Be(TestData.SampleRepoPath);
        installation.HookType.Should().Be(HookType.PreCommit);
        installation.InstalledAt.Should().Be(TestData.SampleUtcNow);
        installation.Version.Should().Be(SampleVersion);
        installation.OriginalHookBackupPath.Should().Be("pre-commit.local.bak");
        installation.Status.Should().Be(HookInstallationStatus.Active);
    }

    [Fact]
    public void Create_WithoutBackup_LeavesBackupPathNull()
    {
        var installation = HookInstallation.Create(TestData.SampleRepoPath, HookType.PrePush, TestData.SampleUtcNow, SampleVersion);

        installation.OriginalHookBackupPath.Should().BeNull();
    }

    [Fact]
    public void Create_TrailingSeparator_IsTrimmed()
    {
        string withSeparator = TestData.SampleRepoPath + Path.DirectorySeparatorChar;

        var installation = HookInstallation.Create(withSeparator, HookType.PreCommit, TestData.SampleUtcNow, SampleVersion);

        installation.RepoPath.Should().Be(TestData.SampleRepoPath);
    }

    [Fact]
    public void Create_NullRepoPath_ThrowsArgumentNullException()
    {
        Action act = () => HookInstallation.Create(null!, HookType.PreCommit, TestData.SampleUtcNow, SampleVersion);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Create_NullVersion_ThrowsArgumentNullException()
    {
        Action act = () => HookInstallation.Create(TestData.SampleRepoPath, HookType.PreCommit, TestData.SampleUtcNow, null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Create_WhitespaceRepoPath_ThrowsInvariantViolation()
    {
        Action act = () => HookInstallation.Create("   ", HookType.PreCommit, TestData.SampleUtcNow, SampleVersion);

        act.Should().Throw<InvariantViolationException>().WithMessage("*05-数据库设计 §4.1*");
    }

    [Fact]
    public void Create_RelativeRepoPath_ThrowsInvariantViolation()
    {
        Action act = () => HookInstallation.Create("repo", HookType.PreCommit, TestData.SampleUtcNow, SampleVersion);

        act.Should().Throw<InvariantViolationException>().WithMessage("*绝对路径*");
    }

    [Fact]
    public void Create_UndefinedHookType_ThrowsInvariantViolation()
    {
        Action act = () => HookInstallation.Create(TestData.SampleRepoPath, (HookType)99, TestData.SampleUtcNow, SampleVersion);

        act.Should().Throw<InvariantViolationException>().WithMessage("*未知的 HookType*");
    }

    [Fact]
    public void Create_NonUtcInstalledAt_ThrowsInvariantViolation()
    {
        var local = new DateTimeOffset(2026, 10, 15, 20, 0, 0, TimeSpan.FromHours(8));

        Action act = () => HookInstallation.Create(TestData.SampleRepoPath, HookType.PreCommit, local, SampleVersion);

        act.Should().Throw<InvariantViolationException>().WithMessage("*UTC*");
    }

    [Fact]
    public void Create_WhitespaceVersion_ThrowsInvariantViolation()
    {
        Action act = () => HookInstallation.Create(TestData.SampleRepoPath, HookType.PreCommit, TestData.SampleUtcNow, "   ");

        act.Should().Throw<InvariantViolationException>().WithMessage("*DDD §3.4*");
    }

    [Fact]
    public void Create_BackupPathWithDirectory_ThrowsInvariantViolation()
    {
        Action act = () => HookInstallation.Create(
            TestData.SampleRepoPath,
            HookType.PreCommit,
            TestData.SampleUtcNow,
            SampleVersion,
            "hooks/pre-commit.local.bak");

        act.Should().Throw<InvariantViolationException>().WithMessage("*纯文件名*");
    }

    [Fact]
    public void Create_BackupPathWithoutSuffix_ThrowsInvariantViolation()
    {
        Action act = () => HookInstallation.Create(
            TestData.SampleRepoPath,
            HookType.PreCommit,
            TestData.SampleUtcNow,
            SampleVersion,
            "pre-commit.bak");

        act.Should().Throw<InvariantViolationException>().WithMessage("*以 .local.bak 结尾*");
    }

    [Fact]
    public void MarkUninstalled_ReturnsCopyWithUninstalledStatus()
    {
        var installation = HookInstallation.Create(
            TestData.SampleRepoPath,
            HookType.PreCommit,
            TestData.SampleUtcNow,
            SampleVersion,
            "pre-commit.local.bak");

        var uninstalled = installation.MarkUninstalled();

        uninstalled.Status.Should().Be(HookInstallationStatus.Uninstalled);
        uninstalled.RepoPath.Should().Be(installation.RepoPath);
        uninstalled.HookType.Should().Be(installation.HookType);
        uninstalled.InstalledAt.Should().Be(installation.InstalledAt);
        uninstalled.Version.Should().Be(installation.Version);
        uninstalled.OriginalHookBackupPath.Should().Be(installation.OriginalHookBackupPath);
        installation.Status.Should().Be(HookInstallationStatus.Active);
    }

    [Fact]
    public void MarkUninstalled_AlreadyUninstalled_Idempotent()
    {
        var uninstalled = HookInstallation.Create(TestData.SampleRepoPath, HookType.PreCommit, TestData.SampleUtcNow, SampleVersion)
            .MarkUninstalled();

        uninstalled.MarkUninstalled().Should().Be(uninstalled);
    }

    [Fact]
    public void Restore_ExplicitStatus_PreservesStatus()
    {
        var installation = HookInstallation.Restore(
            TestData.SampleRepoPath,
            HookType.PrePush,
            TestData.SampleUtcNow,
            SampleVersion,
            null,
            HookInstallationStatus.Uninstalled);

        installation.Status.Should().Be(HookInstallationStatus.Uninstalled);
        installation.HookType.Should().Be(HookType.PrePush);
    }

    [Fact]
    public void Restore_UndefinedStatus_ThrowsInvariantViolation()
    {
        Action act = () => HookInstallation.Restore(
            TestData.SampleRepoPath,
            HookType.PreCommit,
            TestData.SampleUtcNow,
            SampleVersion,
            null,
            (HookInstallationStatus)99);

        act.Should().Throw<InvariantViolationException>().WithMessage("*未知的 HookInstallationStatus*");
    }

    [Fact]
    public void Equals_SameCompositeKeyDifferentFields_IsTrue()
    {
        var first = HookInstallation.Create(TestData.SampleRepoPath, HookType.PreCommit, TestData.SampleUtcNow, SampleVersion);
        var second = HookInstallation.Create(
            TestData.SampleRepoPath,
            HookType.PreCommit,
            TestData.SampleUtcNow.AddMinutes(5),
            "9.9.9",
            "pre-commit.local.bak");

        first.Equals(second).Should().BeTrue();
        first.GetHashCode().Should().Be(second.GetHashCode());
    }

    [Fact]
    public void Equals_DifferentHookType_IsFalse()
    {
        var preCommit = HookInstallation.Create(TestData.SampleRepoPath, HookType.PreCommit, TestData.SampleUtcNow, SampleVersion);
        var prePush = HookInstallation.Create(TestData.SampleRepoPath, HookType.PrePush, TestData.SampleUtcNow, SampleVersion);

        preCommit.Equals(prePush).Should().BeFalse();
    }

    [Fact]
    public void Equals_DifferentRepoPath_IsFalse()
    {
        var first = HookInstallation.Create(TestData.SampleRepoPath, HookType.PreCommit, TestData.SampleUtcNow, SampleVersion);
        var second = HookInstallation.Create(Path.GetFullPath("other"), HookType.PreCommit, TestData.SampleUtcNow, SampleVersion);

        first.Equals(second).Should().BeFalse();
    }

    [Fact]
    public void Equals_Null_IsFalse()
    {
        var installation = HookInstallation.Create(TestData.SampleRepoPath, HookType.PreCommit, TestData.SampleUtcNow, SampleVersion);

        installation.Equals(null).Should().BeFalse();
    }

    [Fact]
    public void Equals_OtherType_IsFalse()
    {
        var installation = HookInstallation.Create(TestData.SampleRepoPath, HookType.PreCommit, TestData.SampleUtcNow, SampleVersion);
        object other = "pre-commit";

        installation.Equals(other).Should().BeFalse();
    }
}
