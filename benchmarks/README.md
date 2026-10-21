# GitGuard 性能基准（benchmarks）

本目录提供 GGD-0020 的基准工具与报告：测量 `NFR-P-001` ~ `NFR-P-004`，并为 `NFR-M-002`（规则级耗时）提供 CLI `--verbose` 验证入口。

## 1. 组成

| 路径 | 说明 |
| --- | --- |
| `GitGuard.Benchmarks/` | 自研轻量基准 harness（不引入 BenchmarkDotNet）：进程级场景 + 进程内吞吐场景，控制台 / JSON 报告 |
| `startup.sh` / `startup.ps1` | `NFR-P-001` 冷启动基准（默认 50 次测量） |
| `run.sh` / `run.ps1` | 全量四场景（startup / staged / push / throughput），并写出 JSON 归档 |

## 2. NFR 与场景映射

| NFR | 场景 | 样本 | 测量口径 | 判定 |
| --- | --- | --- | --- | --- |
| `NFR-P-001` | `startup` | 50 次 | 发布版 Native AOT 可执行文件运行 `gitguard version` 的进程墙钟耗时（含进程启动；该命令内部会探测 `git --version`） | P95 ≤ 150 ms |
| `NFR-P-002` | `staged` | 10 次 | `gitguard scan --staged` 端到端墙钟耗时，暂存区 500 行差异（含进程启动与真实 `git diff`） | P95 ≤ 500 ms |
| `NFR-P-003` | `push` | 10 次 | `gitguard scan --push` 端到端墙钟耗时，50 提交 × 100 行 = 5000 行差异（含进程启动、每个推送区间一次 `git rev-list` 与一次批量 `git log --no-walk=unsorted --stdin`） | P95 ≤ 2000 ms |
| `NFR-P-004` | `throughput` | 3 次 | 进程内驱动 Secret → Privacy → LocalPath → Url 检测器管线，扫描 100 MiB 语料 | P50 ≥ 20 MB/s |
| `NFR-M-002` | — | — | `gitguard scan --staged --verbose` 输出 `Scan.RuleTiming` 日志（`rule_id` / `duration_us`） | 单测 + 人工验证 |

判定规则：延迟类指标取 P95，吞吐取 P50；未达标仅影响报告判定，不作为 CI 门禁（基准入口始终以退出码 0 结束，参数错误为 2、运行失败为 1）。

## 3. 测量方法

- **进程级场景**：`ProcessRunner` 以 `ProcessStartInfo.ArgumentList` 启动发布版可执行文件，重定向 stdout / stderr，`Stopwatch.GetTimestamp` 测量从 `Process.Start` 到退出的墙钟耗时；单次超时 120 s，超时后终止进程树。预热 3 次（startup）/ 1 次（其余）后再进入测量。
- **统计**：最近秩百分位（nearest-rank），P50 / P95 直接取排序后的样本值，不做插值；同时输出 min / max / mean。
- **吞吐**：`scan --all`（FR-060）属 v0.2，因此不经过 CLI，而是在基准进程内按生产装配顺序直接驱动四类检测器，测量 100 MiB 代码语料的总耗时并换算 MB/s（1 MB = 1,000,000 字节）。
- **隔离**：每次运行创建 `%TEMP%/gitguard-bench/<guid>` 临时工作区，样本仓库使用确定的本地 git 配置（`user.name` / `user.email` / `commit.gpgsign=false` / `core.autocrlf=false` / `core.hooksPath=`）；子进程环境固定 `NO_COLOR=1`、`GITGUARD_LANG=en-US`、`GIT_CONFIG_NOSYSTEM=1`，并将 `LOCALAPPDATA` / `HOME` / `USERPROFILE` / `XDG_CONFIG_HOME` / `XDG_DATA_HOME` 指向隔离目录，避免用户配置与审计库影响结果。
- **已知限制**：
  - Windows 的用户配置目录由 Shell Known Folder 解析，无法仅靠环境变量完全重定向；基准通过 `GIT_CONFIG_GLOBAL` 与隔离数据目录降低影响，若本机存在真实 `~/.config/gitguard/config.yml`，结果可能受其策略影响。
  - 进程创建开销与安全软件扫描会显著影响 startup / push 的绝对值；报告同时给出宿主基线（见 §6）。
  - 吞吐语料为合成的代码形态文本，刻意不含密钥 / 隐私 / 路径 / URL 特征，命中数为 0，仅测检测成本。

## 4. 使用

### 4.1 发布 Native AOT 可执行文件

```bash
dotnet publish src/GitGuard.Cli/GitGuard.Cli.csproj -c Release -r win-x64 -p:PublishAot=true -o artifacts/publish/gitguard-win-x64
```

Linux / macOS 将 `-r win-x64` 换成 `linux-x64` / `osx-arm64`，输出目录相应调整。Windows 下若链接阶段报 `vswhere.exe` 找不到，需将 Visual Studio Installer 目录加入 `PATH`（通常为 `C:\Program Files (x86)\Microsoft Visual Studio\Installer`）。

### 4.2 脚本

```bash
bash benchmarks/startup.sh        # NFR-P-001
bash benchmarks/run.sh            # 全量；默认写 artifacts/benchmarks/latest.json

./benchmarks/startup.ps1          # PowerShell 等价入口
./benchmarks/run.ps1
```

- 可执行文件路径：`-Exe <path>` / `GITGUARD_BENCH_EXE`；未指定时使用 `artifacts/publish/<rid>/gitguard[.exe]`。
- JSON 路径：`-Json <path>` / `GITGUARD_BENCH_JSON`（仅 `run`）。
- 其余参数原样透传给 harness，例如 `bash benchmarks/run.sh --runs 20 --staged-lines 1000`。

### 4.3 直接运行 harness

```bash
dotnet run -c Release --project benchmarks/GitGuard.Benchmarks -- <startup|staged|push|throughput|all> [选项]
```

| 选项 | 默认值 | 说明 |
| --- | --- | --- |
| `--exe <path>` | 见 §4.2 | 发布版可执行文件；startup / staged / push 必填 |
| `--runs <n>` | startup 50，扫描 10，吞吐 3 | 测量次数 |
| `--warmup <n>` | startup 3，其余 1 | 预热次数 |
| `--staged-lines <n>` | 500 | 暂存区样本新增行数 |
| `--commits <n>` | 50 | 推送样本提交数 |
| `--lines-per-commit <n>` | 100 | 每提交新增行数 |
| `--size-mb <n>` | 100 | 吞吐样本大小（MB） |
| `--json <path>` | 无 | 同时写出 JSON 结果 |
| `--keep` | 关闭 | 保留临时基准仓库与隔离目录 |

JSON 包含运行环境快照（时间 / OS / 运行时 / 架构 / 核数 / 可执行文件路径）与每项统计量，便于跨机器或跨版本比较。

## 5. 规则级耗时（NFR-M-002）

`--verbose`（Debug 级别）会在扫描完成后为每条被求值的规则输出一行摘要，字段含 `rule_id` 与 `duration_us`：

```text
[debug] Scan.RuleTiming: 规则耗时：GGD/SECRET/AWS-AKID（求值 12 次 / 命中 1 次，共 345 µs） rule_id=GGD/SECRET/AWS-AKID duration_us=345
```

- 只在真正执行正则求值时计时（文件 glob / 行跳过 / 上下文关键字门控之后），普通扫描不创建计时器、无额外开销。
- 汇总按 `rule_id` 字典序输出，便于对比；`--trace` 仍用于逐行匹配细节。
- 单测覆盖：Detection（检测器计时上报）、Application（扫描结果聚合 / 排序 / 开关）、CLI（`--verbose` 中英文日志与字段）。

## 6. 最新实测结果（2026-10-21）

环境：Windows 11（10.0.26200）、.NET 10.0.11 win-x64 Native AOT（单文件 6.6 MB）、14 逻辑核；运行时间 `2026-10-21T09:31:49Z`。

| 场景 | 样本 | P50 | P95 | min / max | 目标 | 判定 |
| --- | --- | --- | --- | --- | --- | --- |
| startup（`gitguard version`） | 50 | 184.78 ms | 200.44 ms | 172.59 / 231.76 ms | P95 ≤ 150 ms | **FAIL** |
| staged（500 行差异） | 10 | 307.00 ms | 311.05 ms | 295.77 / 311.05 ms | P95 ≤ 500 ms | **PASS** |
| push（50 提交 / 5000 行） | 10 | 604.15 ms | 669.78 ms | 581.66 / 669.78 ms | P95 ≤ 2 s | **PASS** |
| throughput（100 MiB，1326 文件） | 3 | 26.08 MB/s | 28.81 MB/s | 25.95 / 28.81 MB/s | P50 ≥ 20 MB/s | **PASS** |

**宿主进程创建基线**（PowerShell `Measure-Command`，各 5 次，独立于本 harness）：

| 命令 | 耗时范围 |
| --- | --- |
| `cmd.exe /c exit` | 165.7 – 185.4 ms |
| `git --version` | 106.3 – 143.8 ms |
| `gitguard.exe version` | 212.1 – 469.6 ms（首次 469.6，后续 212 – 237 ms） |

**结果解读**：

- `NFR-P-001`：本机仅创建 `cmd.exe` 的基线就约 170 ms，已高于 150 ms 阈值；`version` 还会额外探测一次 `git --version`。该绝对阈值在当前宿主上无法验证，需在进程创建开销正常的机器（`cmd.exe` 冷启动 < 20 ms）复测。复测 P95 200.44 ms 仍高于阈值，结果由宿主进程创建开销主导。
- `NFR-P-002`：仅 1 – 2 次 git 子进程调用，端到端 P95 311.05 ms，达标。
- `NFR-P-003`：GGD-0023 将逐提交 `git diff-tree` 改为一次批量 `git log --no-walk=unsorted --stdin` 取回全部提交差异，git 子进程数量不再随提交数增长；P95 由 4687.07 ms 降至 669.78 ms（约 −86%），达标。
- `NFR-P-004`：检测器管线吞吐 26.08 MB/s，高于 20 MB/s 目标。

## 7. 文件编码约定

- `.ps1`：UTF-8 with BOM + CRLF，与仓库 `.editorconfig` / `13-开发规范 §12` 一致。
- `.sh`：按 POSIX 可执行要求保存为 **UTF-8 无 BOM + LF**（BOM / CRLF 会使 shebang 失效）；这是仓库文本格式规则的显式例外，仅限 shell 脚本。
- `.cs` / `.md` / `.csproj` 沿用仓库既有约定。
