<#
.SYNOPSIS
  解包 MSIX 以便发行冒烟（GGD-0047）。

.DESCRIPTION
  使用 Windows SDK 的 MakeAppx.exe 将 MSIX 解包到指定目录。CI 与 Release 的桌面端
  冒烟共用该脚本，避免在多个工作流中重复 SDK 定位逻辑。
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $Package,

    [Parameter(Mandatory = $true)]
    [string] $Destination
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

function Find-WindowsSdkTool([string] $name) {
    $command = Get-Command $name -ErrorAction SilentlyContinue
    if ($null -ne $command) {
        return $command.Source
    }

    $kitsRoot = Join-Path ${env:ProgramFiles(x86)} "Windows Kits/10/bin"
    if (-not (Test-Path $kitsRoot)) {
        throw "未找到 Windows SDK（$kitsRoot），无法定位 $name"
    }

    $candidate = Get-ChildItem $kitsRoot -Directory |
        Sort-Object Name -Descending |
        ForEach-Object { Join-Path $_.FullName "x64/$name" } |
        Where-Object { Test-Path $_ } |
        Select-Object -First 1
    if ($null -eq $candidate) {
        throw "未找到 $name，请安装 Windows SDK"
    }

    return $candidate
}

$packagePath = (Resolve-Path $Package).Path
$destinationPath = if ([System.IO.Path]::IsPathRooted($Destination)) {
    [System.IO.Path]::GetFullPath($Destination)
} else {
    [System.IO.Path]::GetFullPath((Join-Path (Get-Location).Path $Destination))
}

if (Test-Path $destinationPath) {
    Remove-Item $destinationPath -Recurse -Force
}

New-Item -ItemType Directory -Force -Path $destinationPath | Out-Null

$makeAppx = Find-WindowsSdkTool "makeappx.exe"
Write-Host "Unpacking $packagePath..."
& $makeAppx unpack /p $packagePath /d $destinationPath
if ($LASTEXITCODE -ne 0) {
    throw "makeappx unpack 失败（退出码 $LASTEXITCODE）"
}

Write-Host "MSIX unpacked: $destinationPath"
