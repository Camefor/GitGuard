<#
.SYNOPSIS
  构建 macOS DMG 桌面端安装包（GGD-0047）。

.DESCRIPTION
  发布 GitGuard.Desktop 自包含 .app，生成 .icns 图标并做 ad-hoc 签名，再用 hdiutil 打包为 DMG。
  配置 GITGUARD_MACOS_SIGNING_IDENTITY 时改用 Developer ID 签名；同时配置
  GITGUARD_MACOS_NOTARY_APPLE_ID / GITGUARD_MACOS_NOTARY_TEAM_ID / GITGUARD_MACOS_NOTARY_PASSWORD
  时提交 Apple notarization 并 stapling。
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $Version,

    [string] $Runtime = "osx-arm64",

    [string] $OutputDirectory = "artifacts/desktop"
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "../..")).Path
$outputRoot = [System.IO.Path]::GetFullPath((Join-Path $repoRoot $OutputDirectory))
$workRoot = Join-Path $outputRoot "work-dmg-$Runtime"
$publishDirectory = Join-Path $workRoot "publish"
$appPath = Join-Path $workRoot "GitGuard.app"
$artifactName = "gitguard-$Version-$Runtime.dmg"
$artifactPath = Join-Path $outputRoot $artifactName

function ConvertTo-BundleVersion([string] $value) {
    $numeric = ($value -split "-", 2)[0]
    $parts = @($numeric.Split("."))
    while ($parts.Count -lt 3) {
        $parts += "0"
    }

    return (($parts | Select-Object -First 3) -join ".")
}

if (Test-Path $workRoot) {
    Remove-Item $workRoot -Recurse -Force
}

$contentsDirectory = Join-Path $appPath "Contents"
$macOsDirectory = Join-Path $contentsDirectory "MacOS"
$resourcesDirectory = Join-Path $contentsDirectory "Resources"
New-Item -ItemType Directory -Force -Path $outputRoot, $publishDirectory, $macOsDirectory, $resourcesDirectory | Out-Null

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

Copy-Item (Join-Path $publishDirectory "*") $macOsDirectory -Recurse -Force
chmod +x (Join-Path $macOsDirectory "GitGuard.Desktop")

# CFBundleShortVersionString / CFBundleVersion 只接受数字段，预发布后缀不写入 Info.plist
$bundleVersion = ConvertTo-BundleVersion $Version
$infoPlist = (Get-Content (Join-Path $PSScriptRoot "macos/Info.plist.template") -Raw).
    Replace("{{VERSION}}", $bundleVersion).
    Replace("{{BUILD_VERSION}}", $bundleVersion)
[System.IO.File]::WriteAllText(
    (Join-Path $contentsDirectory "Info.plist"),
    $infoPlist,
    [System.Text.UTF8Encoding]::new($false))

# 从 1024 母图生成 iconset，再交给 iconutil 合并为 .icns
$iconSource = Join-Path $PSScriptRoot "assets/icon-1024.png"
$iconsetDirectory = Join-Path $workRoot "GitGuard.iconset"
New-Item -ItemType Directory -Force -Path $iconsetDirectory | Out-Null
foreach ($size in @(16, 32, 128, 256, 512)) {
    & sips -z $size $size $iconSource --out (Join-Path $iconsetDirectory "icon_${size}x${size}.png") | Out-Null
    $double = $size * 2
    & sips -z $double $double $iconSource --out (Join-Path $iconsetDirectory "icon_${size}x${size}@2x.png") | Out-Null
}

& iconutil -c icns $iconsetDirectory -o (Join-Path $resourcesDirectory "GitGuard.icns")
if ($LASTEXITCODE -ne 0) {
    throw "iconutil 生成 icns 失败（退出码 $LASTEXITCODE）"
}

$signingIdentity = $env:GITGUARD_MACOS_SIGNING_IDENTITY
if ([string]::IsNullOrWhiteSpace($signingIdentity)) {
    Write-Host "No Developer ID configured; using ad-hoc signing."
    & codesign --force --deep --sign - $appPath
} else {
    Write-Host "Signing with Developer ID: $signingIdentity"
    & codesign --force --deep --options runtime --timestamp --sign $signingIdentity $appPath
}

if ($LASTEXITCODE -ne 0) {
    throw "codesign 失败（退出码 $LASTEXITCODE）"
}

$dmgStaging = Join-Path $workRoot "dmg"
New-Item -ItemType Directory -Force -Path $dmgStaging | Out-Null
Copy-Item $appPath $dmgStaging -Recurse -Force
New-Item -ItemType SymbolicLink -Path (Join-Path $dmgStaging "Applications") -Target "/Applications" | Out-Null

Write-Host "Creating DMG..."
& hdiutil create -volname "GitGuard" -srcfolder $dmgStaging -ov -format UDZO $artifactPath
if ($LASTEXITCODE -ne 0) {
    throw "hdiutil 失败（退出码 $LASTEXITCODE）"
}

$notaryAppleId = $env:GITGUARD_MACOS_NOTARY_APPLE_ID
$notaryTeamId = $env:GITGUARD_MACOS_NOTARY_TEAM_ID
$notaryPassword = $env:GITGUARD_MACOS_NOTARY_PASSWORD
$hasNotaryCredentials =
    -not [string]::IsNullOrWhiteSpace($notaryAppleId) -and
    -not [string]::IsNullOrWhiteSpace($notaryTeamId) -and
    -not [string]::IsNullOrWhiteSpace($notaryPassword)
if ($hasNotaryCredentials -and [string]::IsNullOrWhiteSpace($signingIdentity)) {
    Write-Warning "已配置 notarization 凭据但未配置 Developer ID 签名身份；ad-hoc 签名无法公证，已跳过。"
} elseif ($hasNotaryCredentials) {
    Write-Host "Submitting DMG for notarization..."
    & xcrun notarytool submit $artifactPath --apple-id $notaryAppleId --team-id $notaryTeamId --password $notaryPassword --wait
    if ($LASTEXITCODE -ne 0) {
        throw "notarytool 失败（退出码 $LASTEXITCODE）"
    }

    & xcrun stapler staple $artifactPath
    if ($LASTEXITCODE -ne 0) {
        throw "stapler 失败（退出码 $LASTEXITCODE）"
    }
} elseif (-not [string]::IsNullOrWhiteSpace($signingIdentity)) {
    Write-Warning "已使用 Developer ID 签名，但未配置 notarization 凭据；产物未公证。"
}

Write-Host "DMG ready: $artifactPath"
