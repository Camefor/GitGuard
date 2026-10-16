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
    "rules_evaluated": 36,
    "blocked": 0,
    "warned": 0,
    "ignored_by_allowlist": 0
  }
}
```

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
      "snippet_masked": "AKIA****************",
      "fingerprint": "sha256:ab12cd34...（64 chars）",
      "action": "Block",
      "advice": "改用环境变量或密钥管理服务"
    }
  ]
}
```

### 4.4 错误输出（JSON）

```json
{
  "schema": "gitguard.error.v1",
  "error_code": "GIT_COMMAND_FAILED",
  "exit_code": 10,
  "message": "执行 git diff --cached 失败：fatal: not a git repository",
  "run_id": "0192f4a5-aa00-7a7f-9f22-b7eac3a4f8d1"
}
```

### 4.5 分页

CLI 命令目前无需分页。`gitguard audit list` 支持 `--limit N --offset M`（默认 20）。

## 5. 命令清单

> MVP 阶段固化以下命令集；新增命令需同步更新本文件、`模块设计/Cli.md`、`AGENT-GUIDE` 任务台账。

### 5.1 `gitguard install` — 安装 Hook

- **用途**：在当前仓库注入 pre-commit / pre-push 分发脚本。
- **选项**：
  - `--force` 允许接管无法安全包裹的既有 Hook（二进制 / 符号链接形态）；普通文本 Hook 自动备份包裹，无需 `--force`；「遗留 `.local.bak` + 非托管目标」冲突不可用 `--force` 绕过。
  - `--hook <name>` 仅安装某个 Hook（默认全部）。
- **输入**：当前工作目录（必须是 Git 仓库）。
- **输出**：安装摘要（人类可读） / JSON（`--format json`）。
- **退出码**：0 成功；2 非 Git 仓库；3 写入权限不足；4 目标 Hook 无法安全包裹（需 `--force` 或不可接管）；50 已有并发 install / uninstall 持锁。
- **幂等**：是（已安装再次执行只刷新版本，不重复备份）。
- **关联 FR**：`FR-010`。
- **示例**：

```bash
gitguard install --format json
```

### 5.2 `gitguard uninstall` — 卸载 Hook

- **选项**：
  - `--hook <name>` 仅卸载某个 Hook。
  - `--force` 仅当备份仍存在时，允许接管被手工替换的非托管目标并还原备份。
- **退出码**：0 成功；2 非 Git 仓库；5 未发现 GitGuard 安装痕迹（不动文件、不写审计，仅提示）；50 已有并发 install / uninstall 持锁。
- **幂等**：是。
- **关联 FR**：`FR-011`。

### 5.3 `gitguard status` — 查询状态

- **输出**：Git 版本、`core.hooksPath`、每个 Hook 文件签名、GitGuard 版本、配置文件加载链。
- **退出码**：0 一切正常；2 非 Git 仓库；6 Hook 存在但未被 GitGuard 托管。
- **关联 FR**：`FR-012`。

### 5.4 `gitguard scan` — 扫描

- **子选项**：
  - `--staged`：扫描暂存区（Hook 内部调用；用户也可手动调用）。
  - `--push`：扫描推送范围；从 stdin 读取 `<local-ref> <local-sha> <remote-ref> <remote-sha>` 行（Git 调用约定）。
  - `--all`：扫描当前 HEAD 下所有被跟踪文件；可加 `--include-history` 枚举历史提交。
  - `--since <commit>`：历史扫描起点。
  - `--max-files <N>`：限制单次扫描文件数。
  - `--format text|json`：输出格式。
  - `--no-color`：禁用彩色输出。
- **输入**：Git 仓库 + 可选 stdin（`--push`）。
- **退出码**：0 Clean/Warned；1 Blocked；10 Git 错误；11 检测器异常；12 超时；13 配置错误。
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

- **输出**：GitGuard 版本、规则库版本、Git 版本、运行时信息。
- **退出码**：0。

### 5.9 `gitguard --help`

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
| 10 | FailClosed | Git 命令失败 | 查看 `--verbose` 日志排查 Git 环境 |
| 11 | FailClosed | 检测器异常 | 查看日志；配置可暂时禁用疑似规则 |
| 12 | FailClosed | 扫描超时 | 调整 `.gitguard.yml` 中 `scan.timeout_ms` |
| 13 | FailClosed | 配置文件解析失败 | 检查 YAML 语法与 `version` 字段 |
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
