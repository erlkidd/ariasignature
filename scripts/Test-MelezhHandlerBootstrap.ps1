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
import sqlite3, sys
c = sqlite3.connect(sys.argv[1])
rows = c.execute("select key from handlers").fetchall()
sched = c.execute("select handler from scheduler_tasks").fetchall()
print("handlers", len(rows))
print("scheduled", len(sched))
for k in ["aria_sync","aria_get_status","aria_get_disks","aria_put_settings"]:
    if (k,) not in rows:
        raise SystemExit("missing handler " + k)
for k in sched:
    if k[0] and not str(k[0]).startswith("aria_get_"):
        raise SystemExit("scheduled non-get handler " + k[0])
'@ | Set-Content -Encoding utf8 $listScript
python $listScript $proj
if ($LASTEXITCODE -ne 0) { throw "handler catalog verification failed" }

Remove-Item -LiteralPath $proj -Force -ErrorAction SilentlyContinue
Write-Host "[Test-MelezhHandlerBootstrap] ok"
exit 0
