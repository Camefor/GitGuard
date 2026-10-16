# 需求规格说明书（SRS）

## 1. 范围

本文件覆盖 GitGuard 的 CLI、Hook 分发器、检测引擎、策略引擎、SQLite 审计五个子系统的功能、数据、接口和非功能需求。Avalonia 桌面端（v0.3）在本文件只做占位，详细需求等桌面端启动时补充。

## 2. 功能需求（FR）

### 2.1 `FR-010` 安装 Hook

- **描述**：系统应在当前 Git 仓库中为 `pre-commit` 和 `pre-push` 两个 Hook 注入 GitGuard 分发脚本。
- **输入**：当前工作目录（必须是 Git 仓库）、可选 `--force`（仅用于接管**二进制 / 符号链接**形态的既有 Hook；普通文本 Hook 自动备份包裹，无需 `--force`）。
- **输出**：Hook 分发脚本写入 `.git/hooks/`（或 `core.hooksPath` 指向的目录），`HookInstallations` 表按 `(repo_path, hook_type)` upsert 为 `Active`（重复安装刷新版本与 `installed_at`，不新增行）；`HookInstalled` 审计事件；终端打印安装摘要。
- **处理规则**：
  - 检测当前 Hook 脚本是否已被 GitGuard 托管（前两行内包含 `# gitguard-managed v` 标记即托管）。
  - 若存在未托管常规文本 Hook，**复制**原文件到 `<hooks-dir>/<name>.local.bak`，分发脚本尾部**直接调用**（非 `exec`）该备份并合并退出码（GitGuard 非零优先，否则沿用原 Hook 退出码）。
  - 识别已配置的 `core.hooksPath`，优先在该路径安装，避免双重安装。
  - 安装采用「预检全部目标 → 渲染全部脚本 → 写入全部文件」三阶段，任一目标冲突时整体失败，不产生半安装状态；与卸载共用 `<git-common-dir>/gitguard.install.lock` 独占锁。
- **异常处理**：无 Git 仓库 → 退出码 2；无写入权限 → 退出码 3；原 Hook 为可执行二进制但非脚本 → 要求用户显式 `--force`（退出码 4，未给 `--force` 时零写入）；符号链接目标 → 同上；「遗留 `.local.bak` + 非托管目标」→ 拒绝接管（退出码 4，`--force` 不适用，防止覆盖真正的原 Hook）；并发安装冲突 → 退出码 50。
- **关联领域规则**：`DR-05`
- **关联测试用例**：`TC-FR-010-01` ~ `TC-FR-010-05`

### 2.2 `FR-011` 卸载 Hook

- **描述**：从当前仓库移除 GitGuard 注入的分发脚本，恢复原 Hook（如有）。
- **输入**：当前工作目录、可选 `--force`（仅当备份仍存在时，允许接管被手工替换的非托管目标并还原备份）。
- **输出**：Hook 文件被删除或还原；`HookInstallations` 行 upsert 翻转 `status='Uninstalled'`（保留 `installed_at` / `version` / 备份路径，不删除行）；`HookUninstalled` 审计事件。
- **处理规则**：若存在 `.local.bak` 备份则删除分发脚本并将备份改名还原；若无备份则直接删除分发脚本；未发现任何 GitGuard 托管痕迹（标记行或备份）时不动文件、不写审计，CLI 以退出码 5 提示「未安装」。
- **异常处理**：与安装共用同一把并发锁，冲突 → 退出码 50。
- **关联领域规则**：`DR-05`
- **关联测试用例**：`TC-FR-011-01` ~ `TC-FR-011-03`

### 2.3 `FR-012` 查询状态

- **描述**：输出当前仓库和用户环境中 GitGuard 的安装状态与版本信息。
- **输出**：JSON 或人类可读文本，包含 Git 版本、`core.hooksPath`、Hook 文件签名、GitGuard 版本、配置文件加载链。
- **关联测试用例**：`TC-FR-012-01`

### 2.4 `FR-020` 暂存区扫描

- **描述**：扫描当前仓库暂存区差异，判定是否存在敏感信息。
- **输入**：`git diff --cached -U0` 的输出（统一 unified-0 格式）。
- **输出**：零发现 → 退出码 0；仅警告 → 退出码 0 + 终端警告；阻断发现 → 退出码 1 + 终端报告 + SQLite 记录。
- **处理规则**：严格使用 `git diff --cached`；不读取磁盘工作区文件；文件 > `scan.max_file_bytes`（默认 1 MiB）跳过并记为「skipped」；二进制文件同样跳过并记为「skipped」。
- **异常处理**：Git 命令失败 → FailClosed 退出码 10；检测器抛异常 → FailClosed 退出码 11。
- **关联领域规则**：`DR-03, DR-04`
- **关联测试用例**：`TC-FR-020-01` ~ `TC-FR-020-10`

### 2.5 `FR-021` 四类检测器

- **描述**：MVP 必须内置以下四类检测器：
  - **SecretDetector**：匹配常见凭据格式（JWT、Slack、GitHub、AWS、通用 `password = "..."`）。
  - **PrivacyDetector**：中国大陆手机号（`^1[3-9]\d{9}$` + 上下文）、18 位身份证（含校验位）、邮箱。
  - **LocalPathDetector**：Windows 驱动器盘符、`~/`、`/Users/`、`/home/`、`/workspace/volumes/`、`C:\Users\` 等。
  - **UrlDetector**：含用户名密码的 URL（`://user:pass@`）、含 `token=` / `apikey=` 查询参数、内网保留 IP 段（10.0.0.0/8、172.16.0.0/12、192.168.0.0/16、127.0.0.0/8）。
- **处理规则**：每个检测器实现 `IDetector`；返回 `IEnumerable<Finding>`；运行顺序：Secret → Privacy → LocalPath → Url；可通过策略配置独立禁用。
- **关联领域规则**：`DR-01, DR-02`
- **关联测试用例**：`TC-FR-021-S-*`（Secret）/ `TC-FR-021-P-*`（Privacy）/ `TC-FR-021-L-*`（LocalPath）/ `TC-FR-021-U-*`（Url）

### 2.6 `FR-022` 策略引擎

- **描述**：将发现项的严重度映射为动作：`block`、`warn`、`allow`。
- **处理规则**：
  - 默认映射：`critical → block, high → block, medium → warn, low → warn`。
  - 项目配置可覆写；允许用户降级（如 `medium → allow`），但 `critical` 的 `block` 不可降级为 `allow`（硬下限）。
  - 任一 `block` 决定整次扫描退出码。
- **关联领域规则**：`DR-03`

### 2.7 `FR-023` 终端报告格式

- **描述**：命中项以统一格式输出到 stderr。
- **输出格式**（人类可读）：

  ```text
  ❌ [GGD/SECRET/AWS-AKID] appsettings.Production.json:18
     片段：AKIA****************
     建议：改用环境变量或密钥管理服务
     指纹：sha256:ab12...（可用于白名单）
  ```

- **可选 JSON 输出**：`--format json` 时输出结构化 Finding 列表。
- **关联测试用例**：`TC-FR-023-01` ~ `TC-FR-023-03`

### 2.8 `FR-030` 推送扫描

- **描述**：扫描一次 `git push` 涉及的所有提交差异。
- **输入**：Git 从 stdin 传入的 `<local-ref> <local-sha> <remote-ref> <remote-sha>` 行。
- **处理规则**：
  - 对每一行：若 `remote-sha` 全零（新分支），计算范围 `<local-sha>`；否则 `<remote-sha>..<local-sha>`。
  - 使用 `git rev-list` 枚举提交，再逐个 `git diff-tree -p -U0 --root --no-commit-id <commit>` 扫描（`--root` 使初始提交按全量新增输出，避免新分支仅含根提交时漏扫；`--no-commit-id` 保证输出为纯 unified 差异）。
  - 若 `local-sha` 全零（删除分支）→ 跳过该行，无提交可扫描。
  - 空 stdin → 无推送目标：直接 Clean（退出码 0）。
  - 提交在多个 ref 行重复出现时按首次出现顺序去重，只扫描一次。
- **异常处理**：Git 输入解析失败（行格式不符）→ FailClosed 退出码 10，不回显原始输入内容（DR-02）；范围枚举 / 提交差异失败 → FailClosed 退出码 10。
- **关联测试用例**：`TC-FR-030-01` ~ `TC-FR-030-04`

### 2.9 `FR-040` 白名单

- **描述**：允许用户以规则 ID、文件路径（glob）、指纹三种粒度登记例外。
- **输入**：`gitguard allowlist add --rule <id>`、`--path <glob>`、`--fingerprint <hash>` 任一或组合；`--reason` 必填。
- **输出**：写入 `Allowlist` 表。
- **处理规则**：匹配时按「rule + path + fingerprint」三者取交集；缺省项视为通配。
- **关联测试用例**：`TC-FR-040-01` ~ `TC-FR-040-05`

### 2.10 `FR-050` 绕过事件审计

- **描述**：识别 `--no-verify` 与 `GITGUARD_SKIP=1` 两种显式绕过，并写入审计。
- **处理规则**：注册轻量 `post-commit` Hook：对比最新提交的 SHA 与上一次扫描的「待提交 SHA 预测」；若未匹配，推断为绕过事件。
- **输出**：`ScanRuns` 中新增一条 `BypassEvent` 行。

### 2.11 `FR-060` 全量扫描

- **描述**：扫描当前 HEAD 下的所有被跟踪文件；可选扫描历史提交。
- **输入**：`gitguard scan --all [--since <commit>] [--include-history] [--max-files <N>]`。
- **处理规则**：默认只扫描 HEAD；加 `--include-history` 时按 `git log` 顺序枚举提交，支持中断续扫（下次从上次 `commit SHA` 继续）。

### 2.12 `FR-070` 配置文件

- **描述**：配置文件 Schema 见 `模块设计/Policy.md` §7；本节保留验收要点。
- **处理规则**：
  - 项目级 `.gitguard.yml` 覆盖用户级。
  - 不存在时使用内置默认。
  - 版本字段 `version: 1` 必填；未知版本 → FailClosed。
  - 未知字段：警告但不阻断（宽松解析），警告经 `gitguard status` 与扫描报告的 `warnings` 透出。
  - 取值越界（`scan.max_file_bytes` / `scan.timeout_ms` / `logging.file.*` / `rules.secrets.min_entropy`）→ FailClosed（退出码 13）。
  - `rules.secrets.use_gitleaks: true`（P2 未实现）→ FailClosed：无法兑现的开关静默忽略会构成 FailOpen。
- **实现**：`GitGuard.Application.Configuration.GitGuardConfigLoader`（`GGD-0009`）。
- **关联测试用例**：`TC-FR-070-01` ~ `TC-FR-070-04`

### 2.12.1 `FR-071` 用户级配置

- **描述**：用户级 `~/.config/gitguard/config.yml`（`PRD FR-071`，P1）。
- **处理规则**：
  - 与项目级配置深度合并：map 递归合并、列表整体替换；项目级优先。
  - 文件缺失不报错（视为未配置）。
  - 格式或取值非法与项目级同等处理（FailClosed，退出码 13）。
- **关联测试用例**：`TC-FR-070-01`（覆盖「项目级覆盖用户级」的加载链顺序断言）

### 2.13 `FR-090` GitleaksAdapter（P2）

- **描述**：通过实现 `IDetector`，把 Gitleaks 命令行作为可选秘密检测引擎接入。
- **处理规则**：
  - 启动时探测 `gitleaks` 可执行路径；不存在 → 禁用适配器，不阻断。
  - 调用方式：`gitleaks detect --no-git --source <staged-tmp> --report-format json`。
  - 适配器把 Gitleaks 输出映射为 `Finding`，规则 ID 前缀 `GLK/`。
- **关联 ADR**：`ADR-0003`

## 3. 数据需求

| 实体 | 关键属性 | 类型 | 取值范围 | 缺失语义 |
| --- | --- | --- | --- | --- |
| ScanRun | Id、RepoPath、Type（pre-commit/pre-push/manual/bypass）、StartedAt、DurationMs、Result | GUID / string / enum / UTC / int / enum | ScanType 枚举、Result ∈ {clean, warned, blocked, failed} | DurationMs 为空 → 异常退出 |
| Finding | Id、RunId、RuleId、FilePath、LineNumber、Severity、Fingerprint、SnippetMasked | GUID / FK / string / string / int / enum / string(64) / string(≤32) | Severity ∈ {Critical, High, Medium, Low} | SnippetMasked 必须掩码到原值长度之内 |
| DetectionRule | RuleId、Name、Pattern、Severity、Enabled、Source（builtin/user/gitleaks） | string(≤48) / string / string / enum / bool / enum | RuleId 命名约定 `GGD/<CATEGORY>/<NAME>` | - |
| Allowlist | Id、RuleId（nullable）、PathPattern（nullable）、Fingerprint（nullable）、Reason | GUID / FK / glob / string(64) / string(≤256) | 至少一个条件非空 | Reason 不得为空 |
| HookInstallation | RepoPath、HookType、InstalledAt、Version、OriginalHookBackupPath | string / enum / UTC / string / string(nullable) | - | OriginalHookBackupPath 空 → 原本无 Hook |

详细表结构见 `05-数据库设计.md`。

## 4. 接口需求

| 接口方向 | 名称 | 用途 | SLA |
| --- | --- | --- | --- |
| 对外（CLI） | `gitguard` 命令集 | 开发者与 Hook 调用 | 冷启动 < 150ms（Native AOT 目标），单命令 FailClosed 必须 < 配置超时 |
| 对外（配置） | `.gitguard.yml` / 用户级 config | 规则与策略 | 解析失败 → FailClosed |
| 对外（审计导出） | `gitguard audit export --format json/csv` | 用户事后分析 | 必须脱敏，无原文 |
| 对内（Git） | `git diff --cached`、`git rev-list`、`git diff-tree`、`git log` | 读取仓库差异 | 失败即 FailClosed |
| 对内（可选） | `gitleaks` CLI | P2 秘密检测补强 | 不可用时降级，不阻断 |

CLI 命令细节见 `06-API接口设计.md`。

## 5. 非功能需求（NFR）

### 5.1 性能

| 编号 | 指标 | 条件 | 目标值 | 测量方式 |
| --- | --- | --- | --- | --- |
| `NFR-P-001` | 冷启动时间 | Native AOT 单文件；Windows 11 NVMe | ≤ 150ms | 基准脚本 `benchmarks/startup.sh` 执行 50 次取 P95 |
| `NFR-P-002` | 暂存区扫描 P95 | ≤ 500 行差异 | ≤ 500ms | 基准脚本 10 组样本仓库 |
| `NFR-P-003` | 推送扫描 P95 | ≤ 50 提交 / ≤ 5000 行差异 | ≤ 2s | 同上 |
| `NFR-P-004` | 全量扫描吞吐 | ≤ 100 MB 代码 | ≥ 20 MB/s | 单机 benchmark |

### 5.2 可用性

| 编号 | 指标 | 目标值 | 测量方式 |
| --- | --- | --- | --- |
| `NFR-A-001` | Hook 兼容率 | 与 Husky / lefthook / pre-commit 共存成功率 100% | 集成测试 `TC-COMPAT-*` |
| `NFR-A-002` | 跨平台可用性 | Windows 11 / macOS 14 / Ubuntu 24.04 均通过验收 | CI 多平台矩阵 |

### 5.3 安全

| 编号 | 要求 | 验证方式 |
| --- | --- | --- |
| `NFR-S-001` | 本地化：无出站网络请求 | 单元测试 + CI 下 `strace`/`netstat` 验证 |
| `NFR-S-002` | SQLite / 日志 / 临时文件无明文敏感原文 | 代码审查 + 测试用「金丝雀 Token」注入后扫文件 |
| `NFR-S-003` | FailClosed：所有异常路径默认阻断 | 故障注入测试 `TC-FAILCLOSED-*` |
| `NFR-S-004` | 绕过事件可追溯 | 审计表查询 |

### 5.4 合规

| 编号 | 法规/标准 | 要求 | 证据 |
| --- | --- | --- | --- |
| `NFR-C-001` | GB/T 35273—2020 | 对手机号、身份证等个人信息采集遵循最小必要；不留原值 | 代码审查 + 数据库 Schema |

### 5.5 可访问性与国际化

| 编号 | 要求 | 验证方式 |
| --- | --- | --- |
| `NFR-U-001` | CLI 支持 `--no-color` 与 `--format json` | 单测 |
| `NFR-U-002` | 终端输出默认中文（MVP）；允许 `GITGUARD_LANG=en` 切换英文 | 单测 |

### 5.6 可维护性与可观测性

| 编号 | 要求 | 验证方式 |
| --- | --- | --- |
| `NFR-M-001` | 日志按 `level + category` 结构化输出 | 代码审查 |
| `NFR-M-002` | `gitguard --verbose` 输出各规则命中耗时 | 单测 |

## 6. 领域规则（DR）

| 编号 | 规则 | 来源 | 关联 FR |
| --- | --- | --- | --- |
| `DR-01` | 本地化：所有扫描与审计必须在本机完成 | CONTEXT §5 | 全部 |
| `DR-02` | 零明文留存：审计/日志/临时文件禁止写入敏感原文 | CONTEXT §5 | FR-023, FR-040, FR-050 |
| `DR-03` | FailClosed：异常默认阻断 | CONTEXT §5 | FR-020, FR-030 |
| `DR-04` | 暂存区真实：pre-commit 必须使用 `git diff --cached` | CONTEXT §5 | FR-020 |
| `DR-05` | Hook 兼容：安装不得静默覆盖已有 Hook | CONTEXT §5 | FR-010, FR-011 |
| `DR-06` | 绕过留痕：`--no-verify` 事件必须审计 | CONTEXT §5 | FR-050 |

详细展开见 `04-领域模型设计(DDD).md` §7。

## 7. 约束与假设

- **技术**：首发目标 .NET 10 LTS；Native AOT 单文件发行。
- **技术**：发行物不包含 Git 二进制；运行时依赖 Git ≥ 2.9。
- **组织**：MVP 阶段仅 1 位开发者维护；文档优先于代码。
- **业务假设**：开发者愿意为可选的二期桌面端额外安装一个 GUI 应用；不愿意安装的用户用 CLI 即可完成全部工作。

## 8. 需求追溯矩阵

| 需求 ID | PRD 场景 | 设计文档 | 测试用例 |
| --- | --- | --- | --- |
| `FR-010` | 3.1 | `03-SAD §6 Hook 模块` / `模块设计/Hook.md` | `TC-FR-010-*` |
| `FR-011` | 3.1 | 同上 | `TC-FR-011-*` |
| `FR-012` | 3.1 | `模块设计/Cli.md` | `TC-FR-012-*` |
| `FR-020` | 3.2 | `模块设计/Secret.md` / `Privacy.md` | `TC-FR-020-*` |
| `FR-021` | 3.2 | `模块设计/Secret.md` ~ `模块设计/Url` | `TC-FR-021-*` |
| `FR-022` | 3.2 | `模块设计/Policy.md` | `TC-FR-022-*` |
| `FR-023` | 3.2 | `模块设计/Cli.md` | `TC-FR-023-*` |
| `FR-030` | 3.3 | `模块设计/Hook.md` | `TC-FR-030-*` |
| `FR-040` | 3.4 | `模块设计/Policy.md` | `TC-FR-040-*` |
| `FR-050` | 3.5 | `模块设计/Hook.md` | `TC-FR-050-*` |
| `FR-060` | 3.6 | `模块设计/Cli.md` | `TC-FR-060-*` |
| `FR-070` | 全部 | `模块设计/Policy.md` | `TC-FR-070-*` |
| `FR-071` | 全部 | `模块设计/Policy.md` | `TC-FR-070-01` |
| `FR-090` | 3.2 | `模块设计/Secret.md` + `ADR-0003` | `TC-FR-090-*` |

## 9. 未解问题

| # | 问题 | 影响 | 决策人 | 期限 |
| --- | --- | --- | --- | --- |
| Q-01 | 是否对 `--no-verify` 加 `post-commit` 探测，还是走 reflog 对比 | 中 | 项目负责人 | MVP 启动前 |
| Q-02 | 自定义正则是否支持命名捕获组用于更精确上下文 | 低 | 项目负责人 | P2 启动前 |

## 10. 变更记录

| 版本 | 日期 | 变更说明 |
| --- | --- | --- |
| 0.1.0 | 2026-10-15 | 初始化：定义 20 条 FR、10+ NFR、6 条 DR 的追溯矩阵 |
| 0.1.1 | 2026-10-16 | GGD-0007：§2.4 补充二进制文件跳过语义；§2.8 补充删除分支（`local-sha` 全零）跳过、空 stdin 无推送目标、跨 ref 提交去重与解析失败不回显输入 |
| 0.1.2 | 2026-10-16 | GGD-0008：§2.1/§2.2 与实现对齐——`--force` 语义限定为二进制/符号链接接管；托管识别改前两行精确匹配；备份为复制且分发脚本直接调用（非 `exec`）并合并退出码（GitGuard 非零优先）；安装三阶段零半安装 + 并发锁（退出码 50）；审计记录为 upsert 状态翻转（非新增行）；补充未安装卸载退出码 5 |
| 0.1.3 | 2026-10-16 | GGD-0009：§2.12 补充宽松解析（未知字段警告不阻断）、取值越界与 `use_gitleaks: true` 的 FailClosed 语义并标注实现落点；新增 §2.12.1 `FR-071` 用户级配置条目（原仅见于 PRD，追溯矩阵同步补行） |
