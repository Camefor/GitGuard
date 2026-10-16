using System.Text;
using GitGuard.Core.Model;

namespace GitGuard.Application.Hooks;

/// <summary>
/// Hook 分发脚本构建器（ADR-0004 / 模块设计/Hook.md §7）：把内嵌模板渲染为 POSIX sh 分发脚本。
/// 模板以 <c>EmbeddedResource</c> 随程序集分发；仓库内按 13-开发规范 存储（UTF-8 with BOM + CRLF），
/// 渲染输出统一归一化为 UTF-8 无 BOM + LF —— BOM 会使 shebang 失效，CR 会让 MSYS sh 解析报错。
/// </summary>
public static class HookScriptBuilder
{
    private const string VERSION_PLACEHOLDER = "{{VERSION}}";
    private const string EXECUTABLE_PLACEHOLDER = "{{GITGUARD_BIN}}";
    private const string HOOK_NAME_PLACEHOLDER = "{{HOOK_NAME}}";

    /// <summary>渲染指定 Hook 类型的分发脚本。</summary>
    /// <param name="hookType">Hook 类型；<see cref="HookType.PostCommit"/> 无模板（顺延至 GGD-0013）。</param>
    /// <param name="executablePath">GitGuard 可执行文件路径（安装时固化到脚本内，08-权限设计 §6）。</param>
    /// <param name="version">写入托管标记行的 GitGuard 版本（安装识别依赖首两行的 <c># gitguard-managed v</c> 标记）。</param>
    /// <returns>LF 行尾、无 BOM 的 POSIX sh 脚本全文（含末尾换行）。</returns>
    /// <exception cref="ArgumentException"><paramref name="executablePath"/> 或 <paramref name="version"/> 为空或全空白。</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="hookType"/> 无对应模板。</exception>
    /// <exception cref="InvalidOperationException">内嵌模板资源缺失（程序集构建配置错误）。</exception>
    public static string Render(HookType hookType, string executablePath, string version)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(version);

        string template = LoadTemplate(hookType);
        string script = template
            .Replace(VERSION_PLACEHOLDER, version, StringComparison.Ordinal)
            .Replace(EXECUTABLE_PLACEHOLDER, NormalizeExecutablePath(executablePath), StringComparison.Ordinal)
            .Replace(HOOK_NAME_PLACEHOLDER, hookType.ToHookName(), StringComparison.Ordinal);

        return NormalizeNewLines(script);
    }

    // 解析 Hook 类型对应的内嵌资源名（与 GitGuard.Application.csproj 的 LogicalName 一一对应）
    private static string ResolveResourceName(HookType hookType) => hookType switch
    {
        HookType.PreCommit => "GitGuard.Application.Hooks.Templates.pre-commit.sh.template",
        HookType.PrePush => "GitGuard.Application.Hooks.Templates.pre-push.sh.template",
        _ => throw new ArgumentOutOfRangeException(
            nameof(hookType),
            hookType,
            "该 Hook 类型没有分发脚本模板；post-commit（FR-050 绕过检测）顺延至 GGD-0013"),
    };

    private static string LoadTemplate(HookType hookType)
    {
        string resourceName = ResolveResourceName(hookType);
        using Stream stream = typeof(HookScriptBuilder).Assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException(
                $"内嵌资源缺失：{resourceName}；请检查 GitGuard.Application.csproj 的 EmbeddedResource 配置。");

        // detectEncodingFromByteOrderMarks：按模板文件 BOM 解码并剥离 BOM（仓库按 13-开发规范存为 UTF-8 with BOM）
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }

    // Windows 上 Hook 由 Git for Windows 的 MSYS sh 执行，反斜杠会被当作转义字符（C:\x → C:x），故转为正斜杠
    private static string NormalizeExecutablePath(string executablePath) =>
        OperatingSystem.IsWindows() ? executablePath.Replace('\\', '/') : executablePath;

    private static string NormalizeNewLines(string text) =>
        text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
}
