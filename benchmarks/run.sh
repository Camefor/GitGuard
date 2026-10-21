#!/usr/bin/env bash
# 全量基准：startup / staged / push / throughput（NFR-P-001~004），并写出 JSON 归档。
# 注意：本文件按 POSIX shell 约定保存为 UTF-8 无 BOM + LF（否则 shebang 无法执行）。
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
project="$root/benchmarks/GitGuard.Benchmarks"
json="${GITGUARD_BENCH_JSON:-$root/artifacts/benchmarks/latest.json}"

case "$(uname -s)" in
  MINGW* | MSYS* | CYGWIN*)
    default_exe="$root/artifacts/publish/gitguard-win-x64/gitguard.exe"
    ;;
  Darwin*)
    default_exe="$root/artifacts/publish/gitguard-osx-arm64/gitguard"
    ;;
  *)
    default_exe="$root/artifacts/publish/gitguard-linux-x64/gitguard"
    ;;
esac

exe="${GITGUARD_BENCH_EXE:-$default_exe}"
if [[ ! -f "$exe" ]]; then
  printf '找不到发布版可执行文件：%s\n' "$exe" >&2
  printf '请先发布：dotnet publish src/GitGuard.Cli/GitGuard.Cli.csproj -c Release -r <rid> -p:PublishAot=true -o %s\n' \
    "$(dirname "$exe")" >&2
  exit 1
fi

# Git Bash 下把 POSIX 路径转换为 Windows 路径，供 .NET 进程使用
if command -v cygpath >/dev/null 2>&1; then
  project="$(cygpath -w "$project")"
  exe="$(cygpath -w "$exe")"
  json="$(cygpath -w "$json")"
fi

mkdir -p "$(dirname "$json")"
exec dotnet run -c Release --project "$project" -- all --exe "$exe" --json "$json" "$@"
