using CommunityToolkit.Mvvm.ComponentModel;
using GitGuard.Application.Hooks;
using GitGuard.Core.Model;
using GitGuard.Desktop.Localization;

namespace GitGuard.Desktop.ViewModels;

/// <summary>
/// Hook 状态行（GGD-0042 / <c>11-前端UI设计</c> §3.2）：展示 pre-commit / pre-push / post-commit 的
/// 探测分类、托管版本与需要接管的警告。
/// </summary>
public sealed class HookStatusRowViewModel : ObservableObject
{
    private string _statusText;
    private string _versionText;

    /// <summary>初始化 Hook 状态行。</summary>
    /// <param name="status">Hook 探测结果。</param>
    /// <param name="language">初始界面语言。</param>
    /// <exception cref="ArgumentNullException"><paramref name="status"/> 为 <see langword="null"/>。</exception>
    public HookStatusRowViewModel(HookStatus status, DesktopLanguage language)
    {
        ArgumentNullException.ThrowIfNull(status);

        Status = status;
        _statusText = DesktopText.HookKindLabel(status.Kind, language);
        _versionText = status.ManagedVersion is { } version ? $"v{version}" : string.Empty;
    }

    /// <summary>底层 Hook 状态。</summary>
    public HookStatus Status { get; }

    /// <summary>Hook 名称（<c>pre-commit</c> / <c>pre-push</c> / <c>post-commit</c>）。</summary>
    public string HookName => Status.HookType.ToHookName();

    /// <summary>Hook 文件绝对路径。</summary>
    public string HookPath => Status.HookPath;

    /// <summary>是否需要接管（未托管脚本 / 二进制 / 符号链接）。</summary>
    public bool NeedsAttention => Status.NeedsAttention;

    /// <summary>探测分类标签。</summary>
    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    /// <summary>托管版本标签（如 <c>v0.1.0</c>）；非托管为空。</summary>
    public string VersionText
    {
        get => _versionText;
        private set => SetProperty(ref _versionText, value);
    }

    /// <summary>按新界面语言刷新文案。</summary>
    /// <param name="language">目标界面语言。</param>
    public void Relocalize(DesktopLanguage language) =>
        StatusText = DesktopText.HookKindLabel(Status.Kind, language);
}
