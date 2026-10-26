# 产品需求（PRD）

## 1. 产品背景与定位

GitGuard 是面向开发者的本地 Git 敏感信息防泄漏工具。在 `git commit` 与 `git push` 两个关键节点通过 Hook 拦截凭据、个人信息、本地路径与含凭据 URL 等敏感信息。承接 `00-项目概述.md` 的三阶段范围（MVP → 规则升级 → 桌面端），本文件把范围细化为可编号、可验收的功能需求。

## 2. 用户角色

| 角色 | 定义 | 权限边界 | 关键能力 |
| --- | --- | --- | --- |
| 开发者（Developer） | 直接使用 Git 的个人用户 | 可读写本地 `.git/hooks`、`.gitguard.yml`、本地审计数据库 | 安装/卸载 Hook、运行扫描、维护项目白名单、查看本地审计 |
| 团队管理员（Admin，二期+） | 下发组织级规则的角色 | 维护组织级 `.gitguard.org.yml`，无法远程读取开发者本地审计 | 定义组织规则基线、规则严重度调整、规则禁用需审计 |
| GitGuard CLI 自身 | 作为 Hook 调用链的被动参与者 | 读 Git 元数据、读暂存区/推送差异、写本地审计 | 返回退出码控制 `commit` / `push` 流程 |

## 3. 核心用户场景

### 3.1 场景「安装 GitGuard 到现有仓库」

- **触发**：开发者在一个已克隆的 Git 仓库中执行 `gitguard install`。
- **主流程**：
  1. CLI 检测仓库是否存在、是否已有 `core.hooksPath` 或 `.git/hooks/pre-commit` 等 Hook。
  2. 若已有 Hook，`HookDispatcher` 将原 Hook 保留并以链式调用方式嵌入。
  3. 写入分发脚本到 `.git/hooks/pre-commit`、`pre-push`（或 `core.hooksPath` 对应目录）。
  4. 在 SQLite `HookInstallations` 表记录安装状态与 GitGuard 版本。
  5. 终端输出安装成功的摘要（涉及 Hook、原有 Hook 的保留方式）。
- **成功指标**：`gitguard status` 将每个 Hook 分类为 `Missing` / `Managed` / `Legacy` / `Binary` / `SymbolicLink`，`Managed` 即 GitGuard 已托管；存在未托管的 `Legacy` / `Binary` / `SymbolicLink` 时退出码 6 并提示运行 `install`。安装后 `git commit --dry-run` 不产生错误。
- **失败分支**：无 Git 仓库（退出码 2）、无写入权限（退出码 3）、原 Hook 为二进制或符号链接且未给 `--force`（退出码 4）→ 显式提示并零写入终止安装。普通文本 Hook 不视为失败：自动复制备份为 `<hook>.local.bak` 并链式包裹。
- **对应功能需求**：`FR-010, FR-011, FR-012`

### 3.2 场景「`git commit` 前被 GitGuard 拦截」

- **触发**：开发者在暂存区包含敏感信息（如 API Key）时执行 `git commit`。
- **主流程**：
  1. Hook 分发器调用 GitGuard，GitGuard 通过 `git diff --cached` 读取暂存区差异。
  2. 加载 `.gitguard.yml` 和用户级配置，运行四类检测器。
  3. 对每个发现项应用策略引擎映射到动作（block/warn/allow）。
  4. 若存在 `critical` 或 `high` 级 block，返回非零退出码并输出终端报告（文件、行号、规则、脱敏片段、动作、severity、fingerprint；v0.1 不输出逐规则「处理建议」文案，见 `02-SRS` §2.7）。
  5. 将 ScanRun 和所有发现项以脱敏指纹形式写入 SQLite。
- **成功指标**：开发者在 10 秒内看到清晰的拦截原因；真实敏感信息被阻断。
- **失败分支**：
  - 扫描超时 → FailClosed 阻断，提示用户查看日志。
  - 规则加载失败 → FailClosed 阻断，提示配置文件路径。
- **对应功能需求**：`FR-020, FR-021, FR-022, FR-023`

### 3.3 场景「`git push` 时覆盖多个本地提交」

- **触发**：开发者本地已经完成 3 次提交，其中一次（哪怕已被后续提交删除）包含过密钥，然后执行 `git push`。
- **主流程**：
  1. `pre-push` Hook 获取 `<local>..<remote>` 推送范围，对每个提交的差异进行扫描。
  2. 把每个提交作为独立 ScanRun 的子集，命中后汇总报告。
  3. 任一提交命中阻断级规则 → FailClosed 阻断整次推送。
- **成功指标**：历史提交中加入的密钥仍被识别，即便后续提交已删除。
- **失败分支**：`remote-sha` 全零（新建分支）→ 范围退化为 `local-sha` 可达的全部提交；空 stdin → 视为无推送目标直接 Clean（退出码 0）。
- **对应功能需求**：`FR-030, FR-031, FR-032`

### 3.4 场景「误报登记为白名单」

- **触发**：开发者在测试代码中引用了形如密钥的占位值，首次被拦截。
- **主流程**：
  1. 开发者查看报告中的 `fingerprint`。
  2. 执行 `gitguard allowlist add --fingerprint <hash> --reason "测试占位值"`。
  3. 下次扫描跳过该指纹，但在终端仍输出一行「已忽略：N 项（白名单）」。
- **成功指标**：误报被豁免，但不等于规则失效；别的真实命中仍能拦截。
- **失败分支**：缺少 `--reason` → 拒绝写入，白名单变更必须留言。
- **对应功能需求**：`FR-040, FR-041, FR-042`
- **v0.1 落地状态**：已实现（`GGD-0030`）。匹配引擎（规则 / 路径 / 指纹三粒度）与扫描汇总可用；白名单来源为 `.gitguard.yml` 的 `allowlist.entries` 与 SQLite `allowlist` 表，`gitguard allowlist add/remove/list` 管理并写 `AllowlistChanged` 审计事件；扫描时合并两个来源。
- **误报治理**：判定口径、粒度选择、复核周期与误报率统计见 [`19-误报治理指南.md`](./19-误报治理指南.md)（`GGD-0037`）。

### 3.5 场景「开发者显式绕过（`--no-verify` 或 `GITGUARD_SKIP`）」

- **触发**：开发者判断本次拦截为误报且无法立即登记白名单，使用 `git commit --no-verify`。
- **主流程**：
  1. `pre-commit` 扫描通过后，分发脚本（隐藏选项 `--record-pending`）记录「父提交 + 暂存树」预测；扫描阻断或失败时清理旧标记。
  2. `git commit --no-verify` 跳过分发脚本，预测标记缺失或与实际提交不匹配。
  3. Git 仍执行 GitGuard 安装的 `post-commit` 轻量 Hook：比对实际提交的树与首个父提交，判定为 `NoVerify`。
  4. 使用 `GITGUARD_SKIP=1 git commit` 时，`pre-commit` 记录 `skipped` 标记且不执行扫描，`post-commit` 判定为 `GitGuardSkip`。
  5. 两种情况均写入 `scan_runs`（`Bypass` / `Clean` / 退出码 0）与 `audit_logs`（`BypassDetected`），载荷含 `RunId`、`CommitSha`、`DetectedAt`、`Reason`。
- **成功指标**：绕过事件可被查询；不伪装为「扫描通过」。
- **失败分支**：同时用 `--no-verify` 和 `GITGUARD_SKIP=1` 时只登记一次 `NoVerify`（无标记优先）。
- **对应功能需求**：`FR-050`
- **v0.1 落地状态**：已实现（`GGD-0013`）：`install` 默认注册 `post-commit`；`GITGUARD_SKIP=1` 仅在 Hook 模式（`--record-pending`）生效；E2E S8 覆盖正常提交 / `--no-verify` / `GITGUARD_SKIP=1` / 两者并用。

### 3.6 场景「仓库全量扫描 / 基线建立」

- **触发**：开发者首次接入 GitGuard 的老仓库，希望先知道历史风险，再决定是否清理。
- **主流程**：
  1. 执行 `gitguard scan --all`。
  2. 扫描当前 HEAD 的所有文件 + 历史提交差异（可选）。
  3. 输出完整报告，可通过 `--baseline <path>` 写入基线文件（本次命中仍按原策略处理）。
  4. 后续 `scan --all` 自动消费仓库根 `.gitguard-baseline.json`：已登记指纹不再阻断，但仍在报告中标出「历史噪声」。
- **成功指标**：老仓库接入零摩擦；基线清晰可维护。
- **失败分支**：仓库过大导致超时 → 支持 `--since <commit>` / `--max-files` 分段；基线损坏 → 退出码 13（FailClosed）。
- **对应功能需求**：`FR-060, FR-061`
- **v0.2 落地状态**：FR-060 已实现（`GGD-0031`）：默认扫描 HEAD 全部被跟踪文件；`--include-history` 扫描历史提交差异；`--since` 从指定提交的父提交续扫；`--max-files` 按提交边界截断并输出 `next_since_sha`。FR-061 已实现（`GGD-0032`）：`--baseline` 生成 `gitguard.baseline.v1` 文件、默认路径自动消费、`AllowedByBaseline` 动作与 `ignored_by_baseline` 汇总；基线指纹含每用户本地盐，不可跨机器移植。

## 4. 功能优先级

| 编号 | 功能 | 优先级 | 对应场景 | 验收要点 | 实现状态 |
| --- | --- | --- | --- | --- | --- |
| `FR-010` | `gitguard install` 安装 Hook | P0 | 3.1 | 安装后 `pre-commit` / `pre-push` 均被注入；原 Hook 保留并被链式调用 | 已实现（GGD-0008） |
| `FR-011` | `gitguard uninstall` 卸载 Hook | P0 | 3.1 | 仅移除 GitGuard 注册项；原 Hook 恢复可运行 | 已实现（GGD-0008） |
| `FR-012` | `gitguard status` 查询安装状态 | P0 | 3.1 | 输出 Git 版本、Hook 路径、GitGuard 版本、Hook 分类 | 已实现（状态分类见 3.1） |
| `FR-013` | `gitguard path` 用户级 PATH 管理（原始需求 3） | P1 | 全部 | 安装后新终端可直接运行 `gitguard`；卸载可逆且不破坏既有 PATH 条目；状态区分已登记 / 当前进程生效 / profile 配置 | 已实现（`GGD-0061`：Windows `HKCU\Environment\Path` / Unix `~/.local/bin` 符号链接 + 可选 profile 标记块，`path install/uninstall/status` 与三种 JSON Schema） |
| `FR-020` | 暂存区扫描（`gitguard scan --staged`） | P0 | 3.2 | 严格使用 `git diff --cached`；不包含未暂存内容 | 已实现（GGD-0007） |
| `FR-021` | 四类检测器 Secret/Privacy/LocalPath/Url | P0 | 3.2 | 命中率符合 §6 验收用例集 | 已实现：60 条内置规则（Secret 21 / Privacy 14 / LocalPath 12 / Url 13；GGD-0033 / GGD-0048 / GGD-0049 / GGD-0050 / GGD-0051 规则扩充） |
| `FR-022` | 策略引擎映射严重度到动作 | P0 | 3.2 | 可通过 `.gitguard.yml` 覆写默认映射 | 已实现（含 `critical` 不可降级为 `allow` 的硬下限） |
| `FR-023` | 终端报告 | P0 | 3.2 | 包含文件、行号、规则 ID、脱敏片段 | 部分实现：文本 / JSON 报告与脱敏片段可用，无逐规则「建议」文案 |
| `FR-030` | 推送扫描（`gitguard scan --push`） | P0 | 3.3 | 覆盖推送范围内每个提交；无法确定远程时降级为扫描所有未推送提交 | 已实现（GGD-0007；新分支退化为 `local-sha` 可达提交） |
| `FR-031` | 多提交聚合报告 | P0 | 3.3 | 每个 Finding 带来源 commit SHA | 已实现 |
| `FR-032` | FailClosed 行为 | P0 | 3.2 / 3.3 | 任何异常默认阻断，不默认放行 | 已实现：Git / 检测器 / 超时 / 配置异常 → 10 / 11 / 12 / 13 |
| `FR-040` | 白名单登记 `gitguard allowlist add/remove/list` | P1 | 3.4 | 必填 reason；写入 SQLite | 已实现（GGD-0030：CLI + SQLite 持久化 + `AllowlistChanged` 审计） |
| `FR-041` | 白名单匹配粒度：规则 / 路径 / 指纹 | P1 | 3.4 | 三种匹配均可独立启用 | 已实现（GGD-0030：配置文件与 SQLite 条目合并参与匹配） |
| `FR-042` | 已忽略项仍在终端汇总 | P1 | 3.4 | 每次扫描输出 `ignored N / blocked M / warned K` | 已实现：汇总行含「白名单忽略 N」 |
| `FR-050` | 绕过事件审计 | P1 | 3.5 | 识别 `--no-verify` 与 `GITGUARD_SKIP=1`，记录为独立事件 | 已实现（GGD-0013：`post-commit` + 父提交 / 暂存树预测） |
| `FR-060` | 全量扫描 `gitguard scan --all` | P1 | 3.6 | 支持 `--since` / `--max-files` 分段 | 已实现（GGD-0031：HEAD 全量树 + 历史提交 + 截断续扫） |
| `FR-061` | 基线文件生成与消费 | P2 | 3.6 | 基线以指纹为单位；用户修改后触发警告 | 已实现（GGD-0032：`--baseline` 生成、默认自动消费、`AllowedByBaseline` 放行、13 / 31 错误码） |
| `FR-070` | 配置：`.gitguard.yml` 项目级 | P0 | 全部 | 支持规则开关、严重度覆写、exclude 路径 | 已实现（`GGD-0019`：`exclude.paths` 匹配仓库相对路径、`exclude.files` 匹配任意深度文件名） |
| `FR-071` | 配置：用户级 `~/.config/gitguard/config.yml` | P1 | 全部 | 项目级覆盖用户级 | 已实现（GGD-0009） |
| `FR-080` | 自定义正则规则 | P2 | 3.2 | 支持命名、严重度、上下文过滤 | 已实现（GGD-0034：`rules.custom` + `gitguard rules list/view`，USR 规则参与四类检测与策略） |
| `FR-090` | GitleaksAdapter | P2 | 3.2 | 通过 `IDetector` 接入；可独立开关；无 Gitleaks 时降级不报错 | 已实现（GGD-0035：`use_gitleaks` / `gitleaks_path`、启动探测、GLK 映射与跨引擎去重；缺失可执行文件告警降级） |
| `FR-100` | Avalonia 桌面端 | P3 | 全部 | 风险列表、规则管理、安装状态查看、多仓库 | 已实现（v0.3，`GGD-0041` ~ `GGD-0047`：四区导航 / 仓库 / 规则 / 日志 / 设置 + Windows MSIX / macOS DMG / Linux AppImage 三平台安装包与发行冒烟） |

## 5. 非功能需求摘要

- **性能**：`pre-commit` P95 < 500ms（单仓库，常规暂存区 < 500 行差异）；`pre-push` P95 < 2s（≤ 50 提交，≤ 5000 行差异）。详见 `02-SRS` §5.1。
- **安全**：本地化、零明文留存、FailClosed、绕过留痕。详见 `02-SRS` §5.3 与 `08-权限设计.md`。
- **合规**：对个人数据按 GB/T 35273 做最小必要原则处理，不记录身份证/手机号原文。
- **可访问性**：CLI 输出支持 `--no-color` 和 `--format json`；`--language zh-CN` / `en-US` 与 `GITGUARD_LANG` 中英切换已实现（`GGD-0014`，命令行优先）；桌面端满足 WCAG 2.1 AA。
- **跨平台兼容性**：Windows 11 / macOS 14 / Ubuntu 24.04 三平台各产出 Native AOT 单一可执行文件；兼容 Git ≥ 2.9（CI 使用各平台最新稳定 Git，旧版本矩阵顺延 `GGD-0017`）。

> v0.1 的逐条 NFR 落地与未验证项见 `02-SRS` §5。

## 6. 产品验收

- [x] `gitguard install` 可在 Clean 仓库和已有文本 Hook（Husky 形态）的仓库中正确安装（场景 3.1；TC-FR-010-01~05）。
- [x] 暂存区包含真实格式的测试密钥时阻止 `commit`（场景 3.2；E2E S1）。
- [x] 未暂存文件存在密钥、但暂存区无问题时**不**误拦截（DR-04；E2E S2）。
- [x] 本地包含密钥的历史提交被 `push` 时仍能拦截（场景 3.3；E2E S3、S4）。
- [x] 普通公开 URL、正常示例代码不被无差别拦截（E2E S5）。
- [x] 已存在的文本 Hook 在安装后仍能被触发，退出码正确传播（DR-05；E2E S6）。
- [x] 扫描器崩溃 / 超时时默认阻断（DR-03；E2E S7a / S7b + TC-DR-03）。
- [x] `--no-verify` 与 `GITGUARD_SKIP=1` 本地绕过被记录为独立审计事件（DR-06；E2E S8、TC-DR-06-01）。
- [x] 白名单登记必填 reason，可按规则 / 路径 / 指纹豁免（配置文件 `allowlist.entries` 与 SQLite `allowlist` 表均可用，CLI 见 `GGD-0030`）。
- [x] SQLite 中无任何 Token / 密码 / 完整敏感行原文（DR-02；TC-DR-02-01）。
- [x] Husky / lefthook / pre-commit 真实框架兼容矩阵（NFR-A-001；`GGD-0021`；TC-COMPAT-01~04，CI `hook-compat`）。
- [x] NFR-P-001~004 性能基准已落地（`GGD-0020`，工具与结果见 `benchmarks/README.md`）：P-002 / P-004 达标；P-001 / P-003 在本机受进程创建基线影响未达标，待正常宿主复测 / `GGD-0023`。
- [x] `gitguard path install / uninstall / status` 用户级 PATH 管理（原始需求 3；`GGD-0061`）：Windows 用户环境变量 / Unix 符号链接 + 可选 profile 标记块；自动化测试覆盖注册表临时子键与临时 HOME，不触碰真实用户 PATH；实机新终端解析与卸载可逆性由用户验收（见 14-部署文档 §10.1）。

## 7. 产品路线与版本计划

- **v0.1 MVP**：场景 3.1-3.5；FR-010 ~ FR-032、FR-040 ~ FR-042、FR-050、FR-070、FR-071；白名单支持配置文件与 SQLite（`gitguard allowlist` CLI）。FR-060 / FR-061 全量扫描不在 v0.1 范围；无 GitleaksAdapter；无桌面端。
- **v0.2**：场景 3.6；FR-042 增强、FR-060 / FR-061、FR-080、FR-090；全历史扫描与基线。
- **v0.3**：Avalonia 桌面端 FR-100；组织级配置。

详细交付节奏见 `17-开发RoadMap.md`。

## 8. 未解问题

| # | 问题 | 影响 | 需要决策人 | 期限 |
| --- | --- | --- | --- | --- |
| Q-01 | `post-commit` 探测 `--no-verify` 绕过是否足够可靠？是否需要同时结合 reflog 比对？ | 中（DR-06 实现方式） | 项目负责人 | 已决议（GGD-0013）：采用 `post-commit` +「父提交 + 暂存树」预测比对；不引入 reflog（无法区分正常 Hook 链，且输出格式不稳定） |
| Q-02 | 桌面端是否打包成 MSIX / dmg / AppImage？还是仅发行 CLI 由桌面独立安装？ | 低（分发策略） | 项目负责人 | 已决议（GGD-0047）：桌面端独立发行 Windows MSIX / macOS DMG / Linux AppImage，安装包自包含且与 CLI 共用审计库；CLI 保持独立分发，桌面端不要求预装 CLI |

## 9. 变更记录

| 版本 | 日期 | 变更说明 |
| --- | --- | --- |
| 0.1.0 | 2026-10-15 | 初始化：定义 6 个核心场景、20 条 FR 及 10 条 MVP 验收 |
| 0.1.1 | 2026-10-18 | GGD-0012 文档回填：§3.1~§3.6 按实现校正 status 状态分类、`--force` 语义、报告字段、新分支降级与未落地范围；§4 新增「v0.1 实现状态」列（21 条规则分布、白名单仅配置文件可用、`exclude` 未消费等）；§5 `--json` 更正为 `--format json`、平台矩阵对齐 NFR-A-002；§6 勾选 S1~S7 与 DR-02 验收并登记未过项；§7 v0.1 范围与 RoadMap 对齐 |
| 0.1.2 | 2026-10-21 | GGD-0013：§3.5 按实现重写绕过检测流程（`--record-pending` 待提交预测、`post-commit` 比对父提交 / 暂存树、`NoVerify` / `GitGuardSkip` 两类原因、两者并用只记一次）；§4 FR-050 标记已实现；§6 勾选 DR-06 验收；§7 v0.1 范围纳入 FR-050；§8 Q-01 记录决议 |
| 0.1.3 | 2026-10-21 | GGD-0019：§4 FR-070 标记已实现（`exclude.paths` 按仓库相对路径、`exclude.files` 按任意深度文件名过滤，扫描期生效） |
| 0.1.4 | 2026-10-21 | GGD-0014：§5 可访问性更新 CLI 中英切换为已实现（`--language` / `GITGUARD_LANG`，命令行优先） |
| 0.1.5 | 2026-10-21 | GGD-0020：§6 勾选 NFR-P-001~004 基准验收，并标注实测达标情况（P-002 / P-004 达标；P-001 / P-003 受宿主进程创建开销影响未达标） |
| 0.1.6 | 2026-10-21 | GGD-0021：§6 勾选 Husky / lefthook / pre-commit 真实框架兼容矩阵验收（TC-COMPAT-01~04，CI `hook-compat`） |
| 0.1.7 | 2026-10-22 | GGD-0030：§3.4 白名单落地状态更新为 CLI + SQLite 已实现（`allowlist add/remove/list`、`AllowlistChanged` 审计、双来源合并）；§6 勾选白名单验收项；§7 v0.1 / v0.2 范围同步 |
| 0.1.8 | 2026-10-22 | GGD-0031：§3.6 / §4 FR-060 标记已实现（HEAD 全量树、`--include-history`、`--since` 续扫、`--max-files` 提交边界截断与 `next_since_sha`）；§4 状态列更名为「实现状态」并同步 FR-040 / FR-041 的 GGD-0030 落地状态 |
| 0.1.9 | 2026-10-22 | GGD-0032：§3.6 主流程更新为生成当次不抑制、后续默认自动消费；§4 FR-061 标记已实现（基线 schema、`AllowedByBaseline`、13 / 31 错误码、本地盐不可移植） |
| 0.1.10 | 2026-10-22 | GGD-0033：§4 FR-021 实现状态更新为 40 条内置规则（Secret 16 / Privacy 9 / LocalPath 7 / Url 8） |
| 0.1.11 | 2026-10-22 | GGD-0034：§4 FR-080 标记已实现（`rules.custom` 自定义规则、USR 前缀与加载期校验、上下文 / 文件 glob 过滤、`gitguard rules list/view`） |
| 0.1.12 | 2026-10-22 | GGD-0035：§4 FR-090 标记已实现（GitleaksAdapter 可选接入、启动探测缺失降级、`GLK/` 命名空间、跨引擎去重与 FailClosed） |
| 0.1.13 | 2026-10-22 | GGD-0037：§3.4 增加误报治理指南链接（判定口径、白名单粒度、复核周期与误报率统计） |
| 0.1.14 | 2026-10-23 | GGD-0046：§4 FR-100 标记桌面端页面已实现（仓库 / 规则 / 日志 / 设置，语言 / 主题 / Gitleaks 覆盖持久化、审计浏览与导出），三平台打包待 `GGD-0047` |
| 0.1.15 | 2026-10-23 | GGD-0047：§4 FR-100 标记三平台安装包与发行冒烟完成；§8 Q-02 决议桌面端独立发行 MSIX / DMG / AppImage、自包含且与 CLI 共用审计库 |
| 0.1.16 | 2026-10-24 | GGD-0048：§4 FR-021 实现状态更新为 45 条内置规则（Secret 21 / Privacy 9 / LocalPath 7 / Url 8），v1.0 规则库扩充启动 |
| 0.1.17 | 2026-10-24 | GGD-0049：§4 FR-021 实现状态更新为 50 条内置规则（Secret 21 / Privacy 14 / LocalPath 7 / Url 8），Privacy 新增 IBAN / 美国电话 / 英国 NINO / 中国车牌 / 出生日期 |
| 0.1.18 | 2026-10-24 | GGD-0050：§4 FR-021 实现状态更新为 55 条内置规则（Secret 21 / Privacy 14 / LocalPath 12 / Url 8），LocalPath 新增 macOS 挂载卷 / root 主目录 / Web 部署目录 / Cygwin 盘符 / Windows 行内盘符路径 |
| 0.1.19 | 2026-10-25 | GGD-0051：§4 FR-021 实现状态更新为 60 条内置规则（Secret 21 / Privacy 14 / LocalPath 12 / Url 13），Url 新增 Google Cloud 签名 URL / Azure SAS / 数据库 URL 内嵌凭据 / OAuth 刷新令牌 / 飞书 Webhook |
| 0.1.20 | 2026-10-26 | GGD-0061：§4 新增 FR-013 用户级 PATH 管理（原始需求 3）并标记已实现；§6 增加 PATH 安装 / 卸载 / 状态验收项 |
