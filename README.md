# GitGuard

[![CI](https://github.com/Camefor/GitGuard/actions/workflows/ci.yml/badge.svg?branch=master)](https://github.com/Camefor/GitGuard/actions/workflows/ci.yml)

> 面向开发者的本地 Git 敏感信息防泄漏工具：在 `git commit` 与 `git push` 前通过 Hook 拦截 Token、密钥、密码、手机号、本地路径、内网 URL 等敏感信息。扫描与审计全部在本机完成，不上传代码或敏感原文。

当前版本：**1.1.0**

## 项目介绍

GitGuard 解决的是「敏感信息被误提交、误推送」的问题。安装后无需改变日常 Git 工作流：`git commit` 时扫描暂存区，`git push` 时扫描本次推送范围；命中阻断规则时命令失败并输出脱敏报告，同时把本次扫描写入本地审计库。

项目由两部分组成：

- **GitGuard CLI**：扫描引擎、Hook 安装与管理、审计 / 白名单 / 规则查询，适合日常提交和自动化脚本。
- **GitGuard Desktop**：Avalonia 桌面管理端，提供仓库登记、Hook 一键安装 / 卸载、扫描历史、白名单、规则库、跨仓库日志与设置。

### 核心原则

- **本地闭环**：扫描、规则匹配、白名单判断和审计落库均在本机完成。
- **零明文留存**：审计只保存规则 ID、文件相对路径、行号、严重度、脱敏片段与 HMAC 指纹，不保存敏感原文。
- **失败关闭**：扫描异常、超时或配置损坏时默认阻断，不静默放行。
- **Hook 兼容**：通过分发器包裹已有 Hook，保留并链式执行 Husky / lefthook / pre-commit 等既有脚本，卸载时可还原。
- **可治理**：严重度策略、白名单、基线文件和自定义规则共同处理误报与历史噪声。

### 检测范围

| 类别 | 典型内容 |
| --- | --- |
| Secret | 云厂商 Access Key、API Token、私钥、数据库连接串、Webhook 等 |
| Privacy | 手机号、身份证号、邮箱、银行卡、IBAN、护照号等 |
| LocalPath | Windows 盘符 / 用户目录、Unix 主目录、UNC 共享、服务器路径等 |
| Url | 内网地址、带凭据的 URL、预签名 URL、Token 查询参数等 |

## 功能清单

| 模块 | 能力 |
| --- | --- |
| 内置规则 | 60 条规则：Secret 21 / Privacy 14 / LocalPath 12 / Url 13；支持上下文关键字、文件 glob、熵值 / Luhn / 身份证 / IBAN 校验 |
| 自定义规则 | `.gitguard.yml` 中声明 `USR/` 前缀正则规则，支持严重度、上下文关键字、文件 glob 与启用开关 |
| 外部引擎 | 可选接入 Gitleaks 8.x，命中映射为 `GLK/SECRET/*` 并与内置规则按指纹去重；缺失时告警降级 |
| 扫描模式 | 暂存区扫描、推送范围扫描、HEAD 全量扫描、历史提交扫描与断点续扫 |
| Hook 拦截 | 安装 `pre-commit` / `pre-push` / `post-commit`；包裹已有 Hook；支持强制接管二进制 / 符号链接 Hook |
| 策略与降噪 | 严重度阻断 / 警告 / 放行策略、URL 公共主机名白名单、Markdown 围栏抑制、路径与文件名排除 |
| 基线 | `gitguard.baseline.v1` 基线文件，按指纹抑制历史噪声，支持默认消费与续扫 |
| 白名单 | 按规则 ID、文件路径 glob、指纹增删查；配置条目与 SQLite 条目同时生效 |
| 审计 | 本地 SQLite 审计、分页查询、单次运行明细、脱敏 JSON / CSV 导出 |
| 规则查询 | `rules list` / `rules view` 查看内置与自定义规则，正则默认隐藏 |
| CLI 体验 | 中英文输出、文本 / JSON 双格式、文件日志、Native AOT 单文件发布 |
| 桌面端 | 多仓库管理、Hook 一键管理、立即扫描、扫描历史与发现项、白名单、规则库、跨仓库日志导出、语言 / 主题设置 |
| 跨平台 | Windows / macOS / Linux；CLI 提供 win-x64 / linux-x64 / osx-arm64 / osx-x64 产物，桌面端提供 MSIX / DMG / AppImage |

## 安装

### 方式 A：下载发行包

从 [GitHub Releases](https://github.com/Camefor/GitGuard/releases) 下载对应平台的产物：

| 组件 | 产物 |
| --- | --- |
| CLI | `gitguard-<version>-<rid>.zip`（内含 `gitguard` / `gitguard.exe` 与 `SHA256SUMS`） |
| Desktop | Windows x64 `gitguard-<version>-win-x64.msix`；macOS `gitguard-<version>-osx-arm64.dmg` / `osx-x64.dmg`；Linux x64 `gitguard-<version>-linux-x64.AppImage` |

若对应版本尚未发布，请使用下方的源码构建方式。解压 CLI 后将所在目录加入 `PATH`。桌面端安装包默认未签名 / 未公证：Windows MSIX 需要开发者模式或信任自签证书，macOS DMG 首次启动需手动放行；正式分发前请按 [部署文档](docs/14-部署文档.md) 配置签名与公证。

### 方式 B：从源码构建

环境要求：

- .NET SDK 10.0.400+（版本由仓库 `global.json` 固定）
- Git ≥ 2.9（GitGuard 调用系统 Git，不自带 Git）

```bash
dotnet build GitGuard.sln -c Release
```

构建产物：

- CLI：`src/GitGuard.Cli/bin/Release/net10.0/gitguard.exe`（Windows）或 `gitguard`（Linux / macOS）
- Desktop：`src/GitGuard.Desktop/bin/Release/net10.0/GitGuard.Desktop.exe`（Windows）或对应平台可执行文件

构建产物**不会自动加入 `PATH`**。可按需临时加入：

```powershell
# Windows PowerShell
$env:PATH = "D:\path\to\GitGuard\src\GitGuard.Cli\bin\Release\net10.0;$env:PATH"
```

```bash
# Git Bash / Linux / macOS
export PATH="/path/to/GitGuard/src/GitGuard.Cli/bin/Release/net10.0:$PATH"
```

未配置 `PATH` 时，下文命令中的 `gitguard` 请替换为可执行文件绝对路径。

可选：发布 Native AOT 单文件（目标机无需 .NET 运行时）：

```bash
dotnet publish src/GitGuard.Cli/GitGuard.Cli.csproj -c Release -r win-x64 -p:PublishAot=true -o artifacts/publish/gitguard-win-x64
```

Linux / macOS 将 `-r` 换成 `linux-x64` / `osx-arm64` / `osx-x64`。

## CLI 使用方法

### 1. 在仓库中安装 Hook

先进入目标 Git 仓库，再执行安装：

```bash
cd /path/to/your-repo
gitguard install
gitguard status
```

- `install` 默认按配置安装 `pre-commit` / `pre-push` / `post-commit` 三类 Hook；也可用 `--hook pre-commit` 只安装指定 Hook。
- 已有文本 Hook 会自动备份为 `<hook>.local.bak` 并被包裹；二进制 / 符号链接 Hook 需加 `--force` 接管。
- `install` 会把**本次执行的可执行文件绝对路径**写入 Hook。移动可执行文件或切换 Debug / Release 产物后，请在仓库中重新执行一次 `install`。
- `status` 显示 Hook 状态、Git 版本与配置加载链。

### 2. 验证拦截

构造一个会被拦截的示例（AWS 官方文档中的假值，非真实凭据）：

```bash
printf 'aws_access_key_id = AKIAIOSFODNN7EXAMPLE\n' > leak-test.txt
git add leak-test.txt
git commit -m "test"          # 预期：被 pre-commit 阻断，退出码 1
git reset HEAD leak-test.txt && rm leak-test.txt
```

Windows PowerShell 把第一行改为：

```powershell
Set-Content -Path leak-test.txt -Value 'aws_access_key_id = AKIAIOSFODNN7EXAMPLE'
```

安装完成后，日常提交和推送无需额外操作：

```bash
git commit -m "feat: ..."
git push
```

如需显式绕过，可使用 `git commit --no-verify` 或 `GITGUARD_SKIP=1 git commit ...`。绕过事件会由 `post-commit` 登记到审计库，不会被伪装成正常通过。

### 3. 手动扫描

```bash
gitguard scan --staged                                  # 暂存区（严格基于 git diff --cached）
gitguard scan --all                                     # HEAD 中被跟踪文件的完整快照
gitguard scan --all --include-history                   # 追加扫描历史提交
gitguard scan --all --include-history --max-files 500   # 按提交边界软截断
gitguard scan --all --since <next_since_sha>            # 从上次截断点继续扫描更早历史
```

- `--staged` / `--push` / `--all` 三选一；`--include-history` / `--since` / `--max-files` 只能与 `--all` 组合。
- `scan --all` 读取的是 HEAD 快照，不会把工作区未提交改动计入结果。
- 推送扫描通常由 `pre-push` 自动执行；手动执行需按 Git 的 stdin 约定传入推送行：

```bash
printf 'refs/heads/master %s refs/heads/master %s\n' \
  "$(git rev-parse HEAD)" "0000000000000000000000000000000000000000" | gitguard scan --push
```

`remote-sha` 全零表示新分支，将扫描从 `local-sha` 可达的全部提交。

### 4. 基线文件（历史噪声治理）

首次接入老仓库时，可把已确认的历史噪声写入基线，后续扫描只提示、不再阻断：

```bash
gitguard scan --all --baseline .gitguard-baseline.json   # 生成基线；当次命中仍会阻断
gitguard scan --all                                      # 后续自动消费仓库根默认基线
```

基线文件使用 `gitguard.baseline.v1` schema，按指纹去重；命中项仍保留在报告与审计中。指纹使用每用户本地盐，基线文件**不能跨机器 / 用户复用**。

### 5. 审计查询与导出

```bash
gitguard audit list                                # 最近 20 条
gitguard audit list --limit 50 --offset 50         # 分页
gitguard audit list --since 2026-10-01 --result Blocked
gitguard audit view <run-id>                       # 单次运行摘要与发现项明细
gitguard audit export --output ./audit.json        # 默认 JSON
gitguard audit export --output ./audit.csv --format csv
gitguard --format json audit list                  # 机器可读 JSON → stdout
```

`audit` 查询整个本地审计库（包含所有仓库），不要求当前目录是 Git 仓库。导出文件只含脱敏片段与 HMAC 指纹。

### 6. 白名单管理

```bash
gitguard allowlist add --rule GGD/SECRET/AWS-AKID --reason "已知误报登记"
gitguard allowlist add --path "docs/examples/**" --reason "示例目录放行"
gitguard allowlist add --fingerprint <64位HMAC指纹> --reason "已确认非真实凭据"
gitguard allowlist list --limit 20 --offset 0
gitguard allowlist remove --id <条目GUID>
```

`--rule` / `--path` / `--fingerprint` 至少提供一个，可组合；`--reason` 必填且至少 5 字符。SQLite 白名单与 `.gitguard.yml` 中的 `allowlist.entries` 会同时参与扫描判定。

### 7. 自定义规则

在 `.gitguard.yml`（或用户级 `config.yml`）中声明项目规则：

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
gitguard rules list --category Secret --enabled
gitguard rules view USR/SECRET/INTERNAL-TOKEN
gitguard rules view USR/SECRET/INTERNAL-TOKEN --show-pattern
gitguard --format json rules list
```

规则 ID 必须以 `USR/` 开头，类别段仅支持 `SECRET` / `PRIVACY` / `LOCALPATH` / `URL`；配置非法（含正则无法编译、重复 ID）时退出码 13。

### 8. 更新与卸载

```bash
gitguard install      # 重新安装即可刷新 Hook 中固化的可执行文件路径
gitguard uninstall    # 移除 GitGuard Hook 并还原被包裹的原 Hook
```

### 9. 全局选项与输出约定

| 选项 | 用途 |
| --- | --- |
| `--format json` | JSON 输出到 stdout；默认 text 输出到 stderr |
| `--language en-US` | 英文输出，等价环境变量 `GITGUARD_LANG=en-US` |
| `--verbose` / `--trace` | 输出调试 / 逐行匹配日志 |
| `--log-file <path>` | 写入 JSON-lines 文件日志 |
| `--no-color` | 禁用彩色输出 |

常用退出码：`0` 成功、`1` 命中阻断、`7` 用法错误、`10` Git 执行失败、`13` 配置错误、`50` 安装锁冲突。完整契约见 [CLI 命令契约](docs/06-API接口设计.md)。

## Desktop 使用方法

### 1. 启动

- 安装发行包后从系统应用入口启动；macOS 首次启动需在「系统设置 → 隐私与安全性」中放行。
- 从源码运行：

```bash
dotnet run --project src/GitGuard.Desktop -c Release
```

桌面端与 CLI 共用同一个用户数据目录中的 `audit.db`；首次启动会读取已存在的审计记录，修改设置后写入 `desktop-settings.json`。

> 桌面端**不内置 CLI**。安装 Hook 时需要能找到 GitGuard CLI：优先使用 `hooks.gitguard_executable_path`，其次从 `PATH` 和常见安装位置探测；仍找不到时会弹出文件选择器。

### 2. 登记仓库

1. 打开左侧导航「仓库」。
2. 在「仓库路径」输入框填写本地 Git 仓库绝对路径（如 `D:\code\my-repo` 或 `~/code/my-repo`）。
3. 点击「登记仓库」。
4. 在「已登记仓库」列表中选择仓库，右侧显示 Git / GitGuard 版本、Hooks 目录、Hook 状态与最近扫描摘要。

仅登记仓库路径，不会复制或修改仓库内容；目录不存在的条目会显示「目录不存在」徽标。

### 3. 安装 / 卸载 Hook

1. 在仓库详情查看「Hook 状态」，确认 `pre-commit` / `pre-push` / `post-commit` 当前状态。
2. 点击「安装 Hook」；已托管时显示「重新安装 Hook」。
3. 如果检测到二进制 / 符号链接 Hook，按提示确认强制接管，GitGuard 会先备份为 `<hook>.local.bak`。
4. 需要移除时点击「卸载 Hook」并二次确认；GitGuard 托管的 Hook 会被删除并还原原备份，未托管 Hook 不受影响。

### 4. 立即扫描

点击仓库详情底部的「立即扫描」，桌面端会对该仓库执行 HEAD 全量快照扫描，完成后原地刷新「最近扫描」摘要。

- 扫描在后台执行，不阻塞界面。
- 暂存区拦截与推送范围拦截仍由 Git Hook / CLI 完成；「立即扫描」适合日常巡检与查看当前状态。

### 5. 查看扫描历史与发现项

1. 在仓库详情点击「扫描历史」。
2. 左栏按时间线分页浏览该仓库的 ScanRun（每页 20 条）。
3. 中栏选择发现项，右栏查看规则 ID、严重度、文件位置、提交 SHA、脱敏片段与 HMAC 指纹。
4. 点击「在编辑器中打开」可跳转到对应文件行号；编辑器按 `GITGUARD_EDITOR` → `VISUAL` → `EDITOR` 顺序选择，未配置时回退系统文件关联。

界面只显示脱敏内容，不展示敏感原文。

### 6. 管理白名单

1. 在仓库详情点击「白名单」。
2. 左栏分页浏览全局白名单条目，右栏填写规则 ID / 路径 glob / 指纹中至少一项及登记原因。
3. 点击「添加」保存；条目对所有已登记仓库生效。
4. 删除条目需二次确认。

白名单变更与 CLI `allowlist` 命令共用同一 SQLite 存储，任一端修改后另一端即可读到。

### 7. 浏览规则库

1. 打开左侧导航「规则」。
2. 使用搜索框（200ms 节流）按规则 ID / 名称查找，或按来源（内置 / 自定义 / Gitleaks）、类别与「仅启用」筛选。
3. 选择规则查看详情；正则默认隐藏，点击「显示正则」后才展开。
4. Gitleaks 分类会展示适配器状态与动态规则说明；静态规则目录与 `gitguard rules list` 同源。

### 8. 日志与导出

1. 打开左侧导航「日志」。
2. 按结果（Clean / Warned / Blocked / Failed）或时间范围（全部 / 7 / 30 / 180 天）筛选，分页浏览所有仓库的审计运行。
3. 点击「导出 JSON」或「导出 CSV」选择保存位置；导出内容与 `gitguard audit export` 完全同契约，仅含脱敏字段。

### 9. 设置

打开左侧导航「设置」：

- **语言**：中文 / English 即时切换，重启后保持。
- **主题**：跟随系统 / 浅色 / 深色，即时生效并持久化到 `desktop-settings.json`。
- **Gitleaks**：启用开关与可执行文件路径覆盖，保存后仅作用于桌面端扫描与规则库展示；清除覆盖后恢复 `config.yml` 的有效值，CLI 行为不受桌面端覆盖影响。

### 10. 桌面端注意事项

- Desktop 与 CLI 共用本地审计库，但 Desktop 的 Gitleaks 覆盖不写回 `config.yml`。
- 安装 / 卸载 Hook 依赖 GitGuard CLI；如果 CLI 移动了位置，请在桌面端重新选择可执行文件或在目标仓库重新执行 `gitguard install`。
- 安装包默认未签名 / 未公证，正式团队分发前请配置签名。

## 配置

### 配置文件与优先级

- 项目级：`<repo>/.gitguard.yml`
- 用户级：`~/.config/gitguard/config.yml`
- 优先级：命令行参数 > 环境变量 > 项目级 > 用户级 > 内置默认

最小示例：

```yaml
version: 1

policy:
  critical: block     # 硬下限，不可改为 allow
  high: warn          # 默认 block；按需降级

hooks:
  pre_commit: true
  pre_push: true
  post_commit_bypass_detection: true

scan:
  pre_commit: true    # false = Hook 模式跳过暂存区扫描（手动 scan 不受影响）
  pre_push: true

rules:
  secrets:
    use_gitleaks: false
  url:
    public_hostname_whitelist: []   # 可信公共文档主机，如 github.com / "*.example.com"

exclude:
  paths:
    - "**/bin/**"
    - "**/node_modules/**"
  files:
    - "*.min.js"
```

完整 Schema 见 [Policy 模块设计](docs/模块设计/Policy.md)。

### 常用环境变量

| 变量 | 作用 |
| --- | --- |
| `GITGUARD_SKIP=1` | 仅在 Hook 模式生效，跳过本次扫描并在 `post-commit` 登记绕过事件 |
| `GITGUARD_LANG=en-US` | 切换英文输出（`--language` 优先） |
| `GITGUARD_EDITOR` | 桌面端「在编辑器中打开」优先使用的编辑器命令 |
| `NO_COLOR` | 禁用彩色输出 |

### 本地数据位置

| 内容 | 位置 |
| --- | --- |
| 审计库 | Windows `%LOCALAPPDATA%\GitGuard\audit.db`；macOS `~/Library/Application Support/GitGuard/audit.db`；Linux `~/.local/share/gitguard/audit.db` |
| 指纹盐 | 数据目录下 `cache/fingerprint.salt` |
| 用户配置 | `~/.config/gitguard/config.yml` |
| 桌面端设置 | 数据目录下 `desktop-settings.json` |

审计库只保存脱敏片段与 HMAC 指纹，不保存敏感原文；可用 `gitguard audit list` / `gitguard audit export` 查询和导出。

## 已知限制

- `scan --staged` 严格只扫描暂存区，不读取未暂存内容。
- 基线指纹使用每用户本地盐，基线文件不能跨机器 / 用户复用。
- 桌面端不内置 CLI，Hook 安装需要可用的 GitGuard CLI。
- 桌面端 Gitleaks 覆盖只影响桌面端，不改变 CLI 与 `config.yml`。
- 安装包默认未签名 / 未公证；Windows MSIX 需开发者模式或信任自签证书，macOS DMG 首次启动需手动放行。
- Hook 兼容性已在 Windows 上针对 Husky v9 / lefthook v2 / pre-commit 4.x 验证；其他平台或更老版本请以实际仓库验证为准。

## 开发与测试

```bash
dotnet build GitGuard.sln -c Release
dotnet test GitGuard.sln
dotnet format GitGuard.sln --verify-no-changes --no-restore
```

CI 覆盖 Release 构建、格式检查、七类测试作业、Git 2.9.3 / 2.40.1 / 最新稳定版 E2E 兼容矩阵与 Windows MSIX 发行冒烟。架构、数据库与测试细节见 [文档索引](docs/README.md)。

## 文档入口

- 产品与范围：[docs/00-项目概述.md](docs/00-项目概述.md)
- 需求规格：[docs/02-需求规格说明书(SRS).md](<docs/02-需求规格说明书(SRS).md>)
- 架构设计：[docs/03-系统架构设计(SAD).md](<docs/03-系统架构设计(SAD).md>)
- CLI 命令契约：[docs/06-API接口设计.md](docs/06-API接口设计.md)
- 桌面 UI 设计：[docs/11-前端UI设计.md](docs/11-前端UI设计.md)
- 发行与部署：[docs/14-部署文档.md](docs/14-部署文档.md)
- 误报治理：[docs/19-误报治理指南.md](docs/19-误报治理指南.md)
- 发行说明：[v1.0](docs/20-v1.0发行说明.md) / [v1.1](docs/21-v1.1发行说明.md)
- 文档索引：[docs/README.md](docs/README.md)

## README 维护约定

**重大功能更新必须同步更新本 README。** 新增或变更以下内容时，请在同一个提交 / PR 中更新「项目介绍」「功能清单」「CLI 使用方法」「Desktop 使用方法」中的对应章节，并核对示例命令、版本号与产物名称：

- 新的检测能力、规则类别或扫描模式；
- CLI 命令、选项、退出码或输出格式变化；
- 桌面端新增页面、核心交互或 Hook 管理能力；
- 配置项、环境变量、本地数据位置变化；
- 发行方式或安装步骤变化。

该约定同样记录在 [docs/AGENT-GUIDE.md](docs/AGENT-GUIDE.md) 的会话收尾协议中。
