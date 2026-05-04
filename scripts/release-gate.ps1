param(
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
Set-Location (Resolve-Path "$PSScriptRoot\..")
$startedAt = Get-Date

function Write-Step {
    param(
        [int]$Index,
        [int]$Total,
        [string]$Name
    )
    Write-Host ("[release-gate][step-start] index={0}/{1} name=""{2}"" utc={3}" -f $Index, $Total, $Name, (Get-Date).ToUniversalTime().ToString("o"))
}

function Assert-ExitCode {
    param(
        [int]$Code,
        [string]$Operation
    )
    if ($Code -ne 0) {
        throw "[release-gate][step-fail] operation=""$Operation"" exit_code=$Code"
    }
}

Write-Step -Index 1 -Total 8 -Name "build-web-ui"
Push-Location .\src\web
npm ci
if ($LASTEXITCODE -ne 0) { Pop-Location; throw "[release-gate][step-fail] operation=""npm-ci"" exit_code=$LASTEXITCODE" }
npm run build
if ($LASTEXITCODE -ne 0) { Pop-Location; throw "[release-gate][step-fail] operation=""npm-build"" exit_code=$LASTEXITCODE" }
Pop-Location

Write-Step -Index 2 -Total 8 -Name "build-solution"
dotnet build .\AriaSignature.slnx -c $Configuration
Assert-ExitCode -Code $LASTEXITCODE -Operation "dotnet-build"

Write-Step -Index 3 -Total 8 -Name "run-tests"
dotnet test .\AriaSignature.slnx -c $Configuration
Assert-ExitCode -Code $LASTEXITCODE -Operation "dotnet-test"

Write-Step -Index 4 -Total 8 -Name "publish-ui"
dotnet publish .\src\ui\AriaSignature.UI\AriaSignature.UI.csproj -c $Configuration -o .\publish\ui
Assert-ExitCode -Code $LASTEXITCODE -Operation "dotnet-publish-ui"

Write-Step -Index 5 -Total 8 -Name "publish-service"
dotnet publish .\src\service\AriaSignature.Service\AriaSignature.Service.csproj -c $Configuration -o .\publish\service
Assert-ExitCode -Code $LASTEXITCODE -Operation "dotnet-publish-service"

Write-Step -Index 6 -Total 8 -Name "prepare-webview2"
$webView2Dir = ".\installer\webview2"
$webView2Exe = Join-Path $webView2Dir "MicrosoftEdgeWebView2RuntimeInstallerX64.exe"
if (-not (Test-Path $webView2Exe)) {
    New-Item -Path $webView2Dir -ItemType Directory -Force | Out-Null
    $url = "https://go.microsoft.com/fwlink/?linkid=2124701"
    Write-Host "Downloading WebView2 offline runtime installer from Microsoft..."
    Invoke-WebRequest -Uri $url -OutFile $webView2Exe
}

Write-Step -Index 7 -Total 8 -Name "prepare-smartctl"
$smartCtlDir = ".\installer\smartctl"
$smartCtlExe = Join-Path $smartCtlDir "smartctl.exe"
$driveDbPath = Join-Path $smartCtlDir "drivedb.h"
if (-not (Test-Path $smartCtlExe)) {
    $smartCtlCmd = Get-Command smartctl -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Source -ErrorAction SilentlyContinue
    if ($smartCtlCmd) {
        New-Item -Path $smartCtlDir -ItemType Directory -Force | Out-Null
        Copy-Item -Path $smartCtlCmd -Destination $smartCtlExe -Force
        $cmdDir = Split-Path -Parent $smartCtlCmd
        $pathDriveDb = Join-Path $cmdDir "drivedb.h"
        if (Test-Path $pathDriveDb) {
            Copy-Item -Path $pathDriveDb -Destination $driveDbPath -Force
        }
    }
    else {
        $tmpRoot = ".\installer\smartctl-temp"
        $extractRoot = Join-Path $tmpRoot "extract"
        $setupExe = Join-Path $tmpRoot "smartmontools-setup.exe"
        New-Item -Path $tmpRoot -ItemType Directory -Force | Out-Null
        $downloadUrl = "https://github.com/smartmontools/smartmontools/releases/download/RELEASE_7_5/smartmontools-7.5.win32-setup.exe"
        Write-Host "Downloading smartmontools setup from GitHub releases..."
        Invoke-WebRequest -Uri $downloadUrl -OutFile $setupExe

        $sevenZip = Get-Command 7z -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Source -ErrorAction SilentlyContinue
        if (-not $sevenZip) {
            throw "7z not found. Install 7-Zip CLI or provide smartctl.exe in PATH/installer\\smartctl."
        }

        Remove-Item -Path $extractRoot -Recurse -Force -ErrorAction SilentlyContinue
        New-Item -Path $extractRoot -ItemType Directory -Force | Out-Null
        & $sevenZip x $setupExe "-o$((Resolve-Path $extractRoot).Path)" -y | Out-Null

        $candidate = Join-Path $extractRoot "bin\smartctl.exe"
        if (-not (Test-Path $candidate)) {
            throw "Failed to extract smartctl.exe from smartmontools setup package."
        }

        New-Item -Path $smartCtlDir -ItemType Directory -Force | Out-Null
        Copy-Item -Path $candidate -Destination $smartCtlExe -Force
        $drivedb = Join-Path $extractRoot "bin\drivedb.h"
        if (Test-Path $drivedb) {
            Copy-Item -Path $drivedb -Destination $driveDbPath -Force
        }
    }
}

if (-not (Test-Path $smartCtlExe)) {
    throw "smartctl.exe is missing after prepare step."
}
if ((Get-Item $smartCtlExe).Length -le 0) {
    throw "smartctl.exe is empty after prepare step."
}
if (-not (Test-Path $driveDbPath)) {
    throw "drivedb.h is missing after prepare step."
}
if ((Get-Item $driveDbPath).Length -le 0) {
    throw "drivedb.h is empty after prepare step."
}

Write-Step -Index 8 -Total 8 -Name "build-installer"
$isccPath = Get-Command iscc -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Source -ErrorAction SilentlyContinue
if (-not $isccPath) {
    $fallback = "C:\Program Files (x86)\Inno Setup 6\ISCC.exe"
    if (Test-Path $fallback) {
        $isccPath = $fallback
    }
}

if (-not $isccPath) {
    throw "ISCC.exe not found. Install Inno Setup 6 or add ISCC to PATH."
}

& $isccPath .\installer\inno\AriaSignature.iss
Assert-ExitCode -Code $LASTEXITCODE -Operation "iscc-build-installer"

$elapsed = [int]((Get-Date) - $startedAt).TotalSeconds
Write-Host ("[release-gate][done] status=success elapsed_sec={0} utc={1}" -f $elapsed, (Get-Date).ToUniversalTime().ToString("o"))
