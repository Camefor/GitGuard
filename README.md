# GitGuard

[![CI](https://github.com/Camefor/GitGuard/actions/workflows/ci.yml/badge.svg?branch=master)](https://github.com/Camefor/GitGuard/actions/workflows/ci.yml)

面向开发者的本地 Git 敏感信息防泄漏工具：在 `git commit` 与 `git push` 前通过 Hook 拦截 Token、密钥、密码、手机号、本地路径、内网 URL 等敏感信息。所有扫描与审计均在本机完成，不上传任何数据。

## 当前状态（v0.1，2026-10-21）

**可以试用。** 「安装 Hook → 提交拦截 → 推送拦截 → 绕过留痕」的核心链路已经完成，并有真实 `git commit` / `git push` 的端到端测试覆盖。

功能完成度（按 PRD §4 的 21 条 FR 统计）：

| 范围 | 完成度 | 说明 |
| --- | --- | --- |
| P0 核心功能（11 条） | 约 95% | 10 条完整实现；终端报告缺少逐规则「建议」文案（其余字段齐全） |
| 全部 FR（21 条） | 约 67% | 13 条完整 + 2 条部分 + 6 条未实现（完整计 1、部分计 0.5） |

已可用：

- `gitguard install` / `uninstall` / `status`：安装、卸载与查询 Hook（`pre-commit` / `pre-push` / `post-commit`）
- `gitguard scan --staged`：暂存区扫描（严格基于 `git diff --cached`）
- `gitguard scan --push`：推送范围扫描（覆盖范围内全部提交，一次批量取差异，Finding 带来源 commit SHA）
- `gitguard audit list` / `audit export`：本地审计查询（分页 / 时间 / 结果筛选）与脱敏 JSON / CSV 导出
- 21 条内置规则：Secret 8 / Privacy 5 / LocalPath 4 / Url 4
- 文本 / JSON 报告、严重度策略、配置文件白名单、`exclude` 路径排除
- `--no-verify` 与 `GITGUARD_SKIP=1` 绕过检测及 SQLite 审计落库
- 中英文输出、Native AOT 单文件发布

尚未实现（不影响核心试用）：

- `scan --all` 全量扫描与基线文件（v0.2）
- `allowlist` 的 CLI / SQLite 管理（目前通过 `.gitguard.yml` 配置）
- `audit view <run-id>` 单次运行明细（v0.2）
- 自定义正则规则、GitleaksAdapter（v0.2）
- Avalonia 桌面端（v0.3）

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

### 5. 审计查询与导出

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

### 6. 卸载

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

exclude:
  paths:
    - "**/bin/**"
    - "**/node_modules/**"
  files:
    - "*.min.js"
```

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

- 全量测试：1023 用例（1018 通过 + 5 个平台 / 工具链相关跳过，0 失败）；真实 Hook 框架兼容用例需设置 `GITGUARD_HOOK_COMPAT=1`（CI `hook-compat` 作业，当前仅 Windows）
- 基准（Windows 11 / Native AOT）：暂存区扫描 P95 311 ms（目标 ≤ 500 ms）；推送扫描 P95 670 ms（目标 ≤ 2 s）；检测吞吐 P50 26 MB/s（目标 ≥ 20 MB/s）
- 基准方法与本机数据见 [benchmarks/README.md](benchmarks/README.md)

## 已知限制

- Hook 兼容性：Husky v9 / lefthook v2 / pre-commit 4.x 的安装包裹、真实提交链式执行、阻断语义与卸载还原已在真实安装仓库中验证（`TC-COMPAT-01~04`）；CI `hook-compat` 作业当前仅覆盖 Windows，macOS / Ubuntu 矩阵暂缓（`GGD-0028`）。
- CI 当前仅运行 Windows 单平台；macOS / Ubuntu 测试矩阵与 Linux DR-01 `strace` 作业暂缓（`GGD-0028`）。
- Windows 下审计库目前依赖用户目录默认 ACL，显式收紧计划中。
- Git 2.9 / 2.40 兼容矩阵尚未在 CI 覆盖，当前以最新稳定版 Git 验证。
- `scan --staged` 只扫描暂存区，不读取未暂存内容；全量历史扫描尚未实现。

## 文档入口

- 产品与范围：[docs/00-项目概述.md](docs/00-项目概述.md)
- 需求规格：[docs/02-需求规格说明书(SRS).md](<docs/02-需求规格说明书(SRS).md>)
- CLI 命令契约：[docs/06-API接口设计.md](docs/06-API接口设计.md)
- 发行与分发：[docs/14-部署文档.md](docs/14-部署文档.md)
- 文档索引与导航：[docs/README.md](docs/README.md)
- Agent 工作指南与任务台账：[docs/AGENT-GUIDE.md](docs/AGENT-GUIDE.md)
