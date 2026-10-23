<#
.SYNOPSIS
  构建 Windows MSIX 桌面端安装包（GGD-0047）。

.DESCRIPTION
  发布 GitGuard.Desktop（win-x64 自包含），生成带图标的 MSIX 布局并调用 Windows SDK 的
  MakeAppx 打包。未配置证书时输出未签名 MSIX（可解包冒烟，不可直接安装）；配置
  GITGUARD_MSIX_CERT_BASE64 / GITGUARD_MSIX_CERT_PASSWORD 后使用 signtool 签名。
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $Version,

    [string] $Runtime = "win-x64",

    [string] $OutputDirectory = "artifacts/desktop",

    [string] $Publisher = "CN=GitGuard"
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "../..")).Path
$outputRoot = [System.IO.Path]::GetFullPath((Join-Path $repoRoot $OutputDirectory))
$workRoot = Join-Path $outputRoot "work-msix-$Runtime"
$publishDirectory = Join-Path $workRoot "publish"
$layoutDirectory = Join-Path $workRoot "layout"
$artifactName = "gitguard-$Version-$Runtime.msix"
$artifactPath = Join-Path $outputRoot $artifactName

function ConvertTo-MsixVersion([string] $value) {
    $numeric = ($value -split "-", 2)[0]
    $parts = @($numeric.Split("."))
    if ($parts.Count -gt 4) {
        throw "MSIX 版本最多四段：$value"
    }

    while ($parts.Count -lt 4) {
        $parts += "0"
    }

    foreach ($part in $parts) {
        $number = 0
        if (-not [int]::TryParse($part, [ref] $number) -or $number -lt 0 -or $number -gt 65535) {
            throw "MSIX 版本段必须是 0..65535 的整数：$value"
        }
    }

    return ($parts -join ".")
}

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

if (Test-Path $workRoot) {
    Remove-Item $workRoot -Recurse -Force
}

New-Item -ItemType Directory -Force -Path $outputRoot, $publishDirectory, $layoutDirectory | Out-Null

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

Copy-Item (Join-Path $publishDirectory "*") $layoutDirectory -Recurse -Force
$assetsDirectory = Join-Path $layoutDirectory "Assets"
New-Item -ItemType Directory -Force -Path $assetsDirectory | Out-Null
Copy-Item (Join-Path $PSScriptRoot "assets/*.png") $assetsDirectory -Force

# 可选签名：证书以 base64 PFX 注入，Publisher 必须与证书 Subject 一致
$certificatePath = $null
$certificatePassword = $env:GITGUARD_MSIX_CERT_PASSWORD
if (-not [string]::IsNullOrWhiteSpace($env:GITGUARD_MSIX_CERT_BASE64)) {
    $certificatePath = Join-Path $workRoot "signing.pfx"
    [System.IO.File]::WriteAllBytes(
        $certificatePath,
        [System.Convert]::FromBase64String($env:GITGUARD_MSIX_CERT_BASE64))
    $certificate = [System.Security.Cryptography.X509Certificates.X509Certificate2]::new(
        $certificatePath,
        $certificatePassword)
    $Publisher = $certificate.Subject
}

$manifestTemplate = Get-Content (Join-Path $PSScriptRoot "msix/AppxManifest.xml.template") -Raw
$manifest = $manifestTemplate.
    Replace("{{PUBLISHER}}", [System.Security.SecurityElement]::Escape($Publisher)).
    Replace("{{VERSION}}", (ConvertTo-MsixVersion $Version)).
    Replace("{{ARCHITECTURE}}", "x64")
[System.IO.File]::WriteAllText(
    (Join-Path $layoutDirectory "AppxManifest.xml"),
    $manifest,
    [System.Text.UTF8Encoding]::new($false))

$makeAppx = Find-WindowsSdkTool "makeappx.exe"
Write-Host "Packing MSIX..."
& $makeAppx pack /d $layoutDirectory /p $artifactPath /o
if ($LASTEXITCODE -ne 0) {
    throw "makeappx 失败（退出码 $LASTEXITCODE）"
}

if ($null -ne $certificatePath) {
    $signtool = Find-WindowsSdkTool "signtool.exe"
    Write-Host "Signing MSIX..."
    & $signtool sign /fd SHA256 /f $certificatePath /p $certificatePassword $artifactPath
    if ($LASTEXITCODE -ne 0) {
        throw "signtool 签名失败（退出码 $LASTEXITCODE）"
    }
}

Write-Host "MSIX ready: $artifactPath"
