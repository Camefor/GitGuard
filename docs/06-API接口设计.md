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
    "ignored_by_allowlist": 0
  },
  "skipped_files": [],
  "audit_write_failed": false,
  "warnings": []
}
```

- `scan_type`：`PreCommit`（`--staged`）/ `PrePush`（`--push`）。
- `duration_ms`：无法计时时为 `null`。
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
- `action`：`Block` / `Warn` / `Allow` / `AllowedByAllowlist`（白名单命中）。
- `commit_sha`：`--push` 扫描时为命中提交 SHA，其他扫描为 `null`。

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
  "version": "0.1.0",
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
  "gitguard_version": "0.1.0",
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
  "gitguard_version": "0.1.0",
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

### 4.7 分页

CLI 命令目前无需分页。`gitguard audit list` 支持 `--limit N --offset M`（默认 20）。

## 5. 命令清单

> MVP 阶段固化以下命令集；新增命令需同步更新本文件、`模块设计/Cli.md`、`AGENT-GUIDE` 任务台账。

### 5.1 `gitguard install` — 安装 Hook

- **用途**：在当前仓库注入 pre-commit / pre-push 分发脚本。
- **选项**：
  - `--force` 允许接管无法安全包裹的既有 Hook（二进制 / 符号链接形态）；普通文本 Hook 自动备份包裹，无需 `--force`；「遗留 `.local.bak` + 非托管目标」冲突不可用 `--force` 绕过。
  - `--hook <name>` 仅安装某个 Hook：`pre-commit` 或 `pre-push`（默认全部；其他取值 → 退出码 7）。
- **输入**：当前工作目录（必须是 Git 仓库）。
- **输出**：安装摘要（人类可读 → stderr） / JSON（`--format json` → stdout，Schema 见 §4.5）。
- **退出码**：0 成功；2 非 Git 仓库；3 写入权限不足；4 目标 Hook 无法安全包裹（需 `--force` 或不可接管）；7 参数非法；50 已有并发 install / uninstall 持锁。
- **幂等**：是（已安装再次执行只刷新版本，不重复备份）。
- **关联 FR**：`FR-010`。
- **示例**：

```bash
gitguard install --format json
```

### 5.2 `gitguard uninstall` — 卸载 Hook

- **选项**：
  - `--hook <name>` 仅卸载某个 Hook：`pre-commit` 或 `pre-push`（默认全部；其他取值 → 退出码 7）。
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
- **MVP 范围**：`--staged` / `--push` 二者必选且互斥（缺失或同时给出 → 退出码 7）；`--all` / `--since` / `--max-files` 属 v0.2（见 `17-开发RoadMap`），本版本未注册。
- **输入**：Git 仓库 + 可选 stdin（`--push`）。
- **输出**：扫描报告（人类可读 → stderr） / JSON（`--format json` → stdout，Schema 见 §4.2 / §4.3）。
- **退出码**：0 Clean/Warned；1 Blocked；2 非 Git 仓库；7 参数非法；10 Git 错误；11 检测器异常；12 超时；13 配置错误。
- **关联 FR**：`FR-020, FR-030, FR-060`。

### 5.5 `gitguard allowlist` — 白名单管理

- **子命令**：
  - `add --rule <id> [--path <glob>] [--fingerprint <hash>] --reason "<text>"`
  - `remove --id <guid>`
  - `list [--format text|json] [--limit N] [--offset M]`
- **约束**：`add` 时三个条件（rule/path/fingerprint）至少一个；`--reason` 必填 ≥ 5 字符。
- **退出码**：0 成功；20 校验失败；21 条目不存在。
- **关联 FR**：`FR-040, FR-041`。

### 5.6 `gitguard audit` — 本地审计查看

- **子命令**：
  - `list --limit 20 --offset 0 [--since <date>] [--result Blocked]`
  - `view <run-id>`
  - `export --output <path> --format json|csv`
- **退出码**：0 成功；30 run_id 不存在；31 导出失败。

### 5.7 `gitguard rules` — 规则查看

- **子命令**：
  - `list [--category Secret|Privacy|LocalPath|Url] [--enabled]`
  - `view <rule-id>`
- **输出**：规则 ID、名称、严重度、来源（Builtin/User/Gitleaks）、是否启用；**不输出正则原文**（避免误用），除非 `--show-pattern`。
- **退出码**：0 成功；40 规则不存在。

### 5.8 `gitguard version` — 版本

- **输入**：无（非 Git 仓库中同样可用）。
- **输出**：GitGuard 版本、规则库版本、Git 版本、运行时信息（人类可读 → stderr） / JSON（`--format json` → stdout，Schema 见 §4.6）。
- **退出码**：0（含 git 探测失败：原因经 `git_failure` 字段输出）。

### 5.9 全局选项（所有子命令共用）

递归挂载于根命令，可出现在子命令前后任意位置：

| 选项 | 说明 |
| --- | --- |
| `--format text\|json` | 输出格式。`text`（默认）报告走 stderr；`json` 走 stdout（单行紧凑）。取值仅接受 `text` / `json`，其他取值 → 退出码 7 |
| `--no-color` | 禁用彩色输出。stderr 非终端（被重定向）或环境变量 `NO_COLOR` 已设置时自动禁用 |
| `--verbose` | 日志级别降到 Debug（关键决策与 Git 调用） |
| `--trace` | 日志级别降到 Trace（逐行匹配细节）；必须与 `--verbose` 同用，否则退出码 7 |
| `--log-file <path>` | 启用文件日志并写入指定路径（JSON-lines，按 `logging.file` 的 `max_bytes` / `max_files` 滚动）；`~` 展开为用户主目录 |

### 5.10 `gitguard --help`

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
| 13 | FailClosed | 配置文件解析失败 | 检查 YAML 语法与 `version` 字段 |
| 14 | FailClosed | 审计库打开 / 迁移 / 读写失败（扫描内落库失败不适用：降级为 `audit_write_failed`，退出码不变） | 检查用户数据目录权限与磁盘空间 |
| 20 | 参数 | 白名单参数校验失败 | 补齐 `--reason` 等参数 |
| 21 | 参数 | 白名单条目不存在 | 用 `allowlist list` 确认 ID |
| 30 | 查询 | run_id 不存在 | 用 `audit list` 列出有效 run_id |
| 31 | 查询 | 审计导出失败 | 检查输出路径与权限 |
| 40 | 查询 | 规则不存在 | 用 `rules list` 列出规则 |
| 99 | 未知 | 代码缺陷导致未分类异常 | 请提交 Issue 并附 `--verbose` 日志 |

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
