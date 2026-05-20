# Integration: Aria API + Melezh bootstrap + handler proxy smoke (requires bundle).
param(
    [string]$MelezhRoot = "",
    [string]$RepoRoot = "",
    [int]$ApiPort = 18767,
    [int]$MelezhPort = 18768
)

$ErrorActionPreference = "Stop"
if ([string]::IsNullOrWhiteSpace($RepoRoot)) {
    $RepoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
}
if ([string]::IsNullOrWhiteSpace($MelezhRoot)) {
    $MelezhRoot = Join-Path $RepoRoot "installer\melezh\bundle"
}

$MelezhRoot = [System.IO.Path]::GetFullPath($MelezhRoot)
$oscript = Join-Path $MelezhRoot "lib\oint\bin\oscript.exe"
if (-not (Test-Path $oscript)) {
    Write-Host "[Test-MelezhAriaBridge] SKIP: bundle missing"
    exit 0
}

$MethodRunProject = -join @(
    [char]0x0417, [char]0x0430, [char]0x043F, [char]0x0443, [char]0x0441, [char]0x0442, [char]0x0438, [char]0x0442, [char]0x044C,
    [char]0x041F, [char]0x0440, [char]0x043E, [char]0x0435, [char]0x043A, [char]0x0442
)

$apiUrl = "http://127.0.0.1:$ApiPort"
$apiProj = Join-Path $RepoRoot "src\api\AriaSignature.Api\AriaSignature.Api.csproj"
$db = Join-Path $env:TEMP ("aria_bridge_api_" + [guid]::NewGuid().ToString("N") + ".db")
$env:ConnectionStrings__AriaSignature = "Data Source=$db"

Write-Host "[Test-MelezhAriaBridge] building API"
dotnet build $apiProj -c Release -v q | Out-Null
if ($LASTEXITCODE -ne 0) { throw "API build failed" }

Write-Host "[Test-MelezhAriaBridge] starting API on $apiUrl"
$apiProc = Start-Process -FilePath "dotnet" -WorkingDirectory $RepoRoot -ArgumentList @(
    "run", "-c", "Release", "--no-build", "--project", $apiProj,
    "--urls", $apiUrl, "--no-launch-profile"
) -PassThru -WindowStyle Hidden
try {
    $ready = $false
    for ($i = 0; $i -lt 60; $i++) {
        try {
            $null = Invoke-WebRequest -Uri "$apiUrl/api/v1/status" -UseBasicParsing -TimeoutSec 5
            $ready = $true
            break
        }
        catch { Start-Sleep -Milliseconds 500 }
    }
    if (-not $ready) { throw "API not ready on $apiUrl" }

    $proj = Join-Path $env:TEMP ("aria_bridge_melezh_" + [guid]::NewGuid().ToString("N").Substring(0, 8) + ".melezh")
    $env:ARIASIGNATURE_MELEZH_ROOT = $MelezhRoot
    $env:ARIASIGNATURE_MELEZH_PROJECT = $proj
    $env:ARIASIGNATURE_API_PORT = "$ApiPort"
    $env:ARIASIGNATURE_MELEZH_PORT = "$MelezhPort"
    Remove-Item $proj -Force -EA SilentlyContinue

    $hostProj = Join-Path $RepoRoot "src\service\AriaSignature.MelezhHost\AriaSignature.MelezhHost.csproj"
    dotnet build $hostProj -c Release -v q | Out-Null
    $hostDll = Get-ChildItem -Path (Join-Path $RepoRoot "src\service\AriaSignature.MelezhHost\bin\Release") -Recurse -Filter "AriaSignature.MelezhHost.dll" | Select-Object -First 1
    dotnet $hostDll.FullName --bootstrap-only 2>&1 | Out-Null
    if (-not (Test-Path $proj)) { throw "melezh project missing after bootstrap" }

    $appOs = Join-Path $MelezhRoot "share\oint\lib\melezh\core\Classes\app.os"
    $melezhProc = Start-Process -FilePath $oscript -ArgumentList @(
        $appOs, $MethodRunProject, "--port", "$MelezhPort", "--proj", $proj
    ) -WorkingDirectory (Split-Path $oscript) -PassThru -WindowStyle Hidden
    try {
        $mzReady = $false
        for ($i = 0; $i -lt 80; $i++) {
            try {
                $null = Invoke-WebRequest -Uri "http://127.0.0.1:${MelezhPort}/aria_ping" -UseBasicParsing -TimeoutSec 5
                $mzReady = $true
                break
            }
            catch { Start-Sleep -Milliseconds 500 }
        }
        if (-not $mzReady) { throw "Melezh not ready on port $MelezhPort" }

        $direct = Invoke-WebRequest -Uri "$apiUrl/api/v1/status" -UseBasicParsing
        $viaHandler = Invoke-WebRequest -Uri "http://127.0.0.1:${MelezhPort}/aria_get_status" -UseBasicParsing -TimeoutSec 120
        if ($viaHandler.StatusCode -lt 200 -or $viaHandler.StatusCode -ge 300) {
            throw "aria_get_status returned $($viaHandler.StatusCode)"
        }
        Write-Host "[Test-MelezhAriaBridge] GET aria_get_status ok ($($viaHandler.Content.Length) bytes); direct status $($direct.StatusCode)"

        $payload = '{"timestampUtc":"2026-05-20T12:00:00Z","agentVersion":"test","system":{},"disks":[],"backups":{"jobs":[],"recentLogs":[]}}'
        $sync = Invoke-WebRequest -Uri "http://127.0.0.1:${MelezhPort}/aria_sync" -Method POST -Body $payload -ContentType "application/json" -UseBasicParsing -TimeoutSec 60
        if ($sync.StatusCode -lt 200 -or $sync.StatusCode -ge 300) {
            throw "aria_sync POST returned $($sync.StatusCode)"
        }
        Write-Host "[Test-MelezhAriaBridge] POST aria_sync ok"
    }
    finally {
        if ($melezhProc -and -not $melezhProc.HasExited) {
            Stop-Process -Id $melezhProc.Id -Force -ErrorAction SilentlyContinue
        }
        Remove-Item $proj -Force -ErrorAction SilentlyContinue
    }
}
finally {
    if ($apiProc -and -not $apiProc.HasExited) {
        Stop-Process -Id $apiProc.Id -Force -ErrorAction SilentlyContinue
    }
}

Write-Host "[Test-MelezhAriaBridge] ok"
exit 0
