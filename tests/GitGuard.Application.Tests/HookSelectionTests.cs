using FluentAssertions;
using GitGuard.Application.Configuration;
using GitGuard.Application.Hooks;
using GitGuard.Core;
using GitGuard.Core.Model;

namespace GitGuard.Application.Tests;

/// <summary>
/// Hook 安装集合选择测试（GGD-0054）：配置子集与显式列表优先语义必须与既有 CLI 契约一致。
/// </summary>
public sealed class HookSelectionTests
{
    [Fact]
    public void FromSettings_Default_ReturnsAllHooksInFixedOrder()
    {
        var hooks = HookSelection.FromSettings(HooksSettings.Default);

        hooks.Should().Equal(HookType.PreCommit, HookType.PrePush, HookType.PostCommit);
    }

    [Fact]
    public void FromSettings_DisabledSwitches_ReturnsOnlyEnabledHooks()
    {
        var settings = new HooksSettings
        {
            PreCommit = false,
            PostCommitBypassDetection = false,
        };

        var hooks = HookSelection.FromSettings(settings);

        hooks.Should().Equal(HookType.PrePush);
    }

    [Fact]
    public void Resolve_ExplicitHooks_OverridesAllDisabledConfiguration()
    {
        var settings = new HooksSettings
        {
            PreCommit = false,
            PrePush = false,
            PostCommitBypassDetection = false,
        };

        IReadOnlyList<HookType> hooks = HookSelection.Resolve(settings, new[] { HookType.PreCommit });

        hooks.Should().Equal(HookType.PreCommit);
    }

    [Fact]
    public void Resolve_AllHooksDisabled_ThrowsConfigurationExceptionWithStableMessage()
    {
        var settings = new HooksSettings
        {
            PreCommit = false,
            PrePush = false,
            PostCommitBypassDetection = false,
        };

        Action act = () => _ = HookSelection.Resolve(settings, explicitHooks: null);

        act.Should().Throw<ConfigurationException>()
            .Which.Message.Should().Be(HookSelection.NO_HOOKS_ENABLED_MESSAGE)
            .And.Contain("无可安装项");
    }

    [Fact]
    public void FromSettings_NullSettings_Throws()
    {
        Action act = () => _ = HookSelection.FromSettings(null!);

        act.Should().Throw<ArgumentNullException>();
    }
}
