<#
.SYNOPSIS
Checks PC Companion inside Windows Sandbox. Started by scripts\sandbox-test.ps1; don't run it on your
own PC: it installs and uninstalls PC Companion, clicks through setup and the settings app, moves the
pointer, and shuts Windows down at the end.

.DESCRIPTION
Inputs:  C:\Test\msi\PCCompanion-*.msi, and C:\Test\upgrade with another build of the same version
         and the next version (read-only).
Outputs: C:\Test\results: report.txt with one PASS or FAIL line per check, a folder for each group
         with its screenshots, the app and host logs and the install logs, and done.txt when
         finished.

Each group of checks is a script in groups\, which runs with the helpers of common.ps1. Before each
group, nothing of PC Companion runs, the user has no settings or logs, and PC Companion is installed
just for the current user or not installed, as the group needs. Each group runs in its own scope, so
nothing it sets reaches the next one, and its logs are checked once it's done.

The checks look for English, Arabic and Bangla text from src\PCCompanion.Localization and for the
English text of the WixUI_Advanced dialogs; update them when those strings change.

.PARAMETER Groups
The groups to run, separated by commas, such as HotCorners,AppWindows, or All. Smoke always runs
first, and the groups run in the order of $startStates below.

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

# The groups, in the order they run, and what each starts from: installed just for the current user
# from C:\Test\msi, as Smoke leaves it, or not installed. The groups that remove PC Companion or
# install it for all users come last.
$startStates = [ordered] @{
    Smoke = 'NotInstalled'
    App = 'Installed'
    Languages = 'Installed'
    HotCorners = 'Installed'
    AppWindows = 'Installed'
    Logging = 'Installed'
    Setup = 'Installed'
    RunAsAdmin = 'NotInstalled'
    Dialogs = 'NotInstalled'
}

# Closes PC Companion, removes the user's settings and logs, and installs or uninstalls it for $State,
# with setup's logs in $Folder.
function Reset-PCCompanion([string] $State, [string] $Folder) {
    Stop-All
    $products = @(Get-InstalledProducts)
    $installed = ($products.Count -eq 1) -and ($products[0].InstallSource -like 'C:\Test\msi*') -and (Test-Path $appExe)
    if ($State -eq 'NotInstalled' -or -not $installed) {
        foreach ($product in $products) {
            $code = (Start-Process msiexec.exe -ArgumentList @('/x', $product.PSChildName, '/qn', '/l*v', "`"$Folder\uninstall-before.log`"") -Wait -PassThru).ExitCode
            Test-Check "uninstall PC Companion $($product.DisplayVersion) before the group" ($code -eq 0) "exit code $code"
        }
    }
    Remove-Item $dataFolder -Recurse -Force -ErrorAction SilentlyContinue
    if ($State -eq 'Installed' -and -not $installed) {
        $code = (Start-Process msiexec.exe -ArgumentList @('/i', "`"$msi`"", '/qn', 'ALLUSERS=2', 'MSIINSTALLPERUSER=1', '/l*v', "`"$Folder\install-before.log`"") -Wait -PassThru).ExitCode
        Test-Check 'install PC Companion for the current user before the group' ($code -eq 0) "exit code $code"
    }
}

# Copies the group's logs to $Folder and checks them. A group that uninstalls PC Companion, which
# removes the logs, checks what it needs before that.
# Whether Windows blocked a file of PC Companion from running, as Smart App Control does with an
# unsigned build: Code Integrity logs event 3077 for each file it blocks.
function Test-Blocked {
    $blocked = Get-WinEvent -FilterHashtable @{ LogName = 'Microsoft-Windows-CodeIntegrity/Operational'; Id = 3077 } -ErrorAction SilentlyContinue |
        Where-Object { $_.Message -like '*\PCCompanion\*' } | Select-Object -First 1
    return [bool] $blocked
}

function Test-GroupLogs([string] $Group, [string] $Folder) {
    Stop-All
    $files = @(Get-ChildItem $logs -File -ErrorAction SilentlyContinue)
    if ($files.Count -eq 0) { return }
    Copy-Item -Path $files.FullName -Destination $Folder -ErrorAction SilentlyContinue
    $text = ($files | ForEach-Object { Get-Content $_.FullName -Raw }) -join "`n"
    Test-Check "${Group}: the logs have no errors" (-not ($text -match '\[(ERR|FTL)\]'))
    Test-Check "${Group}: the logs don't show the user name" (-not ($text -match [regex]::Escape($env:USERNAME)))
}

$elevated = ([Security.Principal.WindowsPrincipal] [Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
Write-Report "msi: $msi; user: $env:USERNAME; elevated: $elevated; screen: $($screen.Width)x$($screen.Height); Windows $([Environment]::OSVersion.Version)"
Write-Report (& (Join-Path $PSScriptRoot 'smart-app-control.ps1') $SmartAppControl)
$requested = @($Groups -split ',' | ForEach-Object { $_.Trim() } | Where-Object { $_ })
$selected = if ($requested -contains 'All') { @($startStates.Keys) } else {
    @($startStates.Keys | Where-Object { $_ -eq 'Smoke' -or $requested -contains $_ }) + @($requested | Where-Object { -not $startStates.Contains($_) })
}
Write-Report "groups: $($selected -join ', ')"

foreach ($group in $selected) {
    Write-Report "== $group"
    $start = @(Get-Content $report -Encoding utf8).Count
    $watch = [Diagnostics.Stopwatch]::StartNew()
    $file = Join-Path $PSScriptRoot "groups\$group.ps1"
    $folder = New-Item -ItemType Directory -Force -Path (Join-Path $results $group)
    if ($startStates.Contains($group) -and (Test-Path $file)) {
        Reset-PCCompanion $startStates[$group] $folder.FullName
        & {
            $results = $folder.FullName
            . $file
        }
        Test-GroupLogs $group $folder.FullName
    } else {
        Test-Check "the group $group is in groups\ and in run-checks.ps1" $false
    }
    $lines = @(Get-Content $report -Encoding utf8 | Select-Object -Skip $start)
    $checks = @($lines | Where-Object { $_ -match '^\[(PASS|FAIL)\]' }).Count
    $failed = @($lines | Where-Object { $_ -match '^\[FAIL\]' }).Count
    Write-Report ('-- {0}: {1} checks, {2} failed, {3} min {4} s' -f $group, $checks, $failed, [int] [math]::Floor($watch.Elapsed.TotalMinutes), $watch.Elapsed.Seconds)

    # Smoke runs first; the other groups would fail the same way.
    if ($group -eq 'Smoke' -and $selected.Count -gt 1 -and (Test-Blocked)) {
        Write-Report "[NOTE] Windows blocked PC Companion from starting (Smart App Control: $SmartAppControl), so these groups didn't run: $(@($selected | Select-Object -Skip 1) -join ', ')."
        break
    }
}

Write-Report "finished $(Get-Date -Format o)"
Set-Content -Path (Join-Path $results 'done.txt') -Value 'done'
shutdown.exe /s /t 5
