namespace GitGuard.Cli;

/// <summary>
/// CLI 退出码稳定契约（<c>docs/06-API接口设计.md</c> §6）：同一 MAJOR 版本内禁止变更或临时自造；
/// 本类是退出码的唯一权威来源，命令实现与异常映射必须引用本类常量。
/// </summary>
public static class ExitCodes
{
    /// <summary>成功：扫描通过（Clean / Warned）或命令成功。</summary>
    public const int SUCCESS = 0;

    /// <summary>业务阻断：命中阻断级规则。</summary>
    public const int BLOCKED = 1;

    /// <summary>环境错误：当前目录不是 Git 仓库。</summary>
    public const int NOT_A_GIT_REPOSITORY = 2;

    /// <summary>环境错误：写入权限不足。</summary>
    public const int WRITE_PERMISSION_DENIED = 3;

    /// <summary>环境错误：目标 Hook 为二进制 / 符号链接（需 <c>--force</c>）或遗留备份冲突（不可接管）。</summary>
    public const int UNKNOWN_HOOK_PRESENT = 4;

    /// <summary>未发现 GitGuard 安装痕迹（仅 <c>uninstall</c>；非故障提示）。</summary>
    public const int HOOK_NOT_INSTALLED = 5;

    /// <summary>Hook 存在但未被 GitGuard 托管（仅 <c>status</c>）。</summary>
    public const int HOOK_NOT_MANAGED = 6;

    /// <summary>参数 / CLI 用法错误（未知命令、选项冲突、取值非法；GGD-0009 新增）。</summary>
    public const int USAGE_ERROR = 7;

    /// <summary>FailClosed：Git 命令失败。</summary>
    public const int GIT_COMMAND_FAILED = 10;

    /// <summary>FailClosed：检测器异常。</summary>
    public const int DETECTOR_FAILED = 11;

    /// <summary>FailClosed：扫描超时（或外部取消）。</summary>
    public const int SCAN_TIMEOUT = 12;

    /// <summary>FailClosed：配置文件解析失败或违反硬下限。</summary>
    public const int CONFIG_INVALID = 13;

    /// <summary>FailClosed：审计存储失败（打开 / 迁移 / 读写失败，GGD-0009 新增）。</summary>
    public const int AUDIT_STORE_FAILED = 14;

    /// <summary>参数错误：白名单参数校验失败（<c>gitguard allowlist add</c>）。</summary>
    public const int ALLOWLIST_VALIDATION_FAILED = 20;

    /// <summary>参数错误：白名单条目不存在（<c>gitguard allowlist remove</c>）。</summary>
    public const int ALLOWLIST_NOT_FOUND = 21;

    /// <summary>查询错误：run_id 不存在（<c>gitguard audit view</c>）。</summary>
    public const int AUDIT_RUN_NOT_FOUND = 30;

    /// <summary>查询错误：审计导出失败（<c>gitguard audit export</c>）。</summary>
    public const int AUDIT_EXPORT_FAILED = 31;

    /// <summary>查询错误：规则不存在（<c>gitguard rules view</c>）。</summary>
    public const int RULE_NOT_FOUND = 40;

    /// <summary>已有并发的 install / uninstall 持锁。</summary>
    public const int INSTALL_IN_PROGRESS = 50;

    /// <summary>PATH 条目冲突：目标路径已有非 GitGuard 托管的同名文件 / 符号链接（需 <c>--force</c>）。</summary>
    public const int PATH_ENTRY_CONFLICT = 60;

    /// <summary>PATH 管理不受支持：当前平台或 Shell 无法自动写入用户 PATH。</summary>
    public const int PATH_UNSUPPORTED = 61;

    /// <summary>PATH 超长：写入后的用户 PATH 超过平台长度上限。</summary>
    public const int PATH_TOO_LONG = 62;

    /// <summary>未知错误：未分类异常（代码缺陷）；stderr 输出并写入脱敏崩溃日志。</summary>
    public const int UNKNOWN_ERROR = 99;
}
