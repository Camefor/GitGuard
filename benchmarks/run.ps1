#Requires -Version 5.1
<#
.SYNOPSIS
  全量基准：startup / staged / push / throughput（NFR-P-001~004）。

.DESCRIPTION
  以发布版 gitguard 可执行文件测量进程级场景，并在进程内测量检测器管线吞吐；
  结果同时输出到控制台并写入 JSON（默认 artifacts/benchmarks/latest.json）。
#>
[CmdletBinding()]
param(
    [string]$Exe = $env:GITGUARD_BENCH_EXE,
    [string]$Json,
    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]]$BenchmarkArguments
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root 'benchmarks/GitGuard.Benchmarks'

if (-not $Exe) {
    $Exe = Join-Path $root 'artifacts/publish/gitguard-win-x64/gitguard.exe'
}

if (-not $Json) {
    $Json = $env:GITGUARD_BENCH_JSON
}

if (-not $Json) {
    $Json = Join-Path $root 'artifacts/benchmarks/latest.json'
}

if (-not (Test-Path -LiteralPath $Exe -PathType Leaf)) {
    Write-Error "找不到发布版可执行文件：$Exe。请先运行：dotnet publish src/GitGuard.Cli/GitGuard.Cli.csproj -c Release -r win-x64 -p:PublishAot=true -o artifacts/publish/gitguard-win-x64"
    exit 1
}

New-Item -ItemType Directory -Force -Path (Split-Path -Parent $Json) | Out-Null

& dotnet run -c Release --project $project -- all --exe $Exe --json $Json @BenchmarkArguments
exit $LASTEXITCODE
