namespace GitGuard.Desktop.Services;

/// <summary>
/// 桌面设置数据访问契约（GGD-0046）：读取已持久化设置、读取配置文件中的有效 Gitleaks 设置，
/// 以及原子保存设置。ViewModel 单测以假对象驱动，不访问真实用户目录。
/// </summary>
public interface ISettingsOperations
{
    /// <summary>当前已加载的设置（保存成功后更新）。</summary>
    DesktopSettings Current { get; }

    /// <summary>读取配置文件合并后的有效 Gitleaks 设置（不受桌面覆盖影响）。</summary>
    /// <param name="cancellationToken">取消标记。</param>
    /// <returns>有效 Gitleaks 设置快照。</returns>
    Task<DesktopGitleaksSettings> LoadGitleaksAsync(CancellationToken cancellationToken = default);

    /// <summary>原子保存完整设置快照并更新 <see cref="Current"/>。</summary>
    /// <param name="settings">要保存的设置。</param>
    /// <exception cref="ArgumentNullException"><paramref name="settings"/> 为 <see langword="null"/>。</exception>
    /// <exception cref="IOException">临时文件写入或替换失败。</exception>
    /// <exception cref="UnauthorizedAccessException">没有写入权限。</exception>
    void Save(DesktopSettings settings);
}
