# CLI 模块设计

## 1. 模块定位

- **一句话定位**：`gitguard` 可执行文件的入口与用户交互层；负责命令解析、参数校验、调用 Application UseCase、终端 / JSON 渲染、退出码映射。
- **所属限界上下文**：Scanning Context
- **承担 FR**：全部 CLI 命令（FR-010 ~ FR-071、FR-080、FR-090）
- **承担 DR**：`DR-03`（FailClosed 全局 catch） + 与其他模块的边界约束
- **关联 ADR**：`ADR-0002`（技术栈）

## 2. 输入输出契约

### 2.1 输入

| 来源 | 内容 |
| --- | --- |
| 命令行参数 | `gitguard <command> [options]` |
| 环境变量 | `GITGUARD_SKIP`（已实现：仅 `scan --staged --record-pending` Hook 模式识别，`GGD-0013`）、`GITGUARD_LANG`（已实现：`GGD-0014`，`--language` 优先）、`NO_COLOR`（已实现） |
| stdin（仅 `scan --push`） | Git push 信息行 |
| 配置文件 | 由 Policy 模块加载 |

### 2.2 输出

| 消费方 | 内容 |
| --- | --- |
| 用户 / Git | stderr 的人类可读报告 + 退出码（`audit list` 文本、`audit export` 摘要、`allowlist add/remove/list`、`rules list/view` 文本同样走 stderr） |
| 脚本调用方 | stdout 的 JSON（`--format json`；含 `audit list` / `audit export` 摘要、`allowlist add/remove/list`、`rules list/view`） |
| Audit | 一次扫描调用对应一个 `ScanRun`；`audit list` / `audit export` 只读查询，不产生 `ScanRun`；`allowlist add/remove` 只写 `AllowlistChanged` 事件 |

## 3. 核心规则

| 规则 | 要点 |
| --- | --- |
| 退出码契约 | 严格按 `06-API接口设计.md` §6 的退出码表；常量唯一来源为 `ExitCodes`，禁止就地写字面量 |
| 解析失败 | 未知命令 / 未知选项 / 受限取值非法 / 参数组合非法 → 退出码 7（`CLI_USAGE_ERROR`），在命令动作执行前返回，不触碰仓库与配置 |
| JSON Schema | 按 `gitguard.scan.v1` / `error.v1` / `install.v1` / `uninstall.v1` / `status.v1` / `version.v1` / `bypass.v1` / `audit.list.v1` / `audit.export.summary.v1` / `allowlist.add.v1` / `allowlist.remove.v1` / `allowlist.list.v1` / `rules.list.v1` / `rules.view.v1` 版本号；单行紧凑、中文不转义 |
| 白名单管理 | `allowlist add/remove/list` 直接读写本地审计库，不要求当前目录是 Git 仓库；`add` 校验失败映射 20、`remove` 条目不存在映射 21、库读写失败映射 14；分页参数非法映射 7 |
| 规则查询 | `rules list` / `rules view` 只读配置与内置规则目录，不要求 Git 仓库、不打开审计库；`--category` 大小写不敏感（非法 → 7）、`--enabled` 按有效启用筛选、默认隐藏正则原文（`--show-pattern` 才输出）；`view` 未知 ID → 40（`GGD-0034`） |
| 全量扫描 | `scan --staged` / `--push` / `--all` 三选一（否则 7）；`--include-history` / `--since` / `--max-files` 仅能与 `--all` 组合；`--since` 从指定提交的父提交向更早历史扫描，`--max-files` 按提交边界截断；文本报告展示历史进度，JSON 输出 `full_scan`（`GGD-0031`） |
| 基线文件 | `--baseline <path>` 仅能与 `--all` 组合（否则 7），生成当次不抑制命中；未指定时若仓库根存在 `.gitguard-baseline.json` 则自动消费；基线命中记为 `AllowedByBaseline`（白名单优先），JSON 输出 `baseline` 对象与 `ignored_by_baseline`；读取 / 解析失败 → 13，写入失败 → 31（`GGD-0032`） |
| 审计格式化 | `audit export` 的文件格式由子命令 `--format json\|csv` 决定，与全局 `--format text\|json` 同名但作用于不同层级；导出为同目录临时文件 + 原子替换，失败映射 31 |
| 隐藏命令 | `hook post-commit`（FR-050）：仅供分发的 `post-commit` 脚本调用，不出现在 `--help`；恒返回 0，审计失败映射 14 |
| 隐藏选项 | `scan --record-pending`：仅供 `pre-commit` 脚本调用，记录待提交预测；不能与 `--push` 组合（退出码 7） |
| 全局 catch | 任何未分类异常走退出码 99；并写 crash 日志（脱敏；写入失败静默跳过） |
| 彩色策略 | TTY + `NO_COLOR` 未设 + `--no-color` 未指定 时才启用 |
| 输出分流 | 文本报告走 stderr；`--format json` 的机器可读结果走 stdout，日志始终走 stderr |
| 脱敏 | 所有写往终端与日志的文本过 `MessageSanitizer`（去 ANSI / 控制字符、折行） |
| 子进程行为 | 不启动任何子进程（Git 由 Infrastructure 的 `GitAccess` 调用，可选 Gitleaks 由 Detection 的 `GitleaksAdapter` 调用） |

## 4. 外部依赖

| 依赖 | 用途 | 失败降级 |
| --- | --- | --- |
| `System.CommandLine` 2.0.x | 命令解析 | 解析失败由本层归一为退出码 7 并给出 `--help` 提示（不沿用框架默认退出码）；`InvocationConfiguration.EnableDefaultExceptionHandler = false`，异常一律由本层出口映射 |
| 控制台 | stderr / stdout | - |

## 5. 数据模型

CLI 层无独立持久化；通过 Application 层间接触达 Audit 模块。

## 6. 关键流程

### 6.1 `gitguard scan --staged` 入口

```mermaid
flowchart LR
    Entry[Program.Main] --> Host[CliHost.CreateDefault]
    Host --> Run[CliApplication.RunAsync]
    Run --> Parse[System.CommandLine 解析]
    Parse -- 失败 --> E7[退出码 7]
    Parse -- 成功 --> Session[GitGuardSession 组合根]
    Session --> Load[加载用户级 config.yml + 项目级 .gitguard.yml]
    Load --> UseCase[ScanStagedUseCase.ExecuteAsync]
    UseCase --> Render{Output}
    Render -- text --> StdErr[stderr 人类可读]
    Render -- json --> StdOut[stdout JSON]
    UseCase --> Exit[Exit Code]
```

装配边界：Cli 不引用 Infrastructure；`GitGuardSession`（`GitGuard.Application.Composition`）在 Application 层完成 Infrastructure 装配并按需延迟构建（`status` / `version` 等只读命令不打开审计库、不生成指纹盐）。

### 6.2 `gitguard scan --all` 入口（FR-060 / FR-061）

```mermaid
flowchart LR
    Entry[scan --all] --> Scope{staged / push / all}
    Scope -- 非唯一 --> E7[退出码 7]
    Scope -- all --> UseCase[ScanAllUseCase.ExecuteAsync]
    UseCase --> Head[GetFullTreeDiff HEAD：全量树快照]
    UseCase -- includeHistory / since / maxFiles --> History[ListCommitShas + GetCommitDiffs 批量历史差异]
    History -- max-files 截断 --> Resume[报告 next_since_sha]
    UseCase -- baseline 指定路径 --> Generate[扫描后 BaselineFile.Save：生成基线]
    UseCase -- 默认文件存在 --> Consume[BaselineFile.Load：按指纹放行]
    Consume --> Pipeline[ScanExecutor：检测 / 策略 / 基线覆盖 / 审计]
    Generate --> Render0[当次命中不抑制]
    Head --> Pipeline
    History --> Pipeline
    Render0 --> Render{Output}
    Pipeline --> Render
    Render -- text --> StdErr[stderr：历史进度 / 续扫提示 / 基线状态]
    Render -- json --> StdOut[stdout：full_scan / baseline 对象]
```

选项校验与历史摘要由 `ScanCommand` / `ScanAllUseCase` 完成；HEAD 全量树差异不读取工作区（DR-04），空仓库直接按 Clean 处理。基线生成仅接受显式 `--baseline` 路径，消费仅自动读取仓库根默认文件；基线读取 / 解析失败按 FailClosed 返回 13，写入失败返回 31。

### 6.3 `gitguard hook post-commit` 入口（FR-050）

```mermaid
flowchart LR
    Git[post-commit 分发脚本] --> Run[CliApplication.RunAsync]
    Run --> Parse[hook post-commit]
    Parse -- 成功 --> Detect[BypassDetectionService.Detect]
    Detect -- 未绕过 --> Silent[文本模式静默；退出码 0]
    Detect -- NoVerify / GitGuardSkip --> Audit[SaveBypassEvent + 警告]
    Audit --> E0[退出码 0]
    Detect -- SqliteException --> E14[退出码 14]
    Detect -- GitException --> E10[退出码 10]
```

`--record-pending` 在 `scan` 动作内处理：`GITGUARD_SKIP=1` 时绕过扫描并写入 `skipped` 标记；否则扫描通过后写入标记、阻断 / 失败时清理旧标记。标记操作失败仅降级为警告，不改变扫描退出码。

### 6.4 全局异常捕获

```mermaid
flowchart LR
    Call[子命令 Handler] --> OK[成功 → 退出码]
    Call -- ConfigurationException --> E13[退出码 13]
    Call -- BaselineException[BaselineException：INVALID / WRITE_FAILED] --> E13_31[退出码 13 / 31]
    Call -- GitException --> E10[退出码 10 / 非仓库 2]
    Call -- HookException --> EH[退出码 3 / 4 / 50]
    Call -- DetectionException --> E11[退出码 11]
    Call -- SqliteException --> E14[退出码 14]
    Call -- OperationCanceled --> E12[退出码 12]
    Call -- 其他 Exception --> E99[退出码 99 + CrashLog]
```

解析期失败（未进入 Handler）由 `CliApplication` 在派发前收敛为退出码 7。

## 7. 配置项

CLI 层无独立配置；选项通过命令行参数传递。全局选项均为根命令递归选项，可出现在子命令前后任意位置。

| 全局选项 | 类型 | 默认 | 说明 |
| --- | --- | --- | --- |
| `--format` | enum `text\|json` | text | 输出格式；取值经 `AcceptOnlyFromAmong` 在解析期校验 |
| `--no-color` | bool | false | 禁用彩色 |
| `--verbose` | bool | false | 日志级别到 Debug；扫描完成后输出 `Scan.RuleTiming` 规则级耗时摘要（`GGD-0020`） |
| `--trace` | bool | false | 日志级别到 Trace（必须与 `--verbose` 同用，否则退出码 7） |
| `--log-file` | path? | null | 启用文件日志（JSON-lines，`~` 展开为用户主目录） |
| `--language` | enum `zh-CN\|en-US` | 环境或中文 | 覆盖输出语言（`GGD-0014`）；取值经 `AcceptOnlyFromAmong` 在解析期校验；未指定时读 `GITGUARD_LANG`，均无效时回退中文 |

语言边界（`GGD-0014`）：命令描述、帮助、文本报告、用法错误与 CLI 日志文案随语言切换；JSON 字段名与取值、日志字段名保持英文；Application / Core 层异常消息与崩溃日志文本不在本期本地化范围（英文模式下部分失败原因仍为中文）。文案表为 `Localization/CliText.cs`（枚举键、AOT 友好）；框架帮助与解析错误经 `CurrentUICulture` 使用 `System.CommandLine` 内置资源。

## 8. 质量属性

| 属性 | 目标 |
| --- | --- |
| 冷启动 | ≤ 150ms（Native AOT；实测口径与宿主基线见 `benchmarks/README.md`） |
| `--help` 响应 | 立即 |
| 终端渲染不换行乱码 | 自动检测 TTY 宽度，Finding 列表自动截断 |

## 9. 测试要点

- [x] 每条退出码都有对应测试（`GitGuard.Cli.Tests`：退出码映射单测 + 各命令端到端用例）。
- [x] JSON Schema 字段与类型断言（`--format json` 用例逐字段校验）。
- [x] 彩色关闭策略（TTY/非 TTY/NO_COLOR）。
- [x] 全局 catch 不会吞 ExitCode。
- [x] 用法错误不落盘：退出码 7 时不创建审计库 / 不写 Hook 文件。
- [x] 绕过检测命令链（GGD-0013）：`--record-pending` 标记写入 / 清理 / 阻断清理、`GITGUARD_SKIP=1` 仅 Hook 模式生效、`hook post-commit` 的 `gitguard.bypass.v1` 字段与恒 0 退出码（`CliBypassTests`）。
- [x] 输出语言（GGD-0014）：默认中文、`--language en-US`（子命令前后 / `=` 形式）、`GITGUARD_LANG=en`、命令行覆盖环境变量、非法取值回退、英文帮助与用法错误、JSON 契约不变（`CliLocalizationTests`）。
- [x] 规则级耗时（GGD-0020）：`--verbose` 输出 `Scan.RuleTiming`（`rule_id` / `duration_us`）、默认级别不输出、英文文案（`CliRuleTimingTests`）。
- [x] 审计查询与导出（GGD-0022）：`audit list` 分页 / `--since` / `--result`、`audit export` JSON / CSV 脱敏、全局与子命令 `--format` 区分、非法参数 7、写入失败 31（`CliAuditCommandTests`）。
- [x] 白名单管理（GGD-0030）：`allowlist add/remove/list` 文本 / JSON 渲染、无仓库环境可用、校验失败 20、条目不存在 21、非法 ID / 分页参数 7、SQLite 条目参与扫描放行（`CliAllowlistCommandTests` + `ExitCodeMapperTests`）。
- [x] 全量扫描（GGD-0031）：`scan --all` HEAD 快照 / 历史提交扫描、`full_scan` JSON 字段、`--max-files` 截断与 `--since` 续扫、三选一与历史选项组合校验（`CliScanAllTests`）。
- [x] 基线文件（GGD-0032）：`--baseline` 生成当次不抑制、默认自动消费与 `AllowedByBaseline` / `ignored_by_baseline`、内容变化重新阻断、损坏基线 13、非法组合 / 空路径 7、写入失败 31、文本基线摘要（`CliScanBaselineTests` + `ExitCodeMapperTests`）。
- [x] 规则查询（GGD-0034）：`rules list` / `view` 文本与 JSON 渲染、类别 / `--enabled` 筛选、`--show-pattern` 门禁、无仓库可用、自定义规则参与扫描、退出码 7 / 40（`CliRulesCommandTests`）。
- [ ] `--help` 覆盖所有命令（随 Shell 补全一并补测）。

## 10. 演进与扩展位

- 新增命令：新建 `Commands/*Command.cs` 并在 `CommandTree` 登记；禁止在 `Program.cs` 内写具体业务。
- Shell 自动补全：P2 启用 `System.CommandLine` 的 shell completion（`bash`、`pwsh`、`zsh`）。
- 国际化：目前只做中英（`GGD-0014` 已实现，文案表见 `Localization/CliText.cs`）；更多语言靠社区贡献。

## 11. 变更记录

| 版本 | 日期 | 变更说明 |
| --- | --- | --- |
| 0.1.0 | 2026-10-15 | 初始化：命令解析入口 + 全局异常映射 + 彩色策略 |
| 0.1.1 | 2026-10-16 | GGD-0009：§2.1 环境变量按实现标注归属（`NO_COLOR` 已实现，`GITGUARD_SKIP` → `GGD-0013`、`GITGUARD_LANG` → `GGD-0014`）；§3 新增「解析失败 → 退出码 7」「输出分流」「脱敏」规则，JSON Schema 补全为六种；§4 `System.CommandLine` 降级说明改为本层归一退出码 7；§6.1 流程与组合根装配对齐（`CliHost` / `GitGuardSession` 懒装配）；§6.2 异常映射补 `HookException` / `SqliteException` / 非仓库 2 与解析期 7；§7 `--language` 标注 v0.2 顺延；§9 勾选已覆盖测试项 |
| 0.1.2 | 2026-10-21 | GGD-0013：§2.1 `GITGUARD_SKIP` 标记已实现（仅 Hook 模式）；§3 JSON Schema 补 `bypass.v1`，新增隐藏命令 / 隐藏选项规则；新增 §6.2 post-commit 入口与 `--record-pending` 处理（原全局异常捕获顺延为 §6.3）；§9 勾选绕过检测命令链测试项 |
| 0.1.3 | 2026-10-21 | GGD-0014：§2.1 `GITGUARD_LANG` 标记已实现；§7 `--language` 标注已注册与解析期校验；§9 勾选输出语言测试项；§10 标注文案表落点 |
| 0.1.4 | 2026-10-21 | GGD-0020：§7 `--verbose` 说明补充规则耗时摘要；§8 冷启动标注实测入口；§9 勾选规则耗时测试项（`CliRuleTimingTests`） |
| 0.1.5 | 2026-10-21 | GGD-0022：§2.2 输出补审计查询 / 导出；§3 JSON Schema 补 `audit.list.v1` / `audit.export.summary.v1` 并新增审计格式化规则；§9 勾选审计查询与导出测试项（`CliAuditCommandTests`） |
| 0.1.6 | 2026-10-22 | GGD-0030：§2.2 输出补白名单管理；§3 JSON Schema 补 `allowlist.add.v1` / `allowlist.remove.v1` / `allowlist.list.v1` 并新增白名单管理规则；§9 勾选白名单管理测试项（`CliAllowlistCommandTests`） |
| 0.1.7 | 2026-10-22 | GGD-0031：§3 新增全量扫描规则（三选一、历史选项组合、截断续扫）；新增 §6.2 `scan --all` 入口流程（原 post-commit / 异常捕获顺延为 §6.3 / §6.4）；§9 勾选全量扫描测试项（`CliScanAllTests`） |
| 0.1.8 | 2026-10-22 | GGD-0032：§3 新增基线文件规则；§6.2 流程补生成 / 消费分支与 `baseline` 输出；§6.4 异常捕获补 `BaselineException` → 13 / 31；§9 勾选基线文件测试项（`CliScanBaselineTests`） |
| 0.1.9 | 2026-10-22 | GGD-0034：§2.2 输出补规则查询；§3 新增规则查询规则、JSON Schema 补 `rules.list.v1` / `rules.view.v1`；§9 勾选规则查询测试项（`CliRulesCommandTests`） |
| 0.1.10 | 2026-10-22 | GGD-0035：§3 子进程行为更正——Git 由 Infrastructure `GitAccess` 调用，可选 Gitleaks 由 Detection `GitleaksAdapter` 调用（CLI 层不启动子进程） |
