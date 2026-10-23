# GitGuard

[![CI](https://github.com/Camefor/GitGuard/actions/workflows/ci.yml/badge.svg?branch=master)](https://github.com/Camefor/GitGuard/actions/workflows/ci.yml)

面向开发者的本地 Git 敏感信息防泄漏工具：在 `git commit` 与 `git push` 前通过 Hook 拦截 Token、密钥、密码、手机号、本地路径、内网 URL 等敏感信息。所有扫描与审计均在本机完成，不上传任何数据。

## 当前状态（v0.1 已完成，v0.2 开发完成，v0.3 开发中，2026-10-23）

**可以试用。** 「安装 Hook → 提交拦截 → 推送拦截 → 绕过留痕」的核心链路已经完成，并有真实 `git commit` / `git push` 的端到端测试覆盖。v0.2 的 `scan --all`、基线文件、40 条内置规则、自定义正则规则、可选 Gitleaks 引擎、`audit view` 与 URL 公共主机名白名单已落地；真实项目试用验收待执行。v0.3 桌面端已启动，Avalonia 11.3 LTS 工程骨架与四区导航壳（仓库 / 规则 / 日志 / 设置、中英切换、系统主题跟随）可用。

功能完成度（按 PRD §4 的 21 条 FR 统计）：

| 范围 | 完成度 | 说明 |
| --- | --- | --- |
| P0 核心功能（11 条） | 约 95% | 10 条完整实现；终端报告缺少逐规则「建议」文案（其余字段齐全） |
| 全部 FR（21 条） | 约 93% | 19 条完整 + 1 条部分 + 1 条未实现（完整计 1、部分计 0.5） |

已可用：

- `gitguard install` / `uninstall` / `status`：安装、卸载与查询 Hook（`pre-commit` / `pre-push` / `post-commit`）
- `gitguard scan --staged`：暂存区扫描（严格基于 `git diff --cached`）
- `gitguard scan --push`：推送范围扫描（覆盖范围内全部提交，一次批量取差异，Finding 带来源 commit SHA）
- `gitguard scan --all`：HEAD 全量快照扫描；`--include-history` 扫描历史提交，`--max-files` 截断后可用 `--since` 续扫
- 基线文件：`scan --all --baseline <path>` 生成 `gitguard.baseline.v1`，后续全量扫描自动消费 `.gitguard-baseline.json`，历史噪声不阻断但保留在报告中
- `gitguard audit list` / `audit view` / `audit export`：本地审计查询（分页 / 时间 / 结果筛选）、单次运行摘要与 Finding 明细、脱敏 JSON / CSV 导出
- `gitguard allowlist add` / `remove` / `list`：按规则 / 路径 / 指纹管理 SQLite 白名单（无需 Git 仓库）
- 40 条内置规则：Secret 16 / Privacy 9 / LocalPath 7 / Url 8（每条规则均含正反例夹具）
- 自定义正则规则：`.gitguard.yml` 的 `rules.custom` 支持命名、严重度、上下文关键字与文件 glob；`gitguard rules list` / `view` 只读查看规则目录
- 可选 Gitleaks 引擎：`rules.secrets.use_gitleaks: true` + `gitleaks_path` 接入外部 Gitleaks 8.x，命中映射为 `GLK/SECRET/*` 并与内置规则按指纹去重；可执行文件缺失时告警降级，不阻断扫描
- 文本 / JSON 报告、严重度策略、配置文件 + SQLite 白名单、`exclude` 路径排除
- `--no-verify` 与 `GITGUARD_SKIP=1` 绕过检测及 SQLite 审计落库
- 中英文输出、Native AOT 单文件发布
- `GitGuard.Desktop` 桌面端工程骨架：Avalonia 11.3 LTS 导航壳、中 / 英即时切换、跟随系统明暗主题；业务页面待 v0.3 后续任务

尚未实现（不影响核心试用）：

- Avalonia 桌面端业务页面：仓库登记与详情、扫描历史与发现项、白名单管理、规则库、设置与日志导出（`GGD-0042` ~ `GGD-0046`）

## 快速开始（从源码构建）

### 1. 环境要求

- .NET SDK 10.0.400+（版本由仓库 `global.json` 固定）
- Git ≥ 2.9（运行时调用系统 Git）

### 2. 构建并确认可执行文件

```bash
dotnet build GitGuard.sln -c Release
```

构建产物（相对 GitGuard 仓库根目录）：

- Windows：`src\GitGuard.Cli\bin\Release\net10.0\gitguard.exe`
- Linux / macOS：`src/GitGuard.Cli/bin/Release/net10.0/gitguard`

构建产物目录**不会自动加入 `PATH`**。如果没有配置 PATH，每次运行都必须使用可执行文件的绝对路径。以本机为例，产物绝对路径为：

```text
D:\PersonalSpace\repo\GitGuard\src\GitGuard.Cli\bin\Release\net10.0\gitguard.exe
```

#### 方式 A：直接使用绝对路径（推荐，临时试用最省事）

Windows cmd：

```bat
D:\PersonalSpace\repo\GitGuard\src\GitGuard.Cli\bin\Release\net10.0\gitguard.exe version
```

Windows PowerShell（路径含空格时必须用 `&` 调用）：

```powershell
& "D:\PersonalSpace\repo\GitGuard\src\GitGuard.Cli\bin\Release\net10.0\gitguard.exe" version
```

Git Bash / Linux / macOS：

```bash
/path/to/GitGuard/src/GitGuard.Cli/bin/Release/net10.0/gitguard version
```

#### 方式 B：把产物目录加入 PATH

配置后可直接使用 `gitguard` 命令（以下仅对当前终端会话生效）：

```powershell
# Windows PowerShell
$env:PATH = "D:\PersonalSpace\repo\GitGuard\src\GitGuard.Cli\bin\Release\net10.0;$env:PATH"
```

```bash
# Git Bash / Linux / macOS
export PATH="/path/to/GitGuard/src/GitGuard.Cli/bin/Release/net10.0:$PATH"
```

> 下文示例统一写作 `gitguard`。**如果没有配置 PATH，请把 `gitguard` 替换为 `gitguard.exe` 的绝对路径**，不要直接输入 `gitguard`。

### 3. 在仓库中启用

先进入你的目标 Git 仓库，再执行安装。以下是**未配置 PATH** 时的完整写法：

Windows cmd：

```bat
cd /d D:\work\my-project
D:\PersonalSpace\repo\GitGuard\src\GitGuard.Cli\bin\Release\net10.0\gitguard.exe install
D:\PersonalSpace\repo\GitGuard\src\GitGuard.Cli\bin\Release\net10.0\gitguard.exe status
```

Windows PowerShell：

```powershell
cd D:\work\my-project
& "D:\PersonalSpace\repo\GitGuard\src\GitGuard.Cli\bin\Release\net10.0\gitguard.exe" install
& "D:\PersonalSpace\repo\GitGuard\src\GitGuard.Cli\bin\Release\net10.0\gitguard.exe" status
```

Git Bash / Linux / macOS：

```bash
cd /path/to/your/repo
/path/to/GitGuard/src/GitGuard.Cli/bin/Release/net10.0/gitguard install
/path/to/GitGuard/src/GitGuard.Cli/bin/Release/net10.0/gitguard status
```

如果已将产物目录加入 PATH，则可以简写为：

```bash
gitguard install      # 注册 pre-commit / pre-push / post-commit
gitguard status       # 查看 Hook 状态、Git 版本与配置加载链
```

安装后按正常流程 `git commit` / `git push` 即可，命中阻断规则时命令会失败并输出脱敏报告。

> `gitguard install` 会把**本次执行的可执行文件绝对路径**写入 Hook。通过绝对路径安装时，Hook 中固化的也是该绝对路径；若之后移动了可执行文件，或从 Debug 切换到 Release / AOT 产物，请在目标仓库重新执行一次 `install` 刷新。Docker、临时目录等无法长期保留路径的场景请使用 AOT 单文件。

### 4. 快速验证

以下命令中的 `gitguard` 均指可执行文件；未配置 PATH 时请替换为绝对路径。

Windows cmd：

```bat
cd /d D:\work\my-project
> leak-test.txt echo aws_access_key_id = AKIAIOSFODNN7EXAMPLE
git add leak-test.txt
git commit -m "test"

D:\PersonalSpace\repo\GitGuard\src\GitGuard.Cli\bin\Release\net10.0\gitguard.exe scan --staged

git reset HEAD leak-test.txt
del leak-test.txt
```

Windows PowerShell：

```powershell
cd D:\work\my-project
Set-Content -Path leak-test.txt -Value 'aws_access_key_id = AKIAIOSFODNN7EXAMPLE'
git add leak-test.txt
git commit -m "test"          # 预期：被 pre-commit 阻断，退出码 1

& "D:\PersonalSpace\repo\GitGuard\src\GitGuard.Cli\bin\Release\net10.0\gitguard.exe" scan --staged

git reset HEAD leak-test.txt
Remove-Item leak-test.txt
```

Git Bash / Linux / macOS：

```bash
# 构造一个会被拦截的示例密钥（AWS 官方文档中的假值，非真实凭据）
echo 'aws_access_key_id = AKIAIOSFODNN7EXAMPLE' > leak-test.txt
git add leak-test.txt
git commit -m "test"          # 预期：被 pre-commit 阻断，退出码 1

gitguard scan --staged        # 手动扫描，报告输出到 stderr
git reset HEAD leak-test.txt && rm leak-test.txt   # 清理
```

推送扫描由 `pre-push` 自动触发；也可手动构造 Git 的 stdin 约定来执行。

Windows PowerShell（未配置 PATH 的绝对路径写法）：

```powershell
"refs/heads/master $(git rev-parse HEAD) refs/heads/master 0000000000000000000000000000000000000000" |
  & "D:\PersonalSpace\repo\GitGuard\src\GitGuard.Cli\bin\Release\net10.0\gitguard.exe" scan --push
```

Git Bash / Linux / macOS：

```bash
printf 'refs/heads/master %s refs/heads/master %s\n' \
  "$(git rev-parse HEAD)" "0000000000000000000000000000000000000000" \
  | gitguard scan --push
```

`remote-sha` 全零表示新分支，扫描从 `local-sha` 可达的全部提交。

全量扫描（v0.2）：

```bash
gitguard scan --all                               # HEAD 全部被跟踪文件（不读工作区）
gitguard scan --all --include-history             # 追加扫描全部历史提交
gitguard scan --all --include-history --max-files 500
gitguard scan --all --since <next_since_sha>      # 按上次截断点续扫更早历史
```

- `--staged` / `--push` / `--all` 三选一；`--include-history` / `--since` / `--max-files` 只能与 `--all` 组合。
- `--max-files` 按提交边界软截断：当前提交会完整扫描，可能略超上限；JSON 的 `full_scan.next_since_sha` 给出续扫起点。
- HEAD 快照通过空树差异读取已提交内容，不会把未提交的工作区改动算入结果；空仓库按 Clean 处理。

### 5. 基线文件（历史噪声治理）

首次接入老仓库时，可以先把已确认的历史噪声写入基线，后续扫描只提示、不再阻断：

```bash
gitguard scan --all --baseline .gitguard-baseline.json   # 生成基线（当次命中仍会阻断）
gitguard scan --all                                     # 后续自动消费默认基线
```

- 生成当次不会抑制命中，便于先确认扫描结果；基线文件使用 `gitguard.baseline.v1` schema，按指纹去重。
- 后续未指定 `--baseline` 的全量扫描会读取仓库根默认的 `.gitguard-baseline.json`；自定义路径只用于生成，不会在后续自动消费。
- 基线命中的 Finding 仍保留在报告 / 审计中，动作显示为 `AllowedByBaseline`（JSON 汇总 `ignored_by_baseline`）；仅含基线抑制时结果 `Warned`、退出码 0。允许清单优先于基线。
- 指纹为带每用户本地盐的 HMAC-SHA256，**基线文件不可跨机器 / 用户复用**；更换机器后需重新生成。
- 基线文件损坏或 schema 非法 → 退出码 13；写入失败 → 退出码 31。

### 6. 审计查询与导出

`audit` 查询整个本地审计库（包含所有仓库），不要求当前目录是 Git 仓库：

```bash
gitguard audit list                          # 最近 20 条（文本 → stderr）
gitguard audit list --limit 50 --offset 50   # 分页（limit 1~1000）
gitguard audit list --since 2026-10-01 --result Blocked
gitguard --format json audit list            # 机器可读 JSON → stdout

gitguard audit export --output ~/gitguard-audit.json --format json
gitguard audit export --output ~/gitguard-audit.csv --format csv
```

- 导出文件只包含脱敏片段与 HMAC 指纹，不含任何原文；目标文件已存在时原子覆盖。
- `audit export` 后面的 `--format` 表示**导出文件格式**（`json` 默认 / `csv`）；若要让导出摘要以 JSON 输出，把全局 `--format json` 放在 `audit` 之前。
- `--result` 只接受 `Clean` / `Warned` / `Blocked` / `Failed`；非法参数退出码 7，导出写入失败退出码 31。

### 7. 白名单管理

`allowlist` 管理本机审计库中的 SQLite 白名单条目，不要求当前目录是 Git 仓库：

```bash
gitguard allowlist add --rule GGD/SECRET/AWS-AKID --reason "已知误报登记"
gitguard allowlist add --path "docs/examples/**" --reason "示例目录放行"
gitguard allowlist add --fingerprint <64位HMAC指纹> --reason "已确认非真实凭据"
gitguard allowlist list --limit 20 --offset 0
gitguard allowlist remove --id <条目 GUID>
```

- `add` 的 `--rule` / `--path` / `--fingerprint` 至少提供一个，可组合；`--reason` 必填且至少 5 字符。
- 条目写入本地 SQLite 并记录 `AllowlistChanged` 审计事件；后续 `scan --staged` / `scan --push` 会同时应用配置文件的 `allowlist.entries` 与 SQLite 条目。
- 校验失败退出码 20，条目不存在退出码 21；可加全局 `--format json` 获取机器可读结果。
- 误报判定、白名单粒度选择、复核周期与误报率统计方法见 [docs/19-误报治理指南.md](docs/19-误报治理指南.md)。

### 8. 自定义规则与规则查询

在 `.gitguard.yml`（或用户级 `config.yml`）中声明项目特定正则：

```yaml
version: 1
rules:
  custom:
    - id: USR/SECRET/INTERNAL-TOKEN
      name: 内部令牌
      severity: High
      pattern: "INT-[A-Z0-9]{8}"
      context_keywords:
        - token
      file_path_globs:
        - "src/**"
```

```bash
gitguard rules list --category Secret --enabled   # 文本列表（stderr）
gitguard rules view USR/SECRET/INTERNAL-TOKEN     # 规则详情
gitguard rules view USR/SECRET/INTERNAL-TOKEN --show-pattern
gitguard --format json rules list                 # 机器可读 JSON（stdout）
```

- 规则 ID 必须以 `USR/` 开头，类别段仅支持 `SECRET` / `PRIVACY` / `LOCALPATH` / `URL`；非法配置（包括无法编译的正则、重复 ID）→ 退出码 13。
- `context_keywords` 为大小写不敏感的任一关键字门禁；`file_path_globs` 匹配仓库相对路径；`enabled` 默认 `true`。
- 自定义规则与内置规则走同一检测 / 策略管线，参与 `disabled_rules` 与类别开关；共享 200ms 正则超时（FailClosed）。
- `rules list` / `view` 不要求 Git 仓库；默认隐藏正则原文，仅 `--show-pattern` 输出；未知规则退出码 40，非法类别退出码 7。

### 9. 卸载

```bash
gitguard uninstall    # 移除 GitGuard Hook 并还原被包裹的原 Hook
```

未配置 PATH 时（Windows cmd）：

```bat
cd /d D:\work\my-project
D:\PersonalSpace\repo\GitGuard\src\GitGuard.Cli\bin\Release\net10.0\gitguard.exe uninstall
```

## 配置

- 项目级：`<repo>/.gitguard.yml`
- 用户级：`~/.config/gitguard/config.yml`
- 优先级：命令行参数 > 环境变量 > 项目级 > 用户级 > 内置默认
- 完整 Schema 见 [docs/模块设计/Policy.md](docs/模块设计/Policy.md)

最小示例：

```yaml
version: 1

policy:
  critical: block     # 硬下限，不可改为 allow
  high: warn          # 默认 block；按需降级

hooks:
  pre_commit: true                  # install 缺省是否注册 pre-commit
  pre_push: true                    # install 缺省是否注册 pre-push
  post_commit_bypass_detection: true  # 是否注册 post-commit 绕过检测
  # gitguard_executable_path: C:/tools/gitguard.exe  # 覆盖写入 Hook 脚本的可执行文件路径

scan:
  pre_commit: true    # false = Hook 模式跳过暂存区扫描（手动 scan 不受影响）
  pre_push: true      # false = Hook 模式跳过推送范围扫描

rules:
  url:
    public_hostname_whitelist: []   # 可信公共文档主机，如 github.com / "*.example.com"

exclude:
  paths:
    - "**/bin/**"
    - "**/node_modules/**"
  files:
    - "*.min.js"
```

- `rules.url.public_hostname_whitelist`：精确主机或 `*.example.com` 通配（不覆盖裸域），大小写不敏感；命中主机的 URL 规则 Finding 不产出。默认空列表；空白项或含协议 / 端口 / 路径的非法项 → 退出码 13。

常用环境变量：

- `GITGUARD_SKIP=1`：仅在 Hook 模式生效，跳过本次扫描并在 `post-commit` 登记为绕过事件
- `GITGUARD_LANG=en-US`：切换英文输出（`--language` 优先）
- `NO_COLOR`：禁用彩色输出

本地数据位置：

| 内容 | 位置 |
| --- | --- |
| 审计库 | Windows `%LOCALAPPDATA%\GitGuard\audit.db`；macOS `~/Library/Application Support/GitGuard/audit.db`；Linux `~/.local/share/gitguard/audit.db` |
| 指纹盐 | 数据目录下 `cache/fingerprint.salt` |
| 用户配置 | `~/.config/gitguard/config.yml` |

审计库只保存脱敏片段与 HMAC 指纹，不保存敏感原文。可用 `gitguard audit list` 查询、`gitguard audit export` 导出（均为脱敏字段），也可直接用 `sqlite3` 查看 `audit.db`。

## 发布 Native AOT 单文件（可选）

```bash
# Windows
dotnet publish src/GitGuard.Cli/GitGuard.Cli.csproj -c Release -r win-x64 -p:PublishAot=true -o artifacts/publish/gitguard-win-x64

# Linux / macOS 将 -r 换成 linux-x64 / osx-arm64 / osx-x64
```

Windows 链接阶段若提示找不到 `vswhere.exe`，将 `C:\Program Files (x86)\Microsoft Visual Studio\Installer` 加入 `PATH`。

仓库包含 `.github/workflows/release.yml`，推送 `v*` Tag 或手动触发即可在 Actions 中生成各平台 zip 产物（附 `SHA256SUMS`）。

## 开发与质量现状

```bash
dotnet build GitGuard.sln -c Release
dotnet test GitGuard.sln
```

- 全量测试：1405 用例（1396 通过 + 9 个平台 / 工具链相关跳过，0 失败）；真实 Hook 框架兼容用例需设置 `GITGUARD_HOOK_COMPAT=1`（CI `hook-compat` 作业，当前仅 Windows）
- CI 门禁：`dotnet build` + `dotnet format --verify-no-changes` + 七类测试作业（Core / Detection / Infrastructure / Application / CLI / Desktop / E2E）；Git 2.9.3 / 2.40.1 / 最新稳定版的 E2E 兼容矩阵（`git-compat` 作业）；每次运行归档 TRX 与 Cobertura 覆盖率构件（保留 30 天，失败时同样上传）
- 基准（Windows 11 / Native AOT）：暂存区扫描 P95 311 ms（目标 ≤ 500 ms）；推送扫描 P95 670 ms（目标 ≤ 2 s）；检测吞吐 P50 26 MB/s（目标 ≥ 20 MB/s）
- 基准方法与本机数据见 [benchmarks/README.md](benchmarks/README.md)

## 已知限制

- Hook 兼容性：Husky v9 / lefthook v2 / pre-commit 4.x 的安装包裹、真实提交链式执行、阻断语义与卸载还原已在真实安装仓库中验证（`TC-COMPAT-01~04`）；CI `hook-compat` 作业当前仅覆盖 Windows（跨平台矩阵不再计划，`GGD-0028` 已取消）。
- CI 当前仅运行 Windows 单平台；macOS / Ubuntu 测试矩阵与 Linux DR-01 `strace` 作业不再计划（`GGD-0028` 已取消）。
- Windows 下审计库目前依赖用户目录默认 ACL，显式收紧计划中。
- Git 2.9.3 / 2.40.1 / 最新稳定版已由 CI `git-compat` 矩阵覆盖；更老版本未验证。
- `scan --staged` 只扫描暂存区，不读取未暂存内容；全量扫描（`scan --all`）已支持 HEAD 快照与历史续扫。
- 基线指纹使用每用户本地盐，基线文件不能跨机器 / 用户复用；更换机器后需重新生成。

## 文档入口

- 产品与范围：[docs/00-项目概述.md](docs/00-项目概述.md)
- 需求规格：[docs/02-需求规格说明书(SRS).md](<docs/02-需求规格说明书(SRS).md>)
- CLI 命令契约：[docs/06-API接口设计.md](docs/06-API接口设计.md)
- 发行与分发：[docs/14-部署文档.md](docs/14-部署文档.md)
- 误报治理：[docs/19-误报治理指南.md](docs/19-误报治理指南.md)
- 文档索引与导航：[docs/README.md](docs/README.md)
- Agent 工作指南与任务台账：[docs/AGENT-GUIDE.md](docs/AGENT-GUIDE.md)
