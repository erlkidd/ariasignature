param(
    [string]$TargetDir = ".\installer\melezh",
    [string]$VersionFile = ".\installer\melezh\VERSION",
    [switch]$Force
)

$ErrorActionPreference = "Stop"
Set-Location (Resolve-Path "$PSScriptRoot\..")

function Write-PrepareLog {
    param([string]$Message)
    Write-Host "[prepare-melezh] $Message"
}

function Get-OintInstallerPath {
    param([string]$Root)
    $candidates = @(
        (Join-Path $Root "oint_2.0.0_installer_ru.exe"),
        (Join-Path $Root "oint_*_installer*.exe")
    )
    foreach ($pattern in $candidates) {
        $hit = Get-ChildItem -Path $pattern -File -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($hit) {
            return $hit.FullName
        }
    }
    return $null
}

function Test-OintBundleReady {
    param([string]$BundleDir)
    $melezhBat = Join-Path $BundleDir "bin\melezh.bat"
    $oscript = Join-Path $BundleDir "lib\oint\bin\oscript.exe"
    return (Test-Path $melezhBat) -and (Test-Path $oscript)
}

function Get-InstallerFingerprint {
    param([string]$InstallerPath)
    return (Get-FileHash -Path $InstallerPath -Algorithm SHA256).Hash.ToLowerInvariant()
}

New-Item -Path $TargetDir -ItemType Directory -Force | Out-Null
$bundleDir = Join-Path $TargetDir "bundle"
$stampFile = Join-Path $bundleDir ".oint-installer.sha256"

if ((Test-OintBundleReady -BundleDir $bundleDir) -and -not $Force) {
    $installerPath = Get-OintInstallerPath -Root $TargetDir
    if ($installerPath -and (Test-Path $stampFile)) {
        $expected = Get-InstallerFingerprint -InstallerPath $installerPath
        $current = (Get-Content -Path $stampFile -Raw).Trim().ToLowerInvariant()
        if ($current -eq $expected) {
            Write-PrepareLog "OInt/Melezh bundle already prepared ($bundleDir)"
            exit 0
        }
        Write-PrepareLog "Installer changed; rebuilding bundle"
    }
    elseif (-not $installerPath) {
        Write-PrepareLog "OInt/Melezh bundle already prepared ($bundleDir)"
        exit 0
    }
}

$externalDir = $env:ARIASIGNATURE_MELEZH_DIR
if (-not [string]::IsNullOrWhiteSpace($externalDir) -and (Test-Path $externalDir)) {
    Write-PrepareLog "Copying OInt tree from ARIASIGNATURE_MELEZH_DIR=$externalDir"
    New-Item -Path $bundleDir -ItemType Directory -Force | Out-Null
    robocopy $externalDir $bundleDir /MIR /NFL /NDL /NJH /NJS /NC /NS /NP | Out-Null
    if ($LASTEXITCODE -ge 8) {
        throw "[prepare-melezh] robocopy failed with exit code $LASTEXITCODE"
    }
    if (Test-OintBundleReady -BundleDir $bundleDir) {
        if ($installerPath = Get-OintInstallerPath -Root $TargetDir) {
            Get-InstallerFingerprint -InstallerPath $installerPath | Out-File -FilePath $stampFile -Encoding ascii -NoNewline
        }
        Write-PrepareLog "Bundle copied from external directory"
        exit 0
    }
    throw "[prepare-melezh] External directory does not contain bin\melezh.bat and lib\oint\bin\oscript.exe"
}

$installerExe = Get-OintInstallerPath -Root $TargetDir
if (-not $installerExe) {
    Write-PrepareLog "ERROR: Place oint_*_installer_ru.exe in $TargetDir (e.g. oint_2.0.0_installer_ru.exe)"
    Write-PrepareLog "See installer/melezh/README.md"
    exit 2
}

$defaultOintRoot = Join-Path ${env:ProgramFiles(x86)} "OInt"
$hadExistingInstall = Test-Path (Join-Path $defaultOintRoot "bin\melezh.bat")

Write-PrepareLog "Running silent OInt installer: $installerExe"
$installProc = Start-Process -FilePath $installerExe -ArgumentList "/S" -Wait -PassThru
if ($installProc.ExitCode -ne 0) {
    throw "[prepare-melezh] OInt installer failed with exit code $($installProc.ExitCode)"
}

if (-not (Test-Path (Join-Path $defaultOintRoot "bin\melezh.bat"))) {
    throw "[prepare-melezh] OInt installer finished but $($defaultOintRoot)\bin\melezh.bat is missing"
}

Write-PrepareLog "Copying OInt payload to $bundleDir"
New-Item -Path $bundleDir -ItemType Directory -Force | Out-Null
robocopy $defaultOintRoot $bundleDir /MIR /XF "unins000.exe" /NFL /NDL /NJH /NJS /NC /NS /NP | Out-Null
if ($LASTEXITCODE -ge 8) {
    throw "[prepare-melezh] robocopy bundle failed with exit code $LASTEXITCODE"
}

$uninstaller = Join-Path $defaultOintRoot "unins000.exe"
if (Test-Path $uninstaller) {
    Write-PrepareLog "Removing temporary system OInt install (silent uninstall)"
    $uninstallProc = Start-Process -FilePath $uninstaller -ArgumentList "/S" -Wait -PassThru
    if ($uninstallProc.ExitCode -ne 0 -and -not $hadExistingInstall) {
        Write-PrepareLog "WARNING: OInt uninstall exit code $($uninstallProc.ExitCode)"
    }
}

if (-not (Test-OintBundleReady -BundleDir $bundleDir)) {
    throw "[prepare-melezh] Bundle validation failed after extract"
}

Get-InstallerFingerprint -InstallerPath $installerExe | Out-File -FilePath $stampFile -Encoding ascii -NoNewline
Write-PrepareLog "OInt/Melezh bundle ready at $bundleDir"
