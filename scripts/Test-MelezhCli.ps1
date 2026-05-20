# Validates OInt/Melezh CLI accepts Russian method names (СоздатьПроект).
param(
    [Parameter(Mandatory = $true)]
    [string]$MelezhRoot,
    [string]$TempProjectPath = ""
)

$ErrorActionPreference = "Stop"

# ASCII-safe: OInt 0.12 CLI expects Russian method tokens (not CreateProject/RunProject).
$MethodCreateProject = -join @(
    [char]0x0421, [char]0x043E, [char]0x0437, [char]0x0434, [char]0x0430, [char]0x0442, [char]0x044C,
    [char]0x041F, [char]0x0440, [char]0x043E, [char]0x0435, [char]0x043A, [char]0x0442
)

$root = [System.IO.Path]::GetFullPath($MelezhRoot)
$oscript = Join-Path $root "lib\oint\bin\oscript.exe"
$appOs = Join-Path $root "share\oint\lib\melezh\core\Classes\app.os"

foreach ($pair in @(
        @{ Path = $oscript; Label = "oscript.exe" },
        @{ Path = $appOs; Label = "app.os" }
    )) {
    if (-not (Test-Path -LiteralPath $pair.Path)) {
        throw "[Test-MelezhCli] Missing $($pair.Label): $($pair.Path)"
    }
}

if ([string]::IsNullOrWhiteSpace($TempProjectPath)) {
    $TempProjectPath = Join-Path ([System.IO.Path]::GetTempPath()) ("AriaSignature-melezh-cli-test-" + [guid]::NewGuid().ToString("N").Substring(0, 8) + ".melezh")
}

$TempProjectPath = [System.IO.Path]::GetFullPath($TempProjectPath)
if (Test-Path -LiteralPath $TempProjectPath) {
    Remove-Item -LiteralPath $TempProjectPath -Force
}

& $oscript $appOs $MethodCreateProject "--path" $TempProjectPath 2>&1 | Out-Null
if ($LASTEXITCODE -ne 0) {
    throw "[Test-MelezhCli] CreateProject (Russian CLI) failed exit=$LASTEXITCODE"
}

if (-not (Test-Path -LiteralPath $TempProjectPath)) {
    throw "[Test-MelezhCli] Project file was not created: $TempProjectPath"
}

Remove-Item -LiteralPath $TempProjectPath -Force -ErrorAction SilentlyContinue
Write-Host "[Test-MelezhCli] ok root=$root"
exit 0
