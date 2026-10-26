namespace GitGuard.Desktop.Services;

/// <summary>
/// GitGuard CLI 可执行文件选择器（GGD-0055）：把 Avalonia 存储选择器从 ViewModel 中隔离，
/// 使 Hook 安装流程可在无 UI 运行时的单测中驱动。返回 <see langword="null"/> 表示用户取消。
/// </summary>
public interface ICliPathPicker
{
    /// <summary>弹出打开文件对话框，选择 GitGuard CLI 可执行文件。</summary>
    /// <param name="title">对话框标题（已本地化）。</param>
    /// <param name="cancellationToken">取消标记。</param>
    /// <returns>用户选择的本地路径；取消或平台不支持时返回 <see langword="null"/>。</returns>
    Task<string?> PickExecutableAsync(string title, CancellationToken cancellationToken = default);
}
