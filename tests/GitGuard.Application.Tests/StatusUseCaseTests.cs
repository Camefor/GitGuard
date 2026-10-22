using FluentAssertions;
using GitGuard.Application.Configuration;
using GitGuard.Application.Hooks;
using GitGuard.Core;
using GitGuard.Core.Model;
using GitGuard.Infrastructure.Git;
using GitGuard.Infrastructure.Time;

namespace GitGuard.Application.Tests;

/// <summary>
/// 状态查询用例测试（TC-FR-012-01 / FR-012，<c>06-API接口设计</c> §5.3）：真实 git 仓库验证 Hook 文件签名分类、
/// 配置加载链回显与 <c>NeedsAttention</c> → 退出码 6 的判定来源。
/// </summary>
public sealed class StatusUseCaseTests
{
    private const string SAMPLE_VERSION = "1.2.3-status";

    [Fact]
    public void Execute_WithoutHooks_ReportsMissingAndNoAttention()
    {
        using var repo = new TempGitRepository();

        StatusReport report = CreateUseCase(repo).Execute();

        report.RepoPath.Should().Be(repo.Root);
        report.GitVersion.Should().StartWith("git version");
        report.GitGuardVersion.Should().Be(SAMPLE_VERSION);
        report.Hooks.Should().HaveCount(3);
        report.Hooks.Should().OnlyContain(hook => hook.Kind == HookStatusKind.Missing);
        report.Hooks.Should().OnlyContain(hook => hook.ManagedVersion == null);
        report.NeedsAttention.Should().BeFalse();
        report.ConfigFiles.Should().BeEmpty("未提供配置文件时加载链为空（全部内置默认）");
    }

    [Fact]
    public void Execute_AfterInstall_ReportsManagedHooksWithVersion()
    {
        using var repo = new TempGitRepository();
        var git = new GitAccess(repo.Root);
        new InstallHookUseCase(git, new RecordingAuditStore(), new SystemClock(), SAMPLE_VERSION)
            .Execute("/opt/gitguard/gitguard");

        StatusReport report = CreateUseCase(repo).Execute();

        report.Hooks.Should().OnlyContain(hook => hook.Kind == HookStatusKind.Managed);
        report.Hooks.Should().OnlyContain(hook => hook.ManagedVersion == SAMPLE_VERSION);
        report.NeedsAttention.Should().BeFalse();
    }

    [Fact]
    public void Execute_WithUnmanagedScript_FlagsNeedsAttention()
    {
        using var repo = new TempGitRepository();
        repo.WriteFile(".git/hooks/pre-commit", "#!/bin/sh\necho husky\n");

        StatusReport report = CreateUseCase(repo).Execute();

        HookStatus preCommit = report.Hooks.Single(hook => hook.HookType == HookType.PreCommit);
        preCommit.Kind.Should().Be(HookStatusKind.Legacy);
        preCommit.ManagedVersion.Should().BeNull();
        report.NeedsAttention.Should().BeTrue();
    }

    [Fact]
    public void Execute_WithBinaryHook_FlagsNeedsAttention()
    {
        using var repo = new TempGitRepository();
        repo.WriteBytes(".git/hooks/pre-push", new byte[] { 0x00, 0x01, 0x02, 0x03 });

        StatusReport report = CreateUseCase(repo).Execute();

        report.Hooks.Single(hook => hook.HookType == HookType.PrePush).Kind.Should().Be(HookStatusKind.Binary);
        report.NeedsAttention.Should().BeTrue();
    }

    [Fact]
    public void Execute_WithConfiguredHooksPath_UsesConfiguredDirectory()
    {
        using var repo = new TempGitRepository();
        string hooksPath = Path.Combine(repo.Root, "custom-hooks");
        Directory.CreateDirectory(hooksPath);
        repo.RunGit("config", "core.hooksPath", hooksPath);

        StatusReport report = CreateUseCase(repo).Execute();

        report.ConfiguredHooksPath.Should().Be(hooksPath);
        report.HooksDirectory.Should().Be(hooksPath);
        report.Hooks.Should().OnlyContain(hook => hook.HookPath.StartsWith(hooksPath));
    }

    [Fact]
    public void Execute_NonRepository_FailsClosed()
    {
        using var plain = new TempPlainDirectory();

        Action act = () => new StatusUseCase(new GitAccess(plain.Root), GitGuardConfig.Default, SAMPLE_VERSION).Execute();

        act.Should().Throw<GitException>().Which.ErrorCode.Should().Be(GitException.NOT_A_GIT_REPOSITORY);
    }

    private static StatusUseCase CreateUseCase(TempGitRepository repo) =>
        new StatusUseCase(new GitAccess(repo.Root), GitGuardConfig.Default, SAMPLE_VERSION);

    /// <summary>非仓库临时目录（验证 FailClosed 分支）。</summary>
    private sealed class TempPlainDirectory : IDisposable
    {
        internal TempPlainDirectory()
        {
            Root = Path.Combine(Path.GetTempPath(), "gitguard-status-tests", Guid.NewGuid().ToString("N"));
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
