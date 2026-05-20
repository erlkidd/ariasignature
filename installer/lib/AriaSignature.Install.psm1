# PowerShell mirror of installer/inno/AriaSignature.iss for agents (install-cli.ps1).
# Production installs use Inno Setup only — not this module.
$script:Config = $null
$script:LogPath = $null

function Get-AriaInstallConfig {
    $configPath = Join-Path $PSScriptRoot "AriaSignature.Install.json"
    if (-not $script:Config) {
        $script:Config = Get-Content -Path $configPath -Raw | ConvertFrom-Json
    }
    return $script:Config
}

function Initialize-AriaInstallLog {
    param([string]$LogDir)
    if (-not $LogDir) {
        $LogDir = Join-Path $env:ProgramData "AriaSignature\logs"
    }
    New-Item -Path $LogDir -ItemType Directory -Force | Out-Null
    $script:LogPath = Join-Path $LogDir ("install-cli-{0:yyyyMMdd-HHmmss}.log" -f (Get-Date))
    return $script:LogPath
}

function Write-AriaInstallLog {
    param([string]$Message)
    $line = "[{0}] {1}" -f (Get-Date).ToString("o"), $Message
    if ($script:LogPath) {
        Add-Content -Path $script:LogPath -Value $line -Encoding UTF8
    }
    Write-Host $line
}

function Get-ScExePath {
    $sysnative = Join-Path $env:WINDIR "Sysnative\sc.exe"
    if (Test-Path $sysnative) { return $sysnative }
    $sys32 = Join-Path $env:WINDIR "System32\sc.exe"
    if (Test-Path $sys32) { return $sys32 }
    throw "sc.exe not found"
}

function Invoke-AriaSc {
    param(
        [string]$Arguments,
        [int[]]$AcceptExitCodes = @(0)
    )
    $sc = Get-ScExePath
    Write-AriaInstallLog "sc $Arguments"
    $proc = Start-Process -FilePath "cmd.exe" -ArgumentList "/c", "`"$sc`" $Arguments" -Wait -PassThru -NoNewWindow -WindowStyle Hidden
    $code = $proc.ExitCode
    return @{
        Ok   = ($AcceptExitCodes -contains $code)
        Code = $code
    }
}

function Stop-AriaProcesses {
    foreach ($name in @(
            "AriaSignature.UI", "AriaSignature.Service", "AriaSignature.Api",
            "AriaSignature.MelezhHost", "oscript"
        )) {
        & taskkill.exe /F /IM "$name.exe" 2>$null | Out-Null
    }
}

function Wait-AriaServiceAbsent {
    param(
        [string]$ServiceName,
        [int]$TimeoutSeconds = 30
    )
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    $tick = 0
    while ((Get-Date) -lt $deadline) {
        $tick++
        & sc.exe query $ServiceName 2>$null | Out-Null
        if ($LASTEXITCODE -ne 0) { return $true }
        if (($tick % 5) -eq 0) {
            Invoke-AriaSc "stop $ServiceName" -AcceptExitCodes @(0, 1062, 1060) | Out-Null
            Invoke-AriaSc "delete $ServiceName" -AcceptExitCodes @(0, 1060, 1072) | Out-Null
            Stop-AriaProcesses
        }
        Start-Sleep -Seconds 1
    }
    return $false
}

function Remove-AriaServiceForce {
    param([string]$ServiceName)
    for ($i = 1; $i -le 5; $i++) {
        Invoke-AriaSc "stop $ServiceName" -AcceptExitCodes @(0, 1062, 1060) | Out-Null
        Invoke-AriaSc "delete $ServiceName" -AcceptExitCodes @(0, 1060, 1072) | Out-Null
        if (Wait-AriaServiceAbsent -ServiceName $ServiceName -TimeoutSeconds 8) {
            return $true
        }
        Stop-AriaProcesses
        Start-Sleep -Seconds 2
    }
    return (Wait-AriaServiceAbsent -ServiceName $ServiceName -TimeoutSeconds 5)
}

function Test-MelezhBundleComplete {
    param(
        [string]$MelezhRoot,
        [string]$InstallRoot,
        [switch]$IncludeInstallRoot
    )
    $testScript = Join-Path (Resolve-Path (Join-Path $PSScriptRoot "..\..\scripts")) "Test-MelezhBundle.ps1"
    if (-not (Test-Path $testScript)) {
        throw "Test-MelezhBundle.ps1 not found"
    }
    $args = @{ RootPath = $MelezhRoot }
    if ($IncludeInstallRoot) {
        $args.IncludeInstallRootChecks = $true
        $args.InstallRoot = $InstallRoot
    }
    & $testScript @args
    if (-not $?) {
        throw "Melezh bundle validation failed"
    }
}

function Get-FileProductVersionSafe {
    param([string]$Path)
    if (-not (Test-Path $Path)) { return "" }
    return (Get-Item -LiteralPath $Path).VersionInfo.ProductVersion
}

function Assert-OnDiskVersion {
    param(
        [string]$ApiExePath,
        [string]$ExpectedVersion
    )
    $actual = Get-FileProductVersionSafe -Path $ApiExePath
    if ([string]::IsNullOrWhiteSpace($actual) -or ($actual -notmatch [regex]::Escape($ExpectedVersion))) {
        throw "On-disk API version mismatch. Expected=$ExpectedVersion Actual=$actual Path=$ApiExePath"
    }
    Write-AriaInstallLog "On-disk version ok: $actual"
}

function Invoke-AriaScCreateWithRetry {
    param(
        [string]$ServiceName,
        [string]$BinPath,
        [string]$DisplayName,
        [int]$MaxAttempts = 12
    )
    $quoted = "`"$BinPath`""
    $variants = @(
        "create $ServiceName binPath= $quoted start= auto DisplayName= `"$DisplayName`" obj= LocalSystem",
        "create $ServiceName binPath= $quoted start= auto obj= LocalSystem"
    )
    for ($attempt = 1; $attempt -le $MaxAttempts; $attempt++) {
        foreach ($create in $variants) {
            $r = Invoke-AriaSc $create
            if ($r.Ok) { return $true }
            if ($r.Code -in 1073, 1072) {
                Remove-AriaServiceForce -ServiceName $ServiceName | Out-Null
            }
        }
        Stop-AriaProcesses
        Start-Sleep -Seconds 2
    }
    return $false
}

function Start-AriaServiceAndWait {
    param([string]$ServiceName)
    Invoke-AriaSc "start $ServiceName" -AcceptExitCodes @(0, 1056) | Out-Null
    $deadline = (Get-Date).AddSeconds(60)
    while ((Get-Date) -lt $deadline) {
        try {
            $s = Get-Service -Name $ServiceName -ErrorAction Stop
            if ($s.Status -eq "Running") { return $true }
        }
        catch { }
        Start-Sleep -Seconds 2
    }
    return $false
}

function Test-AriaApiHealth {
    param(
        [int]$Port = 5160,
        [string]$ExpectedVersion = "",
        [int]$TimeoutSec = 5
    )
    try {
        $status = Invoke-RestMethod -Uri "http://127.0.0.1:$Port/api/v1/status" -TimeoutSec $TimeoutSec
        if ($ExpectedVersion -and $status.version -notmatch [regex]::Escape($ExpectedVersion)) {
            return $false
        }
        return $true
    }
    catch {
        return $false
    }
}

function Test-MelezhUiHealth {
    param(
        [int]$Port = 7788,
        [int]$TimeoutSec = 5,
        [int]$MaxAttempts = 18
    )
    $uris = @("http://127.0.0.1:$Port/ui", "http://127.0.0.1:$Port/")
    for ($i = 0; $i -lt $MaxAttempts; $i++) {
        foreach ($uri in $uris) {
            try {
                $r = Invoke-WebRequest -Uri $uri -UseBasicParsing -TimeoutSec $TimeoutSec
                if ($r.StatusCode -ge 200 -and $r.StatusCode -lt 400) {
                    return $true
                }
            }
            catch { }
        }
        Start-Sleep -Seconds 2
    }
    return $false
}

function Install-AriaMainService {
    param(
        [string]$InstallRoot,
        [string]$ExpectedVersion
    )
    $cfg = Get-AriaInstallConfig
    $name = $cfg.mainServiceName
    $bin = Join-Path $InstallRoot $cfg.paths.serviceExe
    if (-not (Test-Path $bin)) { throw "Missing service exe: $bin" }

    Remove-AriaServiceForce -ServiceName $name | Out-Null
    Stop-AriaProcesses

    if (-not (Invoke-AriaScCreateWithRetry -ServiceName $name -BinPath $bin -DisplayName "AriaSignature")) {
        throw "Failed to create $name"
    }

    Invoke-AriaSc "description $name `"AriaSignature background service`"" | Out-Null
    Invoke-AriaSc "failure $name reset= 86400 actions= restart/5000/restart/5000/restart/5000" | Out-Null

    if (-not (Start-AriaServiceAndWait -ServiceName $name)) {
        throw "Failed to start $name"
    }

    $apiExe = Join-Path $InstallRoot $cfg.paths.apiExe
    Assert-OnDiskVersion -ApiExePath $apiExe -ExpectedVersion $ExpectedVersion

    if (-not (Test-AriaApiHealth -ExpectedVersion $ExpectedVersion)) {
        throw "API health check failed after starting $name"
    }
    Write-AriaInstallLog "Main service install-health:ok"
}

function Install-AriaMelezhService {
    param([string]$InstallRoot)
    $cfg = Get-AriaInstallConfig
    $name = $cfg.melezhServiceName
    $bin = Join-Path $InstallRoot $cfg.paths.melezhHostExe
    $melezhRoot = Join-Path $InstallRoot "melezh"
    Test-MelezhBundleComplete -MelezhRoot $melezhRoot

    Remove-AriaServiceForce -ServiceName $name | Out-Null
    Stop-AriaProcesses

    if (-not (Invoke-AriaScCreateWithRetry -ServiceName $name -BinPath $bin -DisplayName "AriaSignature Melezh")) {
        throw "Failed to create $name"
    }

    Invoke-AriaSc "description $name `"OpenIntegrations Melezh HTTP gateway for AriaSignature`"" | Out-Null
    Invoke-AriaSc "failure $name reset= 86400 actions= restart/60000/restart/60000/restart/60000" | Out-Null

    if (-not (Start-AriaServiceAndWait -ServiceName $name)) {
        throw "Failed to start $name"
    }

    if (-not (Test-MelezhUiHealth -Port $cfg.melezhPort -MaxAttempts 45)) {
        throw "Melezh Web UI did not respond on port $($cfg.melezhPort)"
    }
    Write-AriaInstallLog "Melezh service install-health:ok"
}

function Add-AriaFirewallRules {
    $rules = @(
        @{ Name = "AriaSignature API (TCP 5160)"; Port = 5160 },
        @{ Name = "AriaSignature Melezh (TCP 7788)"; Port = 7788 }
    )
    foreach ($rule in $rules) {
        & netsh.exe advfirewall firewall delete rule name=$($rule.Name) 2>$null | Out-Null
        & netsh.exe advfirewall firewall add rule name=$($rule.Name) dir=in action=allow protocol=TCP localport=$($rule.Port) | Out-Null
    }
}

function Remove-AriaFirewallRules {
    & netsh.exe advfirewall firewall delete rule name="AriaSignature API (TCP 5160)" 2>$null | Out-Null
    & netsh.exe advfirewall firewall delete rule name="AriaSignature Melezh (TCP 7788)" 2>$null | Out-Null
}

function Uninstall-AriaServices {
    param([switch]$WarnIfStuck)
    $cfg = Get-AriaInstallConfig
    Stop-AriaProcesses
    $okM = Remove-AriaServiceForce -ServiceName $cfg.melezhServiceName
    $okA = Remove-AriaServiceForce -ServiceName $cfg.mainServiceName
    Stop-AriaProcesses
    if (-not $okM -or -not $okA) {
        $msg = "Some services could not be removed from SCM (main=$okA melezh=$okM)."
        Write-AriaInstallLog "WARN: $msg"
        if ($WarnIfStuck) {
            throw $msg
        }
    }
    else {
        Write-AriaInstallLog "marker=uninstall-service-removal status=ok"
    }
}

function Invoke-AriaInstallAction {
    param(
        [ValidateSet("Install", "Upgrade", "Uninstall", "Verify", "Diagnose")]
        [string]$Action,
        [string]$InstallRoot,
        [string]$ExpectedVersion = "1.1.0",
        [string]$LogDir = "",
        [switch]$SkipMelezh,
        [switch]$SkipFirewall,
        [switch]$WhatIf
    )

    $log = Initialize-AriaInstallLog -LogDir $LogDir
    Write-AriaInstallLog "action=$Action installRoot=$InstallRoot expectedVersion=$ExpectedVersion log=$log"

    $installRootFull = [System.IO.Path]::GetFullPath($InstallRoot)

    switch ($Action) {
        "Uninstall" {
            if ($WhatIf) { Write-AriaInstallLog "WhatIf: ISS-mirror uninstall"; return }
            if (-not $SkipFirewall) { Remove-AriaFirewallRules }
            Uninstall-IssServices
            return
        }
        "Verify" {
            $cfg = Get-AriaInstallConfig
            Test-MelezhBundleComplete -MelezhRoot (Join-Path $installRootFull "melezh") -IncludeInstallRoot -InstallRoot $installRootFull
            $api = Join-Path $installRootFull $cfg.paths.apiExe
            Assert-OnDiskVersion -ApiExePath $api -ExpectedVersion $ExpectedVersion
            if (-not (Test-AriaApiHealth -ExpectedVersion $ExpectedVersion)) {
                throw "API /status check failed"
            }
            if (-not $SkipMelezh) {
                if (-not (Test-MelezhUiHealth)) {
                    throw "Melezh /ui check failed"
                }
            }
            Write-AriaInstallLog "Verify: ok"
            return
        }
        "Diagnose" {
            $diag = Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) "scripts\diagnose-melezh.ps1"
            if (Test-Path $diag) {
                & $diag -InstallRoot $installRootFull
            }
            $cfg = Get-AriaInstallConfig
            foreach ($svc in @($cfg.mainServiceName, $cfg.melezhServiceName)) {
                Write-AriaInstallLog "--- sc query $svc ---"
                & sc.exe query $svc 2>&1 | ForEach-Object { Write-AriaInstallLog $_ }
            }
            return
        }
        "Install" {
            Invoke-IssMirrorInstallAction -InstallRoot $installRootFull -ExpectedVersion $ExpectedVersion `
                -SkipMelezh:$SkipMelezh -SkipFirewall:$SkipFirewall -WhatIf:$WhatIf
            return
        }
        "Upgrade" {
            Invoke-IssMirrorInstallAction -InstallRoot $installRootFull -ExpectedVersion $ExpectedVersion `
                -SkipMelezh:$SkipMelezh -SkipFirewall:$SkipFirewall -WhatIf:$WhatIf
            return
        }
    }
}

. (Join-Path $PSScriptRoot "AriaSignature.Install.IssMirror.ps1")

function Invoke-IssMirrorInstallAction {
    param(
        [string]$InstallRoot,
        [string]$ExpectedVersion,
        [switch]$SkipMelezh,
        [switch]$SkipFirewall,
        [switch]$WhatIf
    )
    if ($WhatIf) {
        Write-AriaInstallLog "WhatIf: ISS-mirror install (AriaSignature.iss post-install path)"
        return
    }
    if (-not $SkipFirewall) {
        Add-AriaFirewallRules
        Write-AriaInstallLog "Note: firewall rules added ([Run] section equivalent; use -SkipFirewall for SCM-only test)"
    }
    Invoke-IssMirrorInstall -InstallRoot $InstallRoot -ExpectedVersion $ExpectedVersion
    if ($SkipMelezh) {
        Write-AriaInstallLog "WARN: -SkipMelezh is not supported in ISS parity mode"
    }
}

Export-ModuleMember -Function @(
    "Invoke-AriaInstallAction",
    "Test-MelezhBundleComplete",
    "Uninstall-AriaServices",
    "Test-MelezhUiHealth",
    "Test-AriaApiHealth",
    "Get-AriaInstallConfig",
    "Initialize-AriaInstallLog",
    "Write-AriaInstallLog"
)
