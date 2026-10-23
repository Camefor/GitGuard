namespace GitGuard.Desktop.Services;

/// <summary>
/// 导出文件选择器（GGD-0046）：把 Avalonia 存储选择器从 ViewModel 中隔离，
/// 使日志页 ViewModel 单测无需启动 UI 运行时。返回 <see langword="null"/> 表示用户取消。
/// </summary>
public interface IExportPathPicker
{
    /// <summary>弹出保存文件对话框。</summary>
    /// <param name="title">对话框标题（已本地化）。</param>
    /// <param name="suggestedFileName">默认文件名（含扩展名）。</param>
    /// <param name="extension">文件扩展名（不含点，如 <c>json</c> / <c>csv</c>）。</param>
    /// <param name="cancellationToken">取消标记。</param>
    /// <returns>用户选择的本地路径；取消或平台不支持时返回 <see langword="null"/>。</returns>
    Task<string?> PickSaveFileAsync(
        string title,
        string suggestedFileName,
        string extension,
        CancellationToken cancellationToken = default);
}
