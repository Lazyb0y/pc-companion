<#
.SYNOPSIS
Installs PC Companion in Windows Sandbox and checks the installer, the settings app and the host
at runtime, without changing this PC.

.DESCRIPTION
Builds the installer for this PC's architecture, or takes the one given with -Msi, starts Windows
Sandbox with networking off, and
runs the groups of checks in scripts\sandbox\groups inside it, with scripts\sandbox\run-checks.ps1:
all of them, or Smoke and the ones chosen with -Checks. The Sandbox window closes when the checks
finish. The report, screenshots and logs are written to TestResults\sandbox.

Needs the Windows Sandbox feature. Only one Sandbox can run at a time. Keep the Sandbox window open
and the mouse outside it: some checks move the pointer inside the Sandbox, which doesn't work while
the window is minimized.

.PARAMETER Checks
The groups to run, such as HotCorners or Setup,Dialogs: the names of the scripts in
scripts\sandbox\groups. Smoke, a short check that PC Companion installs and starts, always runs
first, and the groups run in a fixed order whatever the order given. Without it, every group runs.

.PARAMETER NoBuild
Uses the installer that is already built.

.PARAMETER Msi
Checks this installer instead of building one, such as an installer downloaded from a draft
release. It must be for this PC's architecture. The Sandbox gets a copy of it without the mark
Windows puts on downloaded files, so the checks don't stop at Windows' security warnings.

.PARAMETER TimeoutMinutes
How long to wait for the checks to finish: by default 30 minutes, or 60 when Smart App Control isn't
off.

.PARAMETER SmartAppControl
What Smart App Control does in the Sandbox. Off, the default, turns it off before anything is
installed: Windows Sandbox starts it in evaluation mode, where, with networking off, every install
waits about two minutes for its check. Evaluation leaves it that way. On turns it on, so it blocks
what it doesn't trust: PC Companion installs, but an unsigned build doesn't start. For an unsigned
installer, this script warns before it starts the Sandbox, and the checks stop after Smoke.

.EXAMPLE
powershell -ExecutionPolicy Bypass -File scripts\sandbox-test.ps1

.EXAMPLE
powershell -ExecutionPolicy Bypass -File scripts\sandbox-test.ps1 -Checks HotCorners

.EXAMPLE
powershell -ExecutionPolicy Bypass -File scripts\sandbox-test.ps1 -Checks Setup,Dialogs

.EXAMPLE
powershell -ExecutionPolicy Bypass -File scripts\sandbox-test.ps1 -Msi $HOME\Downloads\PCCompanion-0.1.0-x64.msi
#>
[CmdletBinding()]
param(
    [string[]] $Checks,
    [switch] $NoBuild,
    [string] $Msi,
    [int] $TimeoutMinutes,
    [ValidateSet('Off', 'Evaluation', 'On')]
    [string] $SmartAppControl = 'Off'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$platform = if ($env:PROCESSOR_ARCHITECTURE -eq 'ARM64') { 'ARM64' } else { 'x64' }
$msiFolder = Join-Path $root "src\PCCompanion.Installer\bin\$platform\Release"
$sandbox = Join-Path $env:WINDIR 'System32\WindowsSandbox.exe'
$testResults = Join-Path $root 'TestResults'
$results = Join-Path $testResults 'sandbox'
$configuration = Join-Path $testResults 'sandbox.wsb'
if (-not $PSBoundParameters.ContainsKey('TimeoutMinutes')) {
    $TimeoutMinutes = if ($SmartAppControl -eq 'Off') { 30 } else { 60 }
}

# The groups are the scripts in scripts\sandbox\groups; run-checks.ps1 runs them in its own order.
$available = @(Get-ChildItem (Join-Path $PSScriptRoot 'sandbox\groups') -Filter '*.ps1' | ForEach-Object { $_.BaseName })
$groups = 'All'
if ($Checks) {
    $chosen = foreach ($name in @($Checks -split ',' | ForEach-Object { $_.Trim() } | Where-Object { $_ })) {
        $match = $available | Where-Object { $_ -eq $name }
        if (-not $match) { throw "There's no group of checks named $name. The groups are: $($available -join ', ')." }
        $match
    }
    $groups = $chosen -join ','
}

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

# The platform an installer is for, from its summary information's Template, such as x64;1033.
function Get-InstallerPlatform([string] $Path) {
    $installer = New-Object -ComObject WindowsInstaller.Installer
    $summary = $installer.GetType().InvokeMember('SummaryInformation', 'GetProperty', $null, $installer, @($Path, [int] 0))
    $template = $summary.GetType().InvokeMember('Property', 'GetProperty', $null, $summary, @([int] 7))
    foreach ($object in $summary, $installer) { [void] [System.Runtime.InteropServices.Marshal]::ReleaseComObject($object) }
    return ($template -split ';')[0]
}

if ($Msi) {
    # A copy in a folder of its own, which the Sandbox maps, without the download mark.
    if (-not (Test-Path -LiteralPath $Msi -PathType Leaf)) {
        throw "There's no installer at $Msi."
    }

    $source = (Resolve-Path -LiteralPath $Msi).ProviderPath
    $msiPlatform = Get-InstallerPlatform $source
    if ($msiPlatform -ne $platform) {
        throw "$source is an installer for $msiPlatform. This PC needs one for $platform."
    }

    $msiFolder = Join-Path $testResults 'sandbox-msi'
    if (Test-Path $msiFolder) { Remove-Item -Recurse -Force $msiFolder }
    New-Item -ItemType Directory -Force -Path $msiFolder | Out-Null
    $name = Split-Path -Leaf $source
    if ($name -notlike 'PCCompanion-*.msi') { $name = "PCCompanion-$platform.msi" }
    $copy = Join-Path $msiFolder $name
    Copy-Item -LiteralPath $source -Destination $copy
    Unblock-File -LiteralPath $copy
    Write-Host "Checking $source."
} elseif (-not $NoBuild) {
    Write-Host "Building the $platform installer..."
    & dotnet build (Join-Path $root 'src\PCCompanion.Installer\PCCompanion.Installer.wixproj') -c Release "-p:Platform=$platform" | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw "The installer build failed. Run: dotnet build src\PCCompanion.Installer\PCCompanion.Installer.wixproj -c Release -p:Platform=$platform"
    }
}

if (-not (Get-ChildItem $msiFolder -Filter 'PCCompanion-*.msi' -ErrorAction SilentlyContinue)) {
    throw "No installer in $msiFolder. Run without -NoBuild, or give one with -Msi."
}

# The checks also install, over this one, a copy of the installer that's another build of the
# same version, and Setup upgrades PC Companion with a copy that's the next version: each with its own
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
$source = (Get-ChildItem $msiFolder -Filter 'PCCompanion-*.msi' | Select-Object -First 1).FullName
if ($SmartAppControl -eq 'On') {
    $signature = (Get-AuthenticodeSignature $source).Status
    if ($signature -ne 'Valid') {
        Write-Host "The installer's signature is $signature. With Smart App Control on, PC Companion installs, but Windows blocks it when it starts, so the checks stop after Smoke." -ForegroundColor Yellow
    }
}
$upgradeVersion = New-UpgradeInstaller $source (Join-Path $upgradeFolder 'PCCompanion-upgrade.msi')
$sameVersion = New-UpgradeInstaller $source (Join-Path $upgradeFolder 'PCCompanion-same-version.msi') -SameVersion
Write-Host "Made another build of version $sameVersion and an upgrade installer, version $upgradeVersion."

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
    <Command>powershell.exe -NoProfile -ExecutionPolicy Bypass -File C:\Test\scripts\run-checks.ps1 -Groups $groups -SmartAppControl $SmartAppControl</Command>
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
    $durations = @(Select-String -Path $report -Pattern '^-- (.*)$' -Encoding utf8 | ForEach-Object { $_.Matches[0].Groups[1].Value })
    if ($durations) {
        Write-Host "`nGroups:"
        $durations | ForEach-Object { Write-Host "  $_" }
    }
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
