# GitGuard

[![CI](https://github.com/Camefor/GitGuard/actions/workflows/ci.yml/badge.svg?branch=master)](https://github.com/Camefor/GitGuard/actions/workflows/ci.yml)

面向开发者的本地 Git 敏感信息防泄漏工具：在 `git commit` 与 `git push` 前通过 Hook 拦截 Token、密钥、密码、手机号、本地路径、内网 URL 等敏感信息。所有扫描与审计均在本机完成，不上传任何数据。

## 文档入口

- 产品与范围：[docs/00-项目概述.md](docs/00-项目概述.md)
- 文档索引与导航：[docs/README.md](docs/README.md)
- Agent 工作指南与任务台账：[docs/AGENT-GUIDE.md](docs/AGENT-GUIDE.md)

## 开发环境

- .NET SDK 10.0 LTS（`dotnet build` / `dotnet test`）
- Git ≥ 2.9（运行时依赖系统 Git）

```bash
dotnet build          # 构建全部项目
dotnet test           # 运行全部测试
```
