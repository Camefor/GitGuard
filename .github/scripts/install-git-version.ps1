#requires -Version 5.1
<#
.SYNOPSIS
    下载并安装指定版本的 Git for Windows 便携版，并把 git 加入后续步骤的 PATH。

.DESCRIPTION
    用于 GGD-0017 的 Git 版本兼容矩阵。GitHub 托管 runner 只提供最新稳定版 Git，
    因此旧版本从 git-for-windows/git 官方 release 下载 PortableGit 自解压包。
    脚本只负责解压与 PATH 注入，不修改 runner 预装的 Git。

.PARAMETER Version
    Git for Windows 版本号（不含 v 前缀与 .windows.N 后缀），例如 2.9.3、2.40.1。
#>
param(
    [Parameter(Mandatory = $true)]
    [string]$Version
)

$ErrorActionPreference = 'Stop'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

$installRoot = Join-Path $env:RUNNER_TEMP "git-$Version"
$assetName = "PortableGit-$Version-64-bit.7z.exe"
$downloadUrl = "https://github.com/git-for-windows/git/releases/download/v$Version.windows.1/$assetName"
$installerPath = Join-Path $installRoot $assetName
$portableGitDirectory = Join-Path $installRoot 'PortableGit'
$gitExecutable = Join-Path $portableGitDirectory 'cmd\git.exe'

Remove-Item -Path $installRoot -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Path $installRoot -Force | Out-Null

Write-Host "Downloading $downloadUrl"
Invoke-WebRequest -Uri $downloadUrl -OutFile $installerPath -UseBasicParsing

# PortableGit 为 7z 自解压包：在目标目录内执行，解压到相对路径 PortableGit
Push-Location $installRoot
try {
    & $installerPath -y | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw "PortableGit 解压失败，退出码 $LASTEXITCODE。"
    }
}
finally {
    Pop-Location
}

if (-not (Test-Path -LiteralPath $gitExecutable)) {
    throw "未找到 git 可执行文件：$gitExecutable"
}

$installedVersion = & $gitExecutable --version
Write-Host "Installed: $installedVersion"
if ($installedVersion -notmatch [regex]::Escape($Version)) {
    throw "Git 版本不匹配：期望 $Version，实际 $installedVersion"
}

Add-Content -Path $env:GITHUB_PATH -Value (Join-Path $portableGitDirectory 'cmd')
