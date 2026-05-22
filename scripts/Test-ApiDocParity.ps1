# Verifies docs/API.md and Swagger-relevant routes match AriaApiExtensions route map.
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
)

$missingInCode = $expected | Where-Object { $_ -notin $codeRoutes }
$extraInCode = $codeRoutes | Where-Object {
    $_ -notin $expected -and $_ -notlike "/disks/*" -and $_ -notlike "/backups/*"
}

if ($missingInCode.Count -gt 0) {
    throw "[Test-ApiDocParity] routes in script missing in code: $($missingInCode -join ', ')"
}

Write-Host "[Test-ApiDocParity] code routes: $($codeRoutes.Count)"
foreach ($r in $codeRoutes) {
    Write-Host "  $r"
}

if ($extraInCode.Count -gt 0) {
    Write-Host "[Test-ApiDocParity] warn: extra routes in code not in baseline: $($extraInCode -join ', ')"
}

$md = Get-Content -LiteralPath $apiMd -Raw
$checks = @(
    @{ Path = "/melezh/push"; Pattern = "POST /melezh/push" },
    @{ Path = "/melezh/ingest"; Pattern = "POST /melezh/ingest" },
    @{ Path = "/settings"; Pattern = "GET /settings" },
    @{ Path = "/disks"; Pattern = "GET /disks" }
)
foreach ($c in $checks) {
    if ($md -notmatch [regex]::Escape($c.Pattern)) {
        throw "[Test-ApiDocParity] API.md missing mention: $($c.Pattern)"
    }
}

Write-Host "[Test-ApiDocParity] ok"
exit 0
