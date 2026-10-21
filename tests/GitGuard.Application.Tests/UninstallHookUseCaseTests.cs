using FluentAssertions;
using GitGuard.Application.Hooks;
using GitGuard.Core;
using GitGuard.Core.Model;
using Microsoft.Data.Sqlite;
using SqliteException = GitGuard.Core.SqliteException;

namespace GitGuard.Application.Tests;

/// <summary>
/// Hook 卸载用例单元测试（FR-011 / TC-FR-010-04）：托管脚本删除、原 Hook 还原、未安装仓库存量不动（CLI 映射退出码 5）。
/// </summary>
public sealed class UninstallHookUseCaseTests
{
    private const string SampleVersion = "9.9.9-test";
    private const string LegacyHookContent = "#!/bin/sh\necho husky-like\n";

    private static readonly DateTimeOffset SampleUtcNow = new DateTimeOffset(2026, 10, 16, 8, 30, 0, TimeSpan.Zero);

    // FR-011：全新安装（无备份）卸载即删除脚本
    [Fact]
    public void Execute_ManagedScriptWithoutBackup_RemovesScriptAndPersistsUninstall()
    {
        using var repo = new TempGitRepository();
        var installStore = new RecordingAuditStore();
        InstallManagedHooks(repo, installStore);
        var uninstallStore = new RecordingAuditStore();
        string hookPath = PreCommitPath(repo);

        HookUninstallReport report = CreateUseCase(repo, uninstallStore).Execute(hooks: new[] { HookType.PreCommit });

        HookUninstallResult result = report.Results.Should().ContainSingle().Subject;
        result.Disposition.Should().Be(HookUninstallDisposition.Removed);
        result.BackupPath.Should().BeNull();
        File.Exists(hookPath).Should().BeFalse();
        File.Exists(hookPath + HookTypeExtensions.BACKUP_FILE_SUFFIX).Should().BeFalse();

        var (installation, restoredLegacyHook) = uninstallStore.SavedHookUninstallations.Should().ContainSingle().Subject;
        restoredLegacyHook.Should().BeFalse();
        installation.HookType.Should().Be(HookType.PreCommit);
        installation.RepoPath.Should().Be(repo.Root);
        installation.InstalledAt.Should().Be(SampleUtcNow);
        installation.OriginalHookBackupPath.Should().BeNull();
    }

    // TC-FR-010-04：安装时包裹了原 Hook，卸载后恢复为原脚本（逐字节一致）
    [Fact]
    public void Execute_ManagedScriptWithBackup_RestoresLegacyHookVerbatim()
    {
        using var repo = new TempGitRepository();
        string hookPath = WriteLegacyHook(repo);
        InstallManagedHooks(repo, new RecordingAuditStore());
        var uninstallStore = new RecordingAuditStore();

        HookUninstallReport report = CreateUseCase(repo, uninstallStore).Execute(hooks: new[] { HookType.PreCommit });

        HookUninstallResult result = report.Results.Should().ContainSingle().Subject;
        result.Disposition.Should().Be(HookUninstallDisposition.RestoredLegacy);
        result.BackupPath.Should().Be(hookPath + HookTypeExtensions.BACKUP_FILE_SUFFIX);
        File.ReadAllText(hookPath).Should().Be(LegacyHookContent);
        File.Exists(hookPath + HookTypeExtensions.BACKUP_FILE_SUFFIX).Should().BeFalse();

        var (installation, restoredLegacyHook) = uninstallStore.SavedHookUninstallations.Should().ContainSingle().Subject;
        restoredLegacyHook.Should().BeTrue();
        installation.OriginalHookBackupPath.Should().Be("pre-commit.local.bak");
    }

    // FR-011：未发现 GitGuard 托管痕迹的 Hook 一律不动（防止误删用户自己的 Hook）
    [Fact]
    public void Execute_UnmanagedScriptWithoutBackup_LeavesItUntouchedWithoutAudit()
    {
        using var repo = new TempGitRepository();
        var store = new RecordingAuditStore();
        string hookPath = WriteLegacyHook(repo);

        HookUninstallReport report = CreateUseCase(repo, store).Execute(hooks: new[] { HookType.PreCommit });

        report.Results.Should().ContainSingle()
            .Which.Disposition.Should().Be(HookUninstallDisposition.NotInstalled);
        File.ReadAllText(hookPath).Should().Be(LegacyHookContent);
        store.SavedHookUninstallations.Should().BeEmpty();
    }

    // 托管脚本被手工替换但备份仍在：仅在 --force（用户显式接管）时删除目标并还原备份
    [Fact]
    public void Execute_UnmanagedTargetWithBackupWithoutForce_LeavesItUntouchedWithoutAudit()
    {
        using var repo = new TempGitRepository();
        var store = new RecordingAuditStore();
        string hookPath = WriteLegacyHook(repo);
        string backupPath = hookPath + HookTypeExtensions.BACKUP_FILE_SUFFIX;
        File.WriteAllText(backupPath, "# replaced backup\n");

        HookUninstallReport report = CreateUseCase(repo, store).Execute(hooks: new[] { HookType.PreCommit });

        report.Results.Should().ContainSingle()
            .Which.Disposition.Should().Be(HookUninstallDisposition.NotInstalled);
        File.ReadAllText(hookPath).Should().Be(LegacyHookContent);
        File.ReadAllText(backupPath).Should().Be("# replaced backup\n");
        store.SavedHookUninstallations.Should().BeEmpty();
    }

    [Fact]
    public void Execute_UnmanagedTargetWithBackupAndForce_RestoresBackupOverTarget()
    {
        using var repo = new TempGitRepository();
        var store = new RecordingAuditStore();
        string hookPath = WriteLegacyHook(repo);
        string backupPath = hookPath + HookTypeExtensions.BACKUP_FILE_SUFFIX;
        File.WriteAllText(backupPath, "# replaced backup\n");

        HookUninstallReport report = CreateUseCase(repo, store)
            .Execute(force: true, hooks: new[] { HookType.PreCommit });

        report.Results.Should().ContainSingle()
            .Which.Disposition.Should().Be(HookUninstallDisposition.RestoredLegacy);
        File.ReadAllText(hookPath).Should().Be("# replaced backup\n");
        File.Exists(backupPath).Should().BeFalse();
        store.SavedHookUninstallations.Should().ContainSingle()
            .Which.RestoredLegacyHook.Should().BeTrue();
    }

    // 托管脚本被手工删除但备份仍在：直接还原，无需 --force
    [Fact]
    public void Execute_MissingTargetWithBackup_RestoresWithoutForce()
    {
        using var repo = new TempGitRepository();
        var store = new RecordingAuditStore();
        string hookPath = PreCommitPath(repo);
        string backupPath = hookPath + HookTypeExtensions.BACKUP_FILE_SUFFIX;
        File.WriteAllText(backupPath, LegacyHookContent);

        HookUninstallReport report = CreateUseCase(repo, store).Execute(hooks: new[] { HookType.PreCommit });

        report.Results.Should().ContainSingle()
            .Which.Disposition.Should().Be(HookUninstallDisposition.RestoredLegacy);
        File.ReadAllText(hookPath).Should().Be(LegacyHookContent);
    }

    [Fact]
    public void Execute_NothingInstalled_ReportsNotInstalledForEachHookWithoutAudit()
    {
        using var repo = new TempGitRepository();
        var store = new RecordingAuditStore();

        HookUninstallReport report = CreateUseCase(repo, store).Execute();

        report.Results.Should().HaveCount(3);
        report.Results.Should().OnlyContain(result => result.Disposition == HookUninstallDisposition.NotInstalled);
        store.SavedHookUninstallations.Should().BeEmpty();
    }

    [Fact]
    public void Execute_MultipleHooks_ReportsInRequestOrder()
    {
        using var repo = new TempGitRepository();
        InstallManagedHooks(repo, new RecordingAuditStore());

        HookUninstallReport report = CreateUseCase(repo, new RecordingAuditStore())
            .Execute(hooks: new[] { HookType.PrePush, HookType.PreCommit });

        report.Results.Select(result => result.HookType).Should().Equal(HookType.PrePush, HookType.PreCommit);
        report.Results.Should().OnlyContain(result => result.Disposition == HookUninstallDisposition.Removed);
    }

    // 06-API接口设计 §8：与安装共用同一把锁，冲突映射退出码 50
    [Fact]
    public void Execute_InstallLockHeld_ThrowsInstallInProgress()
    {
        using var repo = new TempGitRepository();
        var store = new RecordingAuditStore();
        string lockPath = Path.Combine(repo.Root, ".git", "gitguard.install.lock");
        using var heldLock = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);

        Action act = () => CreateUseCase(repo, store).Execute(hooks: new[] { HookType.PreCommit });

        act.Should().Throw<HookException>().Which.ErrorCode.Should().Be(HookException.INSTALL_IN_PROGRESS);
        store.SavedHookUninstallations.Should().BeEmpty();
    }

    [Fact]
    public void Execute_AuditWriteFailure_PropagatesWhileScriptDeleted()
    {
        using var repo = new TempGitRepository();
        InstallManagedHooks(repo, new RecordingAuditStore());
        string hookPath = PreCommitPath(repo);
        var useCase = new UninstallHookUseCase(new FakeGitAccess(repo.Root), new FailingAuditStore(), new FixedClock(SampleUtcNow));

        Action act = () => useCase.Execute(hooks: new[] { HookType.PreCommit });

        act.Should().Throw<SqliteException>();
        File.Exists(hookPath).Should().BeFalse();
    }

    [Fact]
    public void Execute_EmptyHookList_ThrowsArgumentOutOfRange()
    {
        using var repo = new TempGitRepository();

        Action act = () => CreateUseCase(repo, new RecordingAuditStore()).Execute(hooks: Array.Empty<HookType>());

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Execute_DuplicateHookType_ThrowsArgumentException()
    {
        using var repo = new TempGitRepository();

        Action act = () => CreateUseCase(repo, new RecordingAuditStore())
            .Execute(hooks: new[] { HookType.PrePush, HookType.PrePush });

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Execute_PostCommitHook_RemovesBypassDetectionScript()
    {
        using var repo = new TempGitRepository();
        InstallManagedHooks(repo, new RecordingAuditStore());
        string hookPath = Path.Combine(repo.Root, ".git", "hooks", "post-commit");
        File.Exists(hookPath).Should().BeTrue();

        HookUninstallReport report = CreateUseCase(repo, new RecordingAuditStore())
            .Execute(hooks: new[] { HookType.PostCommit });

        report.Results.Should().ContainSingle()
            .Which.Disposition.Should().Be(HookUninstallDisposition.Removed);
        File.Exists(hookPath).Should().BeFalse();
    }

    private static UninstallHookUseCase CreateUseCase(TempGitRepository repo, RecordingAuditStore store) =>
        new UninstallHookUseCase(new FakeGitAccess(repo.Root), store, new FixedClock(SampleUtcNow), SampleVersion);

    private static void InstallManagedHooks(TempGitRepository repo, RecordingAuditStore store)
    {
        string executablePath = Path.Combine(repo.Root, "tools", "gitguard");
        Directory.CreateDirectory(Path.Combine(repo.Root, "tools"));
        File.WriteAllText(executablePath, "#!/bin/sh\nexit 0\n");
        new InstallHookUseCase(new FakeGitAccess(repo.Root), store, new FixedClock(SampleUtcNow), SampleVersion)
            .Execute(executablePath);
    }

    private static string PreCommitPath(TempGitRepository repo) =>
        Path.Combine(repo.Root, ".git", "hooks", "pre-commit");

    private static string WriteLegacyHook(TempGitRepository repo)
    {
        string hookPath = PreCommitPath(repo);
        File.WriteAllText(hookPath, LegacyHookContent);
        return hookPath;
    }
}
