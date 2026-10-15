# ADR-0003：检测引擎采用适配器模式，MVP 不强依赖 Gitleaks

- **状态**：Accepted
- **决策日期**：2026-10-15
- **决策人**：王如生（项目负责人）
- **关联文档**：`模块设计/Secret.md` §10 / `03-SAD §2`
- **关联需求**：`FR-021`、`FR-090`、`NFR-A-002`

## 背景

秘密检测的规则库维护成本很高：

- 常见凭据格式随云厂商演进（AWS、GitHub、Slack 等 Token 格式）。
- 自研规则库从零起步，难以覆盖长尾。
- 开源项目 **Gitleaks**（MIT 许可）已沉淀 100+ 条高质量规则，社区活跃。

但引入 Gitleaks 作为 MVP 的强依赖带来问题：

1. Gitleaks 是独立 Go 二进制，需要随 GitGuard 分发或要求用户单独安装；跨平台分发复杂度增加。
2. Gitleaks 本身可能有 Breaking Change（CLI 参数、输出格式），版本耦合风险。
3. Gitleaks 的规则覆盖**秘密**类，但 GitGuard 还要支持**隐私 / 本地路径 / URL** 三类规则，Gitleaks 不覆盖；两套引擎并存。
4. 本项目的核心价值不只是「秘密扫描」，而是「Hook 兼容 + 本地审计 + 国内场景规则」；在 MVP 阶段把这些做对，比快速上大规则库更重要。

## 决策

**检测引擎采用适配器模式（Adapter Pattern）**：

1. 定义统一接口 `IDetector`，所有检测器实现它：

   ```csharp
   public interface IDetector
   {
       string Name { get; }
       IAsyncEnumerable<Finding> DetectAsync(FileChange change, CancellationToken ct);
   }
   ```

2. **MVP (v0.1) 阶段**：只实现四个纯 C# 检测器——`SecretDetector` / `PrivacyDetector` / `LocalPathDetector` / `UrlDetector`。内置 8 条高置信度秘密规则 + 国内场景规则。
3. **P2 (v0.2) 阶段**：新增 `GitleaksAdapter : IDetector`，通过 `Process` 调用外部 `gitleaks` 二进制。
   - 可独立开关（`rules.secrets.use_gitleaks: false` 默认）。
   - 可执行文件缺失时自动禁用适配器，不阻断流程（降级）。
   - 规则 ID 前缀 `GLK/` 隔离命名空间。
4. **P3 阶段**：允许用户通过 `UserRegexDetector : IDetector` 加载 `.gitguard.yml` 的自定义正则。

## 影响

### 正面

- MVP 不受 Gitleaks 发行节奏约束，可独立交付。
- 二期接入 Gitleaks 对核心代码零改动；用户按需启用。
- 适配器隔离使 Gitleaks 的 Breaking Change 局限在 `GitleaksAdapter` 文件内。
- 允许未来接入其他引擎（如 Trufflehog）用同样模式。

### 负面

- MVP 规则覆盖率弱于直接集成 Gitleaks（约 8 条 vs 100+ 条）。
- 需要用户在二期显式开启 Gitleaks；不开启则召回率较低。
- 两套引擎的 Finding 聚合、去重、冲突处理需要额外设计（见下文）。

### 配套机制

- **规则 ID 命名空间**：`GGD/` 内置、`GLK/` Gitleaks 来源、`USR/` 用户自定义。三者不冲突。
- **去重策略**：按 `fingerprint` 去重；多引擎命中同一指纹时保留第一个（优先级 USR > GLK > GGD）。
- **冲突策略**：严重度不一致时取最高；用户可通过策略配置覆写。
- **Gitleaks 版本锁定**：`GitleaksAdapter` 启动期验证 `gitleaks version`，锁定支持区间；超出区间时打印警告（不阻断）。

## 替代方案

### 方案 A：MVP 直接集成 Gitleaks

- **优点**：规则覆盖率立刻顶格；减少自研工作量。
- **未采纳原因**：
  - 分发复杂度（Go 二进制 + 跨平台签名）。
  - MVP 阶段的核心价值是 Hook 兼容和本地审计，不是规则覆盖率。
  - 对 Gitleaks 的强耦合违反「可独立运行」原则。

### 方案 B：纯 C# 自研规则库（永不集成 Gitleaks）

- **优点**：零外部依赖。
- **未采纳原因**：
  - 维护 100+ 条规则的成本不可持续。
  - 社区规则库是公开的最佳实践，不复用是浪费。

### 方案 C：Gitleaks 作为 NuGet 包嵌入（如存在 .NET 移植）

- **优点**：无需外部进程。
- **未采纳原因**：Gitleaks 当前无官方 .NET 实现；非官方移植维护状态不明。

## 后续动作

- [ ] MVP：实现 `IDetector` 抽象 + 4 个内置检测器（`GGD-0006`）。
- [ ] P2：实现 `GitleaksAdapter`，在 CI 下跑 Mock + 真实二进制两套测试。
- [ ] P2：补充 Finding 聚合与去重的单元测试。
- [ ] P2：发行物中可选内置 Gitleaks 二进制（二进制 vs 用户自装的权衡再做一次 ADR）。

## 备注

- 本 ADR 决定「什么时候引入 Gitleaks」，不决定「是否最终引入」；若 P2 阶段证明 MVP 规则加白名单已满足真实场景，可延后或不引入。
- Trufflehog / detect-secrets 等同类工具，均可用相同的 `IDetector` 模式接入，不需额外 ADR。
