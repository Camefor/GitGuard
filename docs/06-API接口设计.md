# CLI 命令契约（原 API 接口设计）

> **说明**：GitGuard 是本地 CLI + 可选桌面端的本地工具，**不对外提供 HTTP / gRPC / 消息队列接口**。本文件把「接口契约」聚焦在 CLI 命令、退出码、输入输出格式这些对外稳定契约上。配置文件 Schema 另见 `模块设计/Policy.md` §7。

## 1. 接口风格

- **命令行子命令风格**：`gitguard <command> [subcommand] [options]`，参照 `git` / `dotnet` 的命令布局。
- **退出码表达结果**：零=通过；1=命中阻断；≥10=FailClosed 异常。详见 §6。
- **输出目标**：报告与错误走 stderr，机器可读 JSON 走 stdout（通过 `--format json` 启用）。
- **幂等性**：所有 `scan` 命令都是幂等的（只读 Git + 写入审计表）；`install` 命令允许重复执行，等价于一次成功安装。

## 2. 版本管理

- CLI 兼容性遵循 SemVer：
  - **MAJOR**：破坏性变更（删除命令、改退出码语义、调整 JSON Schema 字段）。
  - **MINOR**：新增命令、新增选项、新增 JSON 字段（必须向后兼容）。
  - **PATCH**：Bug fix、规则库更新。
- 退出码表与 JSON Schema 版本在同一次 MAJOR 中保持稳定。
- 废弃命令至少保留一个 MAJOR 周期 + `--help` 中标注 `[Deprecated]`。

## 3. 认证与授权

CLI 为本地工具，使用操作系统用户身份即可，无需额外认证。

- SQLite 文件权限：Unix `0600`，Windows ACL CurrentUser。
- 不设置「超级管理员命令」；全部命令均在当前 OS 用户上下文运行。
- 详细策略见 `08-权限设计.md`。

## 4. 通用请求/响应格式

### 4.1 成功输出（人类可读）

默认 stderr 输出；stdout 保持空（便于被脚本捕获）：

```text
✔ GitGuard scan completed
  仓库：C:/Users/rushe/myproject
  类型：pre-commit
  结果：Clean（扫描 42 文件，发现 0 项）
  耗时：183 ms
```

### 4.2 成功输出（JSON，`--format json`）

JSON 走 stdout，为**单行紧凑格式**（无缩进；下方示例为便于阅读做了美化）。`--format json` 时文本报告不再输出到 stderr：

```json
{
  "schema": "gitguard.scan.v1",
  "run_id": "0192f4a4-aec8-7e7b-8f0a-6b0e8b1d2c33",
  "repo_path": "C:/Users/rushe/myproject",
  "scan_type": "PreCommit",
  "result": "Clean",
  "exit_code": 0,
  "duration_ms": 183,
  "findings": [],
  "summary": {
    "files_scanned": 42,
    "rules_evaluated": 17,
    "blocked": 0,
    "warned": 0,
    "ignored_by_allowlist": 0,
    "ignored_by_baseline": 0
  },
  "skipped_files": [],
  "audit_write_failed": false,
  "warnings": []
}
```

- `scan_type`：`PreCommit`（`--staged`）/ `PrePush`（`--push`）/ `Manual`（`--all`）。
- `duration_ms`：无法计时时为 `null`。
- `full_scan`：仅 `--all` 输出的可选对象 `{ include_history, commits_scanned, remaining_commits, truncated, next_since_sha }`。`commits_scanned` 不含 HEAD（HEAD 由全量树扫描覆盖）；`remaining_commits` 为因 `--max-files` 截断而剩余的提交数；`next_since_sha` 仅在 `truncated = true` 时非空，可直接传给下一次 `--all --since` 续扫。暂存区 / 推送扫描不输出该字段。
- `baseline`：仅 `--all` 且涉及基线时输出的可选对象 `{ path, generated, entries, suppressed_findings }`（`GGD-0032`）。`generated = true` 表示本次通过 `--baseline` 生成文件（当次命中不抑制，`suppressed_findings = 0`）；`generated = false` 表示消费默认 `.gitguard-baseline.json`，此时 `suppressed_findings` 为被基线放行的命中数。`entries` 为基线条目数（按指纹去重）。
- `skipped_files[]`：`{ path, reason: Binary|TooLarge, commit_sha }`（`commit_sha` 可为 `null`）。
- `audit_write_failed`：审计落库降级标记（扫描结果仍有效，退出码不变）。
- `warnings[]`：配置加载链的宽松解析警告（含未知键、顺延开关）。

### 4.3 命中阻断（JSON）

```json
{
  "schema": "gitguard.scan.v1",
  "run_id": "0192f4a4-c1a3-7a7f-aae1-71fb4d55b8c7",
  "result": "Blocked",
  "exit_code": 1,
  "findings": [
    {
      "rule_id": "GGD/SECRET/AWS-AKID",
      "severity": "Critical",
      "file_path": "appsettings.Production.json",
      "line_number": 18,
      "snippet_masked": "AK****************LE",
      "fingerprint": "e1e9fbb8ea52005691a6a295ba25f4a89070542dcb1a8bcaad6662425611ce9d",
      "action": "Block",
      "commit_sha": null
    }
  ]
}
```

- `fingerprint`：64 位小写十六进制（HMAC-SHA256，无 `sha256:` 前缀）。
- `action`：`Block` / `Warn` / `Allow` / `AllowedByAllowlist`（白名单命中）/ `AllowedByBaseline`（基线命中，`GGD-0032`）。白名单优先于基线：同时命中时输出 `AllowedByAllowlist`。
- `commit_sha`：`--push` 与 `--all --include-history` / `--since` 扫描时为命中提交 SHA；HEAD 全量树与暂存区扫描为 `null`。

### 4.4 错误输出（JSON）

```json
{
  "schema": "gitguard.error.v1",
  "error_code": "GIT_COMMAND_FAILED",
  "exit_code": 10,
  "message": "执行 git diff --cached 失败：fatal: not a git repository",
  "trace_id": "8407-8d99"
}
```

- `trace_id`：短追踪 ID（形如 `xxxx-xxxx` 十六进制），排查时随日志一并提供。
- `message`：已脱敏（不含密钥明文与 ANSI 控制字符）。
- 用法错误（退出码 7）同样使用本 Schema，`error_code` 为 `CLI_USAGE_ERROR`，`message` 为折行后的解析错误描述。

### 4.5 Hook 生命周期输出（JSON）

`install` / `uninstall` 共用同一结构（`schema` 分别为 `gitguard.install.v1` / `gitguard.uninstall.v1`）：

```json
{
  "schema": "gitguard.install.v1",
  "repo_path": "C:/Users/rushe/myproject",
  "version": "1.1.0",
  "hooks": [
    {
      "hook": "pre-commit",
      "disposition": "Installed",
      "hook_path": "C:/Users/rushe/myproject/.git/hooks/pre-commit",
      "backup_path": null
    }
  ]
}
```

- `version`：仅 install 输出（GitGuard 版本）；`uninstall` 无该字段。
- `not_installed`：仅 uninstall 输出（`true` 时退出码 5）；`install` 无该字段。
- `disposition`：install 取值 `Installed` / `Refreshed` / `WrappedLegacy`；uninstall 取值 `Removed` / `RestoredLegacy` / `NotInstalled`。
- `backup_path`：无备份时为 `null`。

### 4.6 状态与版本输出（JSON）

`gitguard.status.v1`：

```json
{
  "schema": "gitguard.status.v1",
  "repo_path": "C:/Users/rushe/myproject",
  "gitguard_version": "1.1.0",
  "git_version": "git version 2.53.0.windows.2",
  "configured_hooks_path": null,
  "hooks_directory": "C:/Users/rushe/myproject/.git/hooks",
  "hooks": [
    {
      "hook": "pre-commit",
      "state": "Missing",
      "hook_path": "C:/Users/rushe/myproject/.git/hooks/pre-commit",
      "managed_version": null
    }
  ],
  "config_files": [],
  "warnings": [],
  "needs_attention": false
}
```

- `state`：`Missing` / `Managed` / `Legacy` / `Binary` / `SymbolicLink`。
- `managed_version`：仅 `Managed` 时非空。
- `needs_attention`：为 `true` 时退出码 6。

`gitguard.version.v1`：

```json
{
  "schema": "gitguard.version.v1",
  "gitguard_version": "1.1.0",
  "rules_version": 1,
  "git_version": "git version 2.53.0.windows.2",
  "git_failure": null,
  "runtime": {
    "framework": ".NET 10.0.11",
    "runtime_identifier": "win-x64"
  }
}
```

- `git_version` / `git_failure`：探测成功时 `git_failure` 为 `null`，失败时 `git_version` 为 `null` 且 `git_failure` 携带原因（命令退出码仍为 0）。

### 4.7 绕过检测输出（JSON）

隐藏命令 `gitguard hook post-commit` 的 `--format json` 输出（`gitguard.bypass.v1`）：

```json
{
  "schema": "gitguard.bypass.v1",
  "detected": true,
  "reason": "NoVerify",
  "commit_sha": "4f3c2a1b9d8e7f60514233241506f7e8d9c0b1a2",
  "run_id": "0192f4a4-aec8-7e7b-8f0a-6b0e8b1d2c33"
}
```

- `reason`：`NoVerify`（无待提交标记 / 标记不匹配，含 `--no-verify`）或 `GitGuardSkip`（`GITGUARD_SKIP=1` 且标记匹配）；未检测到绕过时为 `null`。
- `run_id`：绕过事件对应的 `scan_runs.id`；未检测到绕过时为 `null`。
- `detected = false` 时文本模式保持静默（不刷屏）；`detected = true` 时向 stderr 输出中文警告与原因。
- 命令**始终以 0 返回**（Git 忽略 `post-commit` 退出码）；审计写入失败按 FailClosed 映射退出码 14。

### 4.8 分页

`gitguard audit list` 支持 `--limit N --offset M`：`N` 取值 1~1000（默认 20），`M` ≥ 0。结果排序固定为 `started_at DESC, rowid DESC`（同一毫秒内后写入者在前）；`--since` / `--result` 筛选与分页均在 SQLite 查询层完成，`total` 为筛选后的总数。

`gitguard audit view <run-id>` 按运行身份标识读取单次运行摘要与 Finding 明细：`run-id` 必须为合法的非空 GUID（非法 → 退出码 7）；运行不存在 → 退出码 30（`AUDIT_RUN_NOT_FOUND`，输出 `gitguard.error.v1`）。查询范围同样为整个本地审计库。

### 4.9 审计查询与导出输出（JSON / CSV）

`gitguard audit list --format json` 输出 `gitguard.audit.list.v1`（stdout 单行紧凑）：

```json
{
  "schema": "gitguard.audit.list.v1",
  "total": 42,
  "limit": 20,
  "offset": 0,
  "count": 20,
  "runs": [
    {
      "run_id": "0192f4a4-aec8-7e7b-8f0a-6b0e8b1d2c33",
      "repo_path": "D:/work/my-project",
      "scan_type": "PreCommit",
      "started_at": "2026-10-21T08:00:00.000Z",
      "duration_ms": 91,
      "result": "Blocked",
      "exit_code": 1,
      "findings_count": 1
    }
  ]
}
```

- `total`：筛选后的记录总数；`count`：当前页条数（可为 0）。
- `scan_type` / `result`：`ScanType` / `ScanResult` 枚举名；`started_at` 为 UTC ISO 8601（毫秒 + `Z`）。
- `duration_ms`：Bypass 等未记录耗时的运行为 `null`。
- 查询范围为整个本地审计库（含所有仓库）；命令不要求当前目录是 Git 仓库。

`gitguard audit view <run-id> --format json` 输出 `gitguard.audit.view.v1`（stdout 单行紧凑）：

```json
{
  "schema": "gitguard.audit.view.v1",
  "run": {
    "run_id": "0192f4a4-aec8-7e7b-8f0a-6b0e8b1d2c33",
    "repo_path": "D:/work/my-project",
    "scan_type": "PreCommit",
    "started_at": "2026-10-21T08:00:00.000Z",
    "duration_ms": 91,
    "result": "Blocked",
    "exit_code": 1,
    "findings_count": 1
  },
  "findings": [
    {
      "finding_id": "0192f4a4-aec8-7e7b-8f0a-6b0e8b1d2c34",
      "rule_id": "GGD/SECRET/AWS-AKID",
      "file_path": "config/app.env",
      "line_number": 1,
      "severity": "Critical",
      "fingerprint": "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef",
      "snippet_masked": "AK****************42",
      "commit_sha": null
    }
  ]
}
```

- `run` 字段与 `gitguard.audit.list.v1` 的单行对象一致；`findings` 字段与导出文件中的 Finding 对象一致（只含脱敏片段与 HMAC 指纹）。
- 无 Finding 的运行输出空 `findings` 数组；`commit_sha` 在 PreCommit / Manual 运行中为 `null`。
- 文本模式下摘要与 Finding 明细写 stderr，不输出原文。

`gitguard audit export --output <path>` 写出 `gitguard.audit.export.v1`（JSON）或 CSV；文件只包含 `snippet_masked` 与 HMAC 指纹，不包含原文：

```json
{
  "schema": "gitguard.audit.export.v1",
  "exported_at": "2026-10-21T08:00:00.000Z",
  "run_count": 1,
  "finding_count": 1,
  "runs": [
    {
      "run_id": "0192f4a4-aec8-7e7b-8f0a-6b0e8b1d2c33",
      "repo_path": "D:/work/my-project",
      "scan_type": "PreCommit",
      "started_at": "2026-10-21T08:00:00.000Z",
      "duration_ms": 91,
      "result": "Blocked",
      "exit_code": 1,
      "findings": [
        {
          "finding_id": "0192f4a4-aec8-7e7b-8f0a-6b0e8b1d2c34",
          "rule_id": "GGD/SECRET/AWS-AKID",
          "file_path": "config/app.env",
          "line_number": 1,
          "severity": "Critical",
          "fingerprint": "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef",
          "snippet_masked": "AK****************42",
          "commit_sha": null
        }
      ]
    }
  ]
}
```

CSV 契约：

- UTF-8 with BOM、CRLF；表头固定为
  `run_id,repo_path,scan_type,started_at,duration_ms,result,exit_code,finding_id,rule_id,file_path,line_number,severity,fingerprint,snippet_masked,commit_sha`。
- 每个 Finding 一行；无 Finding 的运行保留一行运行级数据（Finding 相关列为空）。
- 含逗号 / 双引号 / CR / LF 的字段按 RFC 4180 加双引号并转义内部双引号；以 `= + - @ \t \r` 开头的字段前置 `'` 防电子表格公式注入。

导出摘要（stdout，`gitguard.audit.export.summary.v1`）：

```json
{
  "schema": "gitguard.audit.export.summary.v1",
  "output_path": "D:/backup/audit.json",
  "format": "json",
  "run_count": 1,
  "finding_count": 1
}
```

写入采用同目录临时文件 + 原子替换：目标已存在时覆盖；路径不可写 / 为目录 / IO 失败 → 退出码 31，且不残留临时文件。

### 4.10 白名单管理输出（JSON）

`gitguard allowlist add --format json` 输出 `gitguard.allowlist.add.v1`（stdout 单行紧凑）：

```json
{
  "schema": "gitguard.allowlist.add.v1",
  "entry": {
    "id": "0192f4a4-aec8-7e7b-8f0a-6b0e8b1d2c33",
    "rule_id": "GGD/SECRET/AWS-AKID",
    "path_pattern": "config/**",
    "fingerprint": null,
    "reason": "已知误报登记",
    "created_at": "2026-10-22T08:00:00.000Z",
    "created_by": "rushe",
    "expires_at": null
  }
}
```

`gitguard allowlist remove --format json` 输出 `gitguard.allowlist.remove.v1`：

```json
{
  "schema": "gitguard.allowlist.remove.v1",
  "id": "0192f4a4-aec8-7e7b-8f0a-6b0e8b1d2c33",
  "removed": true
}
```

`gitguard allowlist list --format json` 输出 `gitguard.allowlist.list.v1`：

```json
{
  "schema": "gitguard.allowlist.list.v1",
  "total": 2,
  "limit": 20,
  "offset": 0,
  "count": 2,
  "entries": [
    {
      "id": "0192f4a4-aec8-7e7b-8f0a-6b0e8b1d2c33",
      "rule_id": "GGD/SECRET/AWS-AKID",
      "path_pattern": "config/**",
      "fingerprint": null,
      "reason": "已知误报登记",
      "created_at": "2026-10-22T08:00:00.000Z",
      "created_by": "rushe",
      "expires_at": null
    }
  ]
}
```

- `rule_id` / `path_pattern` / `fingerprint` 未声明的条件为 `null`；`fingerprint` 为 64 位小写十六进制 HMAC 指纹。
- `created_at` / `expires_at` 为 UTC ISO 8601（毫秒 + `Z`）；CLI `add` 当前不支持过期时间，`expires_at` 恒为 `null`（持久化层仍支持读回已有过期条目）。
- `total` 为全表条目数；`count` 为当前页条数（可为 0）；排序固定为 `created_at DESC, rowid DESC`。
- 管理命令只读写本地审计库，不产生 `ScanRun`；`add` / `remove` 在同一事务内写 `AllowlistChanged` 审计事件（`category = Allowlist`，载荷 `EntryId` / `Action` / `RuleId` / `PathPattern` / `Fingerprint` / `Reason`）。

### 4.11 规则查询输出（JSON）

`gitguard rules list --format json` 输出 `gitguard.rules.list.v1`（stdout 单行紧凑）：

```json
{
  "schema": "gitguard.rules.list.v1",
  "total": 17,
  "count": 17,
  "category": "SECRET",
  "enabled_only": true,
  "rules": [
    {
      "rule_id": "GGD/SECRET/AWS-AKID",
      "name": "AWS Access Key ID",
      "category": "SECRET",
      "severity": "Critical",
      "source": "Builtin",
      "enabled": true
    }
  ]
}
```

`gitguard rules view <rule-id> --format json` 输出 `gitguard.rules.view.v1`：

```json
{
  "schema": "gitguard.rules.view.v1",
  "rule": {
    "rule_id": "USR/SECRET/INTERNAL-TOKEN",
    "name": "内部令牌",
    "category": "SECRET",
    "severity": "High",
    "source": "User",
    "enabled": true
  }
}
```

- `category` 为 `null` 表示未按类别筛选；`total` / `count` 均为筛选后条数（规则目录不分页）。
- `severity` ∈ `Critical` / `High` / `Medium` / `Low`；`source` ∈ `Builtin` / `User` / `Gitleaks`。
- `pattern` 仅在显式 `--show-pattern` 时出现，值为 .NET 正则原文；默认省略以避免被误用。
- 规则顺序为内置规则顺序 + `rules.custom` 声明顺序；未知规则输出 `gitguard.error.v1` 并返回退出码 40。

### 4.12 PATH 管理输出（JSON）

`gitguard path install --format json` 输出 `gitguard.path.install.v1`（stdout 单行紧凑）：

```json
{
  "schema": "gitguard.path.install.v1",
  "platform": "linux",
  "target_directory": "/home/user/.local/bin",
  "executable": "/opt/gitguard/gitguard",
  "already_present": false,
  "path_updated": true,
  "profile_path": "/home/user/.zshrc",
  "profile_updated": true,
  "backup_path": null,
  "effective": false
}
```

`gitguard path uninstall --format json` 输出 `gitguard.path.uninstall.v1`：

```json
{
  "schema": "gitguard.path.uninstall.v1",
  "platform": "linux",
  "target_directory": "/home/user/.local/bin",
  "path_updated": false,
  "link_removed": true,
  "profile_block_removed": true,
  "not_installed": false
}
```

`gitguard path status --format json` 输出 `gitguard.path.status.v1`：

```json
{
  "schema": "gitguard.path.status.v1",
  "platform": "windows",
  "target_directory": "C:\\Tools\\GitGuard",
  "executable": "C:\\Tools\\GitGuard\\gitguard.exe",
  "installed": true,
  "path_entry_present": true,
  "effective": false,
  "profile_path": null,
  "profile_configured": false
}
```

- `platform` ∈ `windows` / `linux` / `macos`；`target_directory` 为实际托管的 PATH 目录（Windows 为可执行文件所在目录，Unix 为 `~/.local/bin`）。
- `already_present` 表示目标目录已在 PATH 中（Windows）或符号链接已指向当前可执行文件（Unix）；此时 `path_updated` 为 `false`。
- `path_updated` 仅表示本次命令是否写入 PATH（Windows 注册表 / Unix 状态中有 profile 更新时由 `profile_updated` 单独表达；Unix 的 `path_updated` 表示符号链接是否新建或接管）。
- `effective` 表示目标目录是否已出现在**当前进程**的 PATH 中；新终端生效需重启终端或重新登录，不代表安装失败。
- `backup_path` 仅在 Unix `--force` 接管冲突符号链接时非空（`<link>.local.bak`）。
- Unix 状态查询的 `path_entry_present` 表示 shell profile 中是否存在托管标记块；`installed` 表示 `~/.local/bin/gitguard` 符号链接是否指向托管可执行文件。Windows 的 `profile_path` 恒为 `null`、`profile_configured` 恒为 `false`。
- 状态文件 `<data>/path-install.json` 使用内部 Schema `gitguard.path.state.v1`，仅用于精确卸载；损坏或缺失时按「未安装」处理，不影响命令执行。

## 5. 命令清单

> MVP 阶段固化以下命令集；新增命令需同步更新本文件、`模块设计/Cli.md`、`AGENT-GUIDE` 任务台账。

### 5.1 `gitguard install` — 安装 Hook

- **用途**：在当前仓库注入 `pre-commit` / `pre-push` / `post-commit` 分发脚本（`post-commit` 用于 FR-050 绕过检测）。
- **选项**：
  - `--force` 允许接管无法安全包裹的既有 Hook（二进制 / 符号链接形态）；普通文本 Hook 自动备份包裹，无需 `--force`；「遗留 `.local.bak` + 非托管目标」冲突不可用 `--force` 绕过。
  - `--hook <name>` 仅安装某个 Hook：`pre-commit` / `pre-push` / `post-commit`（其他取值 → 退出码 7）。省略时按配置 `hooks.pre_commit` / `hooks.pre_push` / `hooks.post_commit_bypass_detection` 注册子集；显式 `--hook` 始终优先（`GGD-0038`）。
- **配置**：`hooks.gitguard_executable_path` 覆盖写入分发脚本的可执行文件路径（支持 `~` / 相对路径，必须在安装期存在）；缺省使用当前可执行文件。三项 Hook 开关均为 `false` 且未显式给出 `--hook` → 退出码 13。
- **输入**：当前工作目录（必须是 Git 仓库）。
- **输出**：安装摘要（人类可读 → stderr） / JSON（`--format json` → stdout，Schema 见 §4.5）。
- **退出码**：0 成功；2 非 Git 仓库；3 写入权限不足；4 目标 Hook 无法安全包裹（需 `--force` 或不可接管）；7 参数非法；13 配置错误（Hook 全禁用 / 配置的可执行文件不存在）；50 已有并发 install / uninstall 持锁。
- **幂等**：是（已安装再次执行只刷新版本，不重复备份）。
- **关联 FR**：`FR-010`。
- **示例**：

```bash
gitguard install --format json
```

### 5.2 `gitguard uninstall` — 卸载 Hook

- **选项**：
  - `--hook <name>` 仅卸载某个 Hook：`pre-commit` / `pre-push` / `post-commit`（默认全部；其他取值 → 退出码 7）。
  - `--force` 仅当备份仍存在时，允许接管被手工替换的非托管目标并还原备份。
- **输出**：卸载摘要（人类可读 → stderr） / JSON（`--format json` → stdout，Schema 见 §4.5）。
- **退出码**：0 成功；2 非 Git 仓库；5 未发现 GitGuard 安装痕迹（不动文件、不写审计，仅提示）；7 参数非法；50 已有并发 install / uninstall 持锁。
- **幂等**：是。
- **关联 FR**：`FR-011`。

### 5.3 `gitguard status` — 查询状态

- **输入**：当前工作目录（必须是 Git 仓库）。
- **输出**：Git 版本、`core.hooksPath`、每个 Hook 文件签名、GitGuard 版本、配置文件加载链（人类可读 → stderr） / JSON（`--format json` → stdout，Schema 见 §4.6）。
- **退出码**：0 一切正常；2 非 Git 仓库；6 Hook 存在但未被 GitGuard 托管。
- **关联 FR**：`FR-012`。

### 5.4 `gitguard scan` — 扫描

- **子选项**：
  - `--staged`：扫描暂存区（Hook 内部调用；用户也可手动调用）。
  - `--push`：扫描推送范围；从 stdin 读取 `<local-ref> <local-sha> <remote-ref> <remote-sha>` 行（Git 调用约定）。
  - `--all`：全量扫描 HEAD 下全部被跟踪文件（以空树与 HEAD 的差异读取，不读取工作区）；空仓库按 Clean 处理。
  - `--include-history`：与 `--all` 同用，额外从 HEAD 向更早提交扫描历史差异；Finding 携带所属提交 `commit_sha`。
  - `--since <commit>`：与 `--all` 同用，从该提交的父提交继续向更早历史扫描（不含该提交本身），并隐含历史模式；通常传入上次报告的 `next_since_sha`。
  - `--max-files <N>`：与 `--all` 同用，历史文件数软上限（按提交边界生效，`N` 必须为正数）；隐含历史模式，截断时报告 `truncated` / `next_since_sha` / `remaining_commits`。
  - `--baseline <path>`：与 `--all` 同用，扫描完成后把当前全部命中的指纹写入指定路径（`GGD-0032`）。生成当次命中的 Finding 仍按原策略阻断；后续未指定 `--baseline` 的全量扫描自动消费仓库根默认 `.gitguard-baseline.json`。`--staged` / `--push` 组合、路径为空或以 `-` 开头 → 退出码 7。
  - `--record-pending`（隐藏）：Hook 模式专用，只能与 `--staged` 同用（否则退出码 7）。扫描通过后记录待提交预测（父提交 + 暂存树）供 `post-commit` 比对；扫描阻断 / 失败时清理旧标记。标记写入失败仅产生警告，不阻断提交。
  - `--hook-mode pre-commit|pre-push`（隐藏，`GGD-0038`）：标记分发脚本来源，分别要求与 `--staged` / `--push` 同用（不匹配 → 退出码 7）。配置 `scan.pre_commit` / `scan.pre_push` 为 `false` 时跳过扫描、退出码 0、不产生 ScanRun；`pre-commit` 仍以 `skipped=false` 写入待提交标记（配置关闭不计为绕过）。手动扫描不带该选项，始终执行。
- **`GITGUARD_SKIP=1`**：仅在 `--record-pending` 模式识别——跳过扫描、写入 `skipped` 标记、退出码 0（终端提示本次提交将登记为绕过事件）；手动 `scan --staged` 忽略该变量。
- **范围校验**：`--staged` / `--push` / `--all` 三选一（缺失或同时给出 → 退出码 7）；历史选项与 `--baseline` 仅能与 `--all` 组合，否则退出码 7；`--max-files` 非正数或 `--since` / `--baseline` 为空、以 `-` 开头 → 退出码 7。
- **输入**：Git 仓库 + 可选 stdin（`--push`）。
- **输出**：扫描报告（人类可读 → stderr） / JSON（`--format json` → stdout，Schema 见 §4.2 / §4.3）；`--all` 的文本报告展示历史扫描进度与续扫提示，JSON 额外携带 `full_scan` 对象；涉及基线时额外输出 `baseline` 对象，文本报告展示生成 / 消费状态与历史噪声条数。
- **退出码**：0 Clean/Warned（含仅基线抑制）；1 Blocked；2 非 Git 仓库；7 参数非法；10 Git 错误；11 检测器异常；12 超时；13 配置错误 / 基线文件损坏或非法；14 审计库异常；31 基线写入失败。
- **关联 FR**：`FR-020, FR-030, FR-050, FR-060, FR-061`。

### 5.5 `gitguard allowlist` — 白名单管理

- **子命令**：
  - `add --rule <id> [--path <glob>] [--fingerprint <hash>] --reason "<text>"`
    - 三个条件（rule / path / fingerprint）至少提供一个，可任意组合；所有声明的条件必须同时匹配才放行。
    - `--reason` 必填，trim 后 5~256 字符；`--fingerprint` 为 64 位十六进制（大小写不敏感，落库统一小写）。
    - 条目 `id` 由 UUIDv7 生成，`created_at` 取注入时钟（UTC），`created_by` 取当前 OS 用户名。
  - `remove --id <guid>`
    - `--id` 缺失或不是合法 GUID → 退出码 7；条目不存在 → 退出码 21。
  - `list [--limit N] [--offset M]`
    - `--limit` 1~1000（默认 20），`--offset` ≥ 0（默认 0）；取值越界 → 退出码 7。
    - 排序固定为 `created_at DESC, rowid DESC`。
- **输入**：本地审计库（含 `allowlist` 表）；命令**不要求**当前目录是 Git 仓库。
- **输出**：文本摘要 → stderr；`--format json` 时 → stdout（Schema 见 §4.10）。管理命令只写 `AllowlistChanged` 审计事件，不产生 `ScanRun`。
- **扫描合并**：`scan --staged` / `scan --push` 同时应用 `.gitguard.yml` 的 `allowlist.entries` 与 SQLite 条目（FR-041）；审计库读取失败按 FailClosed 映射退出码 14，不得静默忽略白名单。
- **退出码**：0 成功；7 用法错误；14 审计库打开 / 读写失败；20 白名单参数校验失败；21 条目不存在。
- **幂等**：`add` 非幂等（每次创建新条目）；`remove` 对已删除条目返回 21；`list` 只读。
- **关联 FR**：`FR-040, FR-041`。

### 5.6 `gitguard audit` — 本地审计查看与导出

- **子命令**：
  - `list [--limit N] [--offset M] [--since <date>] [--result <Clean|Warned|Blocked|Failed>]`
    - `--limit` 1~1000（默认 20）；`--offset` ≥ 0（默认 0）。
    - `--since` 接受 ISO 8601 时间；无时区偏移时按 UTC 解释，带偏移时先转换为 UTC；非法时间 → 退出码 7。
    - `--result` 只接受枚举名（大小写不敏感），拒绝数字取值 → 退出码 7。
  - `export --output <path> [--format json|csv]`
    - `--output` 必填；相对路径按当前工作目录解析，`~` 展开为用户主目录。
    - 子命令后的局部 `--format` 表示**导出文件格式**（默认 `json`）；全局输出格式须置于 `export` 之前（如 `gitguard --format json audit export --output a.json`）。
  - `view <run-id>`
    - `<run-id>` 必填，必须为合法的非空 GUID（非法 / 空 GUID → 退出码 7）；运行不存在 → 退出码 30（`AUDIT_RUN_NOT_FOUND`）。
    - 输出运行摘要（仓库 / 类型 / 开始时间 / 耗时 / 结果 / 退出码 / Finding 数）与 Finding 明细（规则 / 位置 / 脱敏片段 / 严重度 / 指纹 / 提交 SHA）。
- **输入**：无（查询整个本地审计库，含所有仓库；不要求当前目录是 Git 仓库）。
- **输出**：文本摘要 → stderr；`--format json` 时列表 / 详情 / 导出摘要 → stdout（Schema 见 §4.9）；导出文件由用例直接写入 `--output`。
- **脱敏**：列表、详情与导出只包含 `snippet_masked` 与 HMAC 指纹，不恢复、不写入任何原文（DR-02）。
- **退出码**：0 成功；7 参数非法；14 审计库读取失败；30 运行不存在（`view`）；31 导出写入失败。
- **关联需求**：SRS §4 审计导出接口；`DR-02`。

### 5.7 `gitguard rules` — 规则查看

- **子命令**：
  - `list [--category Secret|Privacy|LocalPath|Url] [--enabled] [--show-pattern]`
  - `view <rule-id> [--show-pattern]`
- **输入**：无（不要求当前目录是 Git 仓库）；只读配置与内置规则，不打开审计库。
- **输出**：规则 ID、名称、类别、严重度、来源（`Builtin` / `User` / `Gitleaks`）、是否启用；文本 → stderr，`--format json` → stdout（Schema 见 §4.11）。**默认不输出正则原文**，仅显式 `--show-pattern` 时追加 `pattern`。
- **筛选语义**：`--category` 大小写不敏感，接受 `Secret` / `Privacy` / `LocalPath` / `Url`（其他取值 → 退出码 7）；`--enabled` 仅保留「有效启用」规则（已考虑自定义规则 `enabled`、`disabled_rules` 与类别开关）。
- **退出码**：0 成功；7 类别 / 参数非法；40 规则不存在（输出 `gitguard.error.v1`）。
- **关联 FR**：`FR-080`（`GGD-0034`）。

### 5.8 `gitguard path` — 用户级 PATH 管理

- **用途**：把 GitGuard CLI 加入**当前用户**的 PATH，使用户可在新终端直接运行 `gitguard`；不修改系统级 PATH，也不需要管理员 / `sudo` 权限。命令不要求当前目录是 Git 仓库，也不读写审计库。
- **子命令**：
  - `install [--force] [--update-profile]`
    - Windows：把可执行文件所在目录追加到 `HKCU\Environment\Path`（`REG_EXPAND_SZ`，保留既有 `%VAR%` 引用，大小写不敏感去重，写入后广播 `WM_SETTINGCHANGE`）；超过 30000 字符 → 退出码 62。`--force` / `--update-profile` 在 Windows 上无效果。
    - Unix：在 `~/.local/bin` 创建指向当前可执行文件的 `gitguard` 符号链接。目标已存在且非本工具托管时默认拒绝（退出码 60）；`--force` 将冲突条目备份为 `gitguard.local.bak` 后接管。`--update-profile` 依据 `$SHELL` 向 `~/.bashrc` / `~/.zshrc` / `~/.profile` / fish 配置写入带 `# >>> gitguard >>>` / `# <<< gitguard <<<` 标记的可逆 PATH 块；未识别的 Shell → 退出码 61。
    - 只接受可执行文件名为 `gitguard` / `gitguard.exe` 的进程；`dotnet run`、测试宿主等非发布形态 → 退出码 61，且不触碰用户 PATH（避免误写入 dotnet 安装目录）。
    - 安装成功后写入 `<data>/path-install.json`（内部 Schema `gitguard.path.state.v1`），仅用于精确卸载。
  - `uninstall`
    - 只移除 GitGuard 托管痕迹：Windows 删除状态文件中记录的 PATH 条目；Unix 删除指向托管可执行文件的符号链接与 profile 标记块。其他 PATH 条目保持不变。
    - 未发现托管痕迹（无状态文件且无托管条目）→ 退出码 5，不修改任何内容。
  - `status`
    - 只读查询安装状态：`installed`（登记或链接存在）、`path_entry_present`（持久化条目 / profile 块）、`effective`（当前进程 PATH 是否可见）、`profile_path` / `profile_configured`（Unix）。
- **选项**：`--force` 仅 `install` 可用；`--update-profile` 仅 `install` 可用（Unix 有效）。
- **输出**：文本报告 → stderr；`--format json` → stdout（Schema 见 §4.12）。中英文案随 `--language` / `GITGUARD_LANG` 切换。
- **退出码**：0 成功；3 写入注册表 / 目录 / 符号链接 / profile 权限不足；5 未发现 GitGuard PATH 安装痕迹（仅 `uninstall`）；7 参数非法；60 目标条目冲突（需 `--force` 或手工清理）；61 平台不支持、Shell 无法识别或当前进程不是 gitguard 可执行文件；62 追加后 PATH 超过平台长度上限。
- **幂等**：`install` 是（已在 PATH / 链接已正确指向时不重复写入）；`uninstall` 对已移除状态返回 5；`status` 只读。
- **关联需求**：原始需求 3「GitGuard.Cli 增加写入系统 Path 路径功能」（`GGD-0061`）。
- **示例**：

```bash
gitguard path install --update-profile --format json
gitguard path status
gitguard path uninstall
```

### 5.9 `gitguard version` — 版本

- **输入**：无（非 Git 仓库中同样可用）。
- **输出**：GitGuard 版本、规则库版本、Git 版本、运行时信息（人类可读 → stderr） / JSON（`--format json` → stdout，Schema 见 §4.6）。
- **退出码**：0（含 git 探测失败：原因经 `git_failure` 字段输出）。

### 5.10 `gitguard hook post-commit` — 绕过检测（隐藏）

- **用途**：由 GitGuard 安装的 `post-commit` 分发脚本调用；比对实际提交与 `pre-commit` 记录的待提交预测，判定 `--no-verify` / `GITGUARD_SKIP=1` 绕过并写入审计。
- **可见性**：`hook` 命令组与 `post-commit` 子命令均为隐藏（不出现在 `--help`），供分发脚本内部使用。
- **输入**：当前工作目录（Git 仓库）；无参数、无 stdin。
- **输出**：文本模式未检测到绕过时静默，检测到则向 stderr 输出提交 SHA、原因与运行 ID；JSON 模式见 §4.7。
- **退出码**：始终 0（Git 忽略 `post-commit` 退出码）；审计写入失败 → 14；Git 读取失败 → 10。
- **关联 FR**：`FR-050`。

### 5.11 全局选项（所有子命令共用）

递归挂载于根命令，可出现在子命令前后任意位置：

| 选项 | 说明 |
| --- | --- |
| `--format text\|json` | 输出格式。`text`（默认）报告走 stderr；`json` 走 stdout（单行紧凑）。取值仅接受 `text` / `json`，其他取值 → 退出码 7 |
| `--no-color` | 禁用彩色输出。stderr 非终端（被重定向）或环境变量 `NO_COLOR` 已设置时自动禁用 |
| `--verbose` | 日志级别降到 Debug（关键决策与 Git 调用） |
| `--trace` | 日志级别降到 Trace（逐行匹配细节）；必须与 `--verbose` 同用，否则退出码 7 |
| `--log-file <path>` | 启用文件日志并写入指定路径（JSON-lines，按 `logging.file` 的 `max_bytes` / `max_files` 滚动）；`~` 展开为用户主目录 |

### 5.12 `gitguard --help`

- 全局 `--help` 由 System.CommandLine 自动生成。所有子命令都应支持 `--help` 独立说明。

## 6. 退出码规约

> **稳定契约**：下表所有退出码在同一 MAJOR 版本内保持稳定；禁止临时自造。

| 退出码 | 分类 | 场景 | 用户提示 |
| --- | --- | --- | --- |
| 0 | 成功 | 扫描通过 / 命令成功 | - |
| 1 | 业务阻断 | 命中阻断级规则 | 根据报告修复或登记白名单 |
| 2 | 环境 | 非 Git 仓库 | 在正确仓库目录中执行 |
| 3 | 环境 | 写入权限不足 | 检查文件权限 |
| 4 | 环境 | 目标 Hook 为二进制 / 符号链接（需 `--force`）或遗留备份冲突（不可接管） | 二进制 / 符号链接确认来源后加 `--force`；遗留备份冲突先手工清理 |
| 5 | 环境 | 未发现 GitGuard 安装痕迹 | （非故障：无需卸载；仅在 `uninstall` 中出现） |
| 6 | 环境 | Hook 存在但未被 GitGuard 托管 | 考虑 `gitguard install` 接管（二进制 / 符号链接需 `--force`） |
| 7 | 参数 | 用法错误：未知命令 / 未知选项 / 选项取值非法 / 参数组合非法 | 运行 `gitguard --help` 查看用法 |
| 10 | FailClosed | Git 命令失败 | 查看 `--verbose` 日志排查 Git 环境 |
| 11 | FailClosed | 检测器异常 | 查看日志；配置可暂时禁用疑似规则 |
| 12 | FailClosed | 扫描超时 | 调整 `.gitguard.yml` 中 `scan.timeout_ms` |
| 13 | FailClosed | 配置文件解析失败，或基线文件读取 / 解析失败（`BASELINE_INVALID`） | 检查 YAML 语法与 `version` 字段；修复或重新生成基线文件 |
| 14 | FailClosed | 审计库打开 / 迁移 / 读写失败（扫描内落库失败不适用：降级为 `audit_write_failed`，退出码不变） | 检查用户数据目录权限与磁盘空间 |
| 20 | 参数 | 白名单参数校验失败 | 补齐 `--reason` 等参数 |
| 21 | 参数 | 白名单条目不存在 | 用 `allowlist list` 确认 ID |
| 30 | 查询 | run_id 不存在 | 用 `audit list` 列出有效 run_id |
| 31 | 查询 | 审计导出失败，或基线文件写入失败（`BASELINE_WRITE_FAILED`） | 检查输出路径与权限 |
| 40 | 查询 | 规则不存在 | 用 `rules list` 列出规则 |
| 60 | 环境 | PATH 条目冲突：目标已有非 GitGuard 托管的同名文件 / 符号链接 | 确认来源后加 `--force` 备份接管，或手工清理 |
| 61 | 环境 | PATH 管理不支持：当前平台 / Shell 无法自动写入，或当前进程不是 gitguard 可执行文件（如 `dotnet run`） | 使用发布后的 `gitguard` 可执行文件；检查 `$SHELL` |
| 62 | 环境 | 追加后用户 PATH 超过平台长度上限（Windows 30000 字符） | 先清理不再使用的 PATH 条目 |
| 99 | 未知 | 代码缺陷导致未分类异常 | 请提交 Issue 并附 `--verbose` 日志 |

> 退出码 20 / 21 已由 `allowlist add` / `allowlist remove` 实际使用（`GGD-0030`）；退出码 30 已由 `audit view` 实际使用（`GGD-0036`，`AUDIT_RUN_NOT_FOUND`）；退出码 40 已由 `rules view` 实际使用（`GGD-0034`，`RULE_NOT_FOUND`）。退出码 31 已由 `audit export` 与基线写入失败实际使用（`AUDIT_EXPORT_FAILED`）；退出码 13 自 `GGD-0032` 起同时覆盖基线文件读取 / 解析失败。退出码 60 / 61 / 62 由 `gitguard path` 使用（`GGD-0061`，`PATH_ENTRY_CONFLICT` / `PATH_UNSUPPORTED` / `PATH_TOO_LONG`）；退出码 5 同时用于 `path uninstall` 的「未发现 PATH 托管痕迹」。

## 7. 限流与配额

- **本地工具无速率限制**。
- 单条 Git 命令超时 `scan.timeout_ms`（默认 30s），由 `.gitguard.yml` 控制。
- Regex 匹配超时 200ms（.NET 内置 `Regex.MatchTimeout`），命中超时即 `FailClosed`。

## 8. 并发与幂等

- CLI 对同一仓库同时运行多个 `scan` 时，SQLite 依赖 WAL 模式支持并发读 + 串行写；写冲突时自动重试 3 次（基础退避 50ms）。
- `install` / `uninstall` 命令在同仓库禁止并发（通过 `<git-common-dir>/gitguard.install.lock` 文件锁实现，worktree 场景下各 worktree 共享同一锁）；并发第二条直接退出码 50（忙）。

## 9. 安全头与跨域

N/A（本地 CLI 无 HTTP）。

## 10. 变更记录

| 版本 | 日期 | 变更说明 |
| --- | --- | --- |
| 0.1.0 | 2026-10-15 | 初始化：7 条顶级命令、15 条稳定退出码、JSON Schema `gitguard.scan.v1` |
| 0.1.1 | 2026-10-16 | GGD-0008：§5.1/§5.2 与实现对齐（`--force` 语义限定为二进制 / 符号链接接管；uninstall 补充 `--force` 与退出码 5 / 50 契约）；§6 退出码 4/5/6 场景细化；§8 锁路径更正为 `<git-common-dir>/gitguard.install.lock`（worktree 共享） |
| 0.1.2 | 2026-10-16 | GGD-0009：§4.2/§4.3 与实现对齐（单行紧凑 JSON、`scan_type` / `skipped_files` / `audit_write_failed` / `warnings` 字段、指纹为 64 位十六进制、`action` 取值含 `AllowedByAllowlist`）；§4.4 错误 JSON 的 `run_id` 更正为 `trace_id`；新增 §4.5 Hook 生命周期 JSON（`gitguard.install.v1` / `gitguard.uninstall.v1`）与 §4.6 状态 / 版本 JSON（`gitguard.status.v1` / `gitguard.version.v1`）；§5.1~§5.4/§5.8 补充输出分流与退出码，MVP 范围固化（`scan` 仅 `--staged` / `--push`）；新增 §5.9 全局选项表（`--format` / `--no-color` / `--verbose` / `--trace` / `--log-file`）；§6 新增退出码 7（用法错误）与 14（审计存储失败） |
| 0.1.3 | 2026-10-18 | GGD-0012 文档回填：§4.7 `audit list` 分页标注为 v0.2 规划（`GGD-0022`）；§6 标注退出码 20 / 21 / 30 / 31 / 40 为 v0.2 命令预留，v0.1 不会实际返回 |
| 0.1.4 | 2026-10-21 | GGD-0013：新增 §4.7 `gitguard.bypass.v1` 契约（分页顺延为 §4.8）；§5.1/§5.2 `--hook` 取值补 `post-commit`；§5.4 新增隐藏选项 `--record-pending` 与 `GITGUARD_SKIP=1`（仅 Hook 模式）语义；新增 §5.9 隐藏命令 `hook post-commit`（全局选项 / 帮助顺延为 §5.10 / §5.11） |
| 0.1.5 | 2026-10-21 | GGD-0022：§4.8 落地 `audit list` 分页（上限 1000，`started_at DESC, rowid DESC`）；新增 §4.9 审计列表 / 导出 JSON Schema（`gitguard.audit.list.v1` / `gitguard.audit.export.v1` / `gitguard.audit.export.summary.v1`）与 CSV 转义 / 公式注入防护契约；§5.6 重写 `audit list` / `export` 选项、输出分流与退出码（7 / 14 / 31），`view` 保持 v0.2 预留；§6 退出码 31 转为已使用 |
| 0.1.6 | 2026-10-22 | GGD-0030：§5.5 落地 `allowlist add` / `remove` / `list`（条件校验、分页、无仓库要求、SQLite 条目参与扫描合并、退出码 7 / 14 / 20 / 21）；新增 §4.10 白名单 JSON Schema（`gitguard.allowlist.add.v1` / `gitguard.allowlist.remove.v1` / `gitguard.allowlist.list.v1`）；§6 退出码 20 / 21 转为已使用 |
| 0.1.7 | 2026-10-22 | GGD-0031：§4.2 新增 `--all` 的 `full_scan` 对象契约（`include_history` / `commits_scanned` / `remaining_commits` / `truncated` / `next_since_sha`）与 `scan_type = Manual`；§4.3 补充历史扫描 `commit_sha` 语义；§5.4 注册 `--all` / `--include-history` / `--since` / `--max-files` 并固化三选一与历史选项组合校验 |
| 0.1.8 | 2026-10-22 | GGD-0032：§4.2 新增 `summary.ignored_by_baseline` 与可选 `baseline` 对象（`path` / `generated` / `entries` / `suppressed_findings`）；§4.3 动作枚举新增 `AllowedByBaseline` 并声明白名单优先级；§5.4 注册 `--baseline <path>` 及组合 / 路径校验，补充基线相关退出码 13 / 31；§6 退出码 13 / 31 场景扩展 |
| 0.1.9 | 2026-10-22 | GGD-0034：§4.11 新增 `gitguard.rules.list.v1` / `gitguard.rules.view.v1` Schema（默认隐藏 `pattern`，`--show-pattern` 才输出）；§5.7 重写 `rules list` / `view` 选项、筛选与过滤语义、无仓库可用与退出码 7 / 40；§6 退出码 40 转为已使用 |
| 0.1.10 | 2026-10-22 | GGD-0036：§4.8 注册 `audit view <run-id>`；§4.9 新增 `gitguard.audit.view.v1` Schema（`run` 对象 + `findings` 数组，字段与列表 / 导出一致）；§5.6 补充 `view` 选项、输出分流与退出码 7 / 14 / 30；§6 退出码 30 转为已使用 |
| 0.1.11 | 2026-10-23 | GGD-0038：§5.1 `install` 缺省按 `hooks.*` 注册子集（显式 `--hook` 优先）、`gitguard_executable_path` 覆盖脚本路径、全禁用 / 路径不存在 → 13；§5.4 新增隐藏选项 `--hook-mode` 与 `scan.pre_commit` / `scan.pre_push` 跳过语义（不产生 ScanRun，pre-commit 仍写非绕过标记） |
| 0.1.12 | 2026-10-25 | GGD-0053：§4.5 / §4.6 JSON 示例版本号同步为 1.0.0（契约字段不变） |
| 0.1.13 | 2026-10-26 | GGD-0054 / GGD-0055：§4.5 / §4.6 JSON 示例版本号同步为 1.1.0（CLI 契约字段与退出码不变） |
| 0.1.14 | 2026-10-26 | GGD-0061：新增 §4.12 PATH 管理 JSON Schema（`gitguard.path.install.v1` / `gitguard.path.uninstall.v1` / `gitguard.path.status.v1`）与内部状态文件 `gitguard.path.state.v1`；新增 §5.8 `gitguard path install` / `uninstall` / `status`（Windows 用户环境变量 / Unix `~/.local/bin` 符号链接 + 可选 profile 标记块、用户级不触碰系统 PATH、`--force` / `--update-profile`），原 §5.8~§5.11 顺延为 §5.9~§5.12；§6 新增退出码 60 / 61 / 62 并扩展退出码 3 / 5 的 PATH 场景 |
