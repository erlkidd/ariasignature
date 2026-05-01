param(
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
Set-Location (Resolve-Path "$PSScriptRoot\..")

Write-Host "1/6 Build web UI (npm)..."
Push-Location .\src\web
npm ci
if ($LASTEXITCODE -ne 0) { Pop-Location; throw "npm ci failed" }
npm run build
if ($LASTEXITCODE -ne 0) { Pop-Location; throw "npm run build failed" }
Pop-Location

Write-Host "2/6 Build solution..."
dotnet build .\AriaSignature.slnx -c $Configuration

Write-Host "3/6 Run tests..."
dotnet test .\AriaSignature.slnx -c $Configuration

Write-Host "4/6 Publish UI..."
dotnet publish .\src\ui\AriaSignature.UI\AriaSignature.UI.csproj -c $Configuration -o .\publish\ui

Write-Host "5/6 Publish service..."
dotnet publish .\src\service\AriaSignature.Service\AriaSignature.Service.csproj -c $Configuration -o .\publish\service

Write-Host "6/8 Prepare WebView2 offline runtime..."
$webView2Dir = ".\installer\webview2"
$webView2Exe = Join-Path $webView2Dir "MicrosoftEdgeWebView2RuntimeInstallerX64.exe"
if (-not (Test-Path $webView2Exe)) {
    New-Item -Path $webView2Dir -ItemType Directory -Force | Out-Null
    $url = "https://go.microsoft.com/fwlink/?linkid=2124701"
    Write-Host "Downloading WebView2 offline runtime installer from Microsoft..."
    Invoke-WebRequest -Uri $url -OutFile $webView2Exe
}

Write-Host "7/8 Prepare smartctl runtime..."
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

Write-Host "8/8 Build installer..."
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
if ($LASTEXITCODE -ne 0) {
    throw "ISCC failed with exit code $LASTEXITCODE"
}

Write-Host "Release gate completed successfully."
