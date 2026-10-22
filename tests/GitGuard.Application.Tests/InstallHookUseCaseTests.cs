using System.Text;
using FluentAssertions;
using GitGuard.Application.Hooks;
using GitGuard.Core;
using GitGuard.Core.Model;
using Microsoft.Data.Sqlite;
using SqliteException = GitGuard.Core.SqliteException;

namespace GitGuard.Application.Tests;

/// <summary>
/// Hook 安装用例单元测试（TC-FR-010-01~03 / 05、TC-DR-05-01 的备份与包裹契约）：
/// 文件系统为真实临时目录，Git 访问以 <see cref="FakeGitAccess"/> 注入；审计写入以内存替身记录。
/// </summary>
public sealed class InstallHookUseCaseTests
{
    private const string SAMPLE_VERSION = "9.9.9-test";
    private const string LEGACY_HOOK_CONTENT = "#!/bin/sh\necho legacy\n";
    private const string INSTALL_LOCK_FILE_NAME = "gitguard.install.lock";

    private static readonly DateTimeOffset _sampleUtcNow = new DateTimeOffset(2026, 10, 16, 8, 30, 0, TimeSpan.Zero);

    [Fact]
    public void Execute_FreshRepository_InstallsAllHooksAndPersistsActiveRecords()
    {
        using var repo = new TempGitRepository();
        var store = new RecordingAuditStore();
        var useCase = CreateUseCase(repo, store);
        string executablePath = CreateExecutable(repo);

        HookInstallReport report = useCase.Execute(executablePath);

        report.RepoPath.Should().Be(repo.Root);
        report.Version.Should().Be(SAMPLE_VERSION);
        report.Results.Should().HaveCount(3);
        report.Results.Select(result => result.HookType)
            .Should().Equal(HookType.PreCommit, HookType.PrePush, HookType.PostCommit);
        report.Results.Should().OnlyContain(result => result.Disposition == HookInstallDisposition.Installed);
        report.Results.Should().OnlyContain(result => result.BackupPath == null);

        string preCommitPath = Path.Combine(repo.Root, ".git", "hooks", "pre-commit");
        File.Exists(preCommitPath).Should().BeTrue();
        File.Exists(Path.Combine(repo.Root, ".git", "hooks", "pre-push")).Should().BeTrue();
        File.ReadAllText(Path.Combine(repo.Root, ".git", "hooks", "post-commit")).Should().Contain("hook post-commit");
        string script = File.ReadAllText(preCommitPath);
        script.Should().StartWith($"#!/bin/sh\n# gitguard-managed v{SAMPLE_VERSION}\n");
        script.Should().Contain($"GITGUARD_BIN=\"{HookTestSupport.ToUnixPath(Path.GetFullPath(executablePath))}\"");
        script.Should().NotContain("\r");

        store.SavedHookInstallations.Should().HaveCount(3);
        store.SavedHookInstallations.Should().OnlyContain(installation => installation.Status == HookInstallationStatus.Active);
        store.SavedHookInstallations.Should().OnlyContain(installation => installation.RepoPath == repo.Root);
        store.SavedHookInstallations.Should().OnlyContain(installation => installation.InstalledAt == _sampleUtcNow);
        store.SavedHookInstallations.Should().OnlyContain(installation => installation.Version == SAMPLE_VERSION);
        store.SavedHookInstallations.Should().OnlyContain(installation => installation.OriginalHookBackupPath == null);
    }

    // TC-FR-010-02 / DR-05：原 Hook 静默备份为 <hook>.local.bak 后包裹
    [Fact]
    public void Execute_LegacyScript_BacksUpVerbatimAndWraps()
    {
        using var repo = new TempGitRepository();
        var store = new RecordingAuditStore();
        var useCase = CreateUseCase(repo, store);
        string hookPath = WriteLegacyHook(repo, HookType.PreCommit);

        HookInstallReport report = useCase.Execute(CreateExecutable(repo), hooks: new[] { HookType.PreCommit });

        HookInstallResult result = report.Results.Should().ContainSingle().Subject;
        result.Disposition.Should().Be(HookInstallDisposition.WrappedLegacy);
        result.BackupPath.Should().Be(hookPath + HookTypeExtensions.BACKUP_FILE_SUFFIX);
        File.ReadAllText(result.BackupPath!).Should().Be(LEGACY_HOOK_CONTENT);
        string script = File.ReadAllText(hookPath);
        script.Should().Contain("# gitguard-managed v");
        script.Should().Contain("pre-commit.local.bak");

        store.SavedHookInstallations.Should().ContainSingle()
            .Which.OriginalHookBackupPath.Should().Be("pre-commit.local.bak");
    }

    // TC-FR-010-03：已托管脚本覆盖刷新且不再次备份
    [Fact]
    public void Execute_ManagedScript_RefreshesScriptWithoutNewBackup()
    {
        using var repo = new TempGitRepository();
        var store = new RecordingAuditStore();
        string executablePath = CreateExecutable(repo);
        string hookPath = Path.Combine(repo.Root, ".git", "hooks", "pre-commit");
        CreateUseCase(repo, store, version: "1.0.0").Execute(executablePath, hooks: new[] { HookType.PreCommit });

        HookInstallReport report = CreateUseCase(repo, store, version: "2.0.0")
            .Execute(executablePath, hooks: new[] { HookType.PreCommit });

        report.Results.Should().ContainSingle()
            .Which.Disposition.Should().Be(HookInstallDisposition.Refreshed);
        File.ReadAllText(hookPath).Should().StartWith("#!/bin/sh\n# gitguard-managed v2.0.0\n");
        File.Exists(hookPath + HookTypeExtensions.BACKUP_FILE_SUFFIX).Should().BeFalse();
        store.SavedHookInstallations.Should().HaveCount(2);
        store.SavedHookInstallations.Should().OnlyContain(installation => installation.OriginalHookBackupPath == null);
    }

    // TC-FR-010-05：二进制目标需 --force，未给开关时在写入前失败（零半安装状态）
    [Fact]
    public void Execute_BinaryTargetWithoutForce_ThrowsAndWritesNothing()
    {
        using var repo = new TempGitRepository();
        var store = new RecordingAuditStore();
        var useCase = CreateUseCase(repo, store);
        string hookPath = Path.Combine(repo.Root, ".git", "hooks", "pre-commit");
        byte[] binary = { 0x7F, 0x45, 0x4C, 0x46, 0x00, 0x01, 0x02 };
        File.WriteAllBytes(hookPath, binary);

        Action act = () => useCase.Execute(CreateExecutable(repo));

        act.Should().Throw<HookException>().Which.ErrorCode.Should().Be(HookException.UNKNOWN_HOOK_PRESENT);
        File.ReadAllBytes(hookPath).Should().Equal(binary);
        File.Exists(Path.Combine(repo.Root, ".git", "hooks", "pre-push")).Should().BeFalse();
        File.Exists(hookPath + HookTypeExtensions.BACKUP_FILE_SUFFIX).Should().BeFalse();
        store.SavedHookInstallations.Should().BeEmpty();
    }

    [Fact]
    public void Execute_BinaryTargetWithForce_WrapsAndPreservesBytes()
    {
        using var repo = new TempGitRepository();
        var store = new RecordingAuditStore();
        var useCase = CreateUseCase(repo, store);
        string hookPath = Path.Combine(repo.Root, ".git", "hooks", "pre-commit");
        byte[] binary = { 0x7F, 0x45, 0x4C, 0x46, 0x00, 0x01, 0x02 };
        File.WriteAllBytes(hookPath, binary);

        HookInstallReport report = useCase.Execute(CreateExecutable(repo), force: true, hooks: new[] { HookType.PreCommit });

        report.Results.Should().ContainSingle().Which.Disposition.Should().Be(HookInstallDisposition.WrappedLegacy);
        File.ReadAllBytes(hookPath + HookTypeExtensions.BACKUP_FILE_SUFFIX).Should().Equal(binary);
        File.ReadAllText(hookPath).Should().Contain("# gitguard-managed v");
    }

    // 遗留备份 + 非托管目标：覆盖备份会丢失真正的原 Hook，拒绝接管且 --force 不适用
    [Fact]
    public void Execute_LeftoverBackupWithUnmanagedTarget_ThrowsEvenWithForce()
    {
        using var repo = new TempGitRepository();
        var store = new RecordingAuditStore();
        var useCase = CreateUseCase(repo, store);
        string hookPath = WriteLegacyHook(repo, HookType.PreCommit);
        string backupPath = hookPath + HookTypeExtensions.BACKUP_FILE_SUFFIX;
        File.WriteAllText(backupPath, "#!/bin/sh\necho original\n");

        Action act = () => useCase.Execute(CreateExecutable(repo), force: true, hooks: new[] { HookType.PreCommit });

        act.Should().Throw<HookException>().Which.ErrorCode.Should().Be(HookException.UNKNOWN_HOOK_PRESENT);
        File.ReadAllText(hookPath).Should().Be(LEGACY_HOOK_CONTENT);
        File.ReadAllText(backupPath).Should().Be("#!/bin/sh\necho original\n");
        store.SavedHookInstallations.Should().BeEmpty();
    }

    // 目标缺失但遗留备份仍在（托管脚本被手工删除）：写入分发脚本并继续引用该备份
    [Fact]
    public void Execute_MissingTargetWithLeftoverBackup_KeepsBackupAndReportsIt()
    {
        using var repo = new TempGitRepository();
        var store = new RecordingAuditStore();
        var useCase = CreateUseCase(repo, store);
        string hookPath = Path.Combine(repo.Root, ".git", "hooks", "pre-commit");
        string backupPath = hookPath + HookTypeExtensions.BACKUP_FILE_SUFFIX;
        File.WriteAllText(backupPath, LEGACY_HOOK_CONTENT);

        HookInstallReport report = useCase.Execute(CreateExecutable(repo), hooks: new[] { HookType.PreCommit });

        HookInstallResult result = report.Results.Should().ContainSingle().Subject;
        result.Disposition.Should().Be(HookInstallDisposition.Installed);
        result.BackupPath.Should().Be(backupPath);
        File.ReadAllText(backupPath).Should().Be(LEGACY_HOOK_CONTENT);
        store.SavedHookInstallations.Should().ContainSingle()
            .Which.OriginalHookBackupPath.Should().Be("pre-commit.local.bak");
    }

    // FR-010：core.hooksPath 指定的自定义目录优先于 <git-common-dir>/hooks
    [Fact]
    public void Execute_ConfiguredHooksPath_WritesThereAndCreatesDirectory()
    {
        using var repo = new TempGitRepository();
        string customHooksDirectory = Path.Combine(repo.Root, "custom-hooks");
        var store = new RecordingAuditStore();
        var useCase = new InstallHookUseCase(
            new FakeGitAccess(repo.Root, hooksPath: customHooksDirectory),
            store,
            new FixedClock(_sampleUtcNow),
            SAMPLE_VERSION);

        HookInstallReport report = useCase.Execute(CreateExecutable(repo), hooks: new[] { HookType.PreCommit });

        report.Results.Should().ContainSingle()
            .Which.HookPath.Should().Be(Path.Combine(customHooksDirectory, "pre-commit"));
        File.Exists(Path.Combine(customHooksDirectory, "pre-commit")).Should().BeTrue();
        File.Exists(Path.Combine(repo.Root, ".git", "hooks", "pre-commit")).Should().BeFalse();
    }

    // 06-API接口设计 §8：并发安装 / 卸载以 <git-common-dir>/gitguard.install.lock 互斥，冲突映射退出码 50
    [Fact]
    public void Execute_InstallLockHeld_ThrowsInstallInProgress()
    {
        using var repo = new TempGitRepository();
        var store = new RecordingAuditStore();
        var useCase = CreateUseCase(repo, store);
        string lockPath = Path.Combine(repo.Root, ".git", INSTALL_LOCK_FILE_NAME);
        using var heldLock = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);

        Action act = () => useCase.Execute(CreateExecutable(repo));

        act.Should().Throw<HookException>().Which.ErrorCode.Should().Be(HookException.INSTALL_IN_PROGRESS);
        store.SavedHookInstallations.Should().BeEmpty();
    }

    // 审计写入失败为 FailClosed：异常上抛，但磁盘变更已生效（06-API接口设计 §7）
    [Fact]
    public void Execute_AuditWriteFailure_PropagatesWhileDiskChangesApplied()
    {
        using var repo = new TempGitRepository();
        var useCase = new InstallHookUseCase(
            new FakeGitAccess(repo.Root),
            new FailingAuditStore(),
            new FixedClock(_sampleUtcNow),
            SAMPLE_VERSION);

        Action act = () => useCase.Execute(CreateExecutable(repo), hooks: new[] { HookType.PreCommit });

        act.Should().Throw<SqliteException>();
        File.Exists(Path.Combine(repo.Root, ".git", "hooks", "pre-commit")).Should().BeTrue();
    }

    [Fact]
    public void Execute_NullExecutablePath_ThrowsArgumentNullException()
    {
        using var repo = new TempGitRepository();
        var useCase = CreateUseCase(repo, new RecordingAuditStore());

        Action act = () => useCase.Execute(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Execute_BlankExecutablePath_ThrowsArgumentException(string executablePath)
    {
        using var repo = new TempGitRepository();
        var useCase = CreateUseCase(repo, new RecordingAuditStore());

        Action act = () => useCase.Execute(executablePath);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Execute_EmptyHookList_ThrowsArgumentOutOfRange()
    {
        using var repo = new TempGitRepository();
        var useCase = CreateUseCase(repo, new RecordingAuditStore());

        Action act = () => useCase.Execute(CreateExecutable(repo), hooks: Array.Empty<HookType>());

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Execute_DuplicateHookType_ThrowsArgumentException()
    {
        using var repo = new TempGitRepository();
        var useCase = CreateUseCase(repo, new RecordingAuditStore());

        Action act = () => useCase.Execute(
            CreateExecutable(repo),
            hooks: new[] { HookType.PreCommit, HookType.PreCommit });

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Execute_PostCommitHook_InstallsBypassDetectionScript()
    {
        using var repo = new TempGitRepository();
        var store = new RecordingAuditStore();
        var useCase = CreateUseCase(repo, store);

        HookInstallReport report = useCase.Execute(CreateExecutable(repo), hooks: new[] { HookType.PostCommit });

        HookInstallResult result = report.Results.Should().ContainSingle().Subject;
        result.HookType.Should().Be(HookType.PostCommit);
        result.Disposition.Should().Be(HookInstallDisposition.Installed);
        File.ReadAllText(result.HookPath).Should().Contain("hook post-commit");
        store.SavedHookInstallations.Should().ContainSingle()
            .Which.HookType.Should().Be(HookType.PostCommit);
    }

    [Fact]
    public void Execute_UndefinedHookType_ThrowsArgumentOutOfRange()
    {
        using var repo = new TempGitRepository();
        var useCase = CreateUseCase(repo, new RecordingAuditStore());

        Action act = () => useCase.Execute(CreateExecutable(repo), hooks: new[] { (HookType)99 });

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    private static InstallHookUseCase CreateUseCase(TempGitRepository repo, RecordingAuditStore store, string version = SAMPLE_VERSION) =>
        new InstallHookUseCase(new FakeGitAccess(repo.Root), store, new FixedClock(_sampleUtcNow), version);

    private static string CreateExecutable(TempGitRepository repo)
    {
        string path = Path.Combine(repo.Root, "tools", "gitguard");
        Directory.CreateDirectory(Path.Combine(repo.Root, "tools"));
        File.WriteAllText(path, "#!/bin/sh\nexit 0\n", new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        return path;
    }

    private static string WriteLegacyHook(TempGitRepository repo, HookType hookType)
    {
        string hookPath = Path.Combine(repo.Root, ".git", "hooks", hookType.ToHookName());
        File.WriteAllText(hookPath, LEGACY_HOOK_CONTENT);
        return hookPath;
    }
}
