# 项目上下文（CONTEXT）

> **说明**：本文件是 GitGuard 项目的「领域语言」和「业务上下文」的单一事实来源。所有 AI 产出（代码、文档、Issue、PR）必须优先使用这里定义的术语，不得随意引入同义词。本文件由 `INIT-GUIDE.md` 第 ① 步驱动首次填写，之后随业务理解深化持续更新。

## 1. 项目基本信息

| 字段 | 值 |
| --- | --- |
| 项目名称 | GitGuard |
| 项目缩写 | `GGD`（任务 ID 使用 `GGD-NNNN`） |
| 一句话定位 | 面向开发者的本地 Git 敏感信息防泄漏工具，在 `git commit` 与 `git push` 前通过 Hook 拦截 Token、密钥、密码、手机号、本地路径、内网 URL 等敏感信息 |
| 当前阶段 | v1.1 功能开发完成（`1.1.0`：桌面端 Hook 快捷安装 / 卸载与共享 Hook 管理服务；v1.0 基线 macOS / Linux 发行冒烟与稳定运行观察仍待完成） |
| 启动日期 | 2026-10-15 |

## 2. 业务术语表（Glossary）

> 所有 AI 产出必须对齐此表；需要新概念时先补入此表再引用。

| 中文术语 | 英文命名 | 定义 | 同义词（禁止使用） |
| --- | --- | --- | --- |
| 发现项 | Finding | 一次扫描中命中规则的具体结果，包含规则 ID、文件、行号、脱敏片段、严重度、指纹 | Hit, Issue, Match |
| 扫描运行 | ScanRun | 一次扫描动作的汇总记录，覆盖 `pre-commit` / `pre-push` / `manual` 三种类型 | Scan, Session |
| 检测规则 | DetectionRule | 一条可独立启用/禁用的检测规则，含正则、上下文、文件路径、熵值等判定条件 | Pattern, Signature |
| 严重度 | Severity | 风险等级：`Critical / High / Medium / Low`；与 `Policy` 决定「阻断/警告/放行」 | Level, Priority |
| 策略 | Policy | 对严重度到动作（block/warn/allow）的映射与白名单组合 | Config |
| 白名单 | Allowlist | 对误报的例外登记：按文件路径、规则、指纹豁免 | Ignore, Exception |
| 指纹 | Fingerprint | 对发现项的脱敏哈希，用于去重与白名单匹配；不得存储原始敏感值 | Hash, Signature |
| 暂存区 | StagedIndex | Git 暂存区（通过 `git diff --cached` 读取） | Index, Stage |
| 推送范围 | PushRange | 一次 `git push` 涉及的提交区间 `<local>..<remote>` 内的所有变更 | PushDiff |
| Hook 分发器 | HookDispatcher | GitGuard 安装的统一入口脚本，负责调用 GitGuard 本体并链式触发已有 Hook（Husky 等） | HookRouter |
| 失败关闭 | FailClosed | 扫描器异常/超时/配置损坏时默认拒绝提交或推送 | FailSafe（在本项目中禁用，语义不同） |
| 检测器 | Detector | 检测规则的运行时执行单元：`SecretDetector` / `PrivacyDetector` / `LocalPathDetector` / `UrlDetector` | Scanner（禁用，与 ScanRun 歧义） |
| 适配器 | Adapter | 对外部检测引擎的封装（如 `GitleaksAdapter`），实现 `IDetector` 接口 | Wrapper |

## 3. 关键角色

| 角色 | 诉求 | 权限边界 |
| --- | --- | --- |
| 开发者（Developer） | 在不改变现有 Git 工作流的前提下，被动获得秘密泄漏拦截 | 可读写本地仓库 `.git/hooks` 与 `.gitguard.yml`；无法关闭 `fail-closed` 之外的全局策略 |
| 团队管理员（Admin，可选） | 向团队下发统一规则、维护白名单基线 | 可生成/下发组织级 `.gitguard.org.yml`；本工具不负责分发，仅定义格式 |

## 4. 系统边界与外部依赖

### 4.1 做什么

- 在 `pre-commit` 阶段扫描暂存区差异（通过 `git diff --cached`）。
- 在 `pre-push` 阶段扫描本次推送涉及的所有提交差异。
- 支持手动扫描：暂存区、工作区、仓库全历史。
- 识别四类敏感信息：秘密（Secret）、隐私（Privacy）、本地路径（LocalPath）、含凭据或内网 URL。
- 本地 SQLite 落审计（仅存脱敏指纹，不存原文）。
- 统一安装、更新、卸载 Git Hook，兼容已有 Husky / lefthook / pre-commit 等。

### 4.2 不做什么

- 不把代码或任何敏感内容上传到外部服务。
- 不替代 CI/服务端扫描（服务端扫描是必要的互补防线，但不在本工具范围）。
- 不做 Git 托管平台（GitHub/GitLab/Gitee）的 WebHook 侧扫描。
- 不做实时 IDE 插件（二期可能通过 CLI 适配 IDE，但不内置 IDE 扩展）。
- 不做身份识别或 SSO 等安全无关的身份管理功能。

### 4.3 外部依赖

| 依赖 | 用途 | 许可 / 成本 | 失败影响 |
| --- | --- | --- | --- |
| 系统 Git CLI | 读取暂存区差异、推送范围、提交对象 | GPLv2，随操作系统安装 | 无 Git 环境时直接退出并提示；不自带 Git |
| Gitleaks（v0.2 已集成，可选） | 复用成熟秘密规则库，提升秘密检测召回率 | MIT；二进制随发行版分发 | 通过 `GitleaksAdapter` 隔离；缺失时降级为纯 C# 规则，不影响核心流程 |
| SQLite（嵌入） | 本地审计与白名单持久化 | Public Domain | 文件锁定/损坏时按 fail-closed 处理 |

## 5. 关键业务不变式

> 所有不变式一旦违反，相关实现一律拒绝合入，并需要走 ADR 流程重新评估。

- **DR-01 本地化**：所有扫描、规则匹配、审计写入必须在本机完成，不得向任何外部服务发送代码、文件内容、哈希之外的敏感衍生物。来源：idea §四「核心原则」。
- **DR-02 零明文留存**：SQLite、日志、临时文件、崩溃转储均禁止写入 Token / 密码 / 身份证 / 完整敏感行原文；只记录规则 ID、文件相对路径、行号、严重度、脱敏片段（≤ 32 字符掩码后展示）、强哈希指纹。来源：idea §六。
- **DR-03 失败关闭（FailClosed）**：扫描器崩溃、超时、配置损坏、依赖缺失时默认返回非零退出码阻断 `commit` / `push`；禁止「静默放行」。来源：idea §五.3。
- **DR-04 暂存区真实**：`pre-commit` 必须通过 `git diff --cached` 读取，禁止使用磁盘整文件代替，避免未暂存内容污染判定。来源：idea §二。
- **DR-05 Hook 兼容**：安装不得直接覆盖已有 Hook 文件；必须通过 `HookDispatcher` 保留并链式调用既有 Hook，卸载仅移除 GitGuard 自身注册项。来源：idea §五.2。
- **DR-06 显式绕过留痕**：用户通过 `--no-verify` 或 `GITGUARD_SKIP=1` 显式绕过时，须在本地审计表中登记一条「绕过事件」，不得伪装为正常扫描通过。来源：idea §五.3。

## 6. 技术栈引用

技术栈的单一事实来源：[`docs/03-系统架构设计(SAD).md`](./docs/03-系统架构设计(SAD).md) §2 技术选型。

## 7. 文档体系入口

- **首次使用本仓库**：`docs/INIT-GUIDE.md`（本次已完成，日常会话不再读取）。
- **开始任何任务**：`docs/AGENT-GUIDE.md` 第 2 章「会话启动协议」。
- **理解产品范围**：`docs/00-项目概述.md` → `docs/01-产品需求(PRD).md`。
- **理解架构与数据**：`docs/03-系统架构设计(SAD).md` → `docs/04-领域模型设计(DDD).md` → `docs/05-数据库设计.md`。
- **理解检测规则细节**：`docs/模块设计/Secret.md` / `Privacy.md` / `LocalPathUrl.md` / `Hook.md` / `Policy.md` / `Audit.md` / `Cli.md`。

## 8. 变更记录

| 版本 | 日期 | 变更说明 |
| --- | --- | --- |
| 0.1.0 | 2026-10-15 | 由 INIT-GUIDE 驱动完成占位符替换；项目缩写定为 GGD；6 条业务不变式登记 |
| 0.1.1 | 2026-10-25 | GGD-0052：当前阶段更新为 v1.0 开发中；Gitleaks 集成状态更正为 v0.2 已集成（可选）；文档入口补 LocalPathUrl；团队管理员角色去除过期阶段标注 |
| 0.1.2 | 2026-10-25 | GGD-0053：当前阶段更新为 v1.0 发布基线就绪（1.0.0；Windows 冒烟通过，macOS / Linux 待 Release runner） |
| 0.1.3 | 2026-10-26 | GGD-0054 / GGD-0055：当前阶段更新为 v1.1 功能开发完成（1.1.0：桌面端 Hook 快捷安装 / 卸载与共享 Hook 管理服务） |
