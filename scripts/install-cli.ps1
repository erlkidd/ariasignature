#Requires -RunAsAdministrator
<#
.SYNOPSIS
    CLI installer for AriaSignature (install/upgrade/uninstall/verify/diagnose).
.DESCRIPTION
    Run before Inno Setup build via release-gate smoke, or invoked from AriaSignature.iss post-install.
#>
param(
    [ValidateSet("Install", "Upgrade", "Uninstall", "Verify", "Diagnose")]
    [string]$Action = "Install",
    [string]$InstallRoot = "${env:ProgramFiles}\AriaSignature",
    [string]$ExpectedVersion = "",
    [string]$LogDir = "",
    [switch]$SkipMelezh,
    [switch]$SkipFirewall,
    [switch]$WhatIf,
    [string]$ReportPath = ""
)

$ErrorActionPreference = "Stop"
Set-Location (Resolve-Path "$PSScriptRoot\..")

if ([string]::IsNullOrWhiteSpace($ExpectedVersion)) {
    $propsPath = ".\Directory.Build.props"
    if (Test-Path $propsPath) {
        $xml = [xml](Get-Content $propsPath)
        $ExpectedVersion = $xml.Project.PropertyGroup.Version
    }
    if ([string]::IsNullOrWhiteSpace($ExpectedVersion)) {
        $ExpectedVersion = "1.1.0"
    }
}

$modulePath = Join-Path $PSScriptRoot "..\installer\lib\AriaSignature.Install.psm1"
if (-not (Test-Path $modulePath)) {
    throw "Install module not found: $modulePath"
}

Import-Module $modulePath -Force

try {
    Invoke-AriaInstallAction `
        -Action $Action `
        -InstallRoot $InstallRoot `
        -ExpectedVersion $ExpectedVersion `
        -LogDir $LogDir `
        -SkipMelezh:$SkipMelezh `
        -SkipFirewall:$SkipFirewall `
        -WhatIf:$WhatIf

    if ($ReportPath) {
        $report = @{
            action          = $Action
            installRoot     = $InstallRoot
            expectedVersion = $ExpectedVersion
            status          = "ok"
            utc             = (Get-Date).ToUniversalTime().ToString("o")
        }
        $dir = Split-Path $ReportPath -Parent
        if ($dir) { New-Item -Path $dir -ItemType Directory -Force | Out-Null }
        $report | ConvertTo-Json | Set-Content -Path $ReportPath -Encoding UTF8
    }
    exit 0
}
catch {
    Write-Host "[install-cli] FAIL: $($_.Exception.Message)" -ForegroundColor Red
    if ($ReportPath) {
        @{
            action = $Action
            status = "fail"
            error  = $_.Exception.Message
            utc    = (Get-Date).ToUniversalTime().ToString("o")
        } | ConvertTo-Json | Set-Content -Path $ReportPath -Encoding UTF8
    }
    exit 1
}
