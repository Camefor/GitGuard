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
- **输出**：JSON（`gitguard.status.v1`）或人类可读文本，包含 Git 版本、`core.hooksPath`、Hook 状态分类（`Missing` / `Managed` / `Legacy` / `Binary` / `SymbolicLink`）、GitGuard 版本、配置文件加载链与配置警告；存在未托管的 `Legacy` / `Binary` / `SymbolicLink` 时 `needs_attention=true`（退出码 6）。
- **关联测试用例**：`TC-FR-012-01`

### 2.4 `FR-020` 暂存区扫描

- **描述**：扫描当前仓库暂存区差异，判定是否存在敏感信息。
- **输入**：`git diff --cached -U0` 的输出（统一 unified-0 格式）。
- **输出**：零发现 → 退出码 0；仅警告 → 退出码 0 + 终端警告；阻断发现 → 退出码 1 + 终端报告 + SQLite 记录。
- **处理规则**：严格使用 `git diff --cached`；不读取磁盘工作区文件；文件 > `scan.max_file_bytes`（默认 1 MiB）跳过并记为「skipped」；二进制文件同样跳过并记为「skipped」。
- **异常处理**：Git 命令失败 → FailClosed 退出码 10；检测器抛异常 → FailClosed 退出码 11。
- **关联领域规则**：`DR-03, DR-04`
- **关联测试用例**：`TC-FR-020-01` ~ `TC-FR-020-05`（`ScanEndToEndTests` / `CliScanCommandTests`）；FailClosed 分支见 `TC-DR-03-*`

### 2.5 `FR-021` 四类检测器

- **描述**：MVP 必须内置以下四类检测器：
  - **SecretDetector**：匹配常见凭据格式（JWT、Slack、GitHub、AWS、通用 `password = "..."`）、主流平台 Token（Google / Stripe / npm / GitLab / OpenAI / SendGrid / Hugging Face）及 Azure 存储密钥。
  - **PrivacyDetector**：中国大陆手机号（`^1[3-9]\d{9}$` + 上下文）、18 位身份证（含校验位）、邮箱、护照、座机、美国 SSN、姓名与地址组合。
  - **LocalPathDetector**：Windows 驱动器盘符、`~/`、`/Users/`、`/home/`、`/workspace/volumes/`、`C:\Users\`、UNC 共享路径、`/opt` `/srv` `/data` 部署目录等。
  - **UrlDetector**：含用户名密码的 URL（`://user:pass@`）、含 `token=` / `apikey=` / `password=` 查询参数、Slack / Discord Webhook、AWS 预签名 URL、RFC 1918 私网段与环回段；可按 `rules.url.public_hostname_whitelist` 对可信公共文档主机抑制命中（`GGD-0039`）。
- **处理规则**：每个检测器实现 `IDetector`，`DetectAsync` 返回 `IAsyncEnumerable<Finding>`（流式产出，ADR-0003）；装配与运行顺序固定为 Secret → Privacy → LocalPath → Url（`Application/Composition/DetectorAssembly.cs`）；内置规则共 40 条（Secret 16 / Privacy 9 / LocalPath 7 / Url 8；GGD-0033 扩充），严重度分布为 Critical 7 / High 17 / Medium 9 / Low 7；可按类别 `rules.<category>.enabled: false` 整类禁用，或用 `disabled_rules` 按规则 ID 禁用；四类全部禁用视为配置无效（FailClosed，退出码 13）。
- **关联领域规则**：`DR-01, DR-02`
- **关联测试用例**：`TC-FR-021-S-*`（Secret）/ `TC-FR-021-P-*`（Privacy）/ `TC-FR-021-L-*`（LocalPath）/ `TC-FR-021-U-*`（Url）；每条规则由 `BuiltinRuleFixtureTests` 覆盖 ≥3 正例 / ≥3 反例 / 边界正反例

### 2.6 `FR-022` 策略引擎

- **描述**：将发现项的严重度映射为动作：`block`、`warn`、`allow`。
- **处理规则**：
  - 默认映射：`critical → block, high → block, medium → warn, low → warn`。
  - 项目配置可覆写；允许用户降级（如 `medium → allow`），但 `critical` 的 `block` 不可降级为 `allow`（硬下限）。
  - 任一 `block` 决定整次扫描退出码。
- **关联领域规则**：`DR-03`

### 2.7 `FR-023` 终端报告格式

- **描述**：命中项以统一格式输出到 stderr（`--format json` 时改为 stdout 输出机器可读 JSON，文本报告不再输出）。
- **输出格式**（人类可读，v0.1 实际形态）：

  ```text
  ✖ GitGuard 扫描被阻断（exit_code=1）
    仓库：D:/work/demo
    类型：pre-commit
    结果：Blocked（扫描 3 个文件，发现 1 项：阻断 1 / 警告 0 / 白名单忽略 0）
    耗时：42 ms
    发现项：
      [Block] GGD/SECRET/AWS-AKID  appsettings.Production.json:18  AKIA****************
        severity=Critical  fingerprint=<64 位十六进制 HMAC>  commit=<push 扫描时的来源提交 SHA>
  ```

  - 颜色由 `--no-color` / `NO_COLOR` / 非 TTY 自动关闭；文本模式下进度与警告同样走 stderr。
  - 跳过文件（二进制 / 超限）、审计写入降级与配置警告按需追加「跳过 / 警告」行。
  - v0.1 **不输出逐规则「建议」文案**；`fingerprint` 可用于 v0.2 白名单登记。
- **可选 JSON 输出**：`--format json` 输出 `gitguard.scan.v1` Schema（`findings` / `summary` / `skipped_files` / `warnings` 等字段，见 `06-API接口设计` §4.2）。
- **关联测试用例**：报告文本与 JSON 字段由 `CliScanCommandTests` 与 E2E 场景覆盖；无独立 `ReportWriter` 单测。

### 2.8 `FR-030` 推送扫描

- **描述**：扫描一次 `git push` 涉及的所有提交差异。
- **输入**：Git 从 stdin 传入的 `<local-ref> <local-sha> <remote-ref> <remote-sha>` 行。
- **处理规则**：
  - 对每一行：若 `remote-sha` 全零（新分支），计算范围 `<local-sha>`；否则 `<remote-sha>..<local-sha>`。
  - 使用 `git rev-list` 枚举提交；再一次 `git log --no-walk=unsorted --stdin -p -U0 --root --format=%H` 批量取回全部提交差异（`--root` 使初始提交按全量新增输出，避免新分支仅含根提交时漏扫；`%H` 为每个提交输出 SHA 头，空提交对应空差异；输出无法完整归属到请求提交时 FailClosed，避免静默漏扫）。
  - 若 `local-sha` 全零（删除分支）→ 跳过该行，无提交可扫描。
  - 空 stdin → 无推送目标：直接 Clean（退出码 0）。
  - 提交在多个 ref 行重复出现时按首次出现顺序去重，只扫描一次。
- **异常处理**：Git 输入解析失败（行格式不符）→ FailClosed 退出码 10，不回显原始输入内容（DR-02）；范围枚举 / 提交差异失败 → FailClosed 退出码 10。
- **关联测试用例**：`TC-FR-030-01` ~ `TC-FR-030-02`（`ScanEndToEndTests`）；推送输入解析与去重见 `PushUpdateTests` / `ScanPushUseCaseTests`

### 2.9 `FR-040` 白名单

- **描述**：允许用户以规则 ID、文件路径（glob）、指纹三种粒度登记例外。
- **输入**：`gitguard allowlist add --rule <id>`、`--path <glob>`、`--fingerprint <hash>` 任一或组合；`--reason` 必填。
- **输出**：写入 `Allowlist` 表。
- **处理规则**：匹配时按「rule + path + fingerprint」三者取交集；缺省项视为通配。
- **v0.1 实现状态**：已实现（`GGD-0030`）。匹配引擎（`AllowlistMatcher`）与策略放行可用；白名单来源为 `.gitguard.yml` 的 `allowlist.entries` 与 SQLite `allowlist` 表（`gitguard allowlist add/remove/list` 管理，`add` / `remove` 同事务写 `AllowlistChanged` 审计事件）；扫描策略合并两个来源（`FR-041`），审计库读取失败按 FailClosed 映射退出码 14。
- **治理流程**：误报判定、白名单粒度选择、原因模板、复核周期与禁止事项见 [`19-误报治理指南.md`](./19-误报治理指南.md)（`GGD-0037`）。
- **关联测试用例**：配置解析见 `GitGuardConfigLoaderTests`；匹配 / 放行见 `AllowlistMatcherTests` / `PolicyEvaluatorTests`；CLI / SQLite 见 `CliAllowlistCommandTests` / `SqliteAuditStoreAllowlistTests` / `AllowlistUseCaseTests` / `AllowlistEntryTests`；退出码映射见 `ExitCodeMapperTests`。

### 2.10 `FR-050` 绕过事件审计

- **描述**：识别 `--no-verify` 与 `GITGUARD_SKIP=1` 两种显式绕过，并写入审计。
- **预测机制**：提交 SHA 在 `pre-commit` 阶段无法可靠预测（提交信息、时间戳尚未确定），因此以「父提交（`pre-commit` 时 HEAD，首个提交为 `null`）+ 暂存树（`git write-tree`）」作为待提交预测。
- **处理规则**：
  - `pre-commit` 分发脚本以隐藏选项 `--record-pending` 调用 `scan --staged`：扫描通过后写入标记 `{ parent_sha, tree_sha, skipped, recorded_at }`；扫描阻断或失败时清理旧标记，避免残留掩盖后续真实绕过。
  - `GITGUARD_SKIP=1` **仅在 Hook 模式**（`--record-pending`）生效：不执行扫描，写入 `skipped = true` 的标记并退出 0；手动 `scan --staged` 忽略该变量。
  - `install` 默认注册 `post-commit`，分发脚本调用隐藏命令 `gitguard hook post-commit`：读取实际提交的树与首个父提交，与标记比对。
  - 匹配且未跳过 → 正常提交，消费标记，不写审计；匹配且 `skipped` → `GitGuardSkip`；无标记 / 不匹配 / 标记损坏 → `NoVerify`。同时使用 `--no-verify` 与 `GITGUARD_SKIP=1` 时只登记一次 `NoVerify`。
- **标记存储**：`<git-dir>/gitguard/pending-scan.json`（每个 worktree 独立），原子写（临时文件 + 改名）、UTF-8 无 BOM、Unix `0600`；读取失败或内容损坏按「无标记」处理（宁可多记，不可漏记）。
- **输出**：`scan_runs` 新增一条 `scan_type = Bypass` 行（`result = Clean`、`exit_code = 0`、`duration_ms = NULL`、无 Finding），并追加 `audit_logs` 的 `category = Bypass` / `event_type = BypassDetected` 事件，载荷 `{ RunId, CommitSha, DetectedAt, Reason }`（`Reason ∈ NoVerify | GitGuardSkip`）。`hook post-commit` 始终以 0 返回（Git 忽略其退出码）；审计写入失败 → FailClosed 退出码 14。
- **v0.1 实现状态**：已实现（`GGD-0013`）；E2E 场景 S8 通过。
- **关联测试用例**：`TC-FR-050-01` / `TC-DR-06-01`（`BypassDetectionServiceTests` / `PendingScanRecorderTests` / `FilePendingScanStoreTests` / `GitAccessBypassTests` / `SqliteAuditStoreBypassTests` / `CliBypassTests` / `HookEndToEndTests` / E2E S8）

### 2.11 `FR-060` 全量扫描

- **描述**：扫描当前 HEAD 下的所有被跟踪文件；可选扫描历史提交。
- **输入**：`gitguard scan --all [--since <commit>] [--include-history] [--max-files <N>]`。
- **处理规则**：
  - 默认以空树与 HEAD 的差异（`git diff <empty-tree> HEAD -U0`）读取 HEAD 全部被跟踪文件，不读取工作区（DR-04）；空仓库（无提交）按 Clean（0 文件）处理，不调用 `rev-list`。
  - `--include-history` 按 `git rev-list` 由新到旧枚举 HEAD 之前的提交，批量取回提交差异并扫描；HEAD 完整快照已由全量树差异覆盖，不重复扫描。Finding 的 `commit_sha` 指向泄漏提交。
  - `--since <commit>`：从该提交的父提交继续向更早历史扫描（不含该提交本身），并隐含历史模式；用于按报告中的 `next_since_sha` 续扫。
  - `--max-files <N>`：历史提交的文件数软上限，按提交边界生效（当前提交可整批超过上限）；截断时报告 `truncated=true`、`next_since_sha=<最后完成的提交>` 与剩余提交数。`--since` / `--max-files` 同样隐含历史模式。
  - 历史选项仅能与 `--all` 组合；`--staged` / `--push` / `--all` 三选一，违反返回用法错误（退出码 7）。
- **实现**：`GitGuard.Application.Scanning.ScanAllUseCase`（`GGD-0031`）；差异源为 `IGitAccess.GetFullTreeDiff` / `ListCommitShas` / `GetCommitDiffs`。
- **v0.2 实现状态**：已实现（`GGD-0031`）。
- **关联测试用例**：`TC-FR-060-01~04`（`ScanAllUseCaseTests` / `GitAccessFullScanTests` / `CliScanAllTests`）

### 2.11.1 `FR-061` 基线文件

- **描述**：把首次全量扫描确认的历史噪声指纹写入基线文件；后续全量扫描自动消费基线，对已登记指纹不再阻断，只保留为可追溯记录。
- **输入**：`gitguard scan --all --baseline <path>`（生成基线）。`--baseline` 仅能与 `--all` 组合，路径为空或以 `-` 开头返回用法错误（退出码 7）。
- **文件格式**：JSON，`schema = "gitguard.baseline.v1"`；默认路径为仓库根目录 `.gitguard-baseline.json`；`entries` 每项含 `fingerprint`（64 位小写十六进制）、`rule_id`、`file_path`；读取时只强制校验 `fingerprint`，其余字段作为追溯元数据。指纹按字典序排序、重复指纹去重，保证生成结果确定。
- **处理规则**：
  - **生成**：`--baseline` 指定路径时，扫描完成后写入当前全部命中的指纹；本次命中的 Finding 仍按原策略阻断，不在生成当次抑制。
  - **消费**：未指定 `--baseline` 的全量扫描（`scan --all` / 带历史参数）在默认文件存在时自动加载；自定义路径仅用于生成，后续运行只自动消费默认文件。
  - **判定**：命中指纹在基线内 → 策略动作覆盖为 `Allow` 且 `IgnoredByBaseline = true`，报告 / 审计动作记为 `AllowedByBaseline`；允许清单优先，已由允许清单放行的命中不再标记为基线放行。
  - **结果**：被抑制的 Finding 仍保留在报告与审计中（可追溯）；仅含基线抑制项时结果 `Warned`、退出码 0；存在其他阻断项时仍为 `Blocked`、退出码 1。
  - **读取失败**：基线文件损坏、schema 不符或条目非法 → `BaselineException(BASELINE_INVALID)`，退出码 13（FailClosed，不静默忽略基线）。
  - **写入失败**：基线路径不可写 → `BaselineException(BASELINE_WRITE_FAILED)`，退出码 31。
  - **可移植性限制**：指纹为使用每用户本地盐的 HMAC-SHA256，基线文件不能跨机器 / 用户复用；换机后需重新生成。
- **实现**：`GitGuard.Application.Scanning.BaselineFile` / `ScanAllUseCase`（`GGD-0032`）。
- **v0.2 实现状态**：已实现（`GGD-0032`）。
- **关联测试用例**：`TC-FR-061-01~05`（`BaselineUseCaseTests` / `CliScanBaselineTests` / `ExitCodeMapperTests`）

### 2.12 `FR-070` 配置文件

- **描述**：配置文件 Schema 见 `模块设计/Policy.md` §7；本节保留验收要点。
- **处理规则**：
  - 项目级 `.gitguard.yml` 覆盖用户级。
  - 不存在时使用内置默认。
  - 版本字段 `version: 1` 必填；未知版本 → FailClosed。
  - 未知字段：警告但不阻断（宽松解析），警告经 `gitguard status` 与扫描报告的 `warnings` 透出。
  - 取值越界（`scan.max_file_bytes` / `scan.timeout_ms` / `logging.file.*` / `rules.secrets.min_entropy`）→ FailClosed（退出码 13）。
  - `rules.secrets.use_gitleaks`（bool，默认 false）：启用 GitleaksAdapter（`GGD-0035`）。`rules.secrets.gitleaks_path` 为空白字符串 → FailClosed（退出码 13）；未配置时按 `gitleaks` 在 PATH 中查找。启用但可执行文件缺失 / 无法执行 → 告警并禁用适配器，不阻断扫描。
  - `exclude.paths`：glob 匹配仓库相对路径；`exclude.files`：glob 匹配任意目录下的文件名。命中文件在扫描管线中直接跳过（暂存区与推送共用），不产生 Finding，也不计入 `skipped_files`；空项 → 退出码 13。
  - `hooks.pre_commit` / `hooks.pre_push` / `hooks.post_commit_bypass_detection`（bool，默认 true）：`gitguard install` 未显式给出 `--hook` 时按开关注册 Hook 子集；显式 `--hook` 始终优先；三项全为 `false` 且未显式指定 → FailClosed（退出码 13）。`hooks.gitguard_executable_path`（string?，默认自动探测）：覆盖固化到分发脚本的可执行文件路径，支持 `~` / 相对路径，安装期文件不存在或为空白 → 退出码 13（`GGD-0038`）。
  - `scan.pre_commit: false` / `scan.pre_push: false`：仅控制 Hook 模式扫描（分发脚本携带隐藏选项 `--hook-mode`），手动 `scan --staged` / `scan --push` 始终执行；跳过时退出码 0、无 ScanRun（`GGD-0038`）。`pre-commit` 跳过仍写入 `skipped=false` 的待提交标记，`post-commit` 不将其误判为绕过事件。
  - `rules.url.public_hostname_whitelist`（string[]，默认 `[]`）：公共文档主机白名单，精确主机或 `*.example.com` 通配，大小写不敏感；命中主机的 URL 规则 Finding 不产出。加载期去空白 / 末尾根点、转小写并去重，空白或含协议 / 端口 / 路径 / 非法通配的项 → 退出码 13；项目级声明整体替换用户级列表（`GGD-0039`）。
  - `scan.fail_closed: false`：仅警告，MVP 行为恒为 FailClosed（DR-03）。
- **实现**：`GitGuard.Application.Configuration.GitGuardConfigLoader`（`GGD-0009`）；`hooks.*` 与 `scan.pre_commit` / `scan.pre_push` 接线于 `GGD-0038`；`rules.url.public_hostname_whitelist` 接线于 `GGD-0039`。
- **v0.2 实现状态**：除 `scan.fail_closed` 仍为「仅警告」外，其余字段均生效（`exclude` 自 `GGD-0019`、`hooks.*` 与 Hook 模式扫描开关自 `GGD-0038`、URL 主机白名单自 `GGD-0039` 起生效）。
- **关联测试用例**：`TC-FR-070-01` ~ `TC-FR-070-09`

### 2.12.1 `FR-071` 用户级配置

- **描述**：用户级 `~/.config/gitguard/config.yml`（`PRD FR-071`，P1）。
- **处理规则**：
  - 与项目级配置深度合并：map 递归合并、列表整体替换；项目级优先。
  - 文件缺失不报错（视为未配置）。
  - 格式或取值非法与项目级同等处理（FailClosed，退出码 13）。
- **关联测试用例**：`TC-FR-070-01`（覆盖「项目级覆盖用户级」的加载链顺序断言）

### 2.12.2 `FR-080` 自定义正则规则

- **描述**：项目级 / 用户级配置可通过 `rules.custom` 声明项目特定正则规则，与内置规则一同参与四类检测与策略判定；CLI 提供 `gitguard rules list` / `view` 只读查询。
- **处理规则**：
  - 规则字段：`id` / `name` / `severity` / `pattern` 必填；`context_keywords` / `file_path_globs` 可选；`enabled` 默认 `true`。
  - 规则 ID 必须以 `USR/` 开头并满足 `^(GGD|USR|GLK)/[A-Z]+/[A-Z0-9_-]+$`；类别段仅支持 `SECRET` / `PRIVACY` / `LOCALPATH` / `URL`；同一列表内 ID 重复 → FailClosed（退出码 13）。
  - `pattern` 按 .NET 正则语法在加载期编译；非法正则、非法 `severity`、缺少必填字段、空白 `context_keywords` 项 → 退出码 13。检测与脱敏取第一个捕获组（命名捕获组语法可原样使用，但名称不参与语义）。
  - `context_keywords` 为大小写不敏感的子串门禁：声明后命中行必须包含任一关键字；`file_path_globs` 以仓库相对路径匹配，语义与 `exclude` 的 glob 一致。两者均未声明时不做额外过滤。
  - 自定义规则按声明顺序追加在内置规则之后，来源标注为 `User`；`disabled_rules` 与类别 `enabled: false` 共同决定「有效启用」状态。项目级 `rules.custom` 整体替换用户级列表。
  - 自定义规则与内置规则共用 200ms 正则匹配超时：超时 → `DetectionException`（`DETECTOR_FAILED`）→ 退出码 11。自定义 SECRET 规则由用户显式声明，超长行（> 2048 字符，疑似 minified）不跳过。
  - `gitguard rules list [--category Secret|Privacy|LocalPath|Url] [--enabled] [--show-pattern]` 与 `rules view <rule-id> [--show-pattern]` 不要求当前目录是 Git 仓库；默认隐藏正则原文；未知规则 → 退出码 40，非法类别 → 退出码 7。JSON Schema 为 `gitguard.rules.list.v1` / `gitguard.rules.view.v1`。
- **实现**：`GitGuard.Application.Configuration.GitGuardConfigLoader`（加载与校验）、`GitGuard.Application.Rules.RuleCatalog`（规则目录）、`GitGuard.Cli.Commands.RulesCommand`（CLI）。
- **v0.2 实现状态**：已实现（`GGD-0034`）。
- **关联测试用例**：`TC-FR-080-01` ~ `TC-FR-080-04`（`GitGuardConfigLoaderTests` / `RuleCatalogTests` / `CustomRuleDetectorTests` / `CliRulesCommandTests`）

### 2.13 `FR-090` GitleaksAdapter

- **描述**：通过实现 `IDetector`，把外部 Gitleaks CLI 作为可选秘密检测引擎接入；未启用或不可用时不影响内置检测器。
- **处理规则**：
  - 配置：`rules.secrets.use_gitleaks`（默认 `false`）控制装配；`rules.secrets.gitleaks_path` 可指定可执行文件（支持 `~`），未配置或仅裸名称时按 PATH 查找（Windows 自动补 `.exe`）；空白路径 → FailClosed（退出码 13）。
  - 启动期探测：定位成功后执行 `gitleaks version`；8.x 直接可用，其他可解析版本告警但继续可用；文件缺失 / 无法执行 / 版本探测失败 → 禁用适配器并记录警告，扫描继续使用内置规则（不阻断）。
  - 调用：为每个 `FileChange` 建立隔离临时目录，按原顺序仅写入变更行并保留仓库相对路径（路径逃逸 `..` 直接拒绝）；执行 `gitleaks detect --no-git --source <tmp>/source --report-format json --report-path <tmp>/report.json`，默认 10s 超时，超时终止进程树且不重试。
  - 退出码：0 / 1 均以 JSON 报告为准（1 表示有命中）；其他非零退出码 → `DetectionException`（`DETECTOR_FAILED`，退出码 11）。退出码 0 且未生成报告视为无命中；退出码 1 且未生成报告 → FailClosed。
  - 映射：报告 `RuleID` 规范化为 `GLK/SECRET/<UPPER_SNAKE>`（仅保留 `[A-Z0-9_-]`）；严重度统一为 `High`；`StartLine` 是暂存文件行序号，经 `FileChange.Lines` 还原为 diff 原始行号，越界条目跳过。`Secret` / `Match` 仅用于脱敏与指纹，不进入 Finding。
  - 禁用规则：`disabled_rules` 同时接受 Gitleaks 原始 ID 与 `GLK/...` 命名空间写法（大小写不敏感）；`GGD/` / `USR/` 条目由内置规则选择消费，适配器忽略。
  - 去重：同一提交、文件、行、指纹的多引擎命中保留一条，来源优先级 `USR > GLK > GGD`，严重度取最高（`FindingDeduplicator`）。
  - 报告校验：根节点必须为数组、条目必须为对象且包含 `RuleID` 与整数 `StartLine`；JSON 非法 / 结构不符 → `DETECTOR_FAILED`（退出码 11）。
  - 清理：临时目录（含暂存原文与报告）在 `finally` 中递归删除，Unix 目录权限 0700；清理失败 FailClosed，避免敏感原文残留（DR-02）。
- **关联 ADR**：`ADR-0003`
- **实现**：`GitGuard.Detection.Adapters.GitleaksAdapter` / `GitleaksProbe` / `GitleaksExecutableLocator` / `GitleaksProcessRunner`、`GitGuard.Application.Scanning.FindingDeduplicator`（`GGD-0035`）。
- **v0.2 实现状态**：已实现（`GGD-0035`）。
- **关联测试用例**：`TC-FR-090-01~04`（`GitleaksAdapterTests` / `GitleaksProbeTests` / `GitleaksExecutableLocatorTests` / `GitleaksProcessRunnerTests` / `GitGuardConfigLoaderTests` / `GitGuardSessionTests` / `ScanStagedUseCaseTests` / `CliScanCommandTests`）

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
| 对外（审计查询 / 导出） | `gitguard audit list` / `gitguard audit view <run-id>` / `gitguard audit export --output <path> --format json/csv` | 用户事后分析 | 已实现（`GGD-0022` / `GGD-0036`）：分页 / 时间 / 结果筛选，单次运行摘要与 Finding 明细，导出仅含脱敏片段与 HMAC 指纹 |
| 对内（Git） | `git diff --cached`、`git rev-list`、`git log --no-walk=unsorted --stdin` | 读取仓库差异 | 失败即 FailClosed |
| 对内（可选） | `gitleaks` CLI | v0.2 秘密检测补强（`GGD-0035`） | 启动期缺失 / 不可执行 → 降级禁用，不阻断；扫描期执行失败 → FailClosed |

CLI 命令细节见 `06-API接口设计.md`。

## 5. 非功能需求（NFR）

### 5.1 性能

| 编号 | 指标 | 条件 | 目标值 | 测量方式 | v0.1 状态 |
| --- | --- | --- | --- | --- | --- |
| `NFR-P-001` | 冷启动时间 | Native AOT 单文件；Windows 11 NVMe | ≤ 150ms | 基准脚本 `benchmarks/startup.sh` 执行 50 次取 P95 | 未达标：实测 P95 200.44 ms（2026-10-21）；本机仅 `cmd.exe` 进程创建基线即约 170 ms，绝对阈值低于宿主基线，需在正常宿主复测（`GGD-0020` / `GGD-0023` 复测） |
| `NFR-P-002` | 暂存区扫描 P95 | ≤ 500 行差异 | ≤ 500ms | 基准脚本 `benchmarks/run.sh staged` 执行 10 次 | 已达标：实测 P95 311.05 ms（`GGD-0020`；`GGD-0023` 复测） |
| `NFR-P-003` | 推送扫描 P95 | ≤ 50 提交 / ≤ 5000 行差异 | ≤ 2s | 基准脚本 `benchmarks/run.sh push` 执行 10 次 | 已达标：实测 P95 669.78 ms（2026-10-21）；一次批量 `git log --no-walk=unsorted --stdin` 取回全部提交差异（`GGD-0023`），较逐提交实现 4687.07 ms 下降约 86% |
| `NFR-P-004` | 全量扫描吞吐 | ≤ 100 MB 代码 | ≥ 20 MB/s | 基准脚本进程内检测器管线（`benchmarks/run.sh throughput`） | 已达标：实测 P50 26.08 MB/s（100 MiB / 1326 文件，`GGD-0020`；`GGD-0023` 复测） |

### 5.2 可用性

| 编号 | 指标 | 目标值 | 测量方式 | v0.1 状态 |
| --- | --- | --- | --- | --- |
| `NFR-A-001` | Hook 兼容率 | 与 Husky / lefthook / pre-commit 共存成功率 100% | 集成测试 `TC-COMPAT-*` | 已覆盖：`TC-COMPAT-01~04` 在真实安装的 Husky v9 / lefthook v2 / pre-commit 仓库中验证安装包裹、真实 `git commit` 链式执行、阻断语义与卸载逐字节还原；CI `hook-compat` 作业（Windows）执行（`GGD-0021`）；跨平台矩阵不再计划（`GGD-0028` 已取消） |
| `NFR-A-002` | 跨平台可用性 | Windows 11 / macOS 14 / Ubuntu 24.04 均通过验收 | CI 多平台矩阵 | 部分覆盖：当前 `.github/workflows/ci.yml` 仅 Windows（`GGD-0027`）；跨平台矩阵不再计划（`GGD-0028` 已取消），`release.yml` 仍产出三平台 AOT 发行物 |

### 5.3 安全

| 编号 | 要求 | 验证方式 | v0.1 状态 |
| --- | --- | --- | --- |
| `NFR-S-001` | 本地化：无出站网络请求 | 单元测试 + CI 下 `strace`/`netstat` 验证 | 部分覆盖：编译期 `BannedSymbols` 封禁与本地验证仍生效；CI Linux `strace -e trace=network` 作业不再计划（TC-DR-01-01，`GGD-0028` 已取消） |
| `NFR-S-002` | SQLite / 日志 / 临时文件无明文敏感原文 | 代码审查 + 测试用「金丝雀 Token」注入后扫文件 | 已覆盖：TC-DR-02-01(审计库 / 日志 / 终端输出)；崩溃日志经同一脱敏流水线但无独立金丝雀用例 |
| `NFR-S-003` | FailClosed：所有异常路径默认阻断 | 故障注入测试 `TC-FAILCLOSED-*` | 已覆盖：TC-DR-03-01~04（Git / 检测器 / 超时 / 配置） |
| `NFR-S-004` | 绕过事件可追溯 | 审计表查询 | 已覆盖：TC-FR-050-01 / TC-DR-06-01（`scan_runs` + `audit_logs` 直读断言；E2E S8 覆盖 `--no-verify` / `GITGUARD_SKIP=1` / 两者并用） |

### 5.4 合规

| 编号 | 法规/标准 | 要求 | 证据 | v0.1 状态 |
| --- | --- | --- | --- | --- |
| `NFR-C-001` | GB/T 35273—2020 | 对手机号、身份证等个人信息采集遵循最小必要；不留原值 | 代码审查 + 数据库 Schema | 已满足：审计仅存脱敏片段与 HMAC 指纹（无原值）；无独立法规审计 |

### 5.5 可访问性与国际化

| 编号 | 要求 | 验证方式 | v0.1 状态 |
| --- | --- | --- | --- |
| `NFR-U-001` | CLI 支持 `--no-color` 与 `--format json` | 单测 | 已实现（`--no-color` / `NO_COLOR` / 非 TTY 自动关闭；JSON 走 stdout） |
| `NFR-U-002` | 终端输出默认中文（MVP）；允许 `--language zh-CN` / `en-US` 或 `GITGUARD_LANG` 切换英文（命令行优先） | 单测 | 已实现（`GGD-0014`）：环境变量接受 `zh` / `zh-CN` / `zh-Hans` / `en` / `en-US` / `en-GB`（大小写不敏感）；JSON 契约与日志字段名不本地化 |

### 5.6 可维护性与可观测性

| 编号 | 要求 | 验证方式 | v0.1 状态 |
| --- | --- | --- | --- |
| `NFR-M-001` | 日志按 `level + category` 结构化输出 | 代码审查 | 已实现：`--log-file` / `logging.file` 输出 JSON-lines（含 `level` / `category` 等字段） |
| `NFR-M-002` | `gitguard --verbose` 输出各规则命中耗时 | 单测 | 已实现（`GGD-0020`）：`--verbose`（Debug）在扫描完成后按 `rule_id` 输出 `Scan.RuleTiming`（求值次数 / 命中次数 / `duration_us`），只在正则实际求值时计时；普通扫描零计时开销；`--trace` 仍用于 Trace 逐行匹配细节 |

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

| 需求 ID | PRD 场景 | 设计文档 | 测试用例（v0.1 实际落地） | v0.1 实现状态 |
| --- | --- | --- | --- | --- |
| `FR-010` | 3.1 | `03-SAD §6 Hook 模块` / `模块设计/Hook.md` | `TC-FR-010-01~05`（`InstallHookUseCaseTests` / `HookEndToEndTests` / `CliHookLifecycleTests`） | 已实现 |
| `FR-011` | 3.1 | 同上 | `TC-FR-010-04`（`UninstallHookUseCaseTests` / `HookEndToEndTests`） | 已实现 |
| `FR-012` | 3.1 | `模块设计/Cli.md` / `模块设计/Hook.md` | `TC-FR-012-01`（`StatusUseCaseTests` / `CliHookLifecycleTests`） | 已实现 |
| `FR-020` | 3.2 | `模块设计/Secret.md` / `Privacy.md` / `模块设计/Policy.md` | `TC-FR-020-01~05`（`ScanEndToEndTests` / `ScanStagedUseCaseTests` / `UnifiedDiffParserTests` / `CliScanCommandTests`） | 已实现 |
| `FR-021` | 3.2 | `模块设计/Secret.md` ~ `模块设计/LocalPathUrl.md` | `TC-FR-021-*`（`DetectorTests` / `BuiltinRuleFixtureTests` / `DetectorRobustnessTests`） | 已实现（40 条规则，GGD-0033） |
| `FR-022` | 3.2 | `模块设计/Policy.md` | `PolicyEvaluatorTests` / `SeverityPolicyTests` / `PolicyDecisionTests` | 已实现 |
| `FR-023` | 3.2 | `模块设计/Cli.md` / `06-API接口设计` §4.2 | `CliScanCommandTests` + E2E S1（text / JSON） | 部分实现（无逐规则建议文案） |
| `FR-030` | 3.3 | `模块设计/Hook.md` | `TC-FR-030-01~02`（`ScanEndToEndTests` / `ScanPushUseCaseTests` / `PushUpdateTests`） | 已实现 |
| `FR-031` | 3.3 | `04-DDD` §4 | `TC-FR-030-01`（断言 Finding 携带 `commit_sha`） | 已实现 |
| `FR-032` | 3.2 / 3.3 | `10-异常处理设计` | `TC-DR-03-01~04`（`GitAccessCommitTests` / `ScanEndToEndTests` / `CliScanCommandTests` / E2E S7） | 已实现（10 / 11 / 12 / 13） |
| `FR-040` | 3.4 | `模块设计/Policy.md` | `AllowlistEntryTests` / `AllowlistUseCaseTests` / `SqliteAuditStoreAllowlistTests` / `CliAllowlistCommandTests` / `ExitCodeMapperTests` | 已实现（配置文件 + SQLite 管理，退出码 20 / 21） |
| `FR-041` | 3.4 | `模块设计/Policy.md` | `AllowlistMatcherTests` / `PolicyEvaluatorTests` / `GitGuardConfigLoaderTests` / `CliAllowlistCommandTests`（SQLite 条目参与扫描放行） | 已实现（配置文件与 SQLite 条目合并参与策略判定） |
| `FR-042` | 3.4 | `模块设计/Policy.md` | `PolicyEvaluatorTests` / `CliScanCommandTests`（汇总行） | 已实现（配置文件白名单） |
| `FR-050` | 3.5 | `模块设计/Hook.md` / `模块设计/Audit.md` | `TC-FR-050-01` / `TC-DR-06-01`（`BypassDetectionServiceTests` / `PendingScanRecorderTests` / `FilePendingScanStoreTests` / `GitAccessBypassTests` / `SqliteAuditStoreBypassTests` / `CliBypassTests` / `HookEndToEndTests` / E2E S8） | 已实现 |
| `FR-060` | 3.6 | `模块设计/Cli.md` | `TC-FR-060-01~04`（`ScanAllUseCaseTests` / `GitAccessFullScanTests` / `CliScanAllTests`） | 已实现（`GGD-0031`） |
| `FR-061` | 3.6 | `模块设计/Cli.md` | `TC-FR-061-01~05`（`BaselineUseCaseTests` / `CliScanBaselineTests` / `ExitCodeMapperTests`） | 已实现（`GGD-0032`） |
| `FR-070` | 全部 | `模块设计/Policy.md` | `TC-FR-070-01~09`（`GitGuardConfigLoaderTests` / `ScanStagedUseCaseTests` / `CliScanCommandTests` / `CliHookLifecycleTests` / `HookScriptBuilderTests` / `DetectorTests`） | 已实现 |
| `FR-071` | 全部 | `模块设计/Policy.md` | `TC-FR-070-01`（`GitGuardConfigLoaderTests`） | 已实现 |
| `FR-080` | 3.2 | `模块设计/Policy.md` / `06-API接口设计` §5.7 | `TC-FR-080-01~04`（`GitGuardConfigLoaderTests` / `RuleCatalogTests` / `CustomRuleDetectorTests` / `CliRulesCommandTests`） | 已实现（`GGD-0034`） |
| `FR-090` | 3.2 | `模块设计/Secret.md` + `ADR-0003` | `TC-FR-090-01~04`（`GitleaksAdapterTests` / `GitleaksProbeTests` / `GitleaksExecutableLocatorTests` / `GitleaksProcessRunnerTests` / `GitGuardConfigLoaderTests` / `GitGuardSessionTests` / `ScanStagedUseCaseTests` / `CliScanCommandTests`） | 已实现（`GGD-0035`） |
| `FR-100` | 全部 | `11-前端UI设计` | 无 | 未实现（v0.3） |

## 9. 未解问题

| # | 问题 | 影响 | 决策人 | 期限 |
| --- | --- | --- | --- | --- |
| Q-01 | 是否对 `--no-verify` 加 `post-commit` 探测，还是走 reflog 对比 | 中 | 项目负责人 | 已决议（GGD-0013）：`post-commit` +「父提交 + 暂存树」预测；reflog 无法区分正常 Hook 链且格式不稳定 |
| Q-02 | 自定义正则是否支持命名捕获组用于更精确上下文 | 低 | 项目负责人 | 已决议（GGD-0034）：命名捕获组语法原样支持，但检测与脱敏仍取第一个捕获组、名称不参与语义；上下文过滤由 `context_keywords`（任一关键字 OR）承担，不引入按组名取值 |

## 10. 变更记录

| 版本 | 日期 | 变更说明 |
| --- | --- | --- |
| 0.1.0 | 2026-10-15 | 初始化：定义 20 条 FR、10+ NFR、6 条 DR 的追溯矩阵 |
| 0.1.1 | 2026-10-16 | GGD-0007：§2.4 补充二进制文件跳过语义；§2.8 补充删除分支（`local-sha` 全零）跳过、空 stdin 无推送目标、跨 ref 提交去重与解析失败不回显输入 |
| 0.1.2 | 2026-10-16 | GGD-0008：§2.1/§2.2 与实现对齐——`--force` 语义限定为二进制/符号链接接管；托管识别改前两行精确匹配；备份为复制且分发脚本直接调用（非 `exec`）并合并退出码（GitGuard 非零优先）；安装三阶段零半安装 + 并发锁（退出码 50）；审计记录为 upsert 状态翻转（非新增行）；补充未安装卸载退出码 5 |
| 0.1.3 | 2026-10-16 | GGD-0009：§2.12 补充宽松解析（未知字段警告不阻断）、取值越界与 `use_gitleaks: true` 的 FailClosed 语义并标注实现落点；新增 §2.12.1 `FR-071` 用户级配置条目（原仅见于 PRD，追溯矩阵同步补行） |
| 0.1.4 | 2026-10-18 | GGD-0012 文档回填：§2.3 状态输出改为实际 Hook 分类与退出码 6；§2.4 / §2.8 测试用例编号按实际落地收敛；§2.5 `IEnumerable` 更正为 `IAsyncEnumerable` 并标注 21 条规则与装配顺序；§2.7 报告样例改为 v0.1 实际输出（无建议文案、文本 → stderr / JSON → stdout）；§2.9 / §2.10 / §2.11 / §2.13 标注白名单 CLI、绕过审计、全量扫描、Gitleaks 的未实现状态；§2.12 补充 `exclude` / 扫描类型开关 / `fail_closed` 仅解析告警；§4 `audit export` 标注未实现；§5 全部 NFR 表新增 v0.1 状态列（性能未测量、兼容矩阵部分、i18n 未实现等）；§8 追溯矩阵补齐 FR-031 / FR-032 / FR-041 / FR-042 / FR-061 / FR-080 / FR-100 并新增实现状态列 |
| 0.1.5 | 2026-10-21 | GGD-0013：§2.10 按实现重写 FR-050（父提交 + 暂存树预测、`--record-pending` 隐藏选项、`GITGUARD_SKIP=1` 仅 Hook 模式、标记文件路径 / 原子写 / 失败语义、`NoVerify` / `GitGuardSkip` 判定、`Bypass` 运行与审计载荷、post-commit 退出码）；§5.3 NFR-S-004 标记已覆盖；§8 FR-050 追溯补齐测试类与 E2E S8；§9 Q-01 记录决议 |
| 0.1.6 | 2026-10-21 | GGD-0019：§2.12 `exclude` 改为已消费语义（`paths` 匹配仓库相对路径、`files` 匹配任意深度文件名、命中不计入 `skipped_files`、空项 FailClosed）；§8 FR-070 追溯补 `TC-FR-070-05` 并标记已实现 |
| 0.1.7 | 2026-10-21 | GGD-0014：§5.5 `NFR-U-002` 标记已实现（`--language` 优先于 `GITGUARD_LANG`，环境变量别名与 JSON / 日志不变约定） |
| 0.1.8 | 2026-10-21 | GGD-0020：§5.1 性能 NFR 回填实测结果（P-002 / P-004 达标；P-001 / P-003 受宿主进程创建开销影响未达标并登记 `GGD-0023`）；§5.6 `NFR-M-002` 标记已实现（`--verbose` 输出 `Scan.RuleTiming`） |
| 0.1.9 | 2026-10-21 | GGD-0023：§2.8 / §4 推送差异来源改为一次批量 `git log --no-walk=unsorted --stdin`；§5.1 P-003 回填复测 P95 669.78 ms 达标（较 4687.07 ms 下降约 86%），P-001 / P-002 / P-004 同步最新实测 |
| 0.1.10 | 2026-10-21 | GGD-0022：§4 审计查询 / 导出接口标记已实现（`audit list` 分页 / 筛选，`audit export` 仅脱敏字段；`audit view` 仍为 v0.2） |
| 0.1.11 | 2026-10-21 | GGD-0021：§5.2 NFR-A-001 标记已覆盖（Husky / lefthook / pre-commit 真实安装矩阵 `TC-COMPAT-01~04` + CI `hook-compat`） |
| 0.1.12 | 2026-10-22 | GGD-0027：§5.2 NFR-A-002 改为部分覆盖（CI 收敛为 Windows 单平台，macOS / Ubuntu 矩阵暂缓 `GGD-0028`）；NFR-A-001 的 CI 作业范围同步为 Windows；§5.3 NFR-S-001 标注 Linux `strace` 作业暂缓，编译期封禁仍生效 |
| 0.1.13 | 2026-10-22 | GGD-0030：§2.9 FR-040 标记已实现（`allowlist add/remove/list`、SQLite 持久化 + `AllowlistChanged` 审计事件、配置文件与 SQLite 条目合并参与扫描）；§8 追溯矩阵 FR-040 / FR-041 更新测试类与实现状态 |
| 0.1.14 | 2026-10-22 | `GGD-0028` 取消，NFR-A-001 / A-002 / S-001 的跨平台 CI 与 Linux `strace` 描述改为「不再计划」；v0.1 性能与真实仓库验收由用户亲自执行 |
| 0.1.15 | 2026-10-22 | GGD-0031：§2.11 FR-060 按实现重写（空树 HEAD 快照、历史枚举与提交归属、`--since` 不含起点语义、`--max-files` 提交边界软上限与 `next_since_sha` 续扫、空仓库 Clean）；§8 追溯矩阵 FR-060 更新测试类并标记已实现 |
| 0.1.16 | 2026-10-22 | GGD-0032：新增 §2.11.1 FR-061 基线文件（schema / 默认路径、生成不抑制当次命中、默认自动消费、`AllowedByBaseline` 动作与 `Warned` 结果、允许清单优先级、13 / 31 错误码、本地盐不可移植限制）；§8 追溯矩阵 FR-061 更新测试类并标记已实现 |
| 0.1.17 | 2026-10-22 | GGD-0033：§2.5 FR-021 规则库扩充至 40 条（Secret 16 / Privacy 9 / LocalPath 7 / Url 8），补四类检测器覆盖说明与夹具有效性要求；§8 追溯矩阵标记 40 条规则 |
| 0.1.18 | 2026-10-22 | GGD-0034：新增 §2.12.2 FR-080 自定义正则规则（`rules.custom` 字段 / 校验 / 合并与启用语义、200ms 超时、超长行不跳过、`gitguard rules list/view` 与退出码 7 / 40）；§8 追溯矩阵标记已实现；§9 Q-02 记录命名捕获组决议 |
| 0.1.19 | 2026-10-22 | GGD-0035：§2.12 更新 `use_gitleaks` / `gitleaks_path` 配置语义（空白路径 → 13，缺失可执行文件 → 告警降级）；§2.13 FR-090 按实现重写（启动探测、逐文件暂存、命令与退出码、GLK 映射、禁用规则过滤、跨引擎去重、FailClosed 与临时目录清理）；§4 可选依赖说明与 §8 追溯矩阵同步标记已实现 |
| 0.1.20 | 2026-10-22 | GGD-0036：§4 审计接口补充 `gitguard audit view <run-id>`（单次运行摘要与 Finding 明细，脱敏输出，退出码 7 / 30）并标记已实现 |
| 0.1.21 | 2026-10-22 | GGD-0037：§2.5 补充 40 条规则严重度分布；§2.9 增加误报治理流程链接；§2.11.1 校正基线 `entries` 实际字段（`fingerprint` / `rule_id` / `file_path`，读取仅强制校验指纹） |
| 0.1.22 | 2026-10-23 | GGD-0038：§2.12 接线 `hooks.*`（安装期 Hook 子集 / 可执行文件路径覆盖 / 全禁用退出码 13）与 `scan.pre_commit` / `scan.pre_push`（仅 Hook 模式生效，跳过不产生绕过事件）；「仅警告」项收敛为 `scan.fail_closed`；§8 FR-070 追溯扩展至 `TC-FR-070-01~08` |
| 0.1.23 | 2026-10-23 | GGD-0039：§2.5 UrlDetector 补公共主机白名单；§2.12 新增 `rules.url.public_hostname_whitelist` 语义（规范化 / 非法项 13 / 项目级列表替换）并更新实现状态；§8 FR-070 追溯扩展至 `TC-FR-070-01~09` |
