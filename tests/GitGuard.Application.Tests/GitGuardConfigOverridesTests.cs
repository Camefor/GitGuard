using FluentAssertions;
using GitGuard.Application.Configuration;

namespace GitGuard.Application.Tests;

/// <summary>
/// 宿主配置覆盖测试（GGD-0046）：桌面设置页只覆盖 Gitleaks 相关字段，
/// 其余有效配置保持不变，且不写回配置文件。
/// </summary>
public sealed class GitGuardConfigOverridesTests
{
    [Fact]
    public void ApplyTo_OverridesGitleaksFields()
    {
        GitGuardConfig config = GitGuardConfig.Default with
        {
            Secrets = SecretRulesSettings.Default with
            {
                UseGitleaks = false,
                GitleaksPath = "from-config",
            },
        };
        var overrides = new GitGuardConfigOverrides
        {
            UseGitleaks = true,
            GitleaksPath = "/opt/gitleaks",
        };

        GitGuardConfig result = overrides.ApplyTo(config);

        result.Secrets.UseGitleaks.Should().BeTrue();
        result.Secrets.GitleaksPath.Should().Be("/opt/gitleaks");
        result.Secrets.MinEntropy.Should().Be(config.Secrets.MinEntropy);
        result.Scan.Should().BeSameAs(config.Scan);
        result.LoadedConfigFiles.Should().Equal(config.LoadedConfigFiles);
    }

    [Fact]
    public void ApplyTo_OverridesHookExecutablePath()
    {
        GitGuardConfig config = GitGuardConfig.Default with
        {
            Hooks = HooksSettings.Default with { ExecutablePath = "from-config" },
        };
        var overrides = new GitGuardConfigOverrides
        {
            HookExecutablePath = "/opt/gitguard/gitguard",
        };

        GitGuardConfig result = overrides.ApplyTo(config);

        result.Hooks.ExecutablePath.Should().Be("/opt/gitguard/gitguard");
        result.Hooks.PreCommit.Should().BeTrue();
        result.Hooks.PrePush.Should().BeTrue();
        result.Hooks.PostCommitBypassDetection.Should().BeTrue();
    }

    [Fact]
    public void ApplyTo_WithoutHookExecutablePath_PreservesConfiguredPath()
    {
        GitGuardConfig config = GitGuardConfig.Default with
        {
            Hooks = HooksSettings.Default with { ExecutablePath = "from-config" },
        };

        GitGuardConfig result = new GitGuardConfigOverrides().ApplyTo(config);

        result.Hooks.ExecutablePath.Should().Be("from-config");
    }

    [Fact]
    public void ApplyTo_WithoutValues_PreservesConfigValuesAndClearsPath()
    {
        GitGuardConfig config = GitGuardConfig.Default with
        {
            Secrets = SecretRulesSettings.Default with
            {
                UseGitleaks = true,
                GitleaksPath = "/opt/gitleaks",
            },
        };

        GitGuardConfig result = new GitGuardConfigOverrides().ApplyTo(config);

        result.Secrets.UseGitleaks.Should().BeTrue();
        result.Secrets.GitleaksPath.Should().BeNull("空白覆盖表示从 PATH 查找");
    }

    [Fact]
    public void ApplyTo_NullConfig_Throws()
    {
        Action act = () => _ = new GitGuardConfigOverrides().ApplyTo(null!);

        act.Should().Throw<ArgumentNullException>();
    }
}
