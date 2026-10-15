# LocalPath / Url 模块设计

> **范围说明**：本文件承载 **LocalPath 检测器** 和 **Url 检测器** 两类规则。它们规则特征相近（环境/网络类），放在一起更利于维护；代码上仍是两个独立的 `IDetector` 实现。

## 1. 模块定位

- **一句话定位**：识别开发者本地绝对路径泄漏（如 `C:/Users/rushe/Desktop/project`、`C:\Users\rushe\Desktop`）与可疑 URL（含凭据、含 Token 查询参数、内网保留地址），命中后按配置输出 Finding。
- **所属限界上下文**：Scanning Context
- **承担 FR**：`FR-021`
- **承担 DR**：`DR-02`（脱敏）
- **关联 ADR**：无

## 2. 输入输出契约

同 Secret / Privacy 模块：输入差异 Hunk，输出 `IReadOnlyList<Finding>`。

## 3. 核心规则

### 3.1 LocalPath 规则

| RuleId | 名称 | 严重度 | 判定要点 |
| --- | --- | --- | --- |
| `GGD/LOCALPATH/UNIX-HOME` | Unix 用户主目录绝对路径 | Low | `/home/[\w-]+/` 或 `/Users/[\w.-]+/` |
| `GGD/LOCALPATH/WIN-USER` | Windows 用户目录绝对路径 | Low | `[A-Z]:\\Users\\[\w.-]+\\` |
| `GGD/LOCALPATH/WIN-DRIVE` | Windows 驱动器盘符 | Low | 行首 `[A-Z]:\\` 后跟路径分隔 |
| `GGD/LOCALPATH/TEMP-WORK` | 常见临时/工作区绝对路径 | Low | `/workspace/volumes/`、`/mnt/d/`、`~/workspace/` |

**降级策略**：

- 在 `.gitignore`、`README.md`、`docs/**` 中出现时自动降级一级（低 → 不命中）。
- 示例代码块（Markdown 的三反引号段落）内不计入命中。

### 3.2 Url 规则

| RuleId | 名称 | 严重度 | 判定要点 |
| --- | --- | --- | --- |
| `GGD/URL/CREDENTIALS-IN-URL` | URL 带用户名密码 | High | `(https?\|ftp)://[^/\s:]+:[^@\s/]+@` |
| `GGD/URL/TOKEN-IN-QUERY` | 查询参数含 token/apikey | High | `[?&](token\|apikey\|access_token\|api_key)=[^&\s]+` |
| `GGD/URL/PRIVATE-IP` | 私有 / 环回 IP | Medium | `10.\d+.\d+.\d+`、`172.(1[6-9]\|2[0-9]\|3[0-1]).\d+.\d+`、`192.168.\d+.\d+`、`127.\d+.\d+.\d+` |
| `GGD/URL/LOCAL-HOSTNAME` | 内网域名 | Medium | `*.local`、`*.internal`、`*.corp`、`*.lan`（在非文档上下文） |

**降级策略**：

- URL 出现在以 `example`、`sample`、`demo` 开头的文件名中 → 命中但严重度 - 1。
- 公共文档类 URL（`github.com`、`api.github.com` 等 TLD 白名单）不触发命中。

## 4. 外部依赖

| 依赖 | 用途 | 失败降级 |
| --- | --- | --- |
| Regex 引擎 | 格式判定 | 单规则禁用 + FailClosed |
| Uri 解析器（System.Uri） | 可选深度解析 token 位置 | 解析失败回退纯 Regex 判定 |

## 5. 数据模型

脱敏输出：

- LocalPath：`/Users/q***/Desktop/project` → 保留第一个用户名字符 + `***` + 路径尾段。
- Url：`https://u***:***@host.com/path` → 用户名密码遮罩。

## 6. 关键流程

```mermaid
flowchart LR
    Line[差异行] --> IsCode{是否代码块}
    IsCode -- 是 --> Scan[运行规则]
    IsCode -- 否（markdown 代码块） --> Context{是文档?}
    Context -- 是 --> Downgrade[严重度 -1 或 跳过]
    Scan --> Match{匹配}
    Match -- 命中 --> Mask[脱敏]
    Mask --> Finding
```

## 7. 配置项

| 配置项 | 类型 | 默认 | 说明 |
| --- | --- | --- | --- |
| `rules.local_path.enabled` | bool | true | - |
| `rules.local_path.disabled_rules` | string[] | [] | - |
| `rules.url.enabled` | bool | true | - |
| `rules.url.disabled_rules` | string[] | [] | - |
| `rules.url.public_hostname_whitelist` | string[] | 见默认 | 公共文档 TLD/域名白名单 |

## 8. 质量属性

| 属性 | 目标 |
| --- | --- |
| 内网 IP 命中召回率 | ≥ 95%（已知样本集） |
| 本地路径误报率 | ≤ 10%（在包含文档的真实项目） |
| 单规则 P95 耗时 | ≤ 20ms |

## 9. 测试要点

- [ ] Windows / Unix 路径分别覆盖。
- [ ] 代码块内 vs 文档内行为差异。
- [ ] URL 带 Token 的脱敏输出不含原 Token。
- [ ] 公共 URL 白名单生效（如 `https://github.com`）。

## 10. 演进与扩展位

- 项目级自定义 IP / 路径白名单（P2）。
- 更丰富的内网域名识别（`*.k8s.local` 等）。

## 11. 变更记录

| 版本 | 日期 | 变更说明 |
| --- | --- | --- |
| 0.1.0 | 2026-10-15 | 初始化：4 条 LocalPath + 4 条 Url 规则 |
