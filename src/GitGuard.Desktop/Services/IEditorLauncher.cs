namespace GitGuard.Desktop.Services;

/// <summary>编辑器启动结果分类（GGD-0043）。UI 只展示本地化提示，不拼接进程错误原文。</summary>
public enum EditorLaunchStatus
{
    /// <summary>已向操作系统提交启动请求。</summary>
    Opened,

    /// <summary>仓库目录已不存在。</summary>
    RepositoryMissing,

    /// <summary>目标文件已不存在（常见于查看历史扫描后文件被删除）。</summary>
    FileMissing,

    /// <summary>Finding 的相对路径越出仓库根目录。</summary>
    PathOutsideRepository,

    /// <summary>操作系统无法启动编辑器。</summary>
    LaunchFailed,
}

/// <summary>外部编辑器启动结果。</summary>
public sealed record EditorLaunchResult
{
    /// <summary>初始化启动结果。</summary>
    /// <param name="status">结果分类。</param>
    /// <param name="detail">用于诊断的原始错误（可为空；UI 不直接展示）。</param>
    public EditorLaunchResult(EditorLaunchStatus status, string? detail = null)
    {
        Status = status;
        Detail = detail;
    }

    /// <summary>结果分类。</summary>
    public EditorLaunchStatus Status { get; }

    /// <summary>用于诊断的原始错误（可为空；UI 不直接展示）。</summary>
    public string? Detail { get; }

    /// <summary>启动请求是否已成功提交。</summary>
    public bool Succeeded => Status == EditorLaunchStatus.Opened;
}

/// <summary>
/// 外部编辑器启动契约（GGD-0043）：把仓库相对路径与行号交给用户偏好的编辑器。
/// 支持 <c>GITGUARD_EDITOR</c> / <c>VISUAL</c> / <c>EDITOR</c> 环境变量；未配置时使用操作系统文件关联。
/// </summary>
public interface IEditorLauncher
{
    /// <summary>打开指定文件的指定行。</summary>
    /// <param name="repositoryPath">仓库工作区根目录绝对路径。</param>
    /// <param name="relativeFilePath">仓库相对文件路径（正斜杠或平台分隔符均可）。</param>
    /// <param name="lineNumber">1-based 行号。</param>
    /// <param name="cancellationToken">取消标记。</param>
    /// <returns>启动结果；不等待编辑器进程退出。</returns>
    Task<EditorLaunchResult> OpenAsync(
        string repositoryPath,
        string relativeFilePath,
        int lineNumber,
        CancellationToken cancellationToken = default);
}
