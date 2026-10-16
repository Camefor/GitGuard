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
| 当前任务 ID | `GGD-0007` |
| 当前状态 | `DONE` |
| 当前目标 | 实现 `ScanStagedUseCase` / `ScanPushUseCase`（FR-020 / FR-030）：unified-0 差异解析器（Application 层）、`IClock` 抽象 + `SystemClock`、`PolicyEvaluator` 严重度→动作判定、文件跳过语义（> `scan.max_file_bytes` / 二进制 → skipped）、扫描级超时（exit 12）、ScanRun 单事务落库 + `AuditWriteFailed` 降级；来源 SRS §2.4-2.8，验收：`TC-FR-020-01~10` / `TC-FR-030-01~04` / `TC-DR-03-01~04` / `TC-DR-04-01` |
| 最后完成动作 | 全部实现与验证完成：Application 层新增 `Scanning/` 10 文件（`UnifiedDiffParser`·`LineReader`·`ParsedDiffFile`·`PushUpdate`·`ScanSettings`·`ScanOutcome`·`SkippedFile`·`ScanExecutor`·两个 UseCase），Core 新增 `Abstractions/IClock` + `Policy/PolicyEvaluator`，Infrastructure 新增 `Time/SystemClock`，`GitAccess` 差异命令补 `-c core.quotepath=false`；测试新增 79 例；文档同步 7 份 |
| 下一步动作 | `GGD-0008`：实现 Hook 分发器脚本生成 + Install/Uninstall UseCase（来源：模块设计/Hook.md；验收：TC-FR-010-01~05） |
| 涉及文件 | `src/GitGuard.Application/Scanning/*`（10 新增）、`src/GitGuard.Core/Abstractions/IClock.cs`、`src/GitGuard.Core/Policy/PolicyEvaluator.cs`、`src/GitGuard.Infrastructure/Time/SystemClock.cs`、`src/GitGuard.Infrastructure/Git/GitAccess.cs`、`tests/GitGuard.Application.Tests/*`（9 新增）、`tests/GitGuard.Core.Tests/PolicyEvaluatorTests.cs`、`tests/GitGuard.Infrastructure.Tests/{GitAccessQuotePathTests,SystemClockTests}.cs`、`docs/` 7 份同步 |
| 验证结果 | `dotnet test GitGuard.sln`：663 用例（662 通过 + 1 平台跳过 + 0 失败）；Application 新增 63 例（E2E 真实 git + 真实 SQLite：TC-FR-020-01~05 / TC-FR-030-01~02 / FailClosed 10·11·12 / DR-02 金丝雀 / DR-04）、Core 新增 11 例、Infrastructure 新增 5 例 |
| 阻塞/待确认 | 遗留项（非阻塞）：① `.gitguard.yml` 加载（FR-070）随 GGD-0009 CLI 引入，本期 UseCase 以 `ScanSettings` 默认值（1 MiB / 30s）运行；② `scan.max_file_bytes` 大小信号采用「差异中该文件新增内容 UTF-8 字节数」（DR-04 禁止读工作区，不取真实 blob 大小），已写入 SRS §2.4 |
| 最后更新 | 2026-10-16 |

<!-- agent-state:end -->

## 8. 未完成任务

### 8.1 待办任务

| 完成 | ID | 优先级 | 状态 | 任务 | 来源与验收 |
| --- | --- | --- | --- | --- | --- |
| [ ] | `GGD-0008` | `P0` | `TODO` | 实现 Hook 分发器脚本生成 + Install/Uninstall UseCase | 来源：模块设计/Hook.md / 验收：TC-FR-010-01~05 |
| [ ] | `GGD-0009` | `P0` | `TODO` | 实现 `gitguard` CLI 入口（System.CommandLine）+ 退出码表 | 来源：06-CLI契约 / 验收：所有退出码契约测试 |
| [ ] | `GGD-0010` | `P0` | `TODO` | 完成 8 条 MVP 必过场景 E2E 测试 Fixture | 来源：16-测试方案 §12 / 验收：S1~S8 全部通过 |
| [ ] | `GGD-0011` | `P0` | `TODO` | GitHub Actions 多平台构建 + Native AOT 产物 | 来源：14-部署文档 §3 / 验收：三平台 CI 绿灯 |
| [ ] | `GGD-0012` | `P0` | `TODO` | 文档回填：对比实现与 PRD / SRS 差异，补充遗漏项 | 来源：开发规范 §11 DoD / 验收：设计文档与代码一致 |

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
