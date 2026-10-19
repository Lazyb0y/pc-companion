<#
.SYNOPSIS
Checks PC Companion inside Windows Sandbox. Started by scripts\sandbox-test.ps1; don't run it on your
own PC: it installs and uninstalls PC Companion, clicks through setup and the settings app, moves the
pointer, and shuts Windows down at the end.

.DESCRIPTION
Inputs:  C:\Test\msi\PCCompanion-*.msi, and C:\Test\upgrade with another build of the same version
         and the next version (read-only).
Outputs: C:\Test\results: report.txt with one PASS or FAIL line per check, screenshots, the app and
         host logs, the install logs, and done.txt when finished.

Each group of checks is a script in groups\, which runs with the helpers of common.ps1.

The checks look for English, Arabic and Bangla text from src\PCCompanion.Localization and for the
English text of the WixUI_Advanced dialogs; update them when those strings change.

.PARAMETER Groups
The groups to run, in order, separated by commas, such as Smoke,HotCorners.

.PARAMETER SmartAppControl
What Smart App Control does in the Sandbox, as smart-app-control.ps1 sets it before the first install.
#>
param(
    [Parameter(Mandatory)]
    [string] $Groups,
    [ValidateSet('Off', 'Evaluation', 'On')]
    [string] $SmartAppControl = 'Off'
)

$ErrorActionPreference = 'Continue'
. (Join-Path $PSScriptRoot 'common.ps1')
New-Item -ItemType Directory -Force -Path $results | Out-Null
Set-Content -Path $report -Value "PC Companion Sandbox checks - $(Get-Date -Format o)" -Encoding utf8

$elevated = ([Security.Principal.WindowsPrincipal] [Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
Write-Report "msi: $msi; user: $env:USERNAME; elevated: $elevated; screen: $($screen.Width)x$($screen.Height); Windows $([Environment]::OSVersion.Version)"
Write-Report (& (Join-Path $PSScriptRoot 'smart-app-control.ps1') $SmartAppControl)

foreach ($group in $Groups -split ',') {
    Write-Report "== $group"
    . (Join-Path $PSScriptRoot "groups\$group.ps1")
}

Write-Report "finished $(Get-Date -Format o)"
Set-Content -Path (Join-Path $results 'done.txt') -Value 'done'
shutdown.exe /s /t 5
