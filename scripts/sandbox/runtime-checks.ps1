<#
.SYNOPSIS
Runtime checks of PC Companion inside Windows Sandbox. Started by scripts\sandbox-test.ps1; don't run
it on your own PC: it installs and uninstalls PC Companion and shuts Windows down at the end.

.DESCRIPTION
Inputs:  C:\Test\msi\PCCompanion-*.msi (read-only).
Outputs: C:\Test\results: report.txt with one PASS or FAIL line per check, screenshots, the app and
         host logs, the install logs, and done.txt when finished.

The checks look for English, Arabic and Bangla text from src\PCCompanion.Localization; update them
when those strings change.
#>

$ErrorActionPreference = 'Continue'
$results = 'C:\Test\results'
$report = Join-Path $results 'report.txt'
New-Item -ItemType Directory -Force -Path $results | Out-Null
Set-Content -Path $report -Value "PC Companion runtime checks - $(Get-Date -Format o)" -Encoding utf8

function Write-Report([string] $Text) { Add-Content -Path $report -Value $Text -Encoding utf8 }

function Test-Check([string] $Name, [bool] $Passed, [string] $Detail = '') {
    $status = if ($Passed) { 'PASS' } else { 'FAIL' }
    $suffix = if ($Detail) { " - $Detail" } else { '' }
    Write-Report "[$status] $Name$suffix"
}

Add-Type -AssemblyName System.Windows.Forms, System.Drawing, UIAutomationClient, UIAutomationTypes
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class Win32 {
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindow(string className, string title);
    [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr window, uint message, UIntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
}
'@
[void] [Win32]::SetProcessDPIAware()

function Save-Screenshot([string] $Name) {
    $bounds = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
    $bitmap = New-Object System.Drawing.Bitmap $bounds.Width, $bounds.Height
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.CopyFromScreen($bounds.Location, [System.Drawing.Point]::Empty, $bounds.Size)
    $bitmap.Save((Join-Path $results "$Name.png"), [System.Drawing.Imaging.ImageFormat]::Png)

    # The Sandbox draws nothing while its window is minimized, so screenshots come out blank.
    $first = $bitmap.GetPixel(0, 0)
    $blank = $true
    for ($x = 0; $x -lt $bounds.Width -and $blank; $x += 50) {
        for ($y = 0; $y -lt $bounds.Height -and $blank; $y += 50) {
            if ($bitmap.GetPixel($x, $y) -ne $first) { $blank = $false }
        }
    }
    if ($blank) {
        Write-Report "[NOTE] Screenshot $Name is blank; keep the Sandbox window open and visible while the checks run."
    }

    $graphics.Dispose()
    $bitmap.Dispose()
}

function Wait-Until([scriptblock] $Condition, [int] $Seconds = 30) {
    for ($i = 0; $i -lt ($Seconds * 4); $i++) {
        if (& $Condition) { return $true }
        Start-Sleep -Milliseconds 250
    }
    return $false
}

function Get-Processes([string] $Name) { @(Get-Process -Name $Name -ErrorAction SilentlyContinue) }

function Close-SettingsApp {
    Get-Processes 'PCCompanion' | ForEach-Object { [void] $_.CloseMainWindow() }
    return (Wait-Until { (Get-Processes 'PCCompanion').Count -eq 0 } 15)
}

function Stop-All {
    [void] (Close-SettingsApp)
    foreach ($name in 'PCCompanion', 'PCCompanion.Host') {
        Get-Processes $name | ForEach-Object { Stop-Process -Id $_.Id -Force -ErrorAction SilentlyContinue }
    }
    Start-Sleep -Seconds 1
}

function Get-AppWindow {
    $process = Get-Processes 'PCCompanion' | Select-Object -First 1
    if (-not $process) { return $null }
    $condition = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ProcessIdProperty, $process.Id)
    return [System.Windows.Automation.AutomationElement]::RootElement.FindFirst([System.Windows.Automation.TreeScope]::Children, $condition)
}

function Select-NavigationItem([string] $Name) {
    $window = Get-AppWindow
    if (-not $window) { return $false }
    $condition = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, $Name)
    $item = $window.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
    if (-not $item) { return $false }
    $pattern = $null
    if ($item.TryGetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern, [ref] $pattern)) { $pattern.Select(); return $true }
    if ($item.TryGetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern, [ref] $pattern)) { $pattern.Invoke(); return $true }
    return $false
}

function Set-Toggle([string] $Name, [bool] $On) {
    $window = Get-AppWindow
    if (-not $window) { return $false }
    $condition = New-Object System.Windows.Automation.AndCondition(
        (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, $Name)),
        (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::IsTogglePatternAvailableProperty, $true)))
    $toggle = $window.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
    if (-not $toggle) { return $false }
    $pattern = $toggle.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern)
    if (($pattern.Current.ToggleState -eq [System.Windows.Automation.ToggleState]::On) -ne $On) { $pattern.Toggle() }
    return $true
}

function Get-WindowText {
    $window = Get-AppWindow
    if (-not $window) { return @() }
    $all = $window.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
    return @($all | ForEach-Object { $_.Current.Name } | Where-Object { $_ })
}

# Sends what Explorer sends when the tray icon is used: the host's window gets WM_APP + 1 with the
# event in the low word of lParam and the icon ID (1) in the high word. The icon itself is usually
# hidden in the overflow area of a new Windows 11 profile.
function Send-TrayEvent([int] $TrayEvent, [int] $X = 0, [int] $Y = 0) {
    $window = [Win32]::FindWindow('PCCompanion.Host', $null)
    if ($window -eq [IntPtr]::Zero) { return $false }
    return [Win32]::PostMessage($window, 0x8001, [UIntPtr] [uint32] (($X -band 0xFFFF) -bor ($Y -shl 16)), [IntPtr] ($TrayEvent -bor (1 -shl 16)))
}

$WM_CONTEXTMENU = 0x007B
$NIN_SELECT = 0x0400

function Write-Settings([string] $Json) {
    New-Item -ItemType Directory -Force -Path $dataFolder | Out-Null
    Set-Content -Path (Join-Path $dataFolder 'settings.json') -Value $Json -Encoding utf8
}

$msi = (Get-ChildItem 'C:\Test\msi' -Filter 'PCCompanion-*.msi' | Select-Object -First 1).FullName
$installFolder = Join-Path $env:LOCALAPPDATA 'Programs\PCCompanion'
$appExe = Join-Path $installFolder 'PCCompanion.exe'
$hostExe = Join-Path $installFolder 'PCCompanion.Host.exe'
$dataFolder = Join-Path $env:LOCALAPPDATA 'PCCompanion'
$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$screen = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
$menuX = $screen.Width - 300
$menuY = $screen.Height - 100
Write-Report "msi: $msi; user: $env:USERNAME; screen: $($screen.Width)x$($screen.Height); Windows $([Environment]::OSVersion.Version)"

# 1. Install for the current user.
$code = (Start-Process msiexec.exe -ArgumentList @('/i', "`"$msi`"", '/qn', 'ALLUSERS=2', 'MSIINSTALLPERUSER=1', '/l*v', "`"$results\install.log`"") -Wait -PassThru).ExitCode
Test-Check 'install for the current user' ($code -eq 0) "exit code $code"
Test-Check 'settings app and host installed' ((Test-Path $appExe) -and (Test-Path $hostExe))
Test-Check 'third-party notices installed' (Test-Path (Join-Path $installFolder 'ThirdPartyNotices.txt'))
$run = (Get-ItemProperty $runKey -Name PCCompanion -ErrorAction SilentlyContinue).PCCompanion
Test-Check 'startup entry in HKCU Run' ($run -eq "`"$hostExe`" --startup") "value: $run"

# 2. Started at sign-in while Start with Windows is off: the host exits and creates nothing.
$process = Start-Process $hostExe -ArgumentList '--startup' -PassThru
Test-Check 'host --startup exits when Start with Windows is off' ($process.WaitForExit(10000))
Test-Check 'host --startup creates no data folder' (-not (Test-Path $dataFolder))

# 3. The settings app starts the host and shows Home.
Start-Process $appExe
Test-Check 'settings app window opens' (Wait-Until { Get-AppWindow })
Test-Check 'settings app starts the host' (Wait-Until { (Get-Processes 'PCCompanion.Host').Count -eq 1 } 15)
Start-Sleep -Seconds 3
Save-Screenshot '01-home-english'
$text = Get-WindowText
Test-Check 'Home shows the feature cards' (($text -contains 'Home') -and ($text -contains 'Input & Triggers') -and ($text -contains 'Hot Corners')) ($text -join ' | ')

# 4. The General page.
Test-Check 'select General' (Select-NavigationItem 'General')
Start-Sleep -Seconds 2
Save-Screenshot '02-general-english'
$text = Get-WindowText
Test-Check 'General shows its settings' (($text -contains 'Language') -and ($text -contains 'App theme') -and ($text -contains 'Start with Windows')) ($text -join ' | ')
Test-Check 'General shows how long logs are kept' ($text -contains 'Logs are kept for 14 days.')
Test-Check 'General shows the version' (@($text | Where-Object { $_ -match '^Version \d+\.\d+\.\d+' }).Count -eq 1)

# 5. One settings app per user session.
Start-Process $appExe
Start-Sleep -Seconds 5
Test-Check 'a second launch keeps one settings app' ((Get-Processes 'PCCompanion').Count -eq 1) "processes: $((Get-Processes 'PCCompanion').Count)"

# 6. The tray icon's menu, and selecting the icon.
Test-Check 'tray menu requested' (Send-TrayEvent $WM_CONTEXTMENU $menuX $menuY)
Start-Sleep -Seconds 2
Save-Screenshot '03-tray-menu-english'
[System.Windows.Forms.SendKeys]::SendWait('{ESC}')
Start-Sleep -Seconds 1
Test-Check 'settings app closes' (Close-SettingsApp)
Test-Check 'host keeps running after the settings app closes' ((Get-Processes 'PCCompanion.Host').Count -eq 1)
Test-Check 'tray icon selected' (Send-TrayEvent $NIN_SELECT)
Test-Check 'selecting the tray icon opens the settings app' (Wait-Until { Get-AppWindow } 20)
Start-Process $hostExe -Wait
Test-Check 'a second host launch keeps one host' ((Get-Processes 'PCCompanion.Host').Count -eq 1)

# 7. Arabic with the dark theme, as the General page saves them; the host follows the change.
[void] (Close-SettingsApp)
Write-Settings '{ "schemaVersion": 1, "general": { "language": "ar", "theme": "Dark", "startWithWindows": false } }'
Start-Sleep -Seconds 2
Start-Process $appExe
[void] (Wait-Until { Get-AppWindow })
Start-Sleep -Seconds 3
Save-Screenshot '04-home-arabic-dark'
$text = Get-WindowText
Test-Check 'Home in Arabic' ($text -contains 'الصفحة الرئيسية') ($text -join ' | ')
Test-Check 'select General in Arabic' (Select-NavigationItem 'عام')
Start-Sleep -Seconds 2
Save-Screenshot '05-general-arabic-dark'
Test-Check 'tray menu requested in Arabic' (Send-TrayEvent $WM_CONTEXTMENU $menuX $menuY)
Start-Sleep -Seconds 2
Save-Screenshot '06-tray-menu-arabic'
[System.Windows.Forms.SendKeys]::SendWait('{ESC}')
Start-Sleep -Seconds 1

# 8. Bangla with the light theme.
[void] (Close-SettingsApp)
Write-Settings '{ "schemaVersion": 1, "general": { "language": "bn", "theme": "Light", "startWithWindows": false } }'
Start-Sleep -Seconds 2
Start-Process $appExe
[void] (Wait-Until { Get-AppWindow })
Start-Sleep -Seconds 3
Test-Check 'select General in Bangla' (Select-NavigationItem 'সাধারণ')
Start-Sleep -Seconds 2
Save-Screenshot '07-general-bangla-light'

# 9. Detailed logging: the General page switch turns it on for 24 hours; it turns itself off when the time is up.
[void] (Close-SettingsApp)
Write-Settings '{ "schemaVersion": 1, "general": { "theme": "Light" } }'
Start-Sleep -Seconds 2
Start-Process $appExe
[void] (Wait-Until { Get-AppWindow })
Start-Sleep -Seconds 3
[void] (Select-NavigationItem 'General')
Start-Sleep -Seconds 2
Test-Check 'turn on detailed logging' (Set-Toggle 'Detailed logging' $true)
Start-Sleep -Seconds 3
$saved = (Get-Content (Join-Path $dataFolder 'settings.json') -Raw | ConvertFrom-Json).diagnostics.detailedLoggingUntil
$hoursLeft = if ($saved) { ([DateTimeOffset]::Parse([string] $saved) - [DateTimeOffset]::Now).TotalHours } else { 0 }
Test-Check 'the switch saves an end time 24 hours ahead' (($hoursLeft -gt 23.9) -and ($hoursLeft -le 24)) "saved: $saved"
$text = Get-WindowText
Test-Check 'General shows when detailed logging turns off' (@($text | Where-Object { $_ -like 'On until *' }).Count -ge 1) ($text -join ' | ')
Save-Screenshot '08-general-detailed-logging'
Test-Check 'tray menu requested with detailed logging on' (Send-TrayEvent $WM_CONTEXTMENU $menuX $menuY)
Start-Sleep -Seconds 2
[System.Windows.Forms.SendKeys]::SendWait('{ESC}')
Start-Sleep -Seconds 1
[void] (Close-SettingsApp)
# As if the 24 hours were nearly over.
$soon = [DateTimeOffset]::Now.AddSeconds(20).ToString('o')
Write-Settings "{ ""schemaVersion"": 1, ""general"": { ""theme"": ""Light"" }, ""diagnostics"": { ""detailedLoggingUntil"": ""$soon"" } }"
Start-Sleep -Seconds 25
Test-Check 'tray menu requested after detailed logging ended' (Send-TrayEvent $WM_CONTEXTMENU $menuX $menuY)
Start-Sleep -Seconds 2
[System.Windows.Forms.SendKeys]::SendWait('{ESC}')
Start-Sleep -Seconds 1

# 10. Start with Windows on.
Write-Settings '{ "schemaVersion": 1, "general": { "language": "bn", "theme": "Light", "startWithWindows": true } }'
Start-Sleep -Seconds 2
[void] (Close-SettingsApp)
$process = Start-Process $hostExe -ArgumentList '--startup' -PassThru
Test-Check 'host --startup leaves a running host alone' ($process.WaitForExit(10000))
Start-Sleep -Seconds 3
Test-Check 'host --startup opens no settings window' ((Get-Processes 'PCCompanion').Count -eq 0)
Stop-All
$process = Start-Process $hostExe -ArgumentList '--startup' -PassThru
Start-Sleep -Seconds 5
Test-Check 'host --startup keeps running when Start with Windows is on' (-not $process.HasExited)

# 11. Logs.
Stop-All
$logs = Join-Path $dataFolder 'Logs'
Copy-Item -Path (Join-Path $logs '*') -Destination $results -ErrorAction SilentlyContinue
$logText = (Get-ChildItem $logs -File -ErrorAction SilentlyContinue | ForEach-Object { Get-Content $_.FullName -Raw }) -join "`n"
Test-Check 'settings app and host logs written' ((Test-Path (Join-Path $logs 'app-*.log')) -and (Test-Path (Join-Path $logs 'host-*.log')))
Test-Check 'logs have no errors' (-not ($logText -match '\[(ERR|FTL)\]'))
Test-Check 'logs don''t show the user name' (-not ($logText -match [regex]::Escape($env:USERNAME)))
Test-Check 'host loaded the changed settings' ($logText -match 'The settings changed and were loaded again')

$appLog = (Get-ChildItem $logs -Filter 'app-*.log' -ErrorAction SilentlyContinue | ForEach-Object { Get-Content $_.FullName -Raw }) -join "`n"
$hostLines = @(Get-ChildItem $logs -Filter 'host-*.log' -ErrorAction SilentlyContinue | ForEach-Object { Get-Content $_.FullName })
$turnedOff = @(0..($hostLines.Count - 1) | Where-Object { $hostLines[$_] -match 'Detailed logging is off\.' })
$debugLines = @(0..($hostLines.Count - 1) | Where-Object { $hostLines[$_] -match '\[DBG\]' })
Test-Check 'settings app turned detailed logging on' ($appLog -match 'Detailed logging is on until')
Test-Check 'host turned detailed logging on' (@($hostLines | Where-Object { $_ -match 'Detailed logging is on until' }).Count -ge 1)
Test-Check 'host wrote Debug entries while detailed logging was on' (@($hostLines | Where-Object { $_ -match '\[DBG\] PCCompanion\.Host\.TrayController: Opened the tray menu' }).Count -ge 1)
Test-Check 'detailed logging turned itself off in the host' ($turnedOff.Count -ge 1)
Test-Check 'host wrote no Debug entries after detailed logging turned off' (($turnedOff.Count -ge 1) -and (@($debugLines | Where-Object { $_ -gt $turnedOff[-1] }).Count -eq 0))

# 12. Uninstall.
$code = (Start-Process msiexec.exe -ArgumentList @('/x', "`"$msi`"", '/qn', '/l*v', "`"$results\uninstall.log`"") -Wait -PassThru).ExitCode
Test-Check 'uninstall' ($code -eq 0) "exit code $code"
Test-Check 'startup entry removed' (-not (Get-ItemProperty $runKey -Name PCCompanion -ErrorAction SilentlyContinue))
Test-Check 'install folder removed' (-not (Test-Path $appExe))

Write-Report "finished $(Get-Date -Format o)"
Set-Content -Path (Join-Path $results 'done.txt') -Value 'done'
shutdown.exe /s /t 5
