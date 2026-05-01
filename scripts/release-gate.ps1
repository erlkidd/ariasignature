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

Write-Host "6/6 Build installer..."
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
