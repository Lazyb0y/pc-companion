<#
.SYNOPSIS
Checks a change before a pull request, as CI does: builds every configuration, runs the tests
and checks the lock files.

.PARAMETER Quick
Builds only Debug x64.

.PARAMETER Installer
Also builds the installers.

.EXAMPLE
powershell -ExecutionPolicy Bypass -File scripts\verify.ps1 -Quick
#>
[CmdletBinding()]
param(
    [switch] $Quick,
    [switch] $Installer
)

$ErrorActionPreference = 'Continue'
$root = Split-Path -Parent $PSScriptRoot
$failures = New-Object System.Collections.Generic.List[string]
$platforms = if ($Quick) { @('x64') } else { @('x64', 'ARM64') }
$configurations = if ($Quick) { @('Debug') } else { @('Debug', 'Release') }

function Invoke-Dotnet([string] $Description, [string[]] $Arguments) {
    Write-Host "$Description..."
    $output = & dotnet @Arguments 2>&1 | ForEach-Object { "$_" }
    $diagnostics = $output | Where-Object { $_ -match ': (warning|error) ' } | ForEach-Object { $_.Trim() } | Sort-Object -Unique
    return [pscustomobject]@{ ExitCode = $LASTEXITCODE; Output = $output; Diagnostics = @($diagnostics) }
}

Push-Location $root
try {
    # Like CI: build each configuration, then run the tests of that build.
    foreach ($configuration in $configurations) {
        foreach ($platform in $platforms) {
            $result = Invoke-Dotnet "Building $configuration $platform" @('build', 'PCCompanion.slnx', '-c', $configuration, "-p:Platform=$platform")
            if ($result.ExitCode -ne 0 -or $result.Diagnostics.Count -gt 0) {
                $failures.Add("Build $configuration $platform")
                $result.Diagnostics | ForEach-Object { Write-Host "  $_" }
                continue
            }

            $result = Invoke-Dotnet "Testing $configuration $platform" @('test', '--solution', 'PCCompanion.slnx', '--no-build', '-c', $configuration, "-p:Platform=$platform")
            $result.Output | Where-Object { $_ -match '^\s*(failed|total:|succeeded:|skipped:)' } | ForEach-Object { Write-Host "  $($_.Trim())" }
            if ($result.ExitCode -ne 0) {
                $failures.Add("Tests $configuration $platform")
            }
        }
    }

    if ($Installer) {
        foreach ($platform in $platforms) {
            # The installer build shows known WIX1076 warnings (see docs/development/packaging.md), so only errors fail it.
            $result = Invoke-Dotnet "Building the $platform installer" @('build', 'src/PCCompanion.Installer/PCCompanion.Installer.wixproj', '-c', 'Release', "-p:Platform=$platform")
            if ($result.ExitCode -ne 0) {
                $failures.Add("Installer $platform")
                $result.Diagnostics | Where-Object { $_ -match ': error ' } | ForEach-Object { Write-Host "  $_" }
            }
        }
    }

    # CI restores in locked mode, which fails when a lock file doesn't match the projects.
    foreach ($platform in $platforms) {
        $result = Invoke-Dotnet "Checking the lock files ($platform)" @('restore', 'PCCompanion.slnx', '--locked-mode', "-p:Platform=$platform")
        if ($result.ExitCode -ne 0) {
            $failures.Add("Locked restore $platform")
            $result.Output | Where-Object { $_ -match 'error' } | Select-Object -First 5 | ForEach-Object { Write-Host "  $($_.Trim())" }
        }
    }

    $changedLockFiles = @(git status --porcelain -- '*packages.lock.json')
    if ($changedLockFiles.Count -gt 0) {
        Write-Host 'Lock files changed by the build; commit them with the change:'
        $changedLockFiles | ForEach-Object { Write-Host "  $_" }
    }
}
finally {
    Pop-Location
}

if ($failures.Count -gt 0) {
    Write-Host "Failed: $($failures -join ', ')" -ForegroundColor Red
    exit 1
}

Write-Host 'All checks passed.' -ForegroundColor Green
