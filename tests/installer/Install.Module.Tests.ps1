Describe "AriaSignature.Install module" {
    BeforeAll {
        $script:RepoRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..")
        Set-Location $script:RepoRoot
        Import-Module (Join-Path $script:RepoRoot "installer\lib\AriaSignature.Install.psm1") -Force
    }

    It "loads config with service names" {
        $cfg = Get-AriaInstallConfig
        $cfg.mainServiceName | Should Be "AriaSignatureService"
        $cfg.melezhServiceName | Should Be "AriaSignatureMelezhService"
    }

    It "validates melezh bundle when present" {
        $bundle = Join-Path $script:RepoRoot "installer\melezh\bundle"
        if (-not (Test-Path (Join-Path $bundle "bin\melezh.bat"))) {
            Set-TestInconclusive "OInt bundle not prepared; run prepare-melezh.ps1"
        }
        { Test-MelezhBundleComplete -MelezhRoot $bundle } | Should Not Throw
    }
}
