#Requires -RunAsAdministrator
param(
    [string]$InstallRoot = "${env:ProgramFiles}\AriaSignature",
    [string]$ServiceName = "AriaSignatureMelezhService",
    [int]$Port = 7788,
    [switch]$RestartMainService
)

$ErrorActionPreference = "Stop"

function Write-RepairLog {
    param([string]$Message)
    Write-Host "[repair-melezh] $Message"
}

$hostExe = Join-Path $InstallRoot "melezh-host\AriaSignature.MelezhHost.exe"
$melezhBat = Join-Path $InstallRoot "melezh\bin\melezh.bat"
$oscriptExe = Join-Path $InstallRoot "melezh\lib\oint\bin\oscript.exe"

foreach ($pair in @(
        @{ Path = $hostExe; Label = "Melezh host exe" },
        @{ Path = $melezhBat; Label = "melezh.bat" },
        @{ Path = $oscriptExe; Label = "oscript.exe" }
    )) {
    if (-not (Test-Path $pair.Path)) {
        throw "[repair-melezh] Missing $($pair.Label): $($pair.Path). Re-run installer with OInt bundle (prepare-melezh.ps1)."
    }
}

$svc = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($svc) {
    if ($svc.Status -ne "Stopped") {
        Write-RepairLog "Stopping $ServiceName"
        Stop-Service -Name $ServiceName -Force -ErrorAction SilentlyContinue
        Start-Sleep -Seconds 2
    }
    Write-RepairLog "Deleting existing $ServiceName registration"
    & sc.exe delete $ServiceName | Out-Null
    Start-Sleep -Seconds 2
}

$binPath = "`"$hostExe`""
Write-RepairLog "Creating $ServiceName"
& sc.exe create $ServiceName "binPath= $binPath" "start= auto" "DisplayName= AriaSignature Melezh" "obj= LocalSystem" | Out-Null
if ($LASTEXITCODE -ne 0) {
    throw "[repair-melezh] sc create failed with exit code $LASTEXITCODE"
}

& sc.exe description $ServiceName "OpenIntegrations Melezh HTTP gateway for AriaSignature" | Out-Null
Write-RepairLog "Starting $ServiceName"
& sc.exe start $ServiceName | Out-Null
if ($LASTEXITCODE -ne 0) {
    throw "[repair-melezh] sc start failed with exit code $LASTEXITCODE"
}

$deadline = (Get-Date).AddSeconds(90)
$uiOk = $false
while ((Get-Date) -lt $deadline) {
    try {
        $r = Invoke-WebRequest -Uri "http://127.0.0.1:$Port/ui" -UseBasicParsing -TimeoutSec 5
        if ($r.StatusCode -ge 200 -and $r.StatusCode -lt 400) {
            $uiOk = $true
            break
        }
    }
    catch {
        Start-Sleep -Seconds 2
    }
}

if (-not $uiOk) {
    throw "[repair-melezh] Melezh Web UI did not respond on http://127.0.0.1:$Port/ui"
}

Write-RepairLog "Melezh Web UI is reachable"

if ($RestartMainService) {
    $main = Get-Service -Name "AriaSignatureService" -ErrorAction SilentlyContinue
    if ($main) {
        Write-RepairLog "Restarting AriaSignatureService"
        Restart-Service -Name "AriaSignatureService" -Force
        $status = Invoke-RestMethod -Uri "http://127.0.0.1:5160/api/v1/status" -TimeoutSec 10
        Write-RepairLog "AriaSignature API version: $($status.version)"
    }
}

Write-RepairLog "Repair completed successfully"
