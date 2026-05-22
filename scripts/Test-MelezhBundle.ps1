# Validates OInt/Melezh bundle completeness (build-time bundle dir or installed {app}\melezh).
param(
    [Parameter(Mandatory = $true)]
    [string]$RootPath,
    [string]$ManifestPath = "",
    [switch]$IncludeInstallRootChecks,
    [string]$InstallRoot
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($ManifestPath)) {
    $scriptDir = $PSScriptRoot
    if ([string]::IsNullOrWhiteSpace($scriptDir)) {
        $scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
    }
    $ManifestPath = Join-Path (Resolve-Path (Join-Path $scriptDir "..\installer\melezh")) "required-files.json"
}

if (-not (Test-Path $ManifestPath)) {
    throw "Melezh manifest not found: $ManifestPath"
}

$manifest = Get-Content -Path $ManifestPath -Raw | ConvertFrom-Json
$missing = New-Object System.Collections.Generic.List[string]

function Test-RelPath {
    param([string]$Base, [string]$Rel)
    $full = Join-Path $Base $Rel
    if (-not (Test-Path -LiteralPath $full)) {
        $script:missing.Add($Rel)
    }
}

$root = [System.IO.Path]::GetFullPath($RootPath)
if (-not (Test-Path $root)) {
    throw "Melezh bundle root does not exist: $root"
}

foreach ($rel in $manifest.requiredFiles) {
    Test-RelPath -Base $root -Rel $rel
}

foreach ($rel in $manifest.requiredDirectories) {
    Test-RelPath -Base $root -Rel $rel
}

if ($IncludeInstallRootChecks) {
    if ([string]::IsNullOrWhiteSpace($InstallRoot)) {
        throw "InstallRoot is required when -IncludeInstallRootChecks is set."
    }
    $install = [System.IO.Path]::GetFullPath($InstallRoot)
    foreach ($rel in $manifest.installRootFiles) {
        Test-RelPath -Base $install -Rel $rel
    }
}

if ($missing.Count -gt 0) {
    $list = ($missing | ForEach-Object { "  - $_" }) -join [Environment]::NewLine
    throw "Melezh bundle incomplete under '$root':`n$list"
}

Write-Host "[Test-MelezhBundle] ok root=$root"
exit 0
