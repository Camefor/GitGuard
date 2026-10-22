using FluentAssertions;
using GitGuard.Detection.Adapters;

namespace GitGuard.Detection.Tests;

/// <summary>
/// Gitleaks 可执行文件定位测试（FR-090）：显式路径优先于 PATH，缺失时返回可诊断原因。
/// </summary>
public sealed class GitleaksExecutableLocatorTests
{
    [Fact]
    public void TryLocate_ExistingConfiguredFile_ReturnsAbsolutePath()
    {
        string executable = Path.GetTempFileName();
        try
        {
            bool located = GitleaksExecutableLocator.TryLocate(executable, out string? path, out string? reason);

            located.Should().BeTrue();
            path.Should().Be(Path.GetFullPath(executable));
            reason.Should().BeNull();
        }
        finally
        {
            File.Delete(executable);
        }
    }

    [Fact]
    public void TryLocate_MissingConfiguredPath_ReturnsFalseWithDiagnosticReason()
    {
        string missing = Path.Combine(Path.GetTempPath(), $"gitguard-missing-{Guid.NewGuid():N}.exe");

        bool located = GitleaksExecutableLocator.TryLocate(missing, out string? path, out string? reason);

        located.Should().BeFalse();
        path.Should().BeNull();
        reason.Should().Contain("gitleaks_path").And.Contain("不存在");
    }

    [Fact]
    public void TryLocate_UnresolvableBareName_ReturnsFalseWithDiagnosticReason()
    {
        string name = $"gitguard-missing-{Guid.NewGuid():N}";

        bool located = GitleaksExecutableLocator.TryLocate(name, out string? path, out string? reason);

        located.Should().BeFalse();
        path.Should().BeNull();
        reason.Should().Contain("无法解析");
    }
}
