#Requires -RunAsAdministrator
param(
    [string]$InstallRoot = "${env:ProgramFiles}\AriaSignature",
    [int]$Port = 7788
)

$ErrorActionPreference = "Continue"
$serviceName = "AriaSignatureMelezhService"

function Write-Diag([string]$Message) {
    Write-Host "[diagnose-melezh] $Message"
}

Write-Diag "InstallRoot=$InstallRoot Port=$Port"

$hostExe = Join-Path $InstallRoot "melezh-host\AriaSignature.MelezhHost.exe"
$melezhBat = Join-Path $InstallRoot "melezh\bin\melezh.bat"
$oscript = Join-Path $InstallRoot "melezh\lib\oint\bin\oscript.exe"
$shareOint = Join-Path $InstallRoot "melezh\share\oint"

foreach ($pair in @(
        @{ Path = $hostExe; Label = "MelezhHost" },
        @{ Path = $melezhBat; Label = "melezh.bat" },
        @{ Path = $oscript; Label = "oscript.exe" },
        @{ Path = $shareOint; Label = "share/oint" }
    )) {
    if (Test-Path $pair.Path) {
        Write-Diag "OK $($pair.Label): $($pair.Path)"
    }
    else {
        Write-Diag "MISSING $($pair.Label): $($pair.Path)"
    }
}

$bundleTest = Join-Path $PSScriptRoot "Test-MelezhBundle.ps1"
if ((Test-Path $bundleTest) -and (Test-Path (Join-Path $InstallRoot "melezh"))) {
    try {
        & $bundleTest -RootPath (Join-Path $InstallRoot "melezh")
    }
    catch {
        Write-Diag "Bundle check: $($_.Exception.Message)"
    }
}

Write-Diag "--- sc query $serviceName ---"
& sc.exe query $serviceName 2>&1 | ForEach-Object { Write-Diag $_ }

try {
    $svc = Get-Service -Name $serviceName -ErrorAction Stop
    Write-Diag "Service Status=$($svc.Status) StartType=$($svc.StartType)"
}
catch {
    Write-Diag "Service not registered or inaccessible: $($_.Exception.Message)"
}

$logDir = Join-Path $env:ProgramData "AriaSignature\logs"
if (Test-Path $logDir) {
    $latest = Get-ChildItem -Path $logDir -Filter "melezh-host-*.log" -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if ($latest) {
        Write-Diag "--- tail $($latest.Name) ---"
        Get-Content -Path $latest.FullName -Tail 25 -ErrorAction SilentlyContinue | ForEach-Object { Write-Diag $_ }
    }
}

foreach ($uri in @("http://127.0.0.1:$Port/ui", "http://127.0.0.1:$Port/")) {
  try {
    $r = Invoke-WebRequest -Uri $uri -UseBasicParsing -TimeoutSec 8
    Write-Diag "HTTP $($r.StatusCode) $uri"
  }
  catch {
    Write-Diag "HTTP FAIL $uri : $($_.Exception.Message)"
  }
}

$projectDir = Join-Path $env:ProgramData "AriaSignature\melezh"
if (Test-Path $projectDir) {
    Get-ChildItem -Path $projectDir -Filter "*.melezh" -ErrorAction SilentlyContinue | ForEach-Object {
        Write-Diag "Project file: $($_.FullName)"
    }
}
else {
    Write-Diag "No project dir: $projectDir"
}

Write-Diag "Done."
