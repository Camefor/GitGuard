<#
.SYNOPSIS
  构建 Linux AppImage 桌面端安装包（GGD-0047）。

.DESCRIPTION
  发布 GitGuard.Desktop（linux-x64 自包含），组装 AppDir 并调用 appimagetool 生成无需 root 的
  AppImage。appimagetool 默认从 AppImageKit release 13 下载并缓存在 build/desktop/.tools。
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $Version,

    [string] $Runtime = "linux-x64",

    [string] $OutputDirectory = "artifacts/desktop"
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "../..")).Path
$outputRoot = [System.IO.Path]::GetFullPath((Join-Path $repoRoot $OutputDirectory))
$workRoot = Join-Path $outputRoot "work-appimage-$Runtime"
$publishDirectory = Join-Path $workRoot "publish"
$appDir = Join-Path $workRoot "GitGuard.AppDir"
$artifactName = "gitguard-$Version-$Runtime.AppImage"
$artifactPath = Join-Path $outputRoot $artifactName
$toolsDirectory = Join-Path $PSScriptRoot ".tools"
$appImageTool = Join-Path $toolsDirectory "appimagetool-x86_64.AppImage"

if (-not (Test-Path $appImageTool)) {
    New-Item -ItemType Directory -Force -Path $toolsDirectory | Out-Null
    $url = "https://github.com/AppImage/AppImageKit/releases/download/13/appimagetool-x86_64.AppImage"
    Write-Host "Downloading appimagetool: $url"
    Invoke-WebRequest -Uri $url -OutFile $appImageTool
    chmod +x $appImageTool
}

if (Test-Path $workRoot) {
    Remove-Item $workRoot -Recurse -Force
}

$libDirectory = Join-Path $appDir "usr/lib/gitguard"
New-Item -ItemType Directory -Force -Path $outputRoot, $publishDirectory, $libDirectory | Out-Null

Write-Host "Publishing GitGuard.Desktop ($Runtime, version $Version)..."
& dotnet publish (Join-Path $repoRoot "src/GitGuard.Desktop/GitGuard.Desktop.csproj") `
    -c Release `
    -r $Runtime `
    --self-contained true `
    -p:Version=$Version `
    -p:DebugType=None `
    -p:DebugSymbols=false `
    -o $publishDirectory
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish 失败（退出码 $LASTEXITCODE）"
}

Copy-Item (Join-Path $publishDirectory "*") $libDirectory -Recurse -Force
chmod +x (Join-Path $libDirectory "GitGuard.Desktop")

$appRun = @"
#!/bin/sh
HERE="`$(dirname "`$(readlink -f "`$0")")"
exec "`$HERE/usr/lib/gitguard/GitGuard.Desktop" "`$@"
"@
[System.IO.File]::WriteAllText(
    (Join-Path $appDir "AppRun"),
    $appRun.Replace("`r`n", "`n"),
    [System.Text.UTF8Encoding]::new($false))
chmod +x (Join-Path $appDir "AppRun")

$desktopEntry = @"
[Desktop Entry]
Type=Application
Name=GitGuard
Comment=Local Git secret leak prevention
Exec=GitGuard.Desktop
Icon=gitguard
Terminal=false
Categories=Development;Security;
"@
[System.IO.File]::WriteAllText(
    (Join-Path $appDir "gitguard.desktop"),
    $desktopEntry.Replace("`r`n", "`n"),
    [System.Text.UTF8Encoding]::new($false))

Copy-Item (Join-Path $PSScriptRoot "assets/gitguard.png") (Join-Path $appDir "gitguard.png") -Force
Copy-Item (Join-Path $PSScriptRoot "assets/gitguard.png") (Join-Path $appDir ".DirIcon") -Force

Write-Host "Creating AppImage..."
$environment = @{
    ARCH = "x86_64"
    APPIMAGE_EXTRACT_AND_RUN = "1"
}
$previous = @{}
foreach ($key in $environment.Keys) {
    $previous[$key] = [System.Environment]::GetEnvironmentVariable($key)
    [System.Environment]::SetEnvironmentVariable($key, $environment[$key])
}

try {
    & $appImageTool $appDir $artifactPath
    if ($LASTEXITCODE -ne 0) {
        throw "appimagetool 失败（退出码 $LASTEXITCODE）"
    }
}
finally {
    foreach ($key in $previous.Keys) {
        [System.Environment]::SetEnvironmentVariable($key, $previous[$key])
    }
}

chmod +x $artifactPath
Write-Host "AppImage ready: $artifactPath"
