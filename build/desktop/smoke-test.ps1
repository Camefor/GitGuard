<#
.SYNOPSIS
  桌面端发行冒烟（GGD-0047）：用 CLI 生成审计记录，再让桌面端读取并导出。

.DESCRIPTION
  在隔离的数据 / 配置目录中创建临时 Git 仓库，用发行版 CLI 扫描暂存区中的金丝雀 Token，
  然后以 --smoke-test 启动桌面端（MSIX 解包目录 / .app 二进制 / AppImage），断言：
  1. 桌面端能读取 CLI 写入的审计运行（数量与摘要一致）；
  2. 能读取运行明细（Finding 数量 > 0）；
  3. 能通过 IAuditOperations 导出全部审计记录，且导出数量与查询总数一致。

  Windows 上 Windows PowerShell 5.1 与 PowerShell 7 均可运行；macOS / Linux 需要 PowerShell 7（pwsh）。
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $DesktopExecutable,

    [Parameter(Mandatory = $true)]
    [string] $CliExecutable,

    [ValidateRange(0, 100000)]
    [int] $ExpectedRuns = 1,

    [string] $WorkDirectory,

    [switch] $KeepWorkDirectory
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

function ConvertTo-CommandLineArgument([string] $value) {
    # Windows PowerShell 5.1 的 ProcessStartInfo 没有 ArgumentList，按 Win32 规则转义回退
    if ($value -notmatch '[\s"]') {
        return $value
    }

    $builder = [System.Text.StringBuilder]::new()
    [void] $builder.Append('"')
    $backslashes = 0
    foreach ($character in $value.ToCharArray()) {
        if ($character -eq [char] '\') {
            $backslashes++
            continue
        }

        if ($character -eq [char] '"') {
            [void] $builder.Append([string]::new([char] '\', $backslashes * 2 + 1))
            [void] $builder.Append('"')
            $backslashes = 0
            continue
        }

        if ($backslashes -gt 0) {
            [void] $builder.Append([string]::new([char] '\', $backslashes))
            $backslashes = 0
        }

        [void] $builder.Append($character)
    }

    if ($backslashes -gt 0) {
        [void] $builder.Append([string]::new([char] '\', $backslashes * 2))
    }

    [void] $builder.Append('"')
    return $builder.ToString()
}

function Invoke-Process([string] $FilePath, [string[]] $Arguments, [string] $WorkingDirectory) {
    $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $FilePath
    $startInfo.WorkingDirectory = $WorkingDirectory
    $startInfo.UseShellExecute = $false
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    if ($null -ne $startInfo.PSObject.Properties["ArgumentList"]) {
        foreach ($argument in $Arguments) {
            $startInfo.ArgumentList.Add($argument)
        }
    } else {
        $startInfo.Arguments = ($Arguments | ForEach-Object { ConvertTo-CommandLineArgument $_ }) -join " "
    }

    $process = [System.Diagnostics.Process]::Start($startInfo)
    $stdout = $process.StandardOutput.ReadToEndAsync()
    $stderr = $process.StandardError.ReadToEndAsync()
    $process.WaitForExit()

    return [pscustomobject]@{
        ExitCode = $process.ExitCode
        StandardOutput = $stdout.GetAwaiter().GetResult()
        StandardError = $stderr.GetAwaiter().GetResult()
    }
}

function Restore-Environment([hashtable] $Snapshot) {
    foreach ($key in $Snapshot.Keys) {
        [System.Environment]::SetEnvironmentVariable($key, $Snapshot[$key])
    }
}

$desktopPath = (Resolve-Path $DesktopExecutable).Path
$cliPath = (Resolve-Path $CliExecutable).Path
$workRoot = if ([string]::IsNullOrWhiteSpace($WorkDirectory)) {
    Join-Path ([System.IO.Path]::GetTempPath()) ("gitguard-desktop-smoke-" + [Guid]::NewGuid().ToString("N"))
} else {
    [System.IO.Path]::GetFullPath($WorkDirectory)
}

$isWindowsHost = [System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform(
    [System.Runtime.InteropServices.OSPlatform]::Windows)
$isMacOsHost = [System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform(
    [System.Runtime.InteropServices.OSPlatform]::OSX)
$environmentSnapshot = @{}
foreach ($key in @("LOCALAPPDATA", "XDG_DATA_HOME", "HOME", "USERPROFILE", "APPIMAGE_EXTRACT_AND_RUN")) {
    $environmentSnapshot[$key] = [System.Environment]::GetEnvironmentVariable($key)
}

$succeeded = $false
try {
    New-Item -ItemType Directory -Force -Path $workRoot | Out-Null

    # 与 UserDataPaths 的平台规则保持一致，把 CLI 与桌面端都指向同一隔离数据目录
    if ($isWindowsHost) {
        $env:LOCALAPPDATA = Join-Path $workRoot "localappdata"
        $env:USERPROFILE = Join-Path $workRoot "home"
        $dataDirectory = Join-Path $env:LOCALAPPDATA "GitGuard"
    } elseif ($isMacOsHost) {
        $env:HOME = Join-Path $workRoot "home"
        $dataDirectory = Join-Path $env:HOME "Library/Application Support/GitGuard"
    } else {
        $env:HOME = Join-Path $workRoot "home"
        $env:XDG_DATA_HOME = Join-Path $workRoot "xdgdata"
        $env:APPIMAGE_EXTRACT_AND_RUN = "1"
        $dataDirectory = Join-Path $env:XDG_DATA_HOME "gitguard"
    }

    if (-not $isWindowsHost) {
        # 从 zip / artifact 解出的可执行文件可能丢失执行位
        & chmod +x $cliPath $desktopPath
    }

    $repository = Join-Path $workRoot "repo"
    $reportPath = Join-Path $workRoot "desktop-smoke.json"
    $exportPath = Join-Path $workRoot "audit-export.json"
    $cliLogPath = Join-Path $workRoot "cli-scan.log"
    New-Item -ItemType Directory -Force -Path $repository, $dataDirectory | Out-Null

    Write-Host "Preparing audit record with CLI..."
    $git = (Get-Command git -ErrorAction Stop).Source
    $gitInit = Invoke-Process $git @("init", "--quiet", $repository) $workRoot
    if ($gitInit.ExitCode -ne 0) {
        throw "git init 失败：$($gitInit.StandardError)"
    }

    foreach ($setting in @(
        @("user.email", "smoke@gitguard.local"),
        @("user.name", "GitGuard Smoke"),
        @("commit.gpgsign", "false"),
        @("core.autocrlf", "false")
    )) {
        $configure = Invoke-Process $git (@("-C", $repository, "config") + $setting) $workRoot
        if ($configure.ExitCode -ne 0) {
            throw "git config $($setting[0]) 失败：$($configure.StandardError)"
        }
    }

    [System.IO.File]::WriteAllText(
        (Join-Path $repository "leak.txt"),
        "aws_access_key_id = `"AKIAIOSFODNN7EXAMPLE`"`n",
        [System.Text.UTF8Encoding]::new($false))
    $add = Invoke-Process $git @("-C", $repository, "add", "leak.txt") $workRoot
    if ($add.ExitCode -ne 0) {
        throw "git add 失败：$($add.StandardError)"
    }

    $scan = Invoke-Process $cliPath @("scan", "--staged", "--no-color", "--language", "en-US") $repository
    [System.IO.File]::WriteAllText(
        $cliLogPath,
        "exit=$($scan.ExitCode)`n--- stdout ---`n$($scan.StandardOutput)`n--- stderr ---`n$($scan.StandardError)",
        [System.Text.UTF8Encoding]::new($false))
    if ($scan.ExitCode -ne 1) {
        throw "CLI 扫描应阻断金丝雀 Token 并记录审计（退出码 1），实际退出码 $($scan.ExitCode)。日志：$cliLogPath`n$($scan.StandardError)"
    }

    Write-Host "Starting desktop smoke..."
    $smoke = Invoke-Process $desktopPath @(
        "--smoke-test",
        "--data-directory", $dataDirectory,
        "--expected-runs", $ExpectedRuns.ToString([System.Globalization.CultureInfo]::InvariantCulture),
        "--report", $reportPath,
        "--export", $exportPath
    ) $workRoot
    if (-not (Test-Path $reportPath)) {
        throw "桌面端未生成冒烟报告（退出码 $($smoke.ExitCode)）：$($smoke.StandardError)"
    }

    $report = Get-Content $reportPath -Raw | ConvertFrom-Json
    if ($smoke.ExitCode -ne 0 -or -not $report.ok) {
        throw "桌面端冒烟失败（退出码 $($smoke.ExitCode)）：$($report.error)`n$($smoke.StandardError)"
    }

    if ($report.auditRunCount -lt $ExpectedRuns) {
        throw "桌面端只读取到 $($report.auditRunCount) 条审计记录（期望至少 $ExpectedRuns 条）"
    }

    if ($report.latestRunFindingCount -lt 1) {
        throw "桌面端读取的最近运行没有 Finding 明细"
    }

    if ($report.latestRunResult -ne "Blocked") {
        throw "最近运行结果应为 Blocked，实际为 $($report.latestRunResult)"
    }

    if ($null -eq $report.exportedRunCount -or $report.exportedRunCount -ne $report.auditRunCount) {
        throw "桌面端导出数量 $($report.exportedRunCount) 与查询总数 $($report.auditRunCount) 不一致"
    }

    if (-not (Test-Path $exportPath)) {
        throw "桌面端未生成审计导出文件：$exportPath"
    }

    Write-Host "Desktop smoke passed: $($report.auditRunCount) audit run(s), latest findings=$($report.latestRunFindingCount)."
    $succeeded = $true
}
finally {
    Restore-Environment $environmentSnapshot
    if ($succeeded -and -not $KeepWorkDirectory) {
        Remove-Item $workRoot -Recurse -Force -ErrorAction SilentlyContinue
    } elseif (-not $succeeded) {
        Write-Host "Smoke workspace kept for diagnosis: $workRoot"
    }
}
