# Локальная HTTP-проверка API (без установщика).
# По умолчанию поднимает AriaSignature.Api на 127.0.0.1:18765 с временной SQLite и дергает ключевые GET.
param(
    [string]$Url = "http://127.0.0.1:18765",
    [switch]$SkipRun
)

$ErrorActionPreference = "Stop"
$root = Resolve-Path "$PSScriptRoot\.."
$apiProj = Join-Path $root "src\api\AriaSignature.Api\AriaSignature.Api.csproj"

if (-not $SkipRun) {
    $db = Join-Path $env:TEMP ("aria_smoke_" + [guid]::NewGuid().ToString("N") + ".db")
    $env:ConnectionStrings__AriaSignature = "Data Source=$db"
    Write-Host "Starting API on $Url (DB: $db)..."
    $proc = Start-Process -FilePath "dotnet" -WorkingDirectory $root -ArgumentList @(
        "run", "-c", "Release", "--project", $apiProj,
        "--urls", $Url, "--no-launch-profile"
    ) -PassThru -NoNewWindow
    $baseWait = "$Url/api/v1".TrimEnd('/')
    $ready = $false
    for ($i = 0; $i -lt 40; $i++) {
        try {
            $null = Invoke-WebRequest -Uri "$baseWait/status" -UseBasicParsing -TimeoutSec 5
            $ready = $true
            break
        }
        catch {
            Start-Sleep -Milliseconds 500
        }
    }
    if (-not $ready) {
        if ($proc) { Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue }
        throw "API did not become ready on $Url within timeout."
    }
}

$base = "$Url/api/v1".TrimEnd('/')
$paths = @("/status", "/system", "/settings", "/disks", "/backups")
foreach ($p in $paths) {
    $r = Invoke-WebRequest -Uri "$base$p" -UseBasicParsing -TimeoutSec 120
    Write-Host "GET $p -> $($r.StatusCode) ($($r.Content.Length) bytes)"
}

$sys = (Invoke-WebRequest -Uri "$base/system" -UseBasicParsing).Content | ConvertFrom-Json
Write-Host "system: hostName=$($sys.hostName) agentVersion=$($sys.agentVersion)"

if (-not $SkipRun -and $proc) {
    Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue
    Write-Host "Stopped dotnet API process."
}
