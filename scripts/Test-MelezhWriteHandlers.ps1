# Smoke: POST JSON handlers on :7788 (POST/PUT/DELETE outbound to Aria API).
param(
    [string]$MelezhRoot = "",
    [string]$RepoRoot = "",
    [int]$ApiPort = 18775,
    [int]$MelezhPort = 18776
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
    Write-Host "[Test-MelezhWriteHandlers] SKIP: bundle missing"
    exit 0
}

. (Join-Path $PSScriptRoot "Test-MelezhAssert.ps1")

$MethodRunProject = -join @(
    [char]0x0417, [char]0x0430, [char]0x043F, [char]0x0443, [char]0x0441, [char]0x0442, [char]0x0438, [char]0x0442, [char]0x044C,
    [char]0x041F, [char]0x0440, [char]0x043E, [char]0x0435, [char]0x043A, [char]0x0442
)

$apiUrl = "http://127.0.0.1:$ApiPort"
$apiProj = Join-Path $RepoRoot "src\api\AriaSignature.Api\AriaSignature.Api.csproj"
$db = Join-Path $env:TEMP ("aria_write_api_" + [guid]::NewGuid().ToString("N") + ".db")
$env:ConnectionStrings__AriaSignature = "Data Source=$db"

Write-Host "[Test-MelezhWriteHandlers] building API"
dotnet build $apiProj -c Release -v q | Out-Null
if ($LASTEXITCODE -ne 0) { throw "API build failed" }

Write-Host "[Test-MelezhWriteHandlers] starting API on $apiUrl"
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

    $proj = Join-Path $env:TEMP ("aria_write_melezh_" + [guid]::NewGuid().ToString("N").Substring(0, 8) + ".melezh")
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
                Assert-MelezhHandlerJson -Uri "http://127.0.0.1:${MelezhPort}/aria_ping" -Label "aria_ping" -TimeoutSec 5 | Out-Null
                $mzReady = $true
                break
            }
            catch { Start-Sleep -Milliseconds 500 }
        }
        if (-not $mzReady) { throw "Melezh not ready on port $MelezhPort" }

        Assert-MelezhHandlerJson -Uri "http://127.0.0.1:${MelezhPort}/aria_post_disks_refresh" -Label "aria_post_disks_refresh" -Method POST -Body "{}" | Out-Null
        Write-Host "[Test-MelezhWriteHandlers] POST aria_post_disks_refresh ok"

        $syncPayload = '{"timestampUtc":"2026-05-20T12:00:00Z","agentVersion":"test","system":{},"disks":[],"backups":{"jobs":[],"recentLogs":[]}}'
        $sync = Invoke-WebRequest -Uri "http://127.0.0.1:${MelezhPort}/aria_sync" -Method POST -Body $syncPayload -ContentType "application/json" -UseBasicParsing -TimeoutSec 60
        if ($sync.StatusCode -lt 200 -or $sync.StatusCode -ge 300) {
            throw "aria_sync POST returned $($sync.StatusCode)"
        }
        $syncJson = $sync.Content.Trim() | ConvertFrom-Json
        if ($null -ne $syncJson.PSObject.Properties['result'] -and $syncJson.result -eq $false) {
            throw "aria_sync result=false error=$($syncJson.error)"
        }
        Write-Host "[Test-MelezhWriteHandlers] POST aria_sync ok"

        Assert-MelezhHandlerJson -Uri "http://127.0.0.1:${MelezhPort}/aria_delete_backups_logs" -Label "aria_delete_backups_logs" -Method POST -Body "{}" | Out-Null
        Write-Host "[Test-MelezhWriteHandlers] POST aria_delete_backups_logs ok (DELETE to API)"
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

Write-Host "[Test-MelezhWriteHandlers] ok"
exit 0
