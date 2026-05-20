#Requires -RunAsAdministrator
<#
.SYNOPSIS
    CLI mirror of Inno Setup (AriaSignature.iss) for AI agents and release-gate smoke tests.

.DESCRIPTION
    Replicates the same SCM/Melezh steps as installer/inno/AriaSignature.iss (not a replacement for AriaSignature-Setup.exe).
    Production users install via AriaSignature-Setup.exe only.

    Logs: %ProgramData%\AriaSignature\logs\install-cli-*.log
    Health markers match ISS: install-health:ok, install-health:fail-hard-*, install-health:degraded-*

.EXAMPLE
    .\scripts\install-cli.ps1 -Action Install -InstallRoot "C:\Program Files\AriaSignature"
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
    if (Test-Path ".\VERSION") {
        $ExpectedVersion = (Get-Content ".\VERSION" -Raw).Trim()
    }
    elseif (Test-Path ".\Directory.Build.props") {
        $ExpectedVersion = ([xml](Get-Content ".\Directory.Build.props")).Project.PropertyGroup.Version
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
        @{
            action          = $Action
            installRoot     = $InstallRoot
            expectedVersion = $ExpectedVersion
            status          = "ok"
            issMirror       = $true
            utc             = (Get-Date).ToUniversalTime().ToString("o")
        } | ConvertTo-Json | Set-Content -Path $ReportPath -Encoding UTF8
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
