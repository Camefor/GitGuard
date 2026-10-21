# Agent 工作指南与任务台账

> 本文档是本项目 AI Agent 执行任务、记录进度和跨会话恢复的唯一工作入口。产品与技术范围以各设计文档为准，实时执行状态以本文档为准。

## 1. 核心原则

1. 当前用户的最新明确指令始终优先；不得用历史待办覆盖用户的新要求。
2. 先读取基线、确认工作区和登记任务，再修改代码或文档。
3. RoadMap 负责描述阶段范围，本文档负责记录任务的实际状态、恢复点和验证结果。
4. 同一时间最多一个 `IN_PROGRESS` 任务。未完成任务不得伪装为 `DONE`。
5. 保留用户已有未提交改动；不得擅自回退、删除或重写无关内容。
6. 实现、测试和文档必须同步。设计发生变化时更新对应设计文档，而不只更新本台账。

## 2. 会话启动协议

每次开始仓库任务时按以下顺序执行：

1. **初始化检查**：若 `docs/INIT-GUIDE.md` §5 「完成记录」为空，先执行 `INIT-GUIDE` 全流程，再回到本协议第 2 步。（本项目已于 2026-10-15 完成，无需再触发。）
2. 完整读取根目录 `AGENTS.md`（如存在）和本文档。
3. 读取 [`README.md`](./README.md)，确认文档目录和当前产品基线。
4. 检查本文档「当前执行状态」和任务清单：
   - 存在与用户指令匹配的 `IN_PROGRESS` 或 `PAUSED` 任务时，从「下一步动作」继续。
   - `BLOCKED` 任务的阻塞条件已经解除时，将其改为 `IN_PROGRESS` 后继续。
   - 用户发起新任务时，创建任务 ID 并登记；若需要切换任务，先为旧任务写清恢复点并标记 `PAUSED`。
5. 检查 Git 工作区，识别用户已有改动，明确本次允许触碰的文件范围。
6. 按「文档读取路由」补读相关文档，确认需求、约束和验收标准。
7. 在首次文件修改前，把当前任务设为 `IN_PROGRESS`，填写目标和下一步动作。

若会话因工具、上下文或环境意外中断，下次 Agent 必须优先读取恢复字段，不得从头重复已完成步骤。

## 3. 文档读取路由

### 3.1 每个实现任务必读

| 文档 | 读取目的 |
| --- | --- |
| [项目概述](./00-项目概述.md) | 确认产品边界、MVP 范围和安全约束 |
| [开发规范](./13-开发规范.md) | 确认工程、编码、注释、测试和文件格式要求 |
| [开发 RoadMap](./17-开发RoadMap.md) | 确认阶段顺序、依赖和退出条件 |
| [测试方案](./16-测试方案.md) | 确认与改动风险相匹配的验证要求 |

纯文档整理任务可按实际影响缩减必读范围，但仍必须读取本文档与文档索引。

### 3.2 按任务类型选读

| 任务类型 | 必读文档 |
| --- | --- |
| 产品范围、用户流程、验收 | [PRD](<./01-产品需求(PRD).md>)、[SRS](<./02-需求规格说明书(SRS).md>) |
| 架构、分层、领域建模 | [SAD](<./03-系统架构设计(SAD).md>)、[DDD](<./04-领域模型设计(DDD).md>) |
| 数据库、迁移、持久化 | [数据库设计](./05-数据库设计.md)、[DDD](<./04-领域模型设计(DDD).md>) |
| CLI 命令、退出码、输出格式 | [CLI 契约](./06-API接口设计.md)、[异常处理设计](./10-异常处理设计.md) |
| 核心业务模块 | 对应 `模块设计/<模块名>.md`、相关 ADR |
| 桌面端页面、交互、组件（P3） | [桌面 UI 设计](./11-前端UI设计.md)、[桌面组件设计](./12-前端组件设计.md)、[CLI 契约](./06-API接口设计.md) |
| 缓存、日志、可观测性 | [缓存设计](./07-缓存设计.md)、[日志设计](./09-日志设计.md)、[异常处理设计](./10-异常处理设计.md) |
| 发行、分发、构建 | [发行与分发](./14-部署文档.md)、[Docker 构建](./15-Docker部署.md)、[日志设计](./09-日志设计.md) |
| 架构或范围关键决策 | [ADR 索引](./adr/README.md)、相关历史 ADR |

跨模块任务读取所有受影响模块的文档。不要无目的加载全部文档，也不要在未读相关基线时凭经验实现。

## 4. 任务状态约定

| 状态 | 含义 | 必填信息 |
| --- | --- | --- |
| `TODO` | 已确认但尚未开始 | 任务目标、来源、优先级 |
| `IN_PROGRESS` | 当前正在执行 | 当前进度、下一步动作、涉及文件 |
| `PAUSED` | 被新任务或会话边界暂停，可继续 | 最后完成动作、精确恢复点、剩余工作 |
| `BLOCKED` | 外部条件未满足，当前无法推进 | 阻塞原因、已尝试内容、解除条件 |
| `DONE` | 验收与必要验证均已完成 | 完成日期、验证结果、文档影响 |
| `CANCELLED` | 明确不再执行 | 取消日期、取消原因和决策来源 |

任务 ID 使用 `GGD-NNNN`，新增任务取当前最大编号加一；不得复用或重排历史 ID。复选框只表达是否完成，状态列才是任务生命周期的权威字段。

`PAUSED` 和 `BLOCKED` 任务必须在「暂停/阻塞任务恢复详情」中保留独立记录，不能只依赖会被下一项任务覆盖的「当前执行状态」。`CANCELLED` 任务移入「取消任务」，不得直接删除。

## 5. 执行与更新规则

### 5.1 开始任务

1. 在待办清单新增或选择任务。
2. 将状态设为 `IN_PROGRESS`，同时更新「当前执行状态」。
3. 写清本次可验证的目标，不使用「优化一下」「继续开发」等模糊描述。

### 5.2 执行过程中

- 完成关键里程碑后及时更新「最后完成动作」和「下一步动作」。
- 发现新增范围时登记新任务，不把无关改动偷偷并入当前任务。
- 需求或设计有冲突时，先记录冲突与采用的决策，再继续实现。
- 验证失败时保留失败结果；只有修复并重新验证后才能标记完成。

### 5.3 任务中断与恢复

中断前先把任务状态改为 `PAUSED` 或 `BLOCKED`，并在「暂停/阻塞任务恢复详情」创建或更新该任务的独立记录。记录至少包括：

- 已完成到哪个具体步骤。
- 下一条可直接执行的命令或代码修改。
- 已修改和仍需修改的文件。
- 已执行的验证及其结果。
- 未解决风险、阻塞条件和需要用户确认的决策。

下次会话恢复时：

1. 先检查工作区是否与记录一致。
2. 从「下一步动作」继续，不重复已验证通过的步骤。
3. 若记录与实际文件不一致，以文件和 Git 差异为证据修正台账。
4. 若用户的新指令与旧任务冲突，最新指令优先；旧任务保留为 `PAUSED`，不得静默丢失。

## 6. 会话收尾协议

每次项目任务结束、暂停或阻塞前，Agent 必须在最终回复前完成：

1. 更新「当前执行状态」和任务清单，确保状态与实际结果一致。
2. 完成则移入已办清单，取消则移入取消任务；未完成则保留为 `IN_PROGRESS`、`PAUSED` 或 `BLOCKED` 并写清恢复点。
3. 追加一条「会话记录」，写明结果、验证和下一步。
4. 同步本次影响的需求、设计、API、数据库、测试或部署文档。
5. 检查 Git 差异，确认没有覆盖用户改动，也没有混入无关文件。
6. 对本次改动过的文本文件执行 UTF-8 with BOM 与 CRLF 检查；JSON、YAML 等现代配置文件按其惯例处理。
7. 运行与风险匹配的构建、测试或文档链接检查，并记录未能执行的验证。

纯问答、明确的只读审查或用户明确禁止写入时，不为记录会话而擅自修改仓库；最终回复中应说明本次未更新台账。其他项目任务必须更新本文档。

### 6.1 提交与部署工作流

- **标准 git 提交**：任务完成收尾时，按 `<type>(<scope>): <summary>` 生成标准提交说明（如 `docs(init): initialize project documentation`），仅 `git add` 本次需求相关文件（排除密钥/.env），随后确认后推送。
- **关键词触发部署**：GitGuard 为本地工具，无服务器部署；「发布/上线」等关键词触发「发行与分发」流程，见 [`14-部署文档.md`](./14-部署文档.md)。

## 7. 当前执行状态

<!-- agent-state:start -->

| 字段 | 当前值 |
| --- | --- |
| 当前任务 ID | `GGD-0013` |
| 当前状态 | `DONE` |
| 当前目标 | 实现 FR-050 绕过检测：post-commit 分发脚本 + 待提交 SHA 预测比对 + `BypassEvent` 审计 + `GITGUARD_SKIP=1` 识别 |
| 最后完成动作 | 完成实现、单元 / 集成 / CLI / E2E 测试与全套文档同步；准备本地提交 |
| 下一步动作 | 本地提交本次改动（不推送），等候用户指派下一任务 |
| 涉及文件 | `src/GitGuard.Core`、`src/GitGuard.Application`（`Bypass/`、`Hooks/`）、`src/GitGuard.Infrastructure`（`FileSystem/`、`Git/`、`Sqlite/`）、`src/GitGuard.Cli`（`Commands/`、`Reporting/`）、`tests/`、`docs/` |
| 验证结果 | `dotnet build GitGuard.sln` 0 警告 0 错误；`dotnet test GitGuard.sln -v minimal` 945 用例（943 通过 + 2 平台跳过 + 0 失败） |
| 阻塞/待确认 | 无 |
| 最后更新 | 2026-10-21 |

<!-- agent-state:end -->

## 8. 未完成任务

### 8.1 待办任务

| 完成 | ID | 优先级 | 状态 | 任务 | 来源与验收 |
| --- | --- | --- | --- | --- | --- |
| [ ] | `GGD-0014` | `P1` | `TODO` | CLI 国际化（NFR-U-002）：`--language` 选项 + `GITGUARD_LANG=en` 环境变量 + 中英文案资源表（GGD-0009 顺延；默认中文/英文可切换） | 来源：SRS NFR-U-002 / 模块设计/Cli.md §7 / 验收：语言切换单测 |
| [ ] | `GGD-0015` | `P2` | `TODO` | 审计事件补全与 Windows ACL 加固：`FailClosed` / `PolicyOverride` / `FilePermissionFixed` 事件（08-权限设计 §7）+ `%LOCALAPPDATA%\GitGuard` ACL 收紧与检测（08 §5.2）（GGD-0009 顺延） | 来源：08-权限设计 §5.2/§7 / 验收：事件落库测试 + Windows ACL 测试 |
| [ ] | `GGD-0016` | `P2` | `TODO` | 文件日志默认路径跨平台化：`LogFileSettings.Default.Path` 恒为 XDG 风格 `~/.local/share/gitguard/logs/gitguard.log`，Windows 下与 `UserDataPaths`（`%LOCALAPPDATA%\GitGuard`）不一致；`UserDataPaths.LogDirectory` 目前无消费方（GGD-0009 顺延；仅在显式 `--log-file` 或 `logging.file.enabled: true` 时生效，不影响审计库 / 崩溃日志位置） | 来源：09-日志设计 §7 / 验收：三平台默认路径单测 |
| [ ] | `GGD-0017` | `P2` | `TODO` | Git 版本兼容矩阵（2.9 / 2.40 / 最新）：GitHub 托管 runner 无法提供旧版 Git（且 E2E 夹具 `--initial-branch` 要求 ≥ 2.28），需自托管 runner 或容器化旧版 Git 方案（GGD-0011 顺延） | 来源：16-测试方案 §6 / 验收：目标 Git 版本下 E2E 通过 |
| [ ] | `GGD-0018` | `P2` | `TODO` | 启用 `dotnet format` 门禁并补齐测试报告归档：修复 86 处既有 IDE1006 命名规则冲突（src + tests），CI 增加 `dotnet format --verify-no-changes`，归档 trx + coverage（GGD-0011 顺延） | 来源：13-开发规范 §10.3 / §11 DoD、16-测试方案 §11 / 验收：format 零差异 + 报告归档 |
| [ ] | `GGD-0019` | `P0` | `TODO` | 消费配置 `exclude` 路径过滤（FR-070 部分实现补齐）：扫描链路按 glob 排除路径，并移除「已解析但未消费」告警（GGD-0012 顺延） | 来源：PRD FR-070 / SRS §2.12 / 验收：被排除路径不产生 Finding + 配置测试 |
| [ ] | `GGD-0020` | `P1` | `TODO` | 性能基准与规则级耗时：创建 benchmarks 项目 / 脚本测量 NFR-P-001~004；`--verbose` 输出各规则命中耗时（NFR-M-002）（GGD-0012 顺延） | 来源：SRS §5.1 / §5.6、17-开发RoadMap §2.1 退出条件 / 验收：基准报告 + 单测 |
| [ ] | `GGD-0021` | `P2` | `TODO` | Hook 框架真实兼容矩阵（NFR-A-001）：Husky / lefthook / pre-commit 真实安装夹具与 CI 覆盖（GGD-0012 顺延） | 来源：SRS NFR-A-001 / 16-测试方案 §2 / 验收：三框架共存用例通过 |
| [ ] | `GGD-0022` | `P2` | `TODO` | 审计查询与导出 CLI：`gitguard audit list` / `gitguard audit export --format json/csv`（脱敏、无原文）（GGD-0012 顺延） | 来源：SRS §4 / 14-部署文档 §10 / 验收：命令输出与脱敏测试 |

### 8.2 暂停/阻塞任务恢复详情

> 暂无暂停或阻塞中的任务。

## 9. 已办任务

| 完成 | ID | 任务 | 完成日期 | 验证结果 |
| --- | --- | --- | --- | --- |
| [x] | `GGD-INIT` | 项目文档体系初始化（CONTEXT.md、00-18 主干文档、7 份模块设计、4 份 ADR、INIT-GUIDE 完成记录） | 2026-10-15 | 所有文档写入成功；INIT-GUIDE §5 已登记；AGENT-GUIDE XX→GGD 全局替换 |
| [x] | `GGD-0001` | 创建 .NET 10 解决方案与项目骨架（sln + 6 个 src 项目 + 5 个 tests 项目 + Analyzer + editorconfig + CPM） | 2026-10-15 | `dotnet build` 0 警告 0 错误；`dotnet test` 5/5 通过；三类 Analyzer 违规探针实测拦截成功 |
| [x] | `GGD-0002` | 实现 `Core.Model` 聚合与 `Core.Policy` 值对象（Domain 守卫 / Fingerprint / SnippetMasked / Finding / ScanRun / SeverityPolicy / AllowlistEntry / AllowlistMatcher / GlobMatcher 等） | 2026-10-15 | `dotnet build GitGuard.sln` 0 警告 0 错误；`dotnet test GitGuard.sln` 157/157 通过（Core 新增 152 例）；DDD 同步至 0.1.1 |
| [x] | `GGD-0003` | 实现脱敏三件套：`Masker` / `Fingerprinter`（HMAC-SHA256 + 盐值注入）/ `SanitizingLogger`（TerminalSanitizer + PathAnonymizer）+ `ISecretRandom` 抽象 | 2026-10-15 | `dotnet build GitGuard.sln` 0 警告 0 错误；`dotnet test GitGuard.sln` 218/218 通过（Core 新增 61 例，含 TC-DR-02-01 金丝雀单测）；DDD 同步至 0.1.2 |
| [x] | `GGD-0004` | 实现 `IGitAccess` / `GitAccess`（Process 调用 git CLI）+ `GitException`（ErrorCode 契约，置于 Core）+ 真实临时仓库集成测试 | 2026-10-16 | `dotnet build GitGuard.sln` 0 警告 0 错误；`dotnet test GitGuard.sln` 265/265 通过（Core 新增 5 例、Infrastructure 新增 42 例）；SRS §2.8 / SAD §7.2 / 10-异常处理 0.1.1 同步 |
| [x] | `GGD-0005` | 实现 `SqliteAuditStore` + 迁移器 V001（WAL / 单事务 / BUSY 3 次退避；Core `SqliteException` + `IAuditStore` + `ScanRun`/`Finding` `Restore` 工厂；Unix 0600 + 符号链接拒绝）+ 真实临时文件集成测试 | 2026-10-16 | `dotnet build GitGuard.sln` 0 警告 0 错误；`dotnet test GitGuard.sln` 296 用例（295 通过 + 1 平台跳过：Windows 无符号链接创建特权）；AOT 探针（win-x64）无 trim/AOT 警告且原生运行通过；05-数据库设计 0.1.1 / Audit.md 0.1.1 / 10-异常处理 0.1.2 / ADR-0002 勾选同步 |
| [x] | `GGD-0006` | 实现四类 `IDetector`（Secret / Privacy / LocalPath / Url）+ 内置规则 `BuiltinRules.yaml`（21 条）+ YamlDotNet 静态上下文 trim-safe 加载器 + 声明式校验器（Shannon 熵 / Luhn / 身份证校验位）+ Core `SeverityDowngrade` | 2026-10-16 | `dotnet build GitGuard.sln` 0 警告 0 错误；`dotnet test GitGuard.sln` 584 用例（583 通过 + 1 平台跳过，0 失败；Detection 新增 289 例：TC-FR-021-* 六例 / Fixtures 22 文件 204 行用例 / ReDoS 对抗输入 + 超时 FailClosed / DR-02 金丝雀 / 加载器 22 例）；AOT 探针无 trim/AOT 警告；ADR-0002 勾选 + Secret/Privacy/LocalPathUrl/Policy 0.1.1 + DDD 0.1.3 + 10-异常处理 0.1.3 + 13-开发规范 0.1.1 同步 |
| [x] | `GGD-0007` | 实现 `ScanStagedUseCase` / `ScanPushUseCase`：unified-0 差异解析器 + `IClock`/`SystemClock` + `PolicyEvaluator` + 跳过语义（二进制 / 超限 / 无新增行）+ 扫描级超时 12 + `AuditWriteFailed` 降级 | 2026-10-16 | `dotnet test GitGuard.sln` 663 用例（662 通过 + 1 平台跳过 + 0 失败；Application 新增 63 / Core 11 / Infrastructure 5）；SRS 0.1.1 + 10-异常处理 0.1.4 + DDD 0.1.4 + Policy 0.1.2 + Audit 0.1.2 + Hook 0.1.1 + Secret 0.1.2 同步 |
| [x] | `GGD-0008` | 实现 Hook 分发器脚本生成 + `InstallHookUseCase` / `UninstallHookUseCase`（FR-010 / FR-011）：Core `HookType`/`HookInstallation`/`HookException`、嵌入模板渲染器、安装三阶段零半安装 + 并发锁、卸载还原 / 删除 / 未安装判定、审计 upsert + Hook 事件 | 2026-10-16 | `dotnet test GitGuard.sln` 764 用例（763 通过 + 1 平台跳过 + 0 失败；较基线 663 新增 101 例，含真实 git 提交触发分发脚本的 TC-DR-05-01 与退出码矩阵）；Hook 0.1.2 + SRS 0.1.2 + DDD 0.1.5 + Audit 0.1.3 + 10-异常处理 0.1.5 + 05-数据库 0.1.2 + 06-API 0.1.1 + ADR-0004 实施说明 + SAD §7.3 / 16-测试方案 / 14-部署文档 / 11-前端UI设计 同步 |
| [x] | `GGD-0010` | 完成 8 条 MVP 必过场景 E2E 测试 Fixture（16-测试方案 §12）：`tests/GitGuard.E2E.Tests` 新增 `AcceptanceHarness`（真实临时仓库 + 真实 bare 远端 + 隔离数据 / 配置目录 + 构建产物真实 `gitguard` 可执行文件 + 审计库直读）、`ProcessResult`、`MvpScenarioTests`（S1 暂存密钥阻断 commit 并落库 / S2 未暂存不误拦 / S3 推送区间含密钥阻断 push 且远端不前进 / S4 加入又删除仍阻断并追溯泄漏提交 / S5 公开 URL 与示例零 Finding / S6 旧 Hook 备份 `.local.bak` 链式执行 / S7a·S7b FailClosed 阻断；S8 占位，依赖 `GGD-0013`） | 2026-10-17 | `dotnet build GitGuard.sln` 0 警告 0 错误；`dotnet test GitGuard.sln` 867 用例（865 通过 + 2 跳过[既有平台跳过 + S8 占位] + 0 失败；E2E 新增 9 通过 + 1 占位，含真实 `git commit` / `git push` 阻断链）；16-测试方案 0.1.2 |
| [x] | `GGD-0009` | 实现 `gitguard` CLI 入口（System.CommandLine）+ 退出码表：`ExitCodes`/`ExitCodeMapper`（新增 7 用法错误、14 审计存储失败）、`CliHost` 缝合点、`CliApplication.RunAsync`（解析期 → 退出码 7 / 执行期统一映射）、5 个子命令（install / uninstall / status / scan / version）、全局选项（`--format` / `--no-color` / `--verbose` / `--trace` / `--log-file`）、`ReportWriter`（文本 → stderr / JSON → stdout，六种 Schema）、`CliLogger` + `LogFileSink`（JSON-lines 滚动）、`CrashLogWriter`、`GitGuardSession` 组合根（懒装配）、`GitGuardConfigLoader`（FR-070 / FR-071）、`StatusUseCase`（FR-012）、Core `ConfigurationException`、Infrastructure `UserDataPaths` / `FingerprintSaltStore` / `SystemSecretRandom` / `GitVersionProbe` | 2026-10-16 | `dotnet build GitGuard.sln` 0 警告 0 错误；`dotnet test GitGuard.sln` 857 用例（856 通过 + 1 平台跳过 + 0 失败；较基线 764 新增 93 例：Cli.Tests 新建 37 + Application 33 + Infrastructure 19 + Core 4）；06-API 0.1.2 + 10-异常处理 0.1.6 + Cli.md 0.1.1 + Policy.md 0.1.3 + SRS 0.1.3 + 16-测试方案 0.1.1 同步 |
| [x] | `GGD-0012` | 文档回填：对比实现与 PRD / SRS 差异——PRD 场景 / FR 实现状态、SRS 检测器契约 / 报告格式 / NFR / 追溯矩阵、RoadMap / 14-部署 / 16-测试方案同步；登记 `GGD-0019` ~ `GGD-0022` | 2026-10-18 | 与源码 / 测试逐项核对（21 条规则 8/5/4/4、`IAsyncEnumerable`、ReportWriter 实际文本 / JSON、status 五种状态、`--format json`、`exclude` 未消费、`audit` / `allowlist` / `scan --all` 未实现）；文档 BOM / CRLF 校验通过；无代码改动 |
| [x] | `GGD-0013` | 实现 FR-050 绕过检测：`PendingScan` 预测模型 + `IPendingScanStore` / `FilePendingScanStore`（`<git-dir>/gitguard/pending-scan.json` 原子写 / 0600 / 损坏即无标记）、`PendingScanRecorder` + `BypassDetectionService`、`post-commit.sh.template`、安装默认注册三类 Hook、隐藏命令 `hook post-commit` + 隐藏选项 `scan --staged --record-pending`、`GITGUARD_SKIP=1`（仅 Hook 模式）、`SaveBypassEvent`（Bypass 行 + `BypassDetected` 含 `Reason`）、`gitguard.bypass.v1` JSON | 2026-10-21 | `dotnet build GitGuard.sln` 0 警告 0 错误；`dotnet test GitGuard.sln -v minimal` 945 用例（943 通过 + 2 平台跳过 + 0 失败；较基线 867 新增 78，含 E2E S8）；E2E 真实 `git commit` 验证正常提交零 Bypass 行 / `--no-verify` → `NoVerify` / `GITGUARD_SKIP=1` → `GitGuardSkip` / 两者并用只记一次；PRD / SRS / DDD / 05-数据库 / 06-API / 08-权限 / 14-部署 / 16-测试方案 / 17-RoadMap / Hook.md / Cli.md / Audit.md 同步 |
| [x] | `GGD-0011` | GitHub Actions 多平台构建 + Native AOT 产物：`.github/workflows/ci.yml`（`ubuntu-24.04` / `windows-latest` / `macos-14` 三平台 Release 构建 + 单元/集成/契约/场景分阶段测试 + Linux `strace` DR-01 安全作业）、`.github/workflows/release.yml`（`v*` Tag / 手动触发：`win-x64` / `linux-x64` / `osx-arm64` / `osx-x64` Native AOT、Tag 版本注入、原生冒烟、`SHA256SUMS` + zip 产物）、README CI 徽章 | 2026-10-18 | 本地：Release 构建 0 警告 0 错误；六测试项目 CI 同款命令全绿（867 用例：865 通过 + 2 跳过）；win-x64 AOT 发布 + `version` 冒烟（`0.1.0-ci-test`）+ zip 内容校验（`gitguard.exe` / `e_sqlite3.dll` / `SHA256SUMS`）；两个 workflow YAML 解析通过；三平台 CI 绿灯与各 RID 产物待推送后在 GitHub Actions 确认；14-部署文档 0.1.1 / 16-测试方案 0.1.3 同步 |

## 10. 取消任务

| ID | 任务 | 取消日期 | 取消原因 |
| --- | --- | --- | --- |
| | | | |

## 11. 会话记录

- 2026-10-15 · `GGD-INIT` · 项目文档体系从零初始化完毕（18 份主干文档 + 7 份模块设计 + 4 份 ADR + CONTEXT/README/AGENT-GUIDE） · 文件写入校验：所有 Write 操作成功，无报错 · 下一步：执行 GGD-0001 创建 .NET 10 解决方案骨架
- 2026-10-15 · `GGD-0001` · 完成 .NET 10 解决方案骨架：`GitGuard.sln`（src 六项目 + tests 五项目）、CPM 中央包管理、StyleCop/Roslynator/BannedApi 分析器、`.editorconfig`/`stylecop.json`、层边界 MSBuild 守卫、DR-01 网络命名空间封禁 · 验证：`dotnet build` 0 警告 0 错误、`dotnet test` 5/5、三类违规探针（SA1600、RS0030、层引用越界）实测拦截成功 · 关键决策：新增仓库级 `NuGet.config` 固定 nuget.org 单包源（满足 CPM 包源映射、保证 CI 一致）；`global.json` 指定 MTP 测试运行器（.NET 10 下 `dotnet test` 运行 xUnit v3 需显式启用）与 SDK 基线 10.0.400；FluentAssertions 锁定 7.2.2（8.x 起为商业许可）；xUnit v3 替代 SDK 模板默认的 v2；ADR-0002 的 Sqlite/YamlDotNet AOT 验证顺延至首次引入包的任务（GGD-0005/0006） · 未完成项：benchmarks 项目（SAD 标注可选）留待性能测试任务 · 下一步：GGD-0002
- 2026-10-15 · `GGD-0001`（收尾） · 完成文档同步与格式统一：ADR-0001 勾选边界拦截落地、ADR-0002 两项 AOT 验证顺延至 GGD-0005/0006、SAD §5.1 结构树补充 `Directory.Build.targets`/`NuGet.config`/`global.json`；32 个改动文件统一 CRLF 与编码（.md/.cs/.sln/.txt 带 BOM，JSON/XML/editorconfig/.gitignore 无 BOM） · 验证：`dotnet build` 0 警告 0 错误、`dotnet test GitGuard.sln` 5/5 通过、文档 diff 均为内容变更无 EOL 噪音 · 环境陷阱：`dotnet test` 携带 `--nologo` 时 MTP 层误报「运行了零个测试」（exit 5，SDK 10.0.400 + xunit.v3 4.0.2），执行测试不要加该参数 · 下一步：GGD-0002
- 2026-10-15 · `GGD-0002` · 完成领域层实现：Domain（GitGuardException / InvariantViolationException / DomainGuard / RuleIdFormat）、Model（Severity / ScanType / ScanResult / Fingerprint / SnippetMasked / Finding / ScanRun）、Policy（PolicyAction / PolicyDecision / SeverityPolicy / AllowlistEntry / AllowlistMatcher / GlobMatcher）；聚合与值对象一律经工厂方法创建并强制不变式（DR-02 / DR-03 / FR-020 / FR-022 / FR-040） · 验证：`dotnet build GitGuard.sln` 0 警告 0 错误、`dotnet test GitGuard.sln` 157/157（Core 新增 152 例领域测试） · 关键决策：`.editorconfig` 新增抑制——SA1623/1629/1642（中文 XML 文档不适用）与 CA1707/SA1310（与 13-开发规范 §4.1 的 UPPER_SNAKE_CASE 常量约定冲突）；DDD 升 0.1.1 补全派生不变式（退出码一致性 / 规范化绝对路径 / UTC / Allowlist 过期 / PolicyAction / PolicyDecision） · 环境陷阱：StyleCop 1.1.118 不识别目标类型 `new` 与集合表达式（SA1000/SA1010）；IDE0007（类型明显时用 `var`）经 EnforceCodeStyleInBuild + TreatWarningsAsErrors 升级为构建错误；lambda 内丢弃 `new` 结果触发 CA1806，改用私有工厂方法包装 · 下一步：GGD-0003
- 2026-10-15 · `GGD-0003` · 完成脱敏组件：`Core.Abstractions/ISecretRandom`；`Core.Services` 新增 `Masker`（保留前后 2 字符 + 中间 `*`，短串按 `min(2, L/4)` 收缩以满足 SnippetMasked「≥50% 掩码」不变式）、`Fingerprinter`（HMAC-SHA256，盐 32 字节由调用方注入，`CreateSalt` 经 `ISecretRandom` 生成）、`TerminalSanitizer`（源生成正则去除 ANSI CSI 与控制字符，保留 `\n`/`\t`）、`PathAnonymizer`（SHA-256 截断 16 字符）、`SanitizingLogger`（三步流水线组合） · 验证：`dotnet build GitGuard.sln` 0 警告 0 错误、`dotnet test GitGuard.sln` 218/218（Core 新增 61 例；金丝雀 Token 全文不出现在掩码文本、指纹与组合日志行中） · 关键决策：DDD §6 `Fingerprinter` 依赖修正为 `ISecretRandom` + 盐值注入（`IClock` 非指纹算法所需，待 GGD-0007 使用方引入）；指纹算法采用 HMAC-SHA256（盐为密钥）而非朴素拼接；DDD 升 0.1.2 · 环境陷阱：StyleCop 1.1.118 不识别 C# 主构造函数（SA1009），测试替身改用普通构造函数；测试源码避免内嵌真实控制字符（ANSI 用例以 `(char)0x1B` 常量与 `\uXXXX` 转义文本构造） · 下一步：GGD-0004
- 2026-10-16 · `GGD-0004` · 完成 Git 访问层：`Core/GitException.cs`（`ErrorCode`/`ExitCode`/`StandardError`，供 Cli 映射退出码 2/10）、`Core/Abstractions/IGitAccess.cs`（8 方法）、`Infrastructure/Git/GitAccess.cs`（构造期 `git rev-parse --show-toplevel` 预检，非仓库 → `NOT_A_GIT_REPOSITORY`；子进程以 `ArgumentList` 免 shell、UTF-8、cwd 固定为工作区根、30s 超时后杀进程树且不重试、元数据 Lazy 单进程缓存） · 集成测试：`TempGitRepository` 真实仓库夹具（本地配置中和全局身份/签名/autocrlf/hooksPath）+ 42 例（仓库预检 20 / 暂存差异 6 / 提交枚举与差异 16），真实 git 命令路径全覆盖 · 验证：`dotnet build GitGuard.sln` 0 警告 0 错误、`dotnet test GitGuard.sln` 265/265 通过（Core 新增 5 例 GitException 单测） · 关键决策：差异命令统一携带 `--no-color --no-ext-diff --no-textconv` 中和用户 diff 配置；`diff-tree` 补 `--root`（初始提交按全量新增，`TC-FR-030` 场景）与 `--no-commit-id`（纯 unified 输出）并回写 SRS §2.8 / SAD §7.2；`GitException` 置于 Core 根命名空间（Cli 不得引用 Infrastructure），10-异常处理升 0.1.1；`.editorconfig` 对 `GitException.cs` 关闭 RCS1194（强制带码构造与标准异常构造器三元组冲突） · 环境陷阱：`null!` 传参与同名重载产生 CS0121 二义性，需显式 `(string)` 转换；lambda 内丢弃 `new` 触发 CA1806（沿用工厂方法包装） · 下一步：GGD-0005
- 2026-10-16 · `GGD-0005` · 完成审计存储层：`Core/SqliteException.cs`（根命名空间，与 `GitException` 同置位理由：Cli/Application 需 catch 且不得引用 Infrastructure）、`Core/Abstractions/IAuditStore.cs`（`SaveScanRun(run, decisions)` / `FindScanRun` / `ListRecentScanRuns`）、`Core/Model` 新增 `ScanRun.Restore` / `Finding.Restore`（读回原始 UUIDv7 身份，空 Id 拒绝；`Create` 委托 `Restore`）、`Infrastructure/Sqlite/SchemaMigrator`（内嵌 `VNNN_*.sql` 资源、按版本序、单脚本单事务、`schema_version` 登记、`IF NOT EXISTS` 崩溃重放安全）、`V001_initial_schema.sql`（6 表 + 6 索引）、`SqliteAuditStore`（WAL、ScanRun+Findings+ScanCompleted 事件单事务、BUSY 重试 50/100/200ms 耗尽 FailClosed、Unix 0600、符号链接拒绝、`Pooling=false` 随操作释放句柄） · 测试：31 例新增（Infrastructure 25 例真实临时库文件：迁移幂等/读写回环/身份与字段保留/action 落库/金丝雀不入库/排序过滤/BUSY 两路径/WAL 读不阻塞；Core 6 例 `Restore` 单测） · 验证：`dotnet build GitGuard.sln` 0 警告 0 错误、`dotnet test GitGuard.sln` 296 用例（295 通过 + 1 平台跳过：Windows 无符号链接创建特权）、AOT 探针 `dotnet publish -c Release -r win-x64 -p:PublishAot=true` 无 trim/AOT 警告且原生运行通过（产物自带 `e_sqlite3.dll`） · 关键决策：① 实测 `PRAGMA busy_timeout` 对 Microsoft.Data.Sqlite 无效——BUSY 等待改由连接串 `Default Timeout=1s`（驱动内部重试）+ 应用层退避循环实现，耗尽抛 `SqliteException`（原生错误码经 `InnerException` 携带）；② `findings.action` 落库取值 `Block/Warn/Allow/AllowedByAllowlist`（05-数据库设计 §4.2 补 `Allow`）；③ `detection_rules` 列结构未定义，V001 不建表（§4.7 加实现说明）；④ Windows ACL 加固延后至 CLI 路径解析任务，本期 Unix 0600 + symlink 拒绝；⑤ ADR-0002 Sqlite AOT 待办勾选 · 环境陷阱：`GitGuard.Core.SqliteException` 与 `Microsoft.Data.Sqlite.SqliteException` 同名需 using 别名（CS0104）；StyleCop 1.1.118 对范围运算符 `)..` 与空包容 `)!` 误报 SA1009，改用 `Substring` / `Convert.ToInt64`；CA1416 平台守卫必须置于含 `File.SetUnixFileMode` 调用的方法内（不做跨方法分析）；测试中 CA1861 常量数组参数改 `static readonly` 字段、CA1806 沿用私有工厂方法包装、xUnit1051 动态等待传 `TestContext.Current.CancellationToken`；Windows AOT 链接阶段需 `vswhere.exe` 可见（VS Installer 目录加入 PATH） · 下一步：GGD-0006
- 2026-10-16 · `GGD-0006` · 完成检测层：Core 契约（`Model/FileChange`·`ChangedLine`、`Rules/DetectionRule`·`RulePattern`·`RuleSource`、`DetectionException`（根命名空间，`DETECTOR_FAILED` → exit 11）、`Abstractions/IDetector`（`IAsyncEnumerable<Finding>`，ADR-0003））+ Core `SeverityDowngrade`（Step / StepOrFloor）；`BuiltinRules.yaml` 21 条（Secret 8 / Privacy 5 / LocalPath 4 / Url 4）+ `BuiltinRuleLoader`（`[YamlStaticContext]` 静态反序列化；版本/必填/严重度/正则可编译/ID 重复/`GGD/` 前缀校验；`EmbeddedResource` 单文件发行）+ `RegexRuleDetector` 统一管线（按类别选规则 → 文件 glob 白名单 → 行级跳过 → 上下文关键字（大小写不敏感 Contains）→ 正则（`Compiled｜CultureInvariant` + 200ms 超时 FailClosed）→ 声明式校验器 → 严重度调整 → `FindingFactory`；同一行同一规则至多一条）+ 四检测器（Secret：`.lock`/`.min.js`/`.min.css` 整文件跳过、>2048 行仅高置信度 4 规则、AWS-SECRET 熵 > 4.0；Privacy：`require_context` 可配置关闭、测试文件路径降一级（StepOrFloor）、Luhn/GB 11643 校验器；LocalPath：文档 glob（`.gitignore`/`README.md`/`docs/**`）降一级且 Low 丢弃；Url：示例文件名前缀（example/sample/demo）降一级） · 测试 289 例：TC-FR-021-* 六例（含 U-01 脱敏 `us*****ss` 断言）、Fixtures 22 文件 204 行用例（每规则 ≥3 正/≥3 负 + edge hit/miss；覆盖守卫 `Fixtures_ShouldCoverEveryBuiltinRuleWithMinimumSamples` 强制 21 条规则全覆盖）、ReDoS 14 组对抗输入 × 21 规则 + `^(a+)+$` 实测超时转 `DetectionException(DETECTOR_FAILED)`、DR-02 金丝雀（Finding 字段零明文 + 同盐指纹跨文件稳定）、加载器 22 例、校验器（Shannon 熵 / Luhn / 身份证含日期分支与大小写 x） · 验证：`dotnet build GitGuard.sln` 0 警告 0 错误、`dotnet test GitGuard.sln` 584 用例（583 通过 + 1 平台跳过）、AOT 探针（YamlDotNet 18.1.0 + Vecc 静态生成器）win-x64 无 trim/AOT 警告且原生加载 21 条规则通过 · 关键决策：① 规则按行匹配、不启用 Multiline（`^` 即行文本首，支撑 WIN-DRIVE 行首锚定）；② 敏感值取第 1 捕获组、无捕获组取整段匹配（脱敏与指纹对象）；③ Regex 超时按 §2/§6 收敛为立即抛出 `DetectionException`（10-异常处理 0.1.3 修正原「禁用该规则继续扫描」表述）；④ 未知校验器引用在检测器构造期拒绝（FailClosed，支持集按检测器类别划分）；⑤ `enabled: false` 在基类选规则阶段剔除；⑥ 二进制文件过滤属上游 FR-020、Markdown 三反引号感知与公开 URL 主机白名单记 P2（LocalPathUrl 0.1.1） · 环境陷阱：SA1313（位置 record 参数须小写——测试 DTO 与用例类型改普通类 + PascalCase 属性）；SA1512（节标题注释后禁空行）；SA1025（行内注释禁对齐双空格）；IDE0007（工厂调用 `DetectionRule.Create` / `RulePattern.Create` 须用 `var`）；CA1861（数组字面量实参提 `static readonly` 字段）；YAML 单引号标量内 `''` 转义双引号（GENERIC-PASSWORD 引号语义） · 下一步：GGD-0007
- 2026-10-16 · `GGD-0007` · 完成扫描用例层：Application 新增 `Scanning/` 10 文件（`UnifiedDiffParser` 行号游标按 `-U0` 展开逐文件 `FileChange`、C 风格引用路径解码、`+++` 行尾 TAB 修剪、`\ No newline` 容错；`LineReader`；`ParsedDiffFile`；`PushUpdate`（`<local> <remote>` 解析，零 remote-sha → 范围 = local-sha，零 local-sha → 跳过）；`ScanSettings`（1 MiB / 30s 默认）；`ScanOutcome`；`SkippedFile`；`ScanExecutor`（取差异 → 分拣（二进制 / 超限 / 无新增行）→ 流式检测 → `PolicyEvaluator` 判定 → 聚合 → 单事务落库）；两个 UseCase）；Core 新增 `Abstractions/IClock` + `Policy/PolicyEvaluator`（白名单优先 → `Allow + IgnoredByAllowlist`，否则严重度映射）；Infrastructure 新增 `Time/SystemClock`；`GitAccess` 差异命令补 `-c core.quotepath=false`（非 ASCII 路径输出原文 UTF-8） · 测试新增 79 例（Application 63：`UnifiedDiffParserTests` 19 / `ScanStagedUseCaseTests` 15 / `ScanPushUseCaseTests` 9 / `PushUpdateTests` 10 / `ScanEndToEndTests` 10（真实 git + 真实 SQLite，含 TC-FR-020-01~05、TC-FR-030-01~02、FailClosed 10·11·12、DR-02 金丝雀、DR-04）；Core 11：`PolicyEvaluatorTests`；Infrastructure 5：`GitAccessQuotePathTests` 3 + `SystemClockTests` 2） · 验证：`dotnet test GitGuard.sln` 663 用例（662 通过 + 1 平台跳过 + 0 失败） · 关键决策：① `.gitguard.yml` 加载（FR-070）随 GGD-0009 CLI 引入，本期 UseCase 以 `ScanSettings` 默认值运行；② 扫描级超时 → `Failed(12)` 结果而非抛出（外部取消原样重抛）、`GitException` → 10、`DetectionException` → 11、审计 `SqliteException` → `AuditWriteFailed` 降级标记；③ push stdin 畸形 → `GitException(GIT_COMMAND_FAILED)` → 10 且不回显输入（DR-02），空 stdin → 无推送目标 → Clean/0（否决 Hook.md 原「扫描所有未推送提交」降级并回写文档）；④ 大小信号 = 差异新增内容 UTF-8 字节数（DR-04 禁读工作区，不取真实 blob 大小） · 环境陷阱：git 对含空格路径在 `+++` 行尾追加 TAB（解析器按该契约修剪）；`ImmutableDictionary` 枚举顺序随随机 UUIDv7 键哈希变化——勿断言顺序、只断言成员；`AllowlistEntry` 过期用例数据须满足 `ExpiresAt > CreatedAt` 不变式；测试代码同样受 IDE0007（`var`）/ CA1806 / SA1204 约束 · 文档同步 7 份：SRS 0.1.1 / 10-异常处理 0.1.4 / DDD 0.1.4 / Policy 0.1.2 / Audit 0.1.2 / Hook 0.1.1 / Secret 0.1.2 · 下一步：GGD-0008
- 2026-10-16 · `GGD-0008` · 完成 Hook 分发器模块：Core 新增 `Model/HookType.cs`（+`HookTypeExtensions`：`ToHookName`/`ToBackupFileName`/`TryParseHookName`/`BACKUP_FILE_SUFFIX`）、`Model/HookInstallation.cs`（复合身份 `(RepoPath, HookType)`、`Create`/`Restore`/`MarkUninstalled`、不变式：绝对路径 / UTC / 纯文件名 `.local.bak` 备份）、`Model/HookInstallationStatus.cs`、`HookException.cs`（根命名空间，`WRITE_PERMISSION_DENIED`→3 / `UNKNOWN_HOOK_PRESENT`→4 / `INSTALL_IN_PROGRESS`→50，置位理由同 `GitException`）；`IAuditStore` + `SqliteAuditStore` 扩展（`SaveHookInstallation` upsert+`HookInstalled` 事件单事务、`occurred_at=InstalledAt`；`SaveHookUninstallation(installation, restoredLegacyHook)` 保留安装字段翻转 `Uninstalled`+`HookUninstalled` 事件取当前时刻）；Application 新增 `Hooks/` 10 文件（2 份嵌入模板 + `HookScriptBuilder`（占位符渲染、LF+无 BOM 归一、Windows 反斜杠转正斜杠）+ `HookFileOperations`（8 KiB 头读一次完成托管标记与二进制判定；备份为复制且非 Windows 显式对齐 Unix 权限位；写入 0755）+ `InstallLock`（`OpenOrCreate`+`FileShare.None`）+ 两 UseCase（预检全部 → 渲染全部 → 写入全部 三阶段；文本 Hook 静默复制备份包裹 / 二进制与符号链接需 `--force` / 遗留备份冲突不可接管） · 测试新增 101 例：Core `HookModelTests`+`HookExceptionTests`、Infrastructure `SqliteAuditStoreHookTests`（12 例直读落库校验）、Application `HookScriptBuilderTests` + `InstallHookUseCaseTests`（12）+ `UninstallHookUseCaseTests`（11）+ `HookEndToEndTests`（真实 git + 真实 SQLite + 真实 POSIX sh：安装卸载端到端、`git commit` 触发守卫先行与原 Hook 链式执行、阻断语义、退出码矩阵、可执行文件缺失 exit 10） · 验证：`dotnet test GitGuard.sln` 764 用例（763 通过 + 1 平台跳过 + 0 失败） · 关键决策：① 分发脚本不 `exec`（改为直接调用并合并退出码：GitGuard 非零优先，否则沿用原 Hook），ADR-0004 备注记录实施说明；② 仅 POSIX sh 形态（撤销 PowerShell 变体），脚本内消息英文（POSIX 区域）；③ 卸载 `--force` 语义 = 仅当备份仍存在时接管被手工替换的非托管目标 · 环境陷阱：MSBuild 将模板文件名 `.sh` 段识别为区域代码（Serbo-Croatian），即使显式 `LogicalName` 也会剥离资源名并改派附属程序集 → `<EmbeddedResource>` 必须 `WithCulture="false"`；`File.Copy` 不保留 Unix 可执行位（`[ -x ]` 门控失效）；CS8602（`Equals(null)` 后流分析）、CS0104（`SqliteException` 与 `Microsoft.Data.Sqlite` 同名需别名）、SA1201/1203（常量区置顶）、SA1009（`!` 闭括号）、SA1118 / IDE0007 同前序任务 · 文档同步 12 份：Hook 0.1.2 / SRS 0.1.2 / DDD 0.1.5 / Audit 0.1.3 / 10-异常处理 0.1.5 / 05-数据库 0.1.2 / 06-API 0.1.1 / ADR-0004（清单勾选 + 实施说明）/ SAD §7.3 / 16-测试方案 §4 / 14-部署文档 §5 / 11-前端UI设计 §3.2 · 下一步：GGD-0009
- 2026-10-16 · `GGD-0009` · 完成 CLI 入口与退出码表：Core 新增 `ConfigurationException`（根命名空间，`CONFIG_INVALID` → 13，携带 `SourcePath` / `Line`）；Infrastructure 新增 `FileSystem/UserDataPaths`（三平台数据目录 / 配置目录派生，显式注入优先）、`FileSystem/FingerprintSaltStore`（32 字节盐：首次生成 / 复用 / 损坏再生 / 落盘失败降级为内存临时盐 / Unix 0600）、`Security/SystemSecretRandom`（CSPRNG）、`Git/GitVersionProbe`（探测失败不抛，经结果对象回传）；Application 新增 `Configuration/GitGuardConfigLoader`（FR-070 / FR-071：用户级 `~/.config/gitguard/config.yml` + 项目级 `.gitguard.yml`，map 递归合并、列表整体替换，`version: 1` 必填，`policy.critical: allow` 硬下限，取值越界与 `use_gitleaks: true` FailClosed → 13）、`Composition/GitGuardSession`（组合根：Cli 不引用 Infrastructure；`status` / `version` 等只读命令不打开审计库、不生成指纹盐）、`Hooks/StatusUseCase` + `StatusReport`（FR-012：Hook 签名分类 + `NeedsAttention` → 6 + 配置加载链回显）、`Scanning/ScanPolicy`；Cli 新增 `ExitCodes`（唯一权威常量源，新增 7 用法错误 / 14 审计存储失败）、`ExitCodeMapper`（异常类型 → 退出码全表）、`CliHost`（WorkingDirectory / 三标准流 / DataDirectory / UserConfigDirectory / 版本 / 可执行文件路径 / TTY 与 NO_COLOR）、`CliApplication.RunAsync`（解析期失败 → 7 且不触碰仓库与配置；执行期 `InvocationConfiguration.EnableDefaultExceptionHandler = false`）、`Commands/`（CommandTree + install / uninstall / status / scan / version + GlobalOptions + UsageError + CliRuntime + HookNames）、`Reporting/`（ReportWriter：文本 → stderr、JSON → stdout，六种 Schema 手写 `Utf8JsonWriter`；VersionInfo）、`Logging/`（CliLogger + LogFileSink JSON-lines 滚动）、`CrashLogWriter`、`JsonText`、`MessageSanitizer` · 测试新增 93 例（Cli.Tests 新建 37：退出码映射 10 + 用法错误 12 + version 4 + scan 5 + Hook 生命周期 11 + 日志 8 + 路径展开 2；Application 33：配置加载 20 + 组合根 7 + status 6；Infrastructure 19：盐存储 7 + git 探针 4 + 随机源 4 + 数据目录 4；Core 4：ConfigurationException） · 验证：`dotnet build GitGuard.sln` 0 警告 0 错误；`dotnet test GitGuard.sln` 857 用例（856 通过 + 1 平台跳过 + 0 失败） · 关键决策：① System.CommandLine 2.0.x 解析失败实测后由本层归一为退出码 7（06-API 原记载「框架默认 2」修正），并在派发前返回以保证用法错误不落盘；② 新增退出码 7（用法错误）与 14（审计存储失败），扫描内落库失败仍走 `audit_write_failed` 降级、退出码不变；③ 配置宽松解析落地为「反序列化 `IgnoreUnmatchedProperties()` + YAML 语法树比对已知键产生警告」——静态上下文默认抛 `Property 'x' not found`，不显式放行则宽松解析契约无法兑现（本次由新增用例暴露并修复）；`use_gitleaks: true` 仍 FailClosed（无法兑现的开关静默忽略即 FailOpen）；④ JSON 手写 `Utf8JsonWriter` + `JavaScriptEncoder.Create(UnicodeRanges.All)`：中文原字符输出、保留 HTML 敏感字符转义（AOT 安全、不依赖反射序列化）；⑤ 输出分流固定为「文本 → stderr / JSON → stdout / 日志恒 stderr」，`--format json` 时 stdout 保持干净可解析 · 环境陷阱：FluentAssertions 7.2.2 无 `NotOnlyContain`（改 `Contain(predicate)`）；YamlDotNet 静态上下文需显式 `IgnoreUnmatchedProperties()` 才不抛未知属性；raw string literal 直接作实参必触发 SA1118（提为 `const string` 局部变量）；测试代码同样受 SA1202（public 成员先于 internal）/ IDE0007（`var`）/ IDE0008（显式类型）/ SA1009（`!` 闭括号）约束；`ExpandUserPath("~/x")` 保留 `/` 分隔符（Windows 下混合分隔符，断言需 `Path.GetFullPath` 归一）；`dotnet test` 不得带 `--nologo`（MTP 误报零测试，既有陷阱） · 文档同步 6 份：06-API 0.1.2 / 10-异常处理 0.1.6 / Cli.md 0.1.1 / Policy.md 0.1.3 / SRS 0.1.3 / 16-测试方案 0.1.1；顺延登记 `GGD-0014`（i18n）/ `GGD-0015`（ACL 与审计事件）/ `GGD-0016`（文件日志默认路径跨平台化） · 下一步：GGD-0010
- 2026-10-17 · `GGD-0010`（收尾） · 修复夹具跳过路径副作用：`EnsureHermeticUserConfig` 前移至 `AcceptanceHarness` 构造函数首行——`Assert.Skip` 抛异常时 `Dispose` 不运行，原顺序（先建目录树再检查）在强制跳过核验中于临时根残留 9 个半成品空目录；调整后跳过路径零落盘 · 验证：`dotnet build GitGuard.sln` 0 警告 0 错误、`dotnet test GitGuard.sln` 867 用例（865 通过 + 2 跳过 + 0 失败）；残留临时目录已清理 · 提交 `2801289` · 下一步：待用户指派
- 2026-10-17 · `GGD-0010` · 完成 MVP 必过场景 E2E 测试（16-测试方案 §12）：新增 `tests/GitGuard.E2E.Tests/AcceptanceHarness.cs`（每实例一个隔离工作区：真实 `git init --initial-branch=master` 仓库 + 真实 bare 远端 + 构建产物 apphost `gitguard.exe` 作为 Hook 固化目标 + 审计库直读；子进程数据目录经环境变量重定向——Windows `LOCALAPPDATA` / Unix `XDG_DATA_HOME` / macOS `HOME`，配置目录 Unix 经 `HOME` 完全隔离）、`ProcessResult.cs`、`MvpScenarioTests.cs`（S1 暂存密钥阻断 commit：exit≠0 + `exit_code=1` + 提交数不变 + scan_runs Blocked/action=Block + 金丝雀不入审计落盘；S2 未暂存不误拦且落库 Clean；S3 推送区间含密钥阻断 push 且远端 SHA 不前进；S4 加入又删除仍阻断、findings 精确指向泄漏提交；S5 公开 URL 与示例代码零 Finding；S6 旧 Hook 备份 `.local.bak` 并链式执行；S7a 分发脚本 `[ ! -x ]` → exit 10 阻断、S7b 替身 11/12 退出码透传阻断；S8 `Assert.Skip` 占位） · 验证：`dotnet build GitGuard.sln` 0 警告 0 错误；`dotnet test GitGuard.sln` 867 用例（865 通过 + 2 跳过 + 0 失败；较基线 857 新增 10：E2E 9 通过 + S8 占位）；E2E 单项目 11 用例 24s · 关键决策：① Hook 链无 mock——真实 `git commit` / `git push` → MSYS sh 分发脚本 → 真实 gitguard 子进程 → 检测 → 退出码传播，分发脚本固化测试输出目录中的真实 CLI apphost（项目引用自动复制）；② 子进程隔离依赖环境变量重定向；Windows 主目录由 shell API 解析不可重定向（探针实证），以「真实 `~/.config/gitguard/config.yml` 存在则整套 `Assert.Skip`」保诚实（实测构造函数内 Skip 被 xUnit 正确报告为跳过，非失败）；③ S7 分发链与扫描器侧分工：E2E 验证退出码透传，扫描器自身 10/11/12 由 TC-DR-03 覆盖；④ 替身 / 遗留 Hook 必须无扩展名（MSYS `[ -x ]` 以 shebang 为判据，`.cmd` 不可执行）；⑤ `Microsoft.Data.Sqlite` 经项目引用传递即可直读审计库，未新增包引用 · 环境陷阱：SA1118 多行实参须提局部变量；CA1822 无实例数据的方法改 static；CA1861 常量数组提 `static readonly`；`WritePosixScript` 非 Windows 需 chmod 0755（`File.Copy` 不保留可执行位同源陷阱）；`dotnet test` 不得带 `--nologo`（既有陷阱） · 文档同步：16-测试方案 §12 勾选（S1~S7）+ 0.1.2 · 下一步：待用户指派（`GGD-0011` CI 多平台构建 + AOT / `GGD-0012` 文档回填；S8 回填依赖 `GGD-0013`）
- 2026-10-18 · `GGD-0011` · 完成 GitHub Actions 多平台构建与 Native AOT 发行链路：新增 `.github/workflows/ci.yml`（PR / push `main`/`master` 触发；`ubuntu-24.04` / `windows-latest` / `macos-14` 矩阵、`fail-fast: false`；restore → Release 构建 → Core/Detection 单元 → Infrastructure/Application 集成 → Cli 契约 → E2E 场景；独立 Linux 安全作业在 `strace -e trace=network` 下真实扫描金丝雀并断言无 `AF_INET`/`AF_INET6`，TC-DR-01-01）、`.github/workflows/release.yml`（`v*` Tag / workflow_dispatch；`win-x64` / `linux-x64` / `osx-arm64` 原生 + `osx-x64` 于 arm64 交叉编译；Tag 去 `v` 注入 `Version`；原生平台 `version` 冒烟；删除 pdb/dbg/xml 后生成 `SHA256SUMS` 并 zip；`upload-artifact@v4` 保留 30 天）、README CI 徽章 · 验证：`dotnet build GitGuard.sln -c Release` 0 警告 0 错误；六测试项目 CI 同款 `--no-build` 命令全绿（867 用例：865 通过 + 2 跳过 + 0 失败）；win-x64 AOT 发布 + `version` 冒烟（`gitguard 0.1.0-ci-test`）+ Compress-Archive 产物内容校验（`gitguard.exe` / `e_sqlite3.dll` / `SHA256SUMS`）；两个 YAML 经解析器验证 · 关键决策：① 发行 zip 在 runner 本地生成后上传——GitHub Actions artifact 服务不保留 Unix 可执行位，zip 内部保留 mode；② `osx-x64` 在 `macos-14`（arm64）交叉编译并标记 `native: false` 跳过冒烟；③ CI 暂不包含 `dotnet format`——实测 86 处既有 IDE1006 命名规则冲突，顺延 `GGD-0018`；④ Git 2.9/2.40 矩阵托管 runner 不可得（且 E2E 夹具要求 Git ≥ 2.28），顺延 `GGD-0017`；⑤ 工作流仅申请 `contents: read`，安全作业不引入第三方 action · 环境陷阱：Git Bash 无 `zip`（Windows 走 `Compress-Archive`，Unix 用 runner 自带 `zip`）；macOS 无 `sha256sum` 时回退 `shasum -a 256`；Native AOT Windows 链接需 `vswhere.exe` 在 PATH（本机探针需追加 VS Installer 目录）；`dotnet test` 不得带 `--nologo`（既有陷阱） · 文档同步：14-部署文档 0.1.1 / 16-测试方案 0.1.3 / README · 下一步：待用户指派（`GGD-0012` 文档回填、`GGD-0013` S8；顺延 `GGD-0017`/`GGD-0018`）
- 2026-10-18 · `GGD-0012` · 完成文档回填：逐条核对 PRD / SRS 与实现、测试追溯并将差异写入文档——PRD §3.1 状态输出改为 `Missing` / `Managed` / `Legacy` / `Binary` / `SymbolicLink`（未托管退出码 6）、`--force` 仅接管二进制 / 符号链接、普通文本 Hook 自动备份包裹；§3.2 报告字段改为实际字段（无逐规则建议文案）；§3.3 新分支降级改为 `remote-sha` 全零 → `local-sha` 可达提交；§3.4~§3.6 增加 v0.1 落地状态；§4 新增「v0.1 实现状态」列（FR-010~032 已实现、FR-040~042 仅配置文件级、FR-050 顺延、FR-060/061/080/090 未实现、FR-070 `exclude` 未消费）；§5 `--json` 更正为 `--format json`、平台矩阵对齐 NFR-A-002；§6 勾选 S1~S7 / DR-02 并登记未过项；§7 v0.1 范围与 RoadMap 对齐 · SRS §2.3 status 输出、§2.4 / §2.8 用例编号、§2.5 `IAsyncEnumerable` + 21 条规则（8/5/4/4）+ 装配顺序、§2.7 报告样例改为 v0.1 实际输出（文本 → stderr / JSON → stdout）、§2.9~§2.13 标注未实现项、§2.12 补 `exclude` / 扫描类型开关 / `fail_closed` 仅告警、§4 `audit export` 未实现、§5 全部 NFR 表新增状态列、§8 追溯矩阵补齐 FR-031/032/041/042/061/080/100 + 实现状态列 · RoadMap §1/§2.1 范围与规则数校正（「各 ≥ 10 条」未达成，实际 21 条）、§6 R-01/R-02 标注缓解 · 14-部署 §2 环境变量区分已实现 / 顺延、§4/§10 命令更正为 `gitguard version`、冒烟清单改为 `scan --staged` + 直接查 `audit.db` · 16-测试方案 §4 标注未落地用例 · 06-API §4.7 / §6 标注 `audit list` 与退出码 20/21/30/31/40 为 v0.2 预留；09-日志 §6 与 模块设计/Audit §2.2 标注审计 CLI 未实现；15-Docker 版本命令更正为 `gitguard version`、金丝雀改为可命中的 `AKIAIOSFODNN7EXAMPLE` · 关键决策：文档只陈述已验证事实，规划项保留原 FR 但明确标注未实现 / 顺延，不删除需求；新登记 `GGD-0019`（exclude 消费，P0）、`GGD-0020`（性能基准 + 规则级耗时，P1）、`GGD-0021`（Hook 框架兼容矩阵，P2）、`GGD-0022`（审计 CLI，P2） · 验证：对照 `BuiltinRules.yaml` / `DetectorAssembly` / `ReportWriter` / `StatusReport` / `GlobalOptions` / `ScanCommand` / `CommandTree` / `GitGuardConfigLoader` 与测试类实测；改动的 10 份 `.md` 均 UTF-8 with BOM + CRLF；无代码改动，未运行构建 / 测试（风险匹配） · 下一步：待用户指派（优先 `GGD-0019` / `GGD-0013` / `GGD-0020`）
- 2026-10-21 · `GGD-0013` · 完成 FR-050 绕过检测：Core 新增 `PendingScan`（`ParentSha?` / `TreeSha` / `Skipped` / `RecordedAt`，SHA 与 UTC 守卫）+ `BypassReason`（`NoVerify` / `GitGuardSkip`）+ `IPendingScanStore`；Infrastructure 新增 `FilePendingScanStore`（`<git-dir>/gitguard/pending-scan.json`，临时文件 + 原子改名、UTF-8 无 BOM、Unix 目录 0700 / 文件 0600、损坏即删除并按无标记处理）与 `GitAccess` 新方法（`GetGitDirectory` / `TryGetHeadCommitSha` / `GetStagedTreeSha` / `GetCommitMetadata`）；`SqliteAuditStore.SaveBypassEvent`（Bypass / Clean / 退出码 0 / duration NULL / 无 Finding + `BypassDetected` 载荷含 `Reason`，单事务）；Application 新增 `Bypass/PendingScanRecorder` 与 `BypassDetectionService`、`post-commit.sh.template`，`InstallHookUseCase` 默认注册 pre-commit + pre-push + post-commit；Cli 新增隐藏命令 `hook post-commit`、隐藏选项 `scan --staged --record-pending`、`GITGUARD_SKIP=1`（仅 Hook 模式）、`gitguard.bypass.v1` JSON 与中文文本警告 · 测试新增 78 例：Application（`BypassDetectionServiceTests` 覆盖匹配 / skipped / 无标记 / 树或父提交不匹配 / 根提交 / 合并提交 / 读失败 / 清理失败 / 审计失败不清理标记；`PendingScanRecorderTests`）、Infrastructure（`FilePendingScanStoreTests` 含损坏矩阵 / 原子写无残留 / 0600；`GitAccessBypassTests` 含 linked worktree / root / merge；`SqliteAuditStoreBypassTests` 直读载荷与非法输入）、Cli（`CliBypassTests` 覆盖标记写入 / 阻断清理 / 手动扫描忽略环境变量 / 三种 post-commit 输出）、E2E S8（正常提交 / `--no-verify` / `GITGUARD_SKIP=1` / 两者并用） · 验证：`dotnet build GitGuard.sln` 0 警告 0 错误；`dotnet test GitGuard.sln -v minimal` 945 用例（943 通过 + 2 平台跳过 + 0 失败） · 关键决策：① 提交 SHA 在 pre-commit 不可预测，预测身份改为「父提交 + `git write-tree` 暂存树」，post-commit 比对实际提交树与首个父提交（合并提交只看第一父）；② `GITGUARD_SKIP=1` 仅 Hook 模式识别，手动扫描不受影响；③ 无标记优先于 skipped，`--no-verify` + `GITGUARD_SKIP=1` 只登记一次 `NoVerify`；④ 标记损坏 / 读取失败按无标记处理（宁可多记不可漏记）；⑤ post-commit 命令恒返回 0（Git 忽略其退出码），审计失败仍按 FailClosed 映射 14；⑥ E2E 暴露真实缺陷——`pre-commit.sh.template` 原调用缺少 `--record-pending`，导致正常提交被误判 `NoVerify`，已修复并强化 `HookScriptBuilderTests` / `HookEndToEndTests` 断言；⑦ `hooks.*` 配置段尚未接线，安装器默认注册三类 Hook · 环境陷阱：SA1203（常量须置于实例字段前）、SA1009（`!` 闭括号，改 `?? throw`）、CA1416（`File.GetUnixFileMode` 前的平台守卫需同一方法内 `return`）、IDE0007（`var`）、xUnit `Assert.Skip` 后必须 `return` 才能满足分析器流分析 · 文档同步 12 份：PRD 0.1.2 / SRS 0.1.5 / DDD 0.1.6 / 05-数据库 0.1.3 / 06-API 0.1.4 / 08-权限 0.1.1 / 14-部署 0.1.3 / 16-测试方案 0.1.5 / 17-RoadMap 0.1.2 / Hook.md 0.1.3 / Cli.md 0.1.2 / Audit.md 0.1.5 · 下一步：本地提交（不推送），待用户指派（优先 `GGD-0019` / `GGD-0020`）
