# Verifies Melezh project bootstrap seeds handler catalog (requires OInt bundle + built MelezhHost).
param(
    [string]$MelezhRoot = "",
    [string]$RepoRoot = ""
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
    Write-Host "[Test-MelezhHandlerBootstrap] SKIP: bundle missing at $MelezhRoot"
    exit 0
}

$apiPort = 18773
$testPort = 18769
$apiUrl = "http://127.0.0.1:$apiPort"
$apiProj = Join-Path $RepoRoot "src\api\AriaSignature.Api\AriaSignature.Api.csproj"
$db = Join-Path $env:TEMP ("aria_bootstrap_api_" + [guid]::NewGuid().ToString("N") + ".db")
$env:ConnectionStrings__AriaSignature = "Data Source=$db"
$env:ARIASIGNATURE_API_PORT = "$apiPort"
$env:ARIASIGNATURE_MELEZH_PORT = "$testPort"

Write-Host "[Test-MelezhHandlerBootstrap] building API"
dotnet build $apiProj -c Release -v q | Out-Null
if ($LASTEXITCODE -ne 0) { throw "API build failed" }

Write-Host "[Test-MelezhHandlerBootstrap] starting API on $apiUrl"
$apiProc = Start-Process -FilePath "dotnet" -WorkingDirectory $RepoRoot -ArgumentList @(
    "run", "-c", "Release", "--no-build", "--project", $apiProj,
    "--urls", $apiUrl, "--no-launch-profile"
) -PassThru -WindowStyle Hidden
try {
    $apiReady = $false
    for ($i = 0; $i -lt 60; $i++) {
        try {
            $null = Invoke-WebRequest -Uri "$apiUrl/api/v1/status" -UseBasicParsing -TimeoutSec 5
            $apiReady = $true
            break
        }
        catch { Start-Sleep -Milliseconds 500 }
    }
    if (-not $apiReady) { throw "API not ready on $apiUrl" }

    $proj = Join-Path $env:TEMP ("AriaSignature-melezh-bootstrap-" + [guid]::NewGuid().ToString("N").Substring(0, 8) + ".melezh")
    $env:ARIASIGNATURE_MELEZH_ROOT = $MelezhRoot
    $env:ARIASIGNATURE_MELEZH_PROJECT = $proj
    Remove-Item -LiteralPath $proj -Force -ErrorAction SilentlyContinue

    $hostProj = Join-Path $RepoRoot "src\service\AriaSignature.MelezhHost\AriaSignature.MelezhHost.csproj"
    dotnet build $hostProj -c Release -v q | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "MelezhHost build failed" }

    $hostDll = Join-Path $RepoRoot "src\service\AriaSignature.MelezhHost\bin\Release\net8.0-windows\win-x64\AriaSignature.MelezhHost.dll"
    if (-not (Test-Path $hostDll)) {
        $hostDll = Join-Path $RepoRoot "src\service\AriaSignature.MelezhHost\bin\Release\net8.0\AriaSignature.MelezhHost.dll"
    }
    dotnet $hostDll --bootstrap-only 2>&1 | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "bootstrap-only failed exit=$LASTEXITCODE" }
    if (-not (Test-Path $proj)) { throw "project not created: $proj" }

    $listScript = Join-Path $env:TEMP "melezh-list-handlers.py"
    @'
import sqlite3, sys, re
catalog = [
    "aria_ping", "aria_sync",
    "aria_get_status", "aria_get_health_live", "aria_get_health_ready",
    "aria_get_health_degradation", "aria_get_observability_runtime", "aria_get_settings",
    "aria_get_system", "aria_get_disks", "aria_get_backups", "aria_get_backups_logs",
    "aria_get_disk", "aria_get_disk_smart",
    "aria_post_backups_test_mssql", "aria_post_disks_refresh", "aria_post_backups",
    "aria_post_backup_run", "aria_put_settings", "aria_put_backup",
    "aria_delete_disk_smart", "aria_delete_disks_smart", "aria_delete_backup",
    "aria_delete_backups_logs",
]
scheduled_get = [
    "aria_get_status", "aria_get_health_live", "aria_get_health_ready",
    "aria_get_health_degradation", "aria_get_observability_runtime", "aria_get_settings",
    "aria_get_system", "aria_get_disks", "aria_get_backups", "aria_get_backups_logs",
]
guid_re = re.compile(r"^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$", re.I)
c = sqlite3.connect(sys.argv[1])
rows = [r[0] for r in c.execute("select key from handlers").fetchall()]
sched = c.execute("select handler, cron from scheduler_tasks").fetchall()
print("handlers", len(rows))
print("scheduled", len(sched))
if len(rows) != len(catalog):
    raise SystemExit("handler count expected %d got %d keys=%s" % (len(catalog), len(rows), rows))
for k in catalog:
    if k not in rows:
        raise SystemExit("missing handler " + k)
for key in rows:
    if guid_re.match(key):
        raise SystemExit("orphan GUID handler key: " + key)
for h, cron in sched:
    if h and not str(h).startswith("aria_get_"):
        raise SystemExit("scheduled non-get handler " + h)
if len(sched) != len(scheduled_get):
    raise SystemExit("scheduler count expected %d got %d" % (len(scheduled_get), len(sched)))
for k in scheduled_get:
    if (k,) not in [(h,) for h, _ in sched]:
        raise SystemExit("missing scheduler for " + k)
secs = []
for h, cron in sched:
    parts = str(cron).split()
    if len(parts) < 1:
        raise SystemExit("bad cron for " + h)
    secs.append(int(parts[0]))
if len(secs) != len(set(secs)):
    raise SystemExit("scheduled pull crons must use distinct second offsets: " + str(sched))
ver = c.execute("select value from settings where name='AriaSignature:BootstrapVersion'").fetchone()
if not ver or int(ver[0]) < 6:
    raise SystemExit("bootstrap version expected >= 6, got " + str(ver))
'@ | Set-Content -Encoding utf8 $listScript
    python $listScript $proj
    if ($LASTEXITCODE -ne 0) { throw "handler catalog verification failed" }

    . (Join-Path $PSScriptRoot "Test-MelezhAssert.ps1")
    $MethodRunProject = -join @(
        [char]0x0417, [char]0x0430, [char]0x043F, [char]0x0443, [char]0x0441, [char]0x0442, [char]0x0438, [char]0x0442, [char]0x044C,
        [char]0x041F, [char]0x0440, [char]0x043E, [char]0x0435, [char]0x043A, [char]0x0442
    )
    $appOs = Join-Path $MelezhRoot "share\oint\lib\melezh\core\Classes\app.os"
    $melezhProc = Start-Process -FilePath $oscript -ArgumentList @(
        $appOs, $MethodRunProject, "--port", "$testPort", "--proj", $proj
    ) -WorkingDirectory (Split-Path $oscript) -PassThru -WindowStyle Hidden
    try {
        $mzReady = $false
        for ($i = 0; $i -lt 80; $i++) {
            try {
                Assert-MelezhHandlerJson -Uri "http://127.0.0.1:${testPort}/aria_ping" -Label "aria_ping" -TimeoutSec 5 | Out-Null
                $mzReady = $true
                break
            }
            catch { Start-Sleep -Milliseconds 500 }
        }
        if (-not $mzReady) { throw "Melezh aria_ping did not return result!=false on port $testPort" }
        Assert-MelezhHandlerJson -Uri "http://127.0.0.1:${testPort}/aria_get_status" -Label "aria_get_status" -TimeoutSec 120 | Out-Null
    }
    finally {
        if ($melezhProc -and -not $melezhProc.HasExited) {
            Stop-Process -Id $melezhProc.Id -Force -ErrorAction SilentlyContinue
        }
        Remove-Item -LiteralPath $proj -Force -ErrorAction SilentlyContinue
    }
}
finally {
    if ($apiProc -and -not $apiProc.HasExited) {
        Stop-Process -Id $apiProc.Id -Force -ErrorAction SilentlyContinue
    }
}

Write-Host "[Test-MelezhHandlerBootstrap] ok"
exit 0
