# Verifies docs/API.md and route map match AriaApiExtensions.
param(
    [string]$RepoRoot = ""
)

$ErrorActionPreference = "Stop"
if ([string]::IsNullOrWhiteSpace($RepoRoot)) {
    $RepoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
}

$apiCs = Join-Path $RepoRoot "src\api\AriaSignature.Api\AriaApiExtensions.cs"
$apiMd = Join-Path $RepoRoot "docs\API.md"
if (-not (Test-Path $apiCs)) { throw "Missing $apiCs" }
if (-not (Test-Path $apiMd)) { throw "Missing $apiMd" }

$codeText = Get-Content -LiteralPath $apiCs -Raw
$codeRoutes = [regex]::Matches($codeText, 'api\.Map(?:Get|Post|Put|Delete)\("(/[^"]+)"') |
    ForEach-Object { $_.Groups[1].Value } |
    Sort-Object -Unique

$expected = @(
    "/status",
    "/health/live",
    "/health/ready",
    "/health/degradation",
    "/observability/runtime",
    "/settings",
    "/system",
    "/melezh/push",
    "/melezh/ingest",
    "/backups/test-mssql",
    "/disks",
    "/disks/refresh",
    "/disks/{id:guid}",
    "/disks/{id:guid}/smart",
    "/disks/smart",
    "/backups",
    "/backups/{id:guid}",
    "/backups/{id:guid}/run",
    "/backups/logs"
) | Sort-Object -Unique

$missingInCode = $expected | Where-Object { $_ -notin $codeRoutes }
$extraInCode = $codeRoutes | Where-Object { $_ -notin $expected }

if ($missingInCode.Count -gt 0) {
    throw "[Test-ApiDocParity] routes in baseline missing in code: $($missingInCode -join ', ')"
}

if ($extraInCode.Count -gt 0) {
    throw "[Test-ApiDocParity] extra routes in code not in baseline: $($extraInCode -join ', ')"
}

Write-Host "[Test-ApiDocParity] code routes: $($codeRoutes.Count)"
foreach ($r in $codeRoutes) {
    Write-Host "  $r"
}

$md = Get-Content -LiteralPath $apiMd -Raw

function Get-DocPathPattern {
    param([string]$Route)
    $p = $Route -replace '\{id:guid\}', '\{id\}'
    return [regex]::Escape($p)
}

foreach ($route in $expected) {
    $docPath = $route -replace '\{id:guid\}', '{id}'
    $escaped = [regex]::Escape($docPath)
    if ($md -notmatch $escaped) {
        throw "[Test-ApiDocParity] API.md missing route mention: $docPath (from $route)"
    }
}

$methodChecks = @(
    @{ Route = "/status"; Pattern = "GET /status" },
    @{ Route = "/melezh/push"; Pattern = "POST /melezh/push" },
    @{ Route = "/melezh/ingest"; Pattern = "POST /melezh/ingest" },
    @{ Route = "/settings"; Pattern = "GET /settings" },
    @{ Route = "/disks"; Pattern = "GET /disks" },
    @{ Route = "/health/live"; Pattern = "GET /health/live" },
    @{ Route = "/observability/runtime"; Pattern = "GET /observability/runtime" }
)
foreach ($c in $methodChecks) {
    if ($md -notmatch [regex]::Escape($c.Pattern)) {
        throw "[Test-ApiDocParity] API.md missing: $($c.Pattern)"
    }
}

Write-Host "[Test-ApiDocParity] ok"
exit 0
