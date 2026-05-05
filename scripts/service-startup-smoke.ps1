param(
    [string]$ServiceName = "AriaSignatureService",
    [int]$ApiPort = 5160,
    [int]$WaitSeconds = 25
)

$ErrorActionPreference = "Stop"

Write-Host "[service-smoke][start] service=$ServiceName api_port=$ApiPort wait_sec=$WaitSeconds"

$svc = Get-Service -Name $ServiceName -ErrorAction Stop
if ($svc.Status -ne "Running") {
    Start-Service -Name $ServiceName
}

$deadline = (Get-Date).AddSeconds($WaitSeconds)
$ok = $false
while ((Get-Date) -lt $deadline) {
    try {
        $response = Invoke-WebRequest -UseBasicParsing -Uri "http://127.0.0.1:$ApiPort/api/v1/status" -TimeoutSec 3
        if ($response.StatusCode -eq 200) {
            $ok = $true
            break
        }
    } catch {
        Start-Sleep -Seconds 1
    }
}

if (-not $ok) {
    Write-Host "[service-smoke][fail] status_endpoint_unreachable service=$ServiceName api_port=$ApiPort"
    exit 1
}

Write-Host "[service-smoke][ok] service=$ServiceName api_port=$ApiPort"
