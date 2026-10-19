<#
.SYNOPSIS
Installs PC Companion in Windows Sandbox and checks the installer, the settings app and the host
at runtime, without changing this PC.

.DESCRIPTION
Builds the installer for this PC's architecture, starts Windows Sandbox with networking off, and
runs scripts\sandbox\runtime-checks.ps1 inside it. The Sandbox window closes when the checks
finish. The report, screenshots and logs are written to TestResults\sandbox.

Needs the Windows Sandbox feature. Only one Sandbox can run at a time. Keep the Sandbox window open
and the mouse outside it: some checks move the pointer inside the Sandbox, which doesn't work while
the window is minimized.

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

# The runtime checks also install, over this one, a copy of the installer that's another build of the
# same version, and upgrade PC Companion with a copy that's the next version: each with its own
# product and package codes, and the next version with the version bounds that find this one as older.
function Invoke-Com([object] $Target, [string] $Name, [System.Reflection.BindingFlags] $Kind, [object[]] $Arguments) {
    return $Target.GetType().InvokeMember($Name, $Kind, $null, $Target, $Arguments)
}

function New-UpgradeInstaller([string] $Source, [string] $Destination, [switch] $SameVersion) {
    Copy-Item -Path $Source -Destination $Destination -Force
    $installer = New-Object -ComObject WindowsInstaller.Installer
    $database = Invoke-Com $installer 'OpenDatabase' 'InvokeMethod' @($Destination, [int] 1)
    $view = Invoke-Com $database 'OpenView' 'InvokeMethod' @("SELECT ``Value`` FROM ``Property`` WHERE ``Property``='ProductVersion'")
    [void] (Invoke-Com $view 'Execute' 'InvokeMethod' $null)
    $version = Invoke-Com (Invoke-Com $view 'Fetch' 'InvokeMethod' $null) 'StringData' 'GetProperty' @([int] 1)
    [void] (Invoke-Com $view 'Close' 'InvokeMethod' $null)
    $parts = $version.Split('.')
    $parts[-1] = [string] ([int] $parts[-1] + 1)
    $next = if ($SameVersion) { $version } else { $parts -join '.' }
    $productCode = "{$([guid]::NewGuid().ToString().ToUpperInvariant())}"

    # The Upgrade table's versions are part of its key, so its rows are replaced rather than updated.
    $columns = 'UpgradeCode', 'VersionMin', 'VersionMax', 'Language', 'Attributes', 'Remove', 'ActionProperty'
    $columnList = ($columns | ForEach-Object { '`' + $_ + '`' }) -join ', '
    $view = Invoke-Com $database 'OpenView' 'InvokeMethod' @("SELECT $columnList FROM ``Upgrade``")
    [void] (Invoke-Com $view 'Execute' 'InvokeMethod' $null)
    $rows = @()
    while ($record = Invoke-Com $view 'Fetch' 'InvokeMethod' $null) {
        $rows += , @(1..$columns.Count | ForEach-Object { Invoke-Com $record 'StringData' 'GetProperty' @([int] $_) })
    }
    [void] (Invoke-Com $view 'Close' 'InvokeMethod' $null)
    $queries = @(
        "UPDATE ``Property`` SET ``Value``='$next' WHERE ``Property``='ProductVersion'",
        "UPDATE ``Property`` SET ``Value``='$productCode' WHERE ``Property``='ProductCode'",
        'DELETE FROM `Upgrade`')
    foreach ($row in $rows) {
        $values = for ($i = 0; $i -lt $columns.Count; $i++) {
            $value = if ($row[$i] -eq $version) { $next } else { $row[$i] }
            if ($columns[$i] -eq 'Attributes') { [int] $value } else { "'$value'" }
        }
        $queries += "INSERT INTO ``Upgrade`` ($columnList) VALUES ($($values -join ', '))"
    }
    foreach ($query in $queries) {
        $view = Invoke-Com $database 'OpenView' 'InvokeMethod' @($query)
        [void] (Invoke-Com $view 'Execute' 'InvokeMethod' $null)
        [void] (Invoke-Com $view 'Close' 'InvokeMethod' $null)
    }
    $summary = Invoke-Com $database 'SummaryInformation' 'GetProperty' @([int] 1)
    [void] (Invoke-Com $summary 'Property' 'SetProperty' @([int] 9, "{$([guid]::NewGuid().ToString().ToUpperInvariant())}"))
    [void] (Invoke-Com $summary 'Persist' 'InvokeMethod' $null)
    [void] (Invoke-Com $database 'Commit' 'InvokeMethod' $null)
    foreach ($object in $summary, $view, $database, $installer) { [void] [System.Runtime.InteropServices.Marshal]::ReleaseComObject($object) }
    [GC]::Collect()
    [GC]::WaitForPendingFinalizers()
    return $next
}

$upgradeFolder = Join-Path $testResults 'sandbox-upgrade'
if (Test-Path $upgradeFolder) { Remove-Item -Recurse -Force $upgradeFolder }
New-Item -ItemType Directory -Force -Path $upgradeFolder | Out-Null
if (-not $Installer) {
    $source = (Get-ChildItem $msiFolder -Filter 'PCCompanion-*.msi' | Select-Object -First 1).FullName
    $upgradeVersion = New-UpgradeInstaller $source (Join-Path $upgradeFolder 'PCCompanion-upgrade.msi')
    $sameVersion = New-UpgradeInstaller $source (Join-Path $upgradeFolder 'PCCompanion-same-version.msi') -SameVersion
    Write-Host "Made another build of version $sameVersion and an upgrade installer, version $upgradeVersion."
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
      <HostFolder>$(ConvertTo-Xml $upgradeFolder)</HostFolder>
      <SandboxFolder>C:\Test\upgrade</SandboxFolder>
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

# A report without its last line lost results, for example while another program held it open.
if (-not (Select-String -Path $report -Pattern '^finished ' -Quiet)) {
    Write-Host "The report is incomplete. Don't open it during a run. Screenshots and logs are in $results." -ForegroundColor Red
    exit 1
}

Write-Host "All checks passed. Screenshots and logs are in $results." -ForegroundColor Green
