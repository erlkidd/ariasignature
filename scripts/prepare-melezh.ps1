param(
    [string]$TargetDir = ".\installer\melezh",
    [string]$VersionFile = ".\installer\melezh\VERSION"
)

$ErrorActionPreference = "Stop"
Set-Location (Resolve-Path "$PSScriptRoot\..")

function Write-PrepareLog {
    param([string]$Message)
    Write-Host "[prepare-melezh] $Message"
}

New-Item -Path $TargetDir -ItemType Directory -Force | Out-Null
$melezhExe = Join-Path $TargetDir "melezh.exe"
if (Test-Path $melezhExe) {
    $size = (Get-Item $melezhExe).Length
    if ($size -gt 0) {
        Write-PrepareLog "melezh.exe already present ($size bytes)"
        exit 0
    }
}

$externalDir = $env:ARIASIGNATURE_MELEZH_DIR
if (-not [string]::IsNullOrWhiteSpace($externalDir) -and (Test-Path $externalDir)) {
    Write-PrepareLog "Copying from ARIASIGNATURE_MELEZH_DIR=$externalDir"
    Copy-Item -Path (Join-Path $externalDir "*") -Destination $TargetDir -Recurse -Force
    if (Test-Path $melezhExe) {
        exit 0
    }
}

$version = "1.34.0"
if (Test-Path $VersionFile) {
    $rawVersion = (Get-Content -Path $VersionFile -Raw).Trim()
    if (-not [string]::IsNullOrWhiteSpace($rawVersion)) {
        $version = $rawVersion
    }
}

$gh = Get-Command gh -ErrorAction SilentlyContinue
if ($gh) {
    Write-PrepareLog "Attempting gh release download OpenIntegrations $version"
    $tmp = Join-Path ([System.IO.Path]::GetTempPath()) ("aria-melezh-" + [Guid]::NewGuid().ToString("n"))
    New-Item -Path $tmp -ItemType Directory -Force | Out-Null
    try {
        Push-Location $tmp
        gh release download "v$version" --repo Bayselonarrend/OpenIntegrations --pattern "*win*x64*" --clobber 2>$null
        if ($LASTEXITCODE -ne 0) {
            gh release download "$version" --repo Bayselonarrend/OpenIntegrations --pattern "*win*x64*" --clobber 2>$null
        }
        $asset = Get-ChildItem -Path $tmp -File | Select-Object -First 1
        if ($asset) {
            Write-PrepareLog "Downloaded asset $($asset.Name); extraction is manual for installer bundle"
            Copy-Item -Path $asset.FullName -Destination (Join-Path $TargetDir $asset.Name) -Force
        }
    }
    finally {
        Pop-Location
        Remove-Item -Path $tmp -Recurse -Force -ErrorAction SilentlyContinue
    }
}

if (-not (Test-Path $melezhExe)) {
    Write-PrepareLog "WARNING: melezh.exe is still missing. Place OpenIntegrations/Melezh CLI files in $TargetDir before installer build."
    Write-PrepareLog "See installer/melezh/README.md"
    exit 2
}

Write-PrepareLog "melezh bundle ready"
