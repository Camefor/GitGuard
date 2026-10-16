using FluentAssertions;
using GitGuard.Core.Services;

namespace GitGuard.Core.Tests;

public sealed class SanitizingLoggerTests
{
    /// <summary>ANSI 转义引导字符（ESC，0x1B）；以常量构造避免测试源码内嵌控制字符。</summary>
    private const char Esc = (char)0x1B;

    [Fact]
    public void SanitizeMessage_AnsiEscapeSequences_Removed()
    {
        string message = $"{Esc}[31mblocked{Esc}[0m";

        SanitizingLogger.SanitizeMessage(message).Should().Be("blocked");
    }

    [Fact]
    public void SanitizeMessage_CleanText_Unchanged()
    {
        SanitizingLogger.SanitizeMessage("scan finished").Should().Be("scan finished");
    }

    [Fact]
    public void MaskField_CanaryToken_ReturnsMaskedText()
    {
        string masked = SanitizingLogger.MaskField(TestData.CanaryToken);

        masked.Should().NotContain(TestData.CanaryToken);
        masked.Should().StartWith("AK").And.EndWith("ab");
        masked.Should().Contain("*");
    }

    [Fact]
    public void RepoPathForLog_NonDebug_Returns16CharHash()
    {
        string actual = SanitizingLogger.RepoPathForLog(TestData.SampleRepoPath, isDebugLevel: false);

        actual.Should().Be(PathAnonymizer.Anonymize(TestData.SampleRepoPath));
        actual.Should().MatchRegex("^[0-9a-f]{16}$");
    }

    [Fact]
    public void RepoPathForLog_Debug_ReturnsSanitizedRawPath()
    {
        string path = TestData.SampleRepoPath + $"{Esc}[31m";

        SanitizingLogger.RepoPathForLog(path, isDebugLevel: true).Should().Be(TestData.SampleRepoPath);
    }

    [Fact]
    public void CanaryToken_ThroughFullPipeline_NoPlaintextInLogOutput()
    {
        // TC-DR-02-01（单测层）：金丝雀 Token 经脱敏流水线后，日志行中不得出现其全文
        string repoPath = Path.Combine(TestData.SampleRepoPath, TestData.CanaryToken);

        string line = SanitizingLogger.SanitizeMessage(
            $"event=Scan.Staged repo={SanitizingLogger.RepoPathForLog(repoPath, isDebugLevel: false)} snippet={SanitizingLogger.MaskField(TestData.CanaryToken)}");

        line.Should().NotContain(TestData.CanaryToken);
        line.Should().Contain("AK");
        line.Should().Contain("***");
    }
}
