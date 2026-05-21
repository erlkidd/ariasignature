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

function Invoke-MelezhHandlerSmoke {
    param(
        [string]$Uri,
        [string]$Method = "GET",
        [string]$Body = $null,
        [string]$Label,
        [int]$MaxAttempts = 5
    )
    for ($attempt = 1; $attempt -le $MaxAttempts; $attempt++) {
        try {
            $params = @{
                Uri             = $Uri
                Method          = $Method
                TimeoutSec      = 30
                UseBasicParsing = $true
            }
            if ($Method -eq "POST") {
                $params["Body"] = $Body
                $params["ContentType"] = "application/json"
            }
            $r = Invoke-WebRequest @params
            if ($r.StatusCode -ge 200 -and $r.StatusCode -lt 300) {
                $json = $r.Content | ConvertFrom-Json -ErrorAction SilentlyContinue
                if ($null -ne $json -and $json.PSObject.Properties.Name -contains "result" -and $json.result -eq $false) {
                    throw "$Label returned result=false: $($r.Content)"
                }
                Write-RepairLog "$Label OK"
                return
            }
            throw "$Label HTTP $($r.StatusCode)"
        }
        catch {
            if ($attempt -eq $MaxAttempts) { throw }
            Write-RepairLog "$Label attempt $attempt failed, retrying: $($_.Exception.Message)"
            Start-Sleep -Seconds 2
        }
    }
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

Write-RepairLog "Rebuilding Melezh handler catalog (bootstrap-only)"
$bootstrapOut = & $hostExe --bootstrap-only 2>&1
$bootstrapOut | ForEach-Object { Write-RepairLog $_ }
if ($LASTEXITCODE -ne 0) {
    throw "[repair-melezh] MelezhHost --bootstrap-only failed with exit code $LASTEXITCODE"
}

$needsRestart = $false
foreach ($line in $bootstrapOut) {
    if ($line -match "marker=melezh-bootstrap-upgraded") {
        $needsRestart = $true
        break
    }
}

if ($needsRestart -or ($bootstrapOut -match "restart-recommended=true")) {
    Write-RepairLog "Bootstrap upgraded project; restarting $ServiceName to reset OInt HTTP client"
    Restart-Service -Name $ServiceName -Force -ErrorAction SilentlyContinue
    Start-Sleep -Seconds 3
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

$assertScript = Join-Path $PSScriptRoot "Test-MelezhAssert.ps1"
if (Test-Path $assertScript) {
    . $assertScript
    Assert-MelezhHandlerJson -Uri "http://127.0.0.1:$Port/aria_ping" -Label "aria_ping" | Out-Null
    Write-RepairLog "aria_ping handler OK (result != false)"
}

Invoke-MelezhHandlerSmoke -Uri "http://127.0.0.1:$Port/aria_ping" -Label "aria_ping smoke"
Invoke-MelezhHandlerSmoke -Uri "http://127.0.0.1:$Port/aria_sync" -Method POST -Body "{}" -Label "aria_sync smoke"

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
