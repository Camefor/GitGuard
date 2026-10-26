namespace GitGuard.Desktop.Services;

/// <summary>
/// 桌面端页面服务集合（GGD-0046）：由组合根一次性装配，避免 <c>MainViewModel</c> 构造函数参数继续膨胀。
/// 任一服务为 <see langword="null"/> 时，对应页面保持骨架（设计预览与 ViewModel 单测）。
/// </summary>
public sealed class DesktopServices
{
    /// <summary>仓库页服务。</summary>
    public IRepositoryOperations? RepositoryOperations { get; init; }

    /// <summary>外部编辑器启动器。</summary>
    public IEditorLauncher? EditorLauncher { get; init; }

    /// <summary>规则页服务。</summary>
    public IRuleOperations? RuleOperations { get; init; }

    /// <summary>跨仓库审计页服务。</summary>
    public IAuditOperations? AuditOperations { get; init; }

    /// <summary>桌面设置服务。</summary>
    public ISettingsOperations? SettingsOperations { get; init; }

    /// <summary>审计导出文件选择器。</summary>
    public IExportPathPicker? ExportPathPicker { get; init; }

    /// <summary>GitGuard CLI 可执行文件选择器（GGD-0055 安装 Hook）。</summary>
    public ICliPathPicker? CliPathPicker { get; init; }
}
