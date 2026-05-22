# Verifies Melezh handler catalog keys appear in MELEZH_HANDLER_CATALOG.md.
param(
    [string]$RepoRoot = ""
)

$ErrorActionPreference = "Stop"
if ([string]::IsNullOrWhiteSpace($RepoRoot)) {
    $RepoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
}

$catalogCs = Join-Path $RepoRoot "src\service\AriaSignature.MelezhHost\MelezhAriaApiHandlerCatalog.cs"
$catalogMd = Join-Path $RepoRoot "docs\MELEZH_HANDLER_CATALOG.md"
if (-not (Test-Path $catalogCs)) { throw "Missing $catalogCs" }
if (-not (Test-Path $catalogMd)) { throw "Missing $catalogMd" }

$csText = Get-Content -LiteralPath $catalogCs -Raw
$mdText = Get-Content -LiteralPath $catalogMd -Raw

$keys = [regex]::Matches($csText, '"(aria_[a-z0-9_]+)"') |
    ForEach-Object { $_.Groups[1].Value } |
    Where-Object { $_ -like "aria_*" } |
    Sort-Object -Unique

$inbound = @("aria_ping", "aria_sync")
$outboundGet = $keys | Where-Object { $_ -like "aria_get_*" }
$outboundWrite = $keys | Where-Object {
    $_ -like "aria_post_*" -or $_ -like "aria_put_*" -or $_ -like "aria_delete_*"
}

Write-Host "[Test-MelezhCatalogParity] catalog keys: $($keys.Count)"

foreach ($key in $keys) {
    if ($mdText -notmatch [regex]::Escape("``$key``") -and $mdText -notmatch [regex]::Escape("| ``$key``")) {
        if ($mdText -notmatch [regex]::Escape($key)) {
            throw "[Test-MelezhCatalogParity] MELEZH_HANDLER_CATALOG.md missing key: $key"
        }
    }
}

$scheduledCount = ([regex]::Matches($csText, 'schedule:\s*true')).Count
if ($scheduledCount -ne 10) {
    throw "[Test-MelezhCatalogParity] expected 10 schedule:true OutboundGet, found $scheduledCount"
}

if ($mdText -notmatch "bootstrap.*v6|schema v6|BootstrapVersion") {
    Write-Host "[Test-MelezhCatalogParity] warn: MELEZH_HANDLER_CATALOG.md may not mention bootstrap v6"
}

Write-Host "[Test-MelezhCatalogParity] ok inbound=$($inbound.Count) get=$($outboundGet.Count) write=$($outboundWrite.Count)"
exit 0
