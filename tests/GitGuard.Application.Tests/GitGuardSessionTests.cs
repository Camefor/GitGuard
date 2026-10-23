using FluentAssertions;
using GitGuard.Application.Composition;
using GitGuard.Application.Configuration;
using GitGuard.Application.Hooks;
using GitGuard.Core;
using GitGuard.Core.Abstractions;
using GitGuard.Infrastructure.FileSystem;

namespace GitGuard.Application.Tests;

/// <summary>
/// 组合根测试（SAD §5.1 / ADR-0001）：懒装配边界（只读命令不触碰审计与检测器）、测试注入点生效、
/// FailClosed 异常类型与退出码映射来源一致。
/// </summary>
public sealed class GitGuardSessionTests
{
    [Fact]
    public void ResolveDefaultVersion_ReturnsAssemblyVersionWithoutBuildMetadata()
    {
        string version = GitGuardSession.ResolveDefaultVersion();

        version.Should().NotBeNullOrWhiteSpace();
        version.Should().NotContain("+", "版本号剥离 +build 元数据后写入 Hook 标记行与审计");
    }

    [Fact]
    public void CreateStatus_DoesNotTouchAuditStoreOrFingerprintSalt()
    {
        using var repo = new TempGitRepository();
        using var data = new TempDirectory();
        GitGuardSession session = CreateSession(repo.Root, data.Root);

        StatusReport report = session.CreateStatus().Execute();

        report.RepoPath.Should().Be(repo.Root);
        File.Exists(Path.Combine(data.Root, "audit.db")).Should().BeFalse("status 只读，不打开审计库");
        File.Exists(Path.Combine(data.Root, "cache", "fingerprint.salt")).Should().BeFalse("status 不装配检测器");
    }

    [Fact]
    public void RuleCount_TriggersDetectorAssemblyAndSaltPersistence()
    {
        using var repo = new TempGitRepository();
        using var data = new TempDirectory();
        GitGuardSession session = CreateSession(repo.Root, data.Root);

        int ruleCount = session.RuleCount;

        ruleCount.Should().BePositive();
        File.Exists(Path.Combine(data.Root, "cache", "fingerprint.salt")).Should().BeTrue("检测器装配依赖指纹盐");
        session.Warnings.Should().NotContainNulls();
    }

    // FR-090 / ADR-0003：use_gitleaks 缺失可执行文件时降级为警告，扫描能力不受影响
    [Fact]
    public void UseGitleaksWithMissingExecutable_DegradesWithWarningAndKeepsBuiltInRules()
    {
        using var repo = new TempGitRepository();
        using var data = new TempDirectory();
        string missingPath = Path.Combine(data.Root, "tools", "gitleaks.exe").Replace('\\', '/');
        repo.WriteFile(
            ".gitguard.yml",
            $"version: 1\nrules:\n  secrets:\n    use_gitleaks: true\n    gitleaks_path: \"{missingPath}\"\n");
        GitGuardSession session = CreateSession(repo.Root, data.Root);

        int ruleCount = session.RuleCount;

        ruleCount.Should().BePositive("缺失 Gitleaks 只禁用适配器，内置规则照常装配");
        session.Warnings.Should().Contain(warning => warning.Contains("Gitleaks 适配器已禁用"));
    }

    [Fact]
    public void Config_LoadsProjectFileFromWorkingDirectory()
    {
        using var repo = new TempGitRepository();
        using var data = new TempDirectory();
        repo.WriteFile(".gitguard.yml", "version: 1\nscan:\n  max_file_bytes: 4096\n");
        GitGuardSession session = CreateSession(repo.Root, data.Root);

        session.Config.Scan.MaxFileBytes.Should().Be(4096);
        session.Config.LoadedConfigFiles.Should().Contain(Path.Combine(repo.Root, ".gitguard.yml"));
        session.UserConfigPath.Should().Be(Path.Combine(data.Root, "config", "config.yml"));
        session.UserDataDirectory.Should().Be(data.Root);
        session.CrashLogDirectory.Should().Be(Path.Combine(data.Root, "crash"));
    }

    [Fact]
    public void ConfigOverrides_ApplyAfterProjectConfigMerge()
    {
        using var repo = new TempGitRepository();
        using var data = new TempDirectory();
        repo.WriteFile(".gitguard.yml", "version: 1\nrules:\n  secrets:\n    use_gitleaks: false\n");
        var overrides = new GitGuardConfigOverrides
        {
            UseGitleaks = true,
            GitleaksPath = "/opt/gitleaks",
        };
        GitGuardSession session = CreateSession(repo.Root, data.Root, overrides: overrides);

        session.Config.Secrets.UseGitleaks.Should().BeTrue("桌面覆盖优先于项目配置");
        session.Config.Secrets.GitleaksPath.Should().Be("/opt/gitleaks");
        File.ReadAllText(Path.Combine(repo.Root, ".gitguard.yml"))
            .Should().Contain("use_gitleaks: false", "覆盖不得写回配置文件");
    }

    [Fact]
    public void CreateScopedSession_InheritsParentConfigOverrides()
    {
        using var repo = new TempGitRepository();
        using var data = new TempDirectory();
        var overrides = new GitGuardConfigOverrides
        {
            UseGitleaks = true,
            GitleaksPath = "/opt/gitleaks",
        };
        GitGuardSession session = CreateSession(repo.Root, data.Root, overrides: overrides);

        GitGuardSession scoped = session.CreateScopedSession(repo.Root);

        scoped.Config.Secrets.UseGitleaks.Should().BeTrue();
        scoped.Config.Secrets.GitleaksPath.Should().Be("/opt/gitleaks");
    }

    [Fact]
    public void NonRepositoryWorkingDirectory_FailsClosedWithGitException()
    {
        using var data = new TempDirectory();
        string workingDirectory = Path.Combine(data.Root, "plain");
        Directory.CreateDirectory(workingDirectory);
        GitGuardSession session = CreateSession(workingDirectory, Path.Combine(data.Root, "data"));

        Action status = () => session.CreateStatus().Execute();
        Action scan = () => session.CreateStagedScan();

        status.Should().Throw<GitException>().Which.ErrorCode.Should().Be(GitException.NOT_A_GIT_REPOSITORY);
        scan.Should().Throw<GitException>().Which.ErrorCode.Should().Be(GitException.NOT_A_GIT_REPOSITORY);
    }

    [Fact]
    public void InvalidProjectConfig_FailsClosedWithConfigurationException()
    {
        using var repo = new TempGitRepository();
        using var data = new TempDirectory();
        repo.WriteFile(".gitguard.yml", "version: 42\n");
        GitGuardSession session = CreateSession(repo.Root, data.Root);

        Action status = () => session.CreateStatus().Execute();

        status.Should().Throw<ConfigurationException>().Which.ErrorCode.Should().Be(ConfigurationException.CONFIG_INVALID);
    }

    [Fact]
    public void VersionOption_IsHonouredForHookMarkerLine()
    {
        using var repo = new TempGitRepository();
        using var data = new TempDirectory();
        GitGuardSession session = CreateSession(repo.Root, data.Root, version: "1.2.3-test");

        session.Version.Should().Be("1.2.3-test");
        session.CreateInstall().Execute("/opt/gitguard/gitguard", hooks: null);
        File.ReadAllText(Path.Combine(repo.Root, ".git", "hooks", "pre-commit"))
            .Should().Contain("# gitguard-managed v1.2.3-test");
    }

    private static GitGuardSession CreateSession(
        string workingDirectory,
        string dataDirectory,
        string? version = null,
        GitGuardConfigOverrides? overrides = null) =>
        new GitGuardSession(
            new GitGuardSessionOptions
            {
                WorkingDirectory = workingDirectory,
                DataDirectory = dataDirectory,
                UserConfigDirectory = Path.Combine(dataDirectory, "config"),
                GitGuardVersion = version,
                ConfigOverrides = overrides,
            },
            new FixedClock(new DateTimeOffset(2026, 10, 16, 9, 0, 0, TimeSpan.Zero)),
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
            Root = Path.Combine(Path.GetTempPath(), "gitguard-session-tests", Guid.NewGuid().ToString("N"));
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
