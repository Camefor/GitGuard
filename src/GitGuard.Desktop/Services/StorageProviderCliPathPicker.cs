using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace GitGuard.Desktop.Services;

/// <summary>
/// 基于 Avalonia <see cref="IStorageProvider"/> 的 GitGuard CLI 文件选择器（GGD-0055）：
/// 由组合根注入主窗口访问器，ViewModel 不直接依赖 UI 控件。
/// </summary>
public sealed class StorageProviderCliPathPicker : ICliPathPicker
{
    private static readonly string[] _windowsPatterns = new[] { "gitguard.exe", "*.exe" };
    private static readonly string[] _unixPatterns = new[] { "gitguard", "*" };

    private readonly Func<TopLevel?> _topLevelProvider;

    /// <summary>初始化文件选择器。</summary>
    /// <param name="topLevelProvider">返回当前主窗口（<see cref="TopLevel"/>）的访问器。</param>
    /// <exception cref="ArgumentNullException"><paramref name="topLevelProvider"/> 为 <see langword="null"/>。</exception>
    public StorageProviderCliPathPicker(Func<TopLevel?> topLevelProvider)
    {
        ArgumentNullException.ThrowIfNull(topLevelProvider);
        _topLevelProvider = topLevelProvider;
    }

    /// <inheritdoc />
    public async Task<string?> PickExecutableAsync(string title, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        cancellationToken.ThrowIfCancellationRequested();

        TopLevel? topLevel = _topLevelProvider();
        if (topLevel is null || !topLevel.StorageProvider.CanOpen)
        {
            return null;
        }

        IReadOnlyList<IStorageFile> files = await topLevel.StorageProvider.OpenFilePickerAsync(
            new FilePickerOpenOptions
            {
                Title = title,
                AllowMultiple = false,
                FileTypeFilter = new[]
                {
                    new FilePickerFileType(title)
                    {
                        Patterns = OperatingSystem.IsWindows() ? _windowsPatterns : _unixPatterns,
                    },
                },
            });

        return files.Count == 0 ? null : files[0].TryGetLocalPath();
    }
}
