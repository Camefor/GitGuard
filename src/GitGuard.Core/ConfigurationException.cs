namespace GitGuard.Core;

/// <summary>
/// 配置加载失败的统一异常类型（<c>10-异常处理设计.md</c> §2、FR-070）：<c>.gitguard.yml</c> / 用户级 config.yml
/// 的 YAML 语法错误、<c>version</c> 缺失或未知、违反硬下限（Critical 降级为 allow）等场景必须以本类型向上传播，
/// CLI 映射退出码 13（FailClosed）。置于 Core 根命名空间，与 <see cref="GitException"/> / <see cref="SqliteException"/> /
/// <see cref="DetectionException"/> / <see cref="HookException"/> 同置位理由：Cli 层需要 catch 本类型映射退出码，
/// 而 Cli 不得引用 Application / Infrastructure。
/// </summary>
public sealed class ConfigurationException : GitGuardException
{
    /// <summary>错误码：配置文件解析失败或违反硬下限（<c>10-异常处理设计.md</c> §4 登记值，CLI 映射退出码 13）。</summary>
    public const string CONFIG_INVALID = "CONFIG_INVALID";

    /// <summary>初始化 <see cref="ConfigurationException"/> 的实例。</summary>
    /// <param name="message">异常消息（面向用户的简述，指出违规字段与原因）。</param>
    /// <param name="sourcePath">出错的配置文件路径；非文件级失败（如合并校验）为 <see langword="null"/>。</param>
    /// <param name="line">出错行号（1 起）；未知为 <see langword="null"/>。</param>
    public ConfigurationException(string message, string? sourcePath = null, int? line = null)
        : base(message)
    {
        ErrorCode = CONFIG_INVALID;
        SourcePath = sourcePath;
        Line = line;
    }

    /// <summary>使用内部异常初始化实例（如 <c>YamlDotNet.Core.YamlException</c>）。</summary>
    /// <param name="message">异常消息。</param>
    /// <param name="innerException">内部异常。</param>
    /// <param name="sourcePath">出错的配置文件路径；未知为 <see langword="null"/>。</param>
    /// <param name="line">出错行号（1 起）；未知为 <see langword="null"/>。</param>
    public ConfigurationException(string message, Exception innerException, string? sourcePath, int? line)
        : base(message, innerException)
    {
        ErrorCode = CONFIG_INVALID;
        SourcePath = sourcePath;
        Line = line;
    }

    /// <summary>错误码（<c>10-异常处理设计.md</c> §4 登记值）。</summary>
    public string ErrorCode { get; }

    /// <summary>出错的配置文件路径；非文件级失败为 <see langword="null"/>。</summary>
    public string? SourcePath { get; }

    /// <summary>出错行号（1 起）；未知为 <see langword="null"/>。</summary>
    public int? Line { get; }
}
