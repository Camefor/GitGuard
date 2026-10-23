using CommunityToolkit.Mvvm.ComponentModel;
using GitGuard.Core.Model;
using GitGuard.Desktop.Localization;

namespace GitGuard.Desktop.ViewModels;

/// <summary>
/// 仓库列表项（GGD-0042）：展示名称、绝对路径与目录缺失徽标；数据来自本地仓库注册表。
/// </summary>
public sealed class RepositoryItemViewModel : ObservableObject
{
    private string _missingText;

    /// <summary>初始化仓库列表项。</summary>
    /// <param name="repository">仓库登记记录。</param>
    /// <param name="language">初始界面语言。</param>
    /// <exception cref="ArgumentNullException"><paramref name="repository"/> 为 <see langword="null"/>。</exception>
    public RepositoryItemViewModel(RegisteredRepository repository, DesktopLanguage language)
    {
        ArgumentNullException.ThrowIfNull(repository);

        Repository = repository;
        IsMissing = !Directory.Exists(repository.RepoPath);
        _missingText = DesktopText.RepositoryMissingBadge(language);
    }

    /// <summary>底层登记记录。</summary>
    public RegisteredRepository Repository { get; }

    /// <summary>登记身份标识。</summary>
    public Guid Id => Repository.Id;

    /// <summary>展示名称。</summary>
    public string DisplayName => Repository.DisplayName;

    /// <summary>仓库工作区根目录绝对路径。</summary>
    public string RepoPath => Repository.RepoPath;

    /// <summary>登记时间（UTC）。</summary>
    public DateTimeOffset AddedAt => Repository.AddedAt;

    /// <summary>登记时的仓库目录当前是否已不存在（列表项创建时探测）。</summary>
    public bool IsMissing { get; }

    /// <summary>目录缺失徽标文案。</summary>
    public string MissingText
    {
        get => _missingText;
        private set => SetProperty(ref _missingText, value);
    }

    /// <summary>按新界面语言刷新徽标文案。</summary>
    /// <param name="language">目标界面语言。</param>
    public void Relocalize(DesktopLanguage language) => MissingText = DesktopText.RepositoryMissingBadge(language);
}
