param(
    [string]$VersionFile = ".\VERSION",
    [switch]$WhatIf
)

$ErrorActionPreference = "Stop"
Set-Location (Resolve-Path "$PSScriptRoot\..")

if (-not (Test-Path $VersionFile)) {
    throw "VERSION file not found: $VersionFile"
}

$version = (Get-Content -Path $VersionFile -Raw).Trim()
if ([string]::IsNullOrWhiteSpace($version)) {
    throw "VERSION file is empty"
}

$assemblyVersion = if ($version -match '^\d+\.\d+\.\d+$') { "$version.0" } else { $version }

function Set-FileText {
    param([string]$Path, [string]$Content)
    if ($WhatIf) {
        Write-Host "[sync-version] WhatIf: update $Path"
        return
    }
    Set-Content -Path $Path -Value $Content -Encoding UTF8 -NoNewline
}

$propsPath = ".\Directory.Build.props"
$props = Get-Content $propsPath -Raw
$props = $props -replace '(<Version>)[^<]+(</Version>)', "`${1}$version`${2}"
$props = $props -replace '(<AssemblyVersion>)[^<]+(</AssemblyVersion>)', "`${1}$assemblyVersion`${2}"
$props = $props -replace '(<FileVersion>)[^<]+(</FileVersion>)', "`${1}$assemblyVersion`${2}"
$props = $props -replace '(<InformationalVersion>)[^<]+(</InformationalVersion>)', "`${1}$version`${2}"
Set-FileText $propsPath $props

$issPath = ".\installer\inno\AriaSignature.iss"
$iss = Get-Content $issPath -Raw
$iss = $iss -replace '#define MyAppVersion "[^"]+"', "#define MyAppVersion `"$version`""
Set-FileText $issPath $iss

$docFiles = Get-ChildItem -Path ".\docs" -Filter "*.md" -File
foreach ($doc in $docFiles) {
    $text = Get-Content $doc.FullName -Raw
    if ($text -match 'Версия документа:') {
        $text = $text -replace 'Версия документа: [0-9.]+', "Версия документа: $version"
        Set-FileText $doc.FullName $text
    }
}

Write-Host "[sync-version] ok version=$version"
