#Requires -RunAsAdministrator
<#
.SYNOPSIS
    ISS-mirror CLI install smoke (install → verify → uninstall). Run before building setup if release-gate was not elevated.
#>
$ErrorActionPreference = "Stop"
Set-Location (Resolve-Path "$PSScriptRoot\..")

$expectedProductVersion = (Get-Content ".\VERSION" -Raw).Trim()
if ([string]::IsNullOrWhiteSpace($expectedProductVersion)) {
    $expectedProductVersion = ([xml](Get-Content ".\Directory.Build.props")).Project.PropertyGroup.Version
}

if (-not (Test-Path ".\publish\service\AriaSignature.Api.exe")) {
    throw "Run dotnet publish / release-gate publish steps first (missing publish\service)."
}

$cliTestRoot = Join-Path $env:TEMP "AriaSignature-CliTest-$([guid]::NewGuid().ToString('N').Substring(0,8))"
New-Item -Path $cliTestRoot -ItemType Directory -Force | Out-Null
$logPath = Join-Path (Resolve-Path ".\artifacts") "cli-smoke-last.log"
New-Item -Path (Split-Path $logPath) -ItemType Directory -Force | Out-Null

function Invoke-Robo {
    param([string]$Source, [string]$Dest)
    robocopy $Source $Dest /MIR /NFL /NDL /NJH /NJS /NC /NS /NP | Out-Null
    if ($LASTEXITCODE -ge 8) {
        throw "robocopy failed $Source -> $Dest exit=$LASTEXITCODE"
    }
}

try {
    Invoke-Robo ".\publish\ui" (Join-Path $cliTestRoot "ui")
    Invoke-Robo ".\publish\service" (Join-Path $cliTestRoot "service")
    Invoke-Robo ".\publish\melezh-host" (Join-Path $cliTestRoot "melezh-host")
    Invoke-Robo ".\installer\melezh\bundle" (Join-Path $cliTestRoot "melezh")
    if (Test-Path ".\installer\smartctl") {
        Invoke-Robo ".\installer\smartctl" (Join-Path $cliTestRoot "service\smartctl")
    }

    & ".\scripts\install-cli.ps1" -Action Install -InstallRoot $cliTestRoot -ExpectedVersion $expectedProductVersion -SkipFirewall 2>&1 | Tee-Object -FilePath $logPath
    if ($LASTEXITCODE -ne 0) { throw "install-cli Install failed exit=$LASTEXITCODE" }

    & ".\scripts\install-cli.ps1" -Action Verify -InstallRoot $cliTestRoot -ExpectedVersion $expectedProductVersion 2>&1 | Tee-Object -FilePath $logPath -Append
    if ($LASTEXITCODE -ne 0) { throw "install-cli Verify failed exit=$LASTEXITCODE" }

    & ".\scripts\install-cli.ps1" -Action Uninstall -InstallRoot $cliTestRoot 2>&1 | Tee-Object -FilePath $logPath -Append
    if ($LASTEXITCODE -ne 0) { throw "install-cli Uninstall failed exit=$LASTEXITCODE" }

    foreach ($svc in @("AriaSignatureService", "AriaSignatureMelezhService")) {
        & sc.exe query $svc 2>$null | Out-Null
        if ($LASTEXITCODE -eq 0) {
            throw "Service still present after uninstall: $svc"
        }
    }

    Write-Host "[cli-smoke] OK. Log: $logPath"
    exit 0
}
finally {
    & ".\scripts\install-cli.ps1" -Action Uninstall -InstallRoot $cliTestRoot -SkipFirewall -ErrorAction SilentlyContinue | Out-Null
    Remove-Item -Path $cliTestRoot -Recurse -Force -ErrorAction SilentlyContinue
}
