namespace GitGuard.Cli.Logging;

/// <summary>
/// CLI 日志级别（<c>docs/09-日志设计.md</c> §2）：数值升序即严重度升序，可直接比较。
/// 默认 <see cref="Information"/>；<c>--verbose</c> 降到 <see cref="Debug"/>；<c>--verbose --trace</c> 降到 <see cref="Trace"/>。
/// </summary>
internal enum CliLogLevel
{
    /// <summary>规则级逐行匹配（仅 <c>--verbose --trace</c>）。</summary>
    Trace = 0,

    /// <summary>关键决策、Git 命令调用、配置路径。</summary>
    Debug = 1,

    /// <summary>扫描开始 / 结束、安装 / 卸载 Hook。</summary>
    Information = 2,

    /// <summary>可能的误报配置、文件被跳过、降级提示。</summary>
    Warning = 3,

    /// <summary>FailClosed 导致阻断。</summary>
    Error = 4,

    /// <summary>启动期致命错误 / 未分类异常。</summary>
    Critical = 5,
}
