<#
.SYNOPSIS
Checks PC Companion's installer dialogs inside Windows Sandbox. Started by
scripts\sandbox-test.ps1 -Installer; don't run it on your own PC: it clicks through the dialogs with
the mouse, installs and removes PC Companion for the current user and for all users, and shuts
Windows down at the end.

.DESCRIPTION
Inputs:  C:\Test\msi\PCCompanion-*.msi (read-only).
Outputs: C:\Test\results: report.txt with one PASS or FAIL line per check, a screenshot of each
         dialog page, the install logs, and done.txt when finished.

For each scope, the checks install through Advanced, look at what was installed, and remove it
through the maintenance dialog, with Keep my settings left unchecked just for you and checked for
all users. For all users, another build installed through the pages replaces the first before it's
removed. A scope stops at its first failed step and removes what it
installed. Then they install and remove silently for all users.

The checks look for the English text of the WixUI_Advanced dialogs; update them when the dialogs
change.

.PARAMETER SmartAppControl
What Smart App Control does in the Sandbox, as smart-app-control.ps1 sets it before the first install.
#>
param(
    [ValidateSet('Off', 'Evaluation', 'On')]
    [string] $SmartAppControl = 'Off'
)

$ErrorActionPreference = 'Continue'
$results = 'C:\Test\results'
$report = Join-Path $results 'report.txt'
New-Item -ItemType Directory -Force -Path $results | Out-Null
Set-Content -Path $report -Value "PC Companion installer checks - $(Get-Date -Format o)" -Encoding utf8

function Write-Report([string] $Text) { Add-Content -Path $report -Value $Text -Encoding utf8 }

function Test-Check([string] $Name, [bool] $Passed, [string] $Detail = '') {
    $status = if ($Passed) { 'PASS' } else { 'FAIL' }
    $suffix = if ($Detail) { " - $Detail" } else { '' }
    Write-Report "[$status] $Name$suffix"
}

# A step the rest of the scope needs: a failure stops the scope.
function Assert-Step([string] $Name, [bool] $Passed) {
    if ($Passed) { Test-Check $Name $true; return }
    Test-Check $Name $false "page: $(Get-PageSummary)"
    throw 'step failed'
}

Add-Type -AssemblyName System.Windows.Forms, System.Drawing, UIAutomationClient, UIAutomationTypes
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class Win32 {
    [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern void mouse_event(uint flags, uint dx, uint dy, uint data, UIntPtr extraInfo);
}
'@
[void] [Win32]::SetProcessDPIAware()

$MOUSEEVENTF_LEFTDOWN = 0x2
$MOUSEEVENTF_LEFTUP = 0x4

function Wait-Until([scriptblock] $Condition, [int] $Seconds = 30) {
    for ($i = 0; $i -lt ($Seconds * 4); $i++) {
        if (& $Condition) { return $true }
        Start-Sleep -Milliseconds 250
    }
    return $false
}

function Get-SetupWindow {
    $setup = @(Get-Process msiexec -ErrorAction SilentlyContinue | ForEach-Object { $_.Id })
    $windows = [System.Windows.Automation.AutomationElement]::RootElement.FindAll([System.Windows.Automation.TreeScope]::Children, [System.Windows.Automation.Condition]::TrueCondition)
    foreach ($window in $windows) {
        try {
            if ($window.Current.ClassName -like 'MsiDialog*' -and $setup -contains $window.Current.ProcessId -and -not $window.Current.IsOffscreen) { return $window }
        } catch { }
    }
    return $null
}

# The dialogs keep the controls of every condition and hide the ones that don't apply.
function Get-VisibleControls {
    $window = Get-SetupWindow
    if (-not $window) { return @() }
    try {
        return @($window.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition) | Where-Object { -not $_.Current.IsOffscreen })
    } catch { return @() }
}

# Control names without their & access keys, and the values of text boxes such as the folder.
function Get-PageText {
    return @(Get-VisibleControls | ForEach-Object {
        try {
            $_.Current.Name -replace '&', ''
            $pattern = $null
            if ($_.TryGetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern, [ref] $pattern)) { $pattern.Current.Value }
        } catch { }
    } | Where-Object { $_ })
}

function Get-PageSummary {
    $text = Get-PageText | ForEach-Object {
        $line = ($_ -replace '\s+', ' ').Trim()
        if ($line.Length -gt 60) { $line.Substring(0, 60) + '...' } else { $line }
    }
    if (-not $text) { return 'no setup window' }
    return $text -join ' | '
}

function Test-PageHas([string] $Pattern) { return @(Get-PageText | Where-Object { $_ -like $Pattern }).Count -gt 0 }

function Wait-Page([string] $Pattern, [int] $Seconds = 30) {
    if (-not (Wait-Until { Test-PageHas $Pattern } $Seconds)) { return $false }
    # Let the page finish drawing before a screenshot or a click.
    Start-Sleep -Milliseconds 700
    return $true
}

function Save-Page([string] $Name) {
    $window = Get-SetupWindow
    $bounds = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
    if ($window) {
        $rectangle = $window.Current.BoundingRectangle
        $bounds = New-Object System.Drawing.Rectangle ([int] $rectangle.X), ([int] $rectangle.Y), ([int] $rectangle.Width), ([int] $rectangle.Height)
    }
    $bitmap = New-Object System.Drawing.Bitmap $bounds.Width, $bounds.Height
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.CopyFromScreen($bounds.Location, [System.Drawing.Point]::Empty, $bounds.Size)
    $bitmap.Save((Join-Path $results "$Name.png"), [System.Drawing.Imaging.ImageFormat]::Png)
    $graphics.Dispose()
    $bitmap.Dispose()
}

function Get-Control([string] $Name) {
    return Get-VisibleControls | Where-Object { try { ($_.Current.Name -replace '&', '') -like $Name } catch { $false } } | Select-Object -First 1
}

# Clicks the control with the mouse, as a user would, once it's enabled.
function Invoke-Click([string] $Name) {
    $script:control = $null
    if (-not (Wait-Until { $script:control = Get-Control $Name; $script:control -and $script:control.Current.IsEnabled } 15)) { return $false }
    $window = Get-SetupWindow
    if ($window) { [void] [Win32]::SetForegroundWindow([IntPtr] $window.Current.NativeWindowHandle) }
    Start-Sleep -Milliseconds 200
    $rectangle = $script:control.Current.BoundingRectangle
    [void] [Win32]::SetCursorPos([int] ($rectangle.X + $rectangle.Width / 2), [int] ($rectangle.Y + $rectangle.Height / 2))
    Start-Sleep -Milliseconds 150
    [Win32]::mouse_event($MOUSEEVENTF_LEFTDOWN, 0, 0, 0, [UIntPtr]::Zero)
    [Win32]::mouse_event($MOUSEEVENTF_LEFTUP, 0, 0, 0, [UIntPtr]::Zero)
    Start-Sleep -Milliseconds 700
    return $true
}

function Start-Setup([string] $Log, [string] $Package = $msi) {
    $process = Start-Process msiexec.exe -ArgumentList @('/i', "`"$Package`"", '/l*v', "`"$results\$Log`"") -PassThru
    # Keeps the process handle, so the exit code can be read after setup exits.
    $null = $process.Handle
    return $process
}

function Get-ExitCode($Process) {
    if ($Process.WaitForExit(60000)) { return $Process.ExitCode }
    return 'still running'
}

# The Settings > Apps entries; each one's PSChildName is its product code.
function Get-UninstallEntries {
    return @(Get-ItemProperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\*', 'HKLM:\Software\Microsoft\Windows\CurrentVersion\Uninstall\*', 'HKLM:\Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\*' -ErrorAction SilentlyContinue |
        Where-Object { $_.DisplayName -eq 'PC Companion' })
}

function Get-UninstallEntry { return Get-UninstallEntries | Select-Object -First 1 }

# Settings app and host processes started from $Folder.
function Get-AppProcesses([string] $Folder) {
    return @(Get-Process -Name 'PCCompanion', 'PCCompanion.Host' -ErrorAction SilentlyContinue | Where-Object { $_.Path -like "$Folder\*" })
}

function Test-CheckBoxOn([string] $Name) {
    $checkBox = Get-Control $Name
    return $checkBox -and $checkBox.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern).Current.ToggleState -eq [System.Windows.Automation.ToggleState]::On
}

# The dialogs that a verbose install log shows were opened, in order.
function Get-LogDialogs([string] $Log) {
    return @(Select-String -Path $Log -Pattern 'Action [\d:]+: (\w+)\. Dialog created' -ErrorAction SilentlyContinue | ForEach-Object { $_.Matches[0].Groups[1].Value })
}

$msi = (Get-ChildItem 'C:\Test\msi' -Filter 'PCCompanion-*.msi' | Select-Object -First 1).FullName
$sameVersionMsi = 'C:\Test\upgrade\PCCompanion-same-version.msi'
$elevated = ([Security.Principal.WindowsPrincipal] [Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
$screen = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
Write-Report "msi: $msi; user: $env:USERNAME; elevated: $elevated; screen: $($screen.Width)x$($screen.Height); Windows $([Environment]::OSVersion.Version)"
Write-Report (& (Join-Path $PSScriptRoot 'smart-app-control.ps1') $SmartAppControl)

$scopes = @(
    @{
        Name = 'just for you'; Key = 'user'; Option = 'Install just for you*'; FolderPage = $false; OpenApp = $true; KeepSettings = $false; AnotherBuild = $false
        Folder = (Join-Path $env:LOCALAPPDATA 'Programs\PCCompanion')
        RunKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
        Shortcut = (Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs\PC Companion.lnk')
    },
    @{
        Name = 'all users'; Key = 'machine'; Option = 'Install for all users of this machine'; FolderPage = $true; OpenApp = $false; KeepSettings = $true; AnotherBuild = $true
        Folder = (Join-Path $env:ProgramW6432 'PCCompanion')
        RunKey = 'HKLM:\Software\Microsoft\Windows\CurrentVersion\Run'
        Shortcut = (Join-Path $env:ProgramData 'Microsoft\Windows\Start Menu\Programs\PC Companion.lnk')
    }
)

# 1. Install through Advanced and remove through the maintenance dialog, just for you and for all users.
foreach ($scope in $scopes) {
    $name = $scope.Name
    $key = $scope.Key
    $hostExe = Join-Path $scope.Folder 'PCCompanion.Host.exe'
    $previousPage = if ($scope.FolderPage) { 'Destination Folder' } else { 'Installation Scope' }
    $installedMsi = $msi

    try {
        # The first dialog in a new Sandbox can take a few minutes.
        $process = Start-Setup "install-$key.log"
        Assert-Step "${name}: license page" (Wait-Page 'Please read the PC Companion License Agreement' 300)
        Assert-Step "${name}: accept the license" (Invoke-Click 'I accept the terms in the License Agreement')
        Save-Page "$key-01-license"
        Assert-Step "${name}: Advanced" (Invoke-Click 'Advanced')
        Assert-Step "${name}: scope page" (Wait-Page 'Installation Scope')
        Assert-Step "${name}: choose the scope" (Invoke-Click $scope.Option)
        Save-Page "$key-02-scope"
        Assert-Step "${name}: Next from the scope page" (Invoke-Click 'Next')

        if ($scope.FolderPage) {
            Assert-Step "${name}: folder page" (Wait-Page 'Destination Folder')
            Save-Page "$key-03-folder"
            $proposed = Test-PageHas ([WildcardPattern]::Escape($scope.Folder) + '*')
            Test-Check "${name}: folder page proposes $($scope.Folder)" $proposed $(if (-not $proposed) { "page: $(Get-PageSummary)" })
            Assert-Step "${name}: Next from the folder page" (Invoke-Click 'Next')
        }

        # No feature page: there are no optional features.
        Assert-Step "${name}: Ready to install page" (Wait-Page 'Ready to install PC Companion')
        Save-Page "$key-04-ready"
        Assert-Step "${name}: Back" (Invoke-Click 'Back')
        Assert-Step "${name}: Back returns to the $previousPage page" (Wait-Page $previousPage)
        Save-Page "$key-05-back"
        Assert-Step "${name}: Next again" (Invoke-Click 'Next')
        Assert-Step "${name}: Ready to install page again" (Wait-Page 'Ready to install PC Companion')
        Assert-Step "${name}: Install" (Invoke-Click 'Install')
        Assert-Step "${name}: install finishes" (Wait-Page 'Completed the PC Companion Setup Wizard' 300)
        Save-Page "$key-06-finished"
        Test-Check "${name}: the last page offers to open PC Companion, on by default" (Test-CheckBoxOn 'Open PC Companion')
        if (-not $scope.OpenApp) {
            Assert-Step "${name}: clear Open PC Companion" (Invoke-Click 'Open PC Companion')
            Test-Check "${name}: Open PC Companion is cleared" (-not (Test-CheckBoxOn 'Open PC Companion'))
        }
        Assert-Step "${name}: Finish" (Invoke-Click 'Finish')
        $code = Get-ExitCode $process
        Test-Check "${name}: setup exits with 0" ($code -eq 0) "exit code $code"
        if ($scope.OpenApp) {
            Test-Check "${name}: Finish opens PC Companion" (Wait-Until { @(Get-AppProcesses $scope.Folder | Where-Object { $_.ProcessName -eq 'PCCompanion' }).Count -eq 1 } 30)
        } else {
            Start-Sleep -Seconds 10
            Test-Check "${name}: Finish opens nothing when it's cleared" ((Get-AppProcesses $scope.Folder).Count -eq 0)
        }

        if ($scope.AnotherBuild) {
            # Another build of the same version, installed through the pages for the same scope,
            # replaces this one: setup looks for it once the scope is chosen.
            $productBefore = (Get-UninstallEntry).PSChildName
            $process = Start-Setup "another-build-$key.log" $sameVersionMsi
            Assert-Step "${name}: another build: license page" (Wait-Page 'Please read the PC Companion License Agreement' 300)
            Assert-Step "${name}: another build: accept the license" (Invoke-Click 'I accept the terms in the License Agreement')
            Assert-Step "${name}: another build: Advanced" (Invoke-Click 'Advanced')
            Assert-Step "${name}: another build: scope page" (Wait-Page 'Installation Scope')
            Assert-Step "${name}: another build: choose the scope" (Invoke-Click $scope.Option)
            Assert-Step "${name}: another build: Next from the scope page" (Invoke-Click 'Next')
            if ($scope.FolderPage) {
                Assert-Step "${name}: another build: folder page" (Wait-Page 'Destination Folder')
                Assert-Step "${name}: another build: Next from the folder page" (Invoke-Click 'Next')
            }
            Assert-Step "${name}: another build: Ready to install page" (Wait-Page 'Ready to install PC Companion')
            Assert-Step "${name}: another build: Install" (Invoke-Click 'Install')
            Assert-Step "${name}: another build: install finishes" (Wait-Page 'Completed the PC Companion Setup Wizard' 300)
            Save-Page "$key-06b-another-build"
            Assert-Step "${name}: another build: Finish" (Invoke-Click 'Finish')
            $code = Get-ExitCode $process
            Test-Check "${name}: another build: setup exits with 0" ($code -eq 0) "exit code $code"
            $entries = @(Get-UninstallEntries)
            Test-Check "${name}: another build replaces the installed one, leaving one entry in Settings > Apps" (($entries.Count -eq 1) -and ($entries[0].PSChildName -ne $productBefore)) "before: $productBefore; after: $(($entries | ForEach-Object { $_.PSChildName }) -join ', ')"
            $installedMsi = $sameVersionMsi
        }

        # Removal runs with the settings app and the host open: setup closes them itself.
        if ((Get-AppProcesses $scope.Folder).Count -eq 0) { Start-Process (Join-Path $scope.Folder 'PCCompanion.exe') }
        Test-Check "${name}: the settings app and the host run before removal" (Wait-Until { (Get-AppProcesses $scope.Folder).Count -eq 2 } 30)
        # Removal removes the user's settings and logs, or with Keep my settings, all but the settings.
        $dataFolder = Join-Path $env:LOCALAPPDATA 'PCCompanion'
        New-Item -ItemType Directory -Force -Path $dataFolder | Out-Null
        Set-Content -Path (Join-Path $dataFolder 'settings.json') -Value '{}' -Encoding utf8
        $settingsBefore = Get-Content (Join-Path $dataFolder 'settings.json') -Raw
        Test-Check "${name}: the user's settings and logs are there before removal" (Wait-Until { @(Get-ChildItem (Join-Path $dataFolder 'Logs') -File -ErrorAction SilentlyContinue).Count -gt 0 } 15)

        Test-Check "${name}: installed in $($scope.Folder)" ((Test-Path (Join-Path $scope.Folder 'PCCompanion.exe')) -and (Test-Path $hostExe))
        $run = (Get-ItemProperty $scope.RunKey -Name PCCompanion -ErrorAction SilentlyContinue).PCCompanion
        Test-Check "${name}: startup entry in $($scope.RunKey)" ($run -eq "`"$hostExe`" --startup") "value: $run"
        Test-Check "${name}: Start menu shortcut" (Test-Path $scope.Shortcut) $scope.Shortcut
        $iconLocation = if (Test-Path $scope.Shortcut) { (New-Object -ComObject WScript.Shell).CreateShortcut($scope.Shortcut).IconLocation } else { '' }
        $iconFile = ($iconLocation -split ',')[0]
        Test-Check "${name}: the Start menu shortcut has the app icon" (($iconFile -like '*.ico') -and (Test-Path $iconFile)) "icon: $iconLocation"
        $entry = Get-UninstallEntry
        Test-Check "${name}: Settings > Apps offers no Modify" ($entry -and $entry.NoModify -eq 1) "entry: $([bool] $entry); NoModify: $($entry.NoModify)"

        # Running the MSI again opens the maintenance dialog.
        $process = Start-Setup "remove-$key.log" $installedMsi
        Assert-Step "${name}: maintenance welcome page" (Wait-Page 'Welcome to the PC Companion Setup Wizard' 120)
        Save-Page "$key-07-maintenance-welcome"
        Assert-Step "${name}: Next from the welcome page" (Invoke-Click 'Next')
        Assert-Step "${name}: change, repair or remove page" (Wait-Page 'Change, repair, or remove installation')
        Save-Page "$key-08-maintenance-type"
        $change = Get-Control 'Change'
        Test-Check "${name}: Change is turned off" ($change -and -not $change.Current.IsEnabled) "found: $([bool] $change)"
        Test-Check "${name}: the page says there are no features to change" (Test-PageHas 'PC Companion has no independently selectable features.')
        Assert-Step "${name}: Remove on the change, repair or remove page" (Invoke-Click 'Remove')
        Assert-Step "${name}: Keep my settings page" (Wait-Page 'Keep my settings')
        Save-Page "$key-08b-keep-settings"
        Test-Check "${name}: Keep my settings is unchecked" ((Get-Control 'Keep my settings') -and -not (Test-CheckBoxOn 'Keep my settings'))
        if ($scope.KeepSettings) {
            Assert-Step "${name}: check Keep my settings" (Invoke-Click 'Keep my settings')
            Test-Check "${name}: Keep my settings is checked" (Test-CheckBoxOn 'Keep my settings')
        }
        Assert-Step "${name}: Next from the Keep my settings page" (Invoke-Click 'Next')
        Assert-Step "${name}: Ready to remove page" (Wait-Page 'Ready to remove PC Companion')
        Save-Page "$key-09-ready-to-remove"
        Assert-Step "${name}: Back from the Ready to remove page" (Invoke-Click 'Back')
        Assert-Step "${name}: Back returns to the Keep my settings page" (Wait-Page 'Keep my settings')
        Test-Check "${name}: the page keeps the choice" ((Test-CheckBoxOn 'Keep my settings') -eq $scope.KeepSettings)
        Assert-Step "${name}: Next to the Ready to remove page again" (Invoke-Click 'Next')
        Assert-Step "${name}: Ready to remove page again" (Wait-Page 'Ready to remove PC Companion')
        Assert-Step "${name}: Remove" (Invoke-Click 'Remove')
        # Setup closes them before Windows Installer looks for files in use, which would ask first.
        [void] (Wait-Until { (Test-PageHas 'Completed the PC Companion Setup Wizard') -or (Test-PageHas '*currently in use*') } 300)
        $asked = [bool] (Test-PageHas '*currently in use*')
        Test-Check "${name}: removal doesn't ask to close PC Companion" (-not $asked) $(if ($asked) { Get-PageSummary } else { '' })
        if ($asked) {
            Save-Page "$key-09b-files-in-use"
            # Closing them is its default.
            Assert-Step "${name}: OK on the files in use page" (Invoke-Click 'OK')
        }
        Assert-Step "${name}: removal finishes" (Wait-Page 'Completed the PC Companion Setup Wizard' 300)
        Save-Page "$key-10-removed"
        Test-Check "${name}: the last page after removal doesn't offer to open PC Companion" (-not (Get-Control 'Open PC Companion'))
        Assert-Step "${name}: Finish after removal" (Invoke-Click 'Finish')
        $code = Get-ExitCode $process
        Test-Check "${name}: setup exits with 0 after removal" ($code -eq 0) "exit code $code"
        Test-Check "${name}: removal closes the settings app and the host" ((Get-AppProcesses $scope.Folder).Count -eq 0) "running: $((Get-AppProcesses $scope.Folder | ForEach-Object { $_.ProcessName }) -join ', ')"

        Test-Check "${name}: install folder removed" (-not (Test-Path (Join-Path $scope.Folder 'PCCompanion.exe')))
        Test-Check "${name}: startup entry removed" (-not (Get-ItemProperty $scope.RunKey -Name PCCompanion -ErrorAction SilentlyContinue))
        Test-Check "${name}: Start menu shortcut removed" (-not (Test-Path $scope.Shortcut))
        Test-Check "${name}: Settings > Apps entry removed" (-not (Get-UninstallEntry))
        $left = @(Get-ChildItem $dataFolder -Force -Recurse -Name -ErrorAction SilentlyContinue)
        if ($scope.KeepSettings) {
            Test-Check "${name}: Keep my settings keeps the settings and removes the logs" (($left.Count -eq 1) -and ($left[0] -eq 'settings.json') -and ((Get-Content (Join-Path $dataFolder 'settings.json') -Raw -ErrorAction SilentlyContinue) -eq $settingsBefore)) "left: $($left -join ', ')"
        } else {
            Test-Check "${name}: removal removes the user's settings and logs" (-not (Test-Path $dataFolder)) "left: $($left -join ', ')"
        }
        Remove-Item $dataFolder -Recurse -Force -ErrorAction SilentlyContinue
    } catch {
        # Assert-Step has reported its own failure; anything else is an error in these checks.
        if ("$_" -ne 'step failed') { Test-Check "${name}: checks run without errors" $false "$_" }
        Save-Page "$key-failed"
        Get-Process msiexec -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
        Start-Sleep -Seconds 5
        $code = (Start-Process msiexec.exe -ArgumentList @('/x', "`"$installedMsi`"", '/qn', '/l*v', "`"$results\cleanup-$key.log`"") -Wait -PassThru).ExitCode
        Write-Report "${name}: stopped and removed PC Companion (exit code $code)"
    }

    foreach ($log in "install-$key.log", "another-build-$key.log", "remove-$key.log") {
        $path = Join-Path $results $log
        if (-not (Test-Path $path)) { continue }
        $dialogs = Get-LogDialogs $path
        Test-Check "${name}: $log never opens the feature page" (($dialogs.Count -gt 0) -and -not ($dialogs -contains 'FeaturesDlg')) "dialogs: $($dialogs -join ', ')"
    }
}

# 2. Silent install for all users, as docs\development\packaging.md describes it.
$folder = Join-Path $env:ProgramW6432 'PCCompanion'
$hostExe = Join-Path $folder 'PCCompanion.Host.exe'
$runKey = 'HKLM:\Software\Microsoft\Windows\CurrentVersion\Run'
$code = (Start-Process msiexec.exe -ArgumentList @('/i', "`"$msi`"", '/qn', 'ALLUSERS=2', 'MSIINSTALLPERUSER=""', '/l*v', "`"$results\install-silent.log`"") -Wait -PassThru).ExitCode
Test-Check 'silent install for all users' ($code -eq 0) "exit code $code"
Test-Check "silent install in $folder" ((Test-Path (Join-Path $folder 'PCCompanion.exe')) -and (Test-Path $hostExe))
Start-Sleep -Seconds 5
Test-Check 'silent install opens nothing' ((Get-AppProcesses $folder).Count -eq 0)
$run = (Get-ItemProperty $runKey -Name PCCompanion -ErrorAction SilentlyContinue).PCCompanion
Test-Check "silent install startup entry in $runKey" ($run -eq "`"$hostExe`" --startup") "value: $run"
$code = (Start-Process msiexec.exe -ArgumentList @('/x', "`"$msi`"", '/qn', '/l*v', "`"$results\remove-silent.log`"") -Wait -PassThru).ExitCode
Test-Check 'silent uninstall' ($code -eq 0) "exit code $code"
Test-Check 'silent uninstall removes the install folder' (-not (Test-Path (Join-Path $folder 'PCCompanion.exe')))
Test-Check 'silent uninstall removes the startup entry' (-not (Get-ItemProperty $runKey -Name PCCompanion -ErrorAction SilentlyContinue))

Write-Report "finished $(Get-Date -Format o)"
Set-Content -Path (Join-Path $results 'done.txt') -Value 'done'
shutdown.exe /s /t 5
