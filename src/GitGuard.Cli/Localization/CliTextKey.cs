namespace GitGuard.Cli.Localization;

/// <summary>CLI 人类可读文案键（<c>模块设计/Cli.md</c> §7）：JSON 输出与日志字段名不参与本地化。</summary>
internal enum CliTextKey
{
    // 命令与选项描述
    RootDescription,
    ScanDescription,
    InstallDescription,
    UninstallDescription,
    StatusDescription,
    VersionDescription,
    HookDescription,
    HookPostCommitDescription,
    FormatOptionDescription,
    NoColorOptionDescription,
    VerboseOptionDescription,
    TraceOptionDescription,
    LogFileOptionDescription,
    LanguageOptionDescription,
    ScanStagedDescription,
    ScanPushDescription,
    ScanRecordPendingDescription,
    InstallForceDescription,
    InstallHookDescription,
    UninstallForceDescription,
    UninstallHookDescription,

    // 通用标签与分隔符
    LabelRepo,
    LabelType,
    LabelResult,
    LabelDuration,
    LabelFindings,
    LabelSkipped,
    LabelWarning,
    LabelHint,
    LabelAttention,
    LabelConfigFiles,
    LabelCommit,
    LabelReason,
    LabelRun,
    LabelErrorCode,
    LabelTraceId,
    LabelGitGuardVersion,
    LabelRulesVersion,
    LabelGitVersion,
    LabelRuntime,
    LabelHooksDirectory,
    LabelGit,
    LabelHooksPath,
    LabelGitGuard,
    ValueSeparator,

    // 用法错误
    UsageErrorTitle,
    UsageErrorHint,
    InvalidOptionValues,
    TraceRequiresVerbose,
    ScanScopeRequired,
    RecordPendingRequiresStaged,
    UnknownHook,
    ErrorJoinSeparator,

    // 扫描报告
    ScanFailedHeader,
    ScanBlockedHeader,
    ScanWarnedHeader,
    ScanCleanHeader,
    ScanSummary,
    UnknownDuration,
    SkipSummary,
    AuditWriteFailed,
    FindingAllowlisted,
    HintGitCommandFailed,
    HintDetectorFailed,
    HintScanTimeout,
    HintConfigInvalid,
    HintScanIncomplete,

    // 绕过检测
    BypassDetected,
    BypassReasonSkip,
    BypassReasonNoVerify,

    // Hook 安装 / 卸载
    InstallHeader,
    InstallBackupSuffix,
    UninstallHeader,
    UninstallNotInstalledHeader,
    UninstallRestoreSuffix,
    HookLine,
    DispositionInstalled,
    DispositionRefreshed,
    DispositionWrappedLegacy,
    DispositionRemoved,
    DispositionRestoredLegacy,
    DispositionNotInstalled,

    // 状态查询
    StatusHeader,
    HooksPathNotConfigured,
    ManagedVersionSuffix,
    ConfigFilesBuiltin,
    ConfigFilesSeparator,
    StatusAttention,
    HookStateMissing,
    HookStateManaged,
    HookStateLegacy,
    HookStateBinary,
    HookStateSymbolicLink,

    // 版本查询
    GitUnavailable,
    RuntimeDescription,

    // 错误报告
    ErrorHeader,

    // 日志消息
    LogCommandStarted,
    LogScanSkipped,
    LogScanCompleted,
    LogHookInstalled,
    LogHookUninstalled,
    LogHookNotInstalled,
    LogStatusChecked,
    LogVersionReported,
    LogBypassDetected,
    LogBypassChecked,
    LogCrashLogWritten,
    LogFileWriteFailed,

    // 提示行
    NoticeSkip,
    PendingClearFailed,
    PendingWriteFailed,
}
