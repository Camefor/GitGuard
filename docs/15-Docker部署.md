# Docker（仅用于 CI 构建）

> **重要**：GitGuard 的运行时**不依赖 Docker**，用户不需要安装任何容器环境。本文件描述的是**CI 阶段**使用 Docker 做可重复的多平台构建与测试，以及 CI 环境中临时 Git 仓库 Fixture 的隔离方式。

## 1. 镜像构建

- **基础镜像**：
  - 构建期：`mcr.microsoft.com/dotnet/sdk:10.0`（固定版本 tag，禁用 `latest`）。
  - 运行期（CI 自动化测试）：`mcr.microsoft.com/dotnet/runtime-deps:10.0`。
  - **发行物本身是 Native AOT 单文件，无容器运行需求**。
- **多阶段构建**：是（见 §1.1）。
- **安全扫描**：CI 内置 `trivy` 对 build image 扫描一次高危 CVE；仅作提醒，不阻断构建。

### 1.1 Dockerfile 骨架（CI 用）

```dockerfile
# syntax=docker/dockerfile:1

# ---- build stage ----
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
RUN dotnet restore GitGuard.sln && \
    dotnet build GitGuard.sln -c Release --no-restore && \
    dotnet test GitGuard.sln -c Release --no-build --logger "trx;LogFileName=test_results.trx"

# ---- publish stage (Native AOT) ----
FROM build AS publish
ARG RID=linux-x64
RUN dotnet publish src/GitGuard.Cli/GitGuard.Cli.csproj \
    -c Release -r ${RID} --self-contained true -o /out \
    -p:PublishAot=true

# ---- smoke stage ----
FROM mcr.microsoft.com/dotnet/runtime-deps:10.0 AS smoke
WORKDIR /app
COPY --from=publish /out/gitguard /usr/local/bin/gitguard
RUN gitguard --version && apt-get update && apt-get install -y git && \
    cd /tmp && git init demo && cd demo && \
    gitguard install && echo "AKIAIOSFODNN7EXAMPLE_TEST" > key.txt && \
    git add key.txt && (git commit -m "canary" || echo "blocked as expected")
```

> 实际 CI 不会把 smoke 阶段的产物发行；只用于本地验证。

## 2. 镜像标签与版本

- 构建镜像仅存在于 CI 临时产物中，不推送到任何 registry。
- 若未来需要发布 GitGuard 的 Docker 测试镜像（例如用于 CI 第三方集成），使用 `v<semver>` + `<sha>` 组合标签；禁用 `latest`。

## 3. Compose 编排

**N/A**。GitGuard 运行时无 Compose 需求。CI 构建通常由 GitHub Actions 的 Service Container 直接提供（如需 PostgreSQL 等——但我们不需要，因为只用本地 SQLite）。

## 4. 持久化与卷

**N/A**（无运行时容器）。

## 5. 网络与端口

**N/A**。GitGuard CLI 不监听任何端口。CI 构建镜像在 build/smoke 阶段显式禁网（`--network=none`）以验证 DR-01「本地化」。

## 6. 环境配置注入

- CI 构建环境变量：
  - `RID`：构建目标 Runtime Identifier（如 `linux-x64`、`linux-arm64`、`win-x64`、`osx-arm64`）。
  - `VERSION`：Git tag 推导的 SemVer。
- 禁止将任何真实 Token / 签名密钥通过明文环境变量传入；CI 签名密钥走托管 Secret。

## 7. 资源限制

- CI 构建的内存限制 ≥ 4 GiB（Native AOT 构建耗内存）。
- CPU 限制：无强制，CI 默认即可。

## 8. 常用运维命令

```bash
# 本地复现 CI 构建
docker build -t gitguard-ci:local .

# 带 RID 的构建
docker build --build-arg RID=linux-arm64 -t gitguard-ci:arm64 .

# 禁网 smoke 验证 DR-01
docker run --rm --network=none gitguard-ci:local gitguard --version
```

## 9. 本地开发

本地开发不需要 Docker；直接：

```bash
dotnet restore
dotnet build
dotnet test
dotnet run --project src/GitGuard.Cli -- scan --staged
```

仅当需要复现 CI 构建问题时才用 Docker。

## 10. 变更记录

| 版本 | 日期 | 变更说明 |
| --- | --- | --- |
| 0.1.0 | 2026-10-15 | 初始化：澄清 Docker 仅用于 CI 构建；运行时无容器依赖；禁网 smoke 验证 DR-01 |
