using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace GitGuard.Desktop.Services;

/// <summary>
/// 基于 Avalonia <see cref="IStorageProvider"/> 的导出文件选择器（GGD-0046）：
/// 由组合根注入主窗口访问器，ViewModel 不直接依赖 UI 控件。
/// </summary>
public sealed class StorageProviderExportPathPicker : IExportPathPicker
{
    private readonly Func<TopLevel?> _topLevelProvider;

    /// <summary>初始化文件选择器。</summary>
    /// <param name="topLevelProvider">返回当前主窗口（<see cref="TopLevel"/>）的访问器。</param>
    /// <exception cref="ArgumentNullException"><paramref name="topLevelProvider"/> 为 <see langword="null"/>。</exception>
    public StorageProviderExportPathPicker(Func<TopLevel?> topLevelProvider)
    {
        ArgumentNullException.ThrowIfNull(topLevelProvider);
        _topLevelProvider = topLevelProvider;
    }

    /// <inheritdoc />
    public async Task<string?> PickSaveFileAsync(
        string title,
        string suggestedFileName,
        string extension,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentException.ThrowIfNullOrWhiteSpace(suggestedFileName);
        ArgumentException.ThrowIfNullOrWhiteSpace(extension);
        cancellationToken.ThrowIfCancellationRequested();

        TopLevel? topLevel = _topLevelProvider();
        if (topLevel is null || !topLevel.StorageProvider.CanSave)
        {
            return null;
        }

        var file = await topLevel.StorageProvider.SaveFilePickerAsync(
            new FilePickerSaveOptions
            {
                Title = title,
                SuggestedFileName = suggestedFileName,
                DefaultExtension = extension,
                FileTypeChoices = new[]
                {
                    new FilePickerFileType(title)
                    {
                        Patterns = new[] { "*." + extension },
                    },
                },
                ShowOverwritePrompt = true,
            });

        return file?.TryGetLocalPath();
    }
}
