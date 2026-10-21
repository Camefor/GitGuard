#Requires -Version 5.1
<#
.SYNOPSIS
  NFR-P-001：冷启动基准（默认 50 次测量，P95 ≤ 150 ms）。

.DESCRIPTION
  以发布版 gitguard 可执行文件运行 `gitguard version`，统计进程墙钟耗时。
  默认使用 artifacts/publish/gitguard-win-x64/gitguard.exe；
  也可通过 -Exe 或环境变量 GITGUARD_BENCH_EXE 指定。
#>
[CmdletBinding()]
param(
    [string]$Exe = $env:GITGUARD_BENCH_EXE,
    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]]$BenchmarkArguments
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root 'benchmarks/GitGuard.Benchmarks'

if (-not $Exe) {
    $Exe = Join-Path $root 'artifacts/publish/gitguard-win-x64/gitguard.exe'
}

if (-not (Test-Path -LiteralPath $Exe -PathType Leaf)) {
    Write-Error "找不到发布版可执行文件：$Exe。请先运行：dotnet publish src/GitGuard.Cli/GitGuard.Cli.csproj -c Release -r win-x64 -p:PublishAot=true -o artifacts/publish/gitguard-win-x64"
    exit 1
}

& dotnet run -c Release --project $project -- startup --exe $Exe @BenchmarkArguments
exit $LASTEXITCODE
