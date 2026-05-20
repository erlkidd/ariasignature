#Requires -RunAsAdministrator
param(
    [string]$InstallRoot = "${env:ProgramFiles}\AriaSignature",
    [string]$ExpectedVersion = "1.1.0",
    [switch]$RepairMelezh,
    [switch]$SkipMelezh
)

$ErrorActionPreference = "Stop"

function Write-RepairLog {
    param([string]$Message)
    Write-Host "[repair-upgrade] $Message"
}

function Stop-AriaProcesses {
    foreach ($name in @(
            "AriaSignature.UI",
            "AriaSignature.Service",
            "AriaSignature.Api",
            "AriaSignature.MelezhHost",
            "oscript"
        )) {
        & taskkill.exe /F /IM "$name.exe" 2>$null | Out-Null
    }
}

function Wait-ScmServiceAbsent {
    param(
        [string]$ServiceName,
        [int]$TimeoutSeconds = 30
    )
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        & sc.exe query $ServiceName 2>$null | Out-Null
        if ($LASTEXITCODE -ne 0) {
            return $true
        }
        Start-Sleep -Seconds 1
    }
    return $false
}

function Get-OnDiskProductVersion {
    param([string]$Path)
    if (-not (Test-Path $Path)) {
        return ""
    }
    return (Get-Item -LiteralPath $Path).VersionInfo.ProductVersion
}

$apiExe = Join-Path $InstallRoot "service\AriaSignature.Api.exe"
$serviceExe = Join-Path $InstallRoot "service\AriaSignature.Service.exe"

Write-RepairLog "Install root: $InstallRoot"
Write-RepairLog "Expected version: $ExpectedVersion"

if (-not (Test-Path $apiExe)) {
    throw "[repair-upgrade] Missing $apiExe. Re-run AriaSignature installer."
}

Stop-AriaProcesses

foreach ($svc in @("AriaSignatureService", "AriaSignatureMelezhService")) {
    $existing = Get-Service -Name $svc -ErrorAction SilentlyContinue
    if ($existing) {
        if ($existing.Status -ne "Stopped") {
            Write-RepairLog "Stopping $svc"
            Stop-Service -Name $svc -Force -ErrorAction SilentlyContinue
            Start-Sleep -Seconds 2
        }
        Write-RepairLog "Deleting SCM entry $svc"
        & sc.exe delete $svc | Out-Null
        if (-not (Wait-ScmServiceAbsent -ServiceName $svc -TimeoutSeconds 30)) {
            throw "[repair-upgrade] $svc still present in SCM after delete"
        }
    }
}

Stop-AriaProcesses
Start-Sleep -Seconds 2

$onDisk = Get-OnDiskProductVersion -Path $apiExe
Write-RepairLog "On-disk API version: $onDisk"
if ($onDisk -notlike "*$ExpectedVersion*") {
    throw "[repair-upgrade] On-disk version mismatch. Expected $ExpectedVersion, got '$onDisk'. Reboot or re-run installer after closing all AriaSignature processes."
}

Write-RepairLog "Creating AriaSignatureService"
& sc.exe create AriaSignatureService "binPath= `"$serviceExe`"" "start= auto" "DisplayName= AriaSignature" "obj= LocalSystem" | Out-Null
if ($LASTEXITCODE -ne 0) {
    throw "[repair-upgrade] sc create AriaSignatureService failed with exit code $LASTEXITCODE"
}

Write-RepairLog "Starting AriaSignatureService"
& sc.exe start AriaSignatureService | Out-Null
if ($LASTEXITCODE -ne 0 -and $LASTEXITCODE -ne 1056) {
    throw "[repair-upgrade] sc start AriaSignatureService failed with exit code $LASTEXITCODE"
}

$deadline = (Get-Date).AddSeconds(60)
$apiOk = $false
while ((Get-Date) -lt $deadline) {
    try {
        $status = Invoke-RestMethod -Uri "http://127.0.0.1:5160/api/v1/status" -TimeoutSec 5
        Write-RepairLog "API version: $($status.version)"
        if ("$($status.version)" -like "*$ExpectedVersion*") {
            $apiOk = $true
            break
        }
    }
    catch {
        Start-Sleep -Seconds 2
    }
}

if (-not $apiOk) {
    throw "[repair-upgrade] API did not report version $ExpectedVersion on :5160"
}

if ($RepairMelezh -or -not $SkipMelezh) {
    $repairMelezh = Join-Path $PSScriptRoot "repair-melezh.ps1"
    if (Test-Path $repairMelezh) {
        Write-RepairLog "Running repair-melezh.ps1"
        & $repairMelezh -InstallRoot $InstallRoot
        if ($LASTEXITCODE -ne 0) {
            throw "[repair-upgrade] repair-melezh.ps1 failed with exit code $LASTEXITCODE"
        }
    }
    else {
        Write-RepairLog "repair-melezh.ps1 not found; skipping Melezh repair"
    }
}

Write-RepairLog "Upgrade repair completed successfully"
