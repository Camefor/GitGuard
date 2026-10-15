# ADR-0002：技术栈选 .NET 10 LTS + Native AOT + Avalonia

- **状态**：Accepted
- **决策日期**：2026-10-15
- **决策人**：王如生（项目负责人）
- **关联文档**：`03-系统架构设计(SAD).md` §2 技术选型（SSOT）
- **关联需求**：`NFR-P-001`（冷启动 ≤ 150ms）、`NFR-A-002`（跨平台可用性）

## 背景

GitGuard 需要在开发者本机（Windows / macOS / Linux）作为 CLI 高频运行（每次 `commit` / `push` 触发），对以下维度有刚性要求：

1. **冷启动速度**：Hook 触发的用户感知窗口 ≤ 500ms 全链路；CLI 本身占用必须 ≤ 150ms。
2. **零运行时依赖**：用户下载即用，不要求安装 Node / JVM / Python 运行时。
3. **跨平台单一代码库**：避免 Windows / Linux / macOS 分别维护。
4. **开发者维护成本可控**：匹配本项目负责人的核心技术栈（C# / .NET）。

同时需要为 P3 阶段的桌面端预留能力：桌面端技术栈最好能复用 CLI 的 Core / Application。

## 决策

### 核心运行时

- **语言 / 运行时**：**C# / .NET 10 LTS**
- **发行形态**：**Native AOT 单文件 + `dotnet tool` 全局工具**（二选一，用户按偏好选）
- **CLI 框架**：**System.CommandLine**（Microsoft 官方维护；兼容 Native AOT）
- **Git 交互**：**调用系统 git CLI**（通过 `System.Diagnostics.Process`），**不**使用 LibGit2Sharp（Native 绑定复杂度与许可证成本）
- **本地存储**：**SQLite** via `Microsoft.Data.Sqlite`
- **配置解析**：**YamlDotNet**
- **日志**：`Microsoft.Extensions.Logging` + 自写脱敏 Sink
- **单元测试**：**xUnit v3 + FluentAssertions**

### 桌面端（P3）

- **桌面框架**：**Avalonia UI 11.x+**
- 使用 `CompiledBindings` 避免反射；复用 CLI 的 `GitGuard.Application` 层

## 影响

### 正面

- 匹配项目负责人技术栈，不引入学习成本。
- Native AOT 单文件分发简单，用户下载 ~20 MB 即可运行。
- .NET 10 LTS 支持周期至 2028 年底，覆盖 MVP → P3 全周期。
- Avalonia 跨平台一致性好，不走各家平台差异化（相比 MAUI）。

### 负面

- Native AOT 对部分反射依赖的 NuGet 包不兼容；YamlDotNet / Microsoft.Data.Sqlite 需确认 trim-safe 的替代方案。
- Native AOT 的构建耗时 / 内存占用较高，CI 需要 ≥ 4 GiB 内存的 Runner。
- System.CommandLine 当前仍为预览版（Microsoft 承诺持续维护，MVP 可用；若发生破坏性变化，自写轻量包装即可）。

### 需要配套的实践

- 启动期测试：每次 CI 跑 Native AOT 产物的 `--version` 冒烟，验证 trim-safe。
- 对所有依赖固定 MAJOR.MINOR 版本，避免跨版本漂移。
- 维护 `Directory.Packages.props` 做中央包管理。

## 替代方案

### 方案 A：Node.js + TypeScript

- **优点**：生态丰富，Gitleaks / Trufflehog 等相关工具多数提供 Node 绑定。
- **未采纳原因**：与团队技术栈不匹配；需要 Node 运行时；冷启动较慢（~100ms+ 的 V8 启动开销）。

### 方案 B：Go

- **优点**：Native 单文件；跨平台优秀；Gitleaks 本身是 Go 实现。
- **未采纳原因**：团队学习曲线；放弃 .NET 生态积累；桌面端选择面窄。

### 方案 C：Rust

- **优点**：性能顶级；Native 单文件；现代工具链。
- **未采纳原因**：学习曲线陡；团队无 Rust 经验；对快速迭代不利。

### 方案 D：.NET 10 + 普通 `dotnet publish`（非 Native AOT）

- **优点**：构建快；兼容性广。
- **未采纳原因**：发行物依赖 .NET 运行时或自包含部署（~70 MB）；冷启动 ~250ms，难以满足 NFR-P-001。
- **折中**：仍保留 `dotnet tool` 作为 .NET SDK 用户的二选一渠道。

### 方案 E：桌面端用 WPF（Windows-only）

- **未采纳原因**：无法覆盖 macOS / Linux，违反 NFR-A-002。

### 方案 F：桌面端用 MAUI

- **未采纳原因**：当前 MAUI 对 Linux 支持不足；社区活跃度不如 Avalonia。

## 后续动作

- [ ] 验证 `Microsoft.Data.Sqlite` 在 Native AOT 下的可用性（`GGD-0001`）。
- [ ] 验证 `YamlDotNet` 的 trim-safe 配置或预编译模型（`GGD-0001`）。
- [ ] 建立 CI 多平台构建矩阵（`GGD-0011`）。
- [ ] P3 启动时锁定 Avalonia 具体 LTS 版本。

## 备注

- Gitleaks 作为独立二进制通过 `GitleaksAdapter` 接入（见 ADR-0003），不改变本 ADR 的技术栈决策。
- 若未来 .NET 11 LTS 发布且 Native AOT 支持更好，可通过新 ADR 升级。
