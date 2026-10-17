<#
.SYNOPSIS
Installs PC Companion in Windows Sandbox and checks the installer, the settings app and the host
at runtime, without changing this PC.

.DESCRIPTION
Builds the installer for this PC's architecture, starts Windows Sandbox with networking off, and
runs scripts\sandbox\runtime-checks.ps1 inside it. The Sandbox window closes when the checks
finish. The report, screenshots and logs are written to TestResults\sandbox.

Needs the Windows Sandbox feature. Only one Sandbox can run at a time.

.PARAMETER Installer
Checks the installer's dialogs instead, with scripts\sandbox\installer-checks.ps1, and writes the
results to TestResults\sandbox-installer.

.PARAMETER NoBuild
Uses the installer that is already built.

.PARAMETER TimeoutMinutes
How long to wait for the checks to finish.

.EXAMPLE
powershell -ExecutionPolicy Bypass -File scripts\sandbox-test.ps1

.EXAMPLE
powershell -ExecutionPolicy Bypass -File scripts\sandbox-test.ps1 -Installer
#>
[CmdletBinding()]
param(
    [switch] $Installer,
    [switch] $NoBuild,
    [int] $TimeoutMinutes = 15
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$platform = if ($env:PROCESSOR_ARCHITECTURE -eq 'ARM64') { 'ARM64' } else { 'x64' }
$msiFolder = Join-Path $root "src\PCCompanion.Installer\bin\$platform\Release"
$sandbox = Join-Path $env:WINDIR 'System32\WindowsSandbox.exe'
$testResults = Join-Path $root 'TestResults'
$checks = if ($Installer) { 'installer-checks.ps1' } else { 'runtime-checks.ps1' }
$results = Join-Path $testResults $(if ($Installer) { 'sandbox-installer' } else { 'sandbox' })
$configuration = Join-Path $testResults 'sandbox.wsb'

if (-not (Test-Path $sandbox)) {
    throw 'Windows Sandbox is not turned on. Turn on the "Windows Sandbox" Windows feature and restart.'
}

# Newer versions of Windows Sandbox keep a server process running when no Sandbox is, and list the
# running Sandboxes with wsb.exe.
function Test-SandboxRunning {
    if (Get-Command wsb.exe -ErrorAction SilentlyContinue) {
        return @(& wsb.exe list | Where-Object { $_.Trim() }).Count -gt 0
    }
    return [bool] (Get-Process -Name 'WindowsSandbox*' -ErrorAction SilentlyContinue)
}

if (Test-SandboxRunning) {
    throw 'Windows Sandbox is already running. Close it first; only one Sandbox can run at a time. A Sandbox that was just closed can take a minute to shut down.'
}

if (-not $NoBuild) {
    Write-Host "Building the $platform installer..."
    & dotnet build (Join-Path $root 'src\PCCompanion.Installer\PCCompanion.Installer.wixproj') -c Release "-p:Platform=$platform" | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw "The installer build failed. Run: dotnet build src\PCCompanion.Installer\PCCompanion.Installer.wixproj -c Release -p:Platform=$platform"
    }
}

if (-not (Get-ChildItem $msiFolder -Filter 'PCCompanion-*.msi' -ErrorAction SilentlyContinue)) {
    throw "No installer in $msiFolder. Run without -NoBuild."
}

if (Test-Path $results) {
    try {
        Remove-Item -Recurse -Force $results
    } catch {
        throw "Can't clear $results. A Sandbox that was closed before it finished can keep it locked for a few minutes; try again then. $($_.Exception.Message)"
    }
}

New-Item -ItemType Directory -Force -Path $results | Out-Null

function ConvertTo-Xml([string] $Text) { [System.Security.SecurityElement]::Escape($Text) }

Set-Content -Path $configuration -Encoding utf8 -Value @"
<Configuration>
  <Networking>Disable</Networking>
  <MappedFolders>
    <MappedFolder>
      <HostFolder>$(ConvertTo-Xml $msiFolder)</HostFolder>
      <SandboxFolder>C:\Test\msi</SandboxFolder>
      <ReadOnly>true</ReadOnly>
    </MappedFolder>
    <MappedFolder>
      <HostFolder>$(ConvertTo-Xml (Join-Path $PSScriptRoot 'sandbox'))</HostFolder>
      <SandboxFolder>C:\Test\scripts</SandboxFolder>
      <ReadOnly>true</ReadOnly>
    </MappedFolder>
    <MappedFolder>
      <HostFolder>$(ConvertTo-Xml $results)</HostFolder>
      <SandboxFolder>C:\Test\results</SandboxFolder>
      <ReadOnly>false</ReadOnly>
    </MappedFolder>
  </MappedFolders>
  <LogonCommand>
    <Command>powershell.exe -NoProfile -ExecutionPolicy Bypass -File C:\Test\scripts\$checks</Command>
  </LogonCommand>
</Configuration>
"@

Write-Host 'Starting Windows Sandbox. Its window closes when the checks finish.'
Write-Host "Keep it visible until then: while it's minimized, its screenshots come out blank."
Start-Process -FilePath $sandbox -ArgumentList "`"$configuration`""

$done = Join-Path $results 'done.txt'
$deadline = (Get-Date).AddMinutes($TimeoutMinutes)
while (-not (Test-Path $done) -and (Get-Date) -lt $deadline) {
    Start-Sleep -Seconds 5
}

$report = Join-Path $results 'report.txt'
if (Test-Path $report) {
    Get-Content $report -Encoding utf8 | Write-Host
}

if (-not (Test-Path $done)) {
    Write-Host "The checks didn't finish within $TimeoutMinutes minutes. Results so far are in $results." -ForegroundColor Red
    exit 1
}

if (Select-String -Path $report -Pattern '^\[FAIL\]' -Quiet) {
    Write-Host "Some checks failed. Screenshots and logs are in $results." -ForegroundColor Red
    exit 1
}

Write-Host "All checks passed. Screenshots and logs are in $results." -ForegroundColor Green
