using GitGuard.Core.Model;

namespace GitGuard.Application.Hooks;

/// <summary>
/// 单个 Hook 的安装预检结果：先全量预检、再统一写入，
/// 避免「前一个 Hook 已写入、后一个才发现需要 --force」的半安装状态。
/// </summary>
internal sealed record HookInstallPlan
{
    internal HookInstallPlan(HookType hookType, HookInstallDisposition disposition, string hookPath, string? backupPath)
    {
        HookType = hookType;
        Disposition = disposition;
        HookPath = hookPath;
        BackupPath = backupPath;
    }

    internal HookType HookType { get; }

    internal HookInstallDisposition Disposition { get; }

    internal string HookPath { get; }

    internal string? BackupPath { get; }
}
