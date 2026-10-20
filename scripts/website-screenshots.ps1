<#
.SYNOPSIS
Takes the website's screenshots of the settings app in Windows Sandbox, without changing this PC,
and replaces the ones of the pages given in website\images.

.DESCRIPTION
Builds the installer for this PC's architecture, or takes the one given with -Msi, and starts
Windows Sandbox with networking off. Inside it, scripts\sandbox\website-screenshots.ps1 installs
PC Companion, turns its features on, and saves each page of the settings app in the light and dark
themes, cropped to the window. The Sandbox window closes when it finishes. If every step passed, the
screenshots of the pages in -Pages, or of every page without it, replace those in website\images;
the report and every page's screenshots are also in TestResults\website-screenshots. See
docs\development\website.md.

Needs the Windows Sandbox feature. Only one Sandbox can run at a time. Keep the Sandbox window open
and visible until it closes: while it's minimized, it draws nothing, so the screenshots come out
blank.

.PARAMETER NoBuild
Uses the installer that is already built.

.PARAMETER Msi
Takes the screenshots with this installer instead of building one, such as the one of the release
the website describes. It must be for this PC's architecture.

.PARAMETER Pages
The pages whose screenshots replace those in website\images, by their names in the settings app's
navigation pane, such as "System Meters" or "Hot Corners,General". Commit only new or changed
screenshots, since every version of an image stays in the repository's history. Without -Pages,
every page's screenshots are replaced, for a full refresh.

.PARAMETER TimeoutMinutes
How long to wait for the screenshots: by default 15 minutes.

.EXAMPLE
powershell -ExecutionPolicy Bypass -File scripts\website-screenshots.ps1 -Msi $HOME\Downloads\PCCompanion-0.2.0-x64.msi -Pages "System Meters"
#>
[CmdletBinding()]
param(
    [switch] $NoBuild,
    [string] $Msi,
    [string[]] $Pages,
    [int] $TimeoutMinutes = 15
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$platform = if ($env:PROCESSOR_ARCHITECTURE -eq 'ARM64') { 'ARM64' } else { 'x64' }
$sandbox = Join-Path $env:WINDIR 'System32\WindowsSandbox.exe'
$testResults = Join-Path $root 'TestResults'
$results = Join-Path $testResults 'website-screenshots'
$msiFolder = Join-Path $testResults 'website-msi'
$configuration = Join-Path $testResults 'website-screenshots.wsb'
$images = Join-Path $root 'website\images'

if (-not (Test-Path $sandbox)) {
    throw 'Windows Sandbox is not turned on. Turn on the "Windows Sandbox" Windows feature and restart.'
}

# Newer versions of Windows Sandbox keep a server process running when no Sandbox is, and list the
# running Sandboxes with wsb.exe.
$running = if (Get-Command wsb.exe -ErrorAction SilentlyContinue) {
    @(& wsb.exe list | Where-Object { $_.Trim() }).Count -gt 0
} else {
    [bool] (Get-Process -Name 'WindowsSandbox*' -ErrorAction SilentlyContinue)
}
if ($running) {
    throw 'Windows Sandbox is already running. Close it first; only one Sandbox can run at a time.'
}

if ($Msi) {
    if (-not (Test-Path -LiteralPath $Msi -PathType Leaf)) {
        throw "There's no installer at $Msi."
    }
    $source = (Resolve-Path -LiteralPath $Msi).ProviderPath
} else {
    $source = Join-Path $root "src\PCCompanion.Installer\bin\$platform\Release\PCCompanion-$platform.msi"
    if (-not $NoBuild) {
        Write-Host "Building the $platform installer..."
        & dotnet build (Join-Path $root 'src\PCCompanion.Installer\PCCompanion.Installer.wixproj') -c Release "-p:Platform=$platform" | Out-Null
        if ($LASTEXITCODE -ne 0) {
            throw "The installer build failed. Run: dotnet build src\PCCompanion.Installer\PCCompanion.Installer.wixproj -c Release -p:Platform=$platform"
        }
    }
    if (-not (Test-Path $source)) {
        throw "No installer at $source. Run without -NoBuild, or give one with -Msi."
    }
}

# A copy in a folder of its own, which the Sandbox maps, without the mark Windows puts on downloaded
# files, so setup doesn't stop at Windows' security warning.
foreach ($folder in $msiFolder, $results) {
    if (Test-Path $folder) {
        try {
            Remove-Item -Recurse -Force $folder
        } catch {
            throw "Can't clear $folder. A Sandbox that was closed before it finished can keep it locked for a few minutes; try again then. $($_.Exception.Message)"
        }
    }
    New-Item -ItemType Directory -Force -Path $folder | Out-Null
}
$copy = Join-Path $msiFolder "PCCompanion-$platform.msi"
Copy-Item -LiteralPath $source -Destination $copy
Unblock-File -LiteralPath $copy
Write-Host "Taking the screenshots with $source."

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
    <Command>powershell.exe -NoProfile -ExecutionPolicy Bypass -File C:\Test\scripts\website-screenshots.ps1</Command>
  </LogonCommand>
</Configuration>
"@

Write-Host 'Starting Windows Sandbox. Its window closes when the screenshots are taken.'
Write-Host "Keep it visible until then: while it's minimized, its screenshots come out blank."
Start-Process -FilePath $sandbox -ArgumentList "`"$configuration`""

# The report is read only once the Sandbox is done: holding it open while the Sandbox writes to it
# loses lines.
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
    Write-Host "The screenshots weren't taken within $TimeoutMinutes minutes. What there is is in $results." -ForegroundColor Red
    exit 1
}

if ((Select-String -Path $report -Pattern '^\[FAIL\]' -Quiet) -or -not (Select-String -Path $report -Pattern '^finished ' -Quiet)) {
    Write-Host "A step failed, so website\images is unchanged. The screenshots are in $results." -ForegroundColor Red
    exit 1
}

# A page's screenshots are named after it: System Meters is system-meters-light.png and -dark.png.
$screenshots = @(Get-ChildItem $results -Filter '*.png')
if ($Pages) {
    $names = @($Pages -split ',' | ForEach-Object { $_.Trim().ToLowerInvariant() -replace ' ', '-' } | Where-Object { $_ })
    $taken = @($screenshots | ForEach-Object { $_.BaseName -replace '-(light|dark)$', '' } | Sort-Object -Unique)
    $unknown = @($names | Where-Object { $taken -notcontains $_ })
    if ($unknown) {
        Write-Host "No screenshots of $($unknown -join ', '), so website\images is unchanged. The pages are: $($taken -join ', ')." -ForegroundColor Red
        exit 1
    }
    $screenshots = @($screenshots | Where-Object { $names -contains ($_.BaseName -replace '-(light|dark)$', '') })
}
New-Item -ItemType Directory -Force -Path $images | Out-Null
Copy-Item -Path $screenshots.FullName -Destination $images -Force
Write-Host "Saved $($screenshots.Count) screenshots to $images. Look through them before you commit them." -ForegroundColor Green
