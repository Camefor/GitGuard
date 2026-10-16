using System.Globalization;
using System.Runtime.InteropServices;

namespace GitGuard.Cli.Reporting;

/// <summary><c>gitguard version</c> 的输出数据（<c>docs/06-API接口设计.md</c> §5.8）：GitGuard 版本、规则库版本、Git 版本与运行时信息。</summary>
internal sealed class VersionInfo
{
    /// <summary>初始化版本信息。</summary>
    /// <param name="gitGuardVersion">GitGuard 版本（SemVer）。</param>
    /// <param name="rulesVersion">内置规则库版本。</param>
    /// <param name="gitVersionLine">本机 git 版本行；不可用时为 <see langword="null"/>。</param>
    /// <param name="gitFailureMessage">git 不可用原因；可用时为 <see langword="null"/>。</param>
    /// <exception cref="ArgumentException"><paramref name="gitGuardVersion"/> 为空或全空白。</exception>
    internal VersionInfo(string gitGuardVersion, int rulesVersion, string? gitVersionLine, string? gitFailureMessage)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gitGuardVersion);

        GitGuardVersion = gitGuardVersion;
        RulesVersion = rulesVersion;
        GitVersionLine = gitVersionLine;
        GitFailureMessage = gitFailureMessage;
        FrameworkDescription = RuntimeInformation.FrameworkDescription;
        RuntimeIdentifier = RuntimeInformation.RuntimeIdentifier;
        RuntimeDescription = string.Create(
            CultureInfo.InvariantCulture,
            $"{FrameworkDescription}（{RuntimeIdentifier}）");
    }

    /// <summary>GitGuard 版本（SemVer）。</summary>
    internal string GitGuardVersion { get; }

    /// <summary>内置规则库版本。</summary>
    internal int RulesVersion { get; }

    /// <summary>本机 git 版本行（如 <c>git version 2.43.0</c>）；不可用时为 <see langword="null"/>。</summary>
    internal string? GitVersionLine { get; }

    /// <summary>git 不可用原因；可用时为 <see langword="null"/>。</summary>
    internal string? GitFailureMessage { get; }

    /// <summary>.NET 运行时描述（如 <c>.NET 10.0.0</c>；构造时快照）。</summary>
    internal string FrameworkDescription { get; }

    /// <summary>本进程的运行时标识符（如 <c>win-x64</c>；构造时快照）。</summary>
    internal string RuntimeIdentifier { get; }

    /// <summary>终端展示用的一行运行时信息。</summary>
    internal string RuntimeDescription { get; }
}
