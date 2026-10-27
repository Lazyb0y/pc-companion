# App: the settings app's pages, layout and window, one instance, the tray icon and menu, and Start with Windows.

Start-Process $appExe
[void] (Wait-Until { Get-AppWindow })
Start-Sleep -Seconds 3

# The General page.
Test-Check 'select General' (Select-NavigationItem 'General')
Start-Sleep -Seconds 2
Save-Screenshot '02-general-english'
$text = Get-WindowText
Test-Check 'General shows its settings' (($text -contains 'Language') -and ($text -contains 'App theme') -and ($text -contains 'Start with Windows')) ($text -join ' | ')
Test-Check 'General shows how long logs are kept' ($text -contains 'Logs are kept for 14 days.')
Test-Check 'General shows the version' (@($text | Where-Object { $_ -match '^Version \d+\.\d+\.\d+' }).Count -eq 1)
$adminToggle = Find-Toggle 'Always run as administrator'
Test-Check 'Always run as administrator is unavailable for a per-user install' (($null -ne $adminToggle) -and -not $adminToggle.Current.IsEnabled)
Test-Check 'General says why' ($text -contains 'Available when PC Companion is installed for all users.') ($text -join ' | ')

# App theme, chosen in its list on the General page, applies at once and is saved. The Sandbox's
# Windows mode is light, so Use system setting is light too.
foreach ($theme in @{ Name = 'Dark'; Saved = 'Dark' }, @{ Name = 'Light'; Saved = 'Light' }, @{ Name = 'Use system setting'; Saved = 'System' }) {
    Test-Check "choose the app theme $($theme.Name)" (Select-ListItem 'App theme' $theme.Name)
    Start-Sleep -Seconds 1
    $brightness = Get-AppWindowBrightness
    $saved = (Get-Content (Join-Path $dataFolder 'settings.json') -Raw | ConvertFrom-Json).general.theme
    $looks = if ($theme.Saved -eq 'Dark') { ($brightness -ge 0) -and ($brightness -lt 100) } else { $brightness -gt 160 }
    Test-Check "the app theme $($theme.Name) applies at once and is saved" ($looks -and ($saved -eq $theme.Saved)) "brightness $brightness, saved: $saved"
    Save-Screenshot "02a-general-theme-$($theme.Saved.ToLowerInvariant())"
}

# Page layout at window widths from narrow (the pane folds away) to the full screen, on Home,
# Hot Corners, App Windows, System Meters, Clipboard Actions, Network Profiles, Folder Rules and General: a centered column, as Windows Settings
# lays out its pages. At the full screen's width, the feature pages' headers too.
$pages = @(@{ Name = 'Home'; Title = 'Home' }, @{ Name = 'Hot Corners'; Title = 'Hot Corners'; Header = $true }, @{ Name = 'App Windows'; Title = 'App Windows'; Header = $true }, @{ Name = 'System Meters'; Title = 'System Meters'; Header = $true }, @{ Name = 'Clipboard Actions'; Title = 'Clipboard Actions'; Header = $true }, @{ Name = 'Network Profiles'; Title = 'Network Profiles'; Header = $true }, @{ Name = 'Folder Rules'; Title = 'Folder Rules'; Header = $true }, @{ Name = 'General'; Title = 'General' })
$windowBounds = (Get-AppWindow).Current.BoundingRectangle
$letterHeights = @{}
foreach ($width in 600, 1000, 1300, $screen.Width) {
    [void] (Set-AppWindowWidth $width)
    foreach ($page in $pages) {
        [void] (Show-Page $page.Name)
        Start-Sleep -Milliseconds 1500
        Test-PageLayout $page.Name $page.Title $width
        if ($width -eq $screen.Width -and $page.Header) {
            $letterHeights[$page.Name] = Test-HeaderAlignment $page.Name $page.Title
        }
        if ($width -eq $screen.Width -or $width -eq 600) {
            Save-Screenshot "02b-layout-$($page.Name.ToLowerInvariant() -replace ' ', '-')-$width"
        }
    }
}

# The headers at the largest Windows text size, which the settings app reads when it starts.
$accessibility = 'HKCU:\Software\Microsoft\Accessibility'
$textScale = (Get-ItemProperty $accessibility -ErrorAction SilentlyContinue).TextScaleFactor
if (-not (Test-Path $accessibility)) { [void] (New-Item $accessibility -Force) }
[void] (New-ItemProperty $accessibility -Name TextScaleFactor -Value 225 -PropertyType DWord -Force)
[void] (Close-SettingsApp)
Start-Process $appExe
[void] (Wait-Until { Get-AppWindow })
Start-Sleep -Seconds 3
[void] (Set-AppWindowWidth $screen.Width)
foreach ($page in @($pages | Where-Object { $_.Header })) {
    [void] (Show-Page $page.Name)
    Start-Sleep -Milliseconds 1500
    $height = Test-HeaderAlignment $page.Name $page.Title ' at 225% text size'
    # Windows enlarges big text less than small text: the title's letters by about a third.
    Test-Check "$($page.Name)'s title is larger at 225% text size" ($height -ge 1.2 * $letterHeights[$page.Name]) "first letter $height pixels high, $($letterHeights[$page.Name]) at 100%"
    Save-Screenshot "02c-header-$($page.Name.ToLowerInvariant() -replace ' ', '-')-text-225"
}
if ($null -eq $textScale) {
    Remove-ItemProperty $accessibility -Name TextScaleFactor -ErrorAction SilentlyContinue
}
else {
    [void] (New-ItemProperty $accessibility -Name TextScaleFactor -Value $textScale -PropertyType DWord -Force)
}
[void] (Close-SettingsApp)
Start-Process $appExe
[void] (Wait-Until { Get-AppWindow })
Start-Sleep -Seconds 3
Restore-AppWindow $windowBounds
[void] (Select-NavigationItem 'General')

# The settings window isn't seen empty: it's out of sight (cloaked) when it first shows, until
# its page is there, and it's cloaked before it's taken down when it closes.
$windowClass = 'WinUIDesktopWin32WindowClass'
$closing = [Win32]::FindWindow($windowClass, 'PC Companion')
$process = Get-Processes 'PCCompanion' | Select-Object -First 1
$cloakedBeforeGone = $false
[void] [Win32]::PostMessage($closing, 0x0010, [UIntPtr]::Zero, [IntPtr]::Zero) # WM_CLOSE
$watch = [System.Diagnostics.Stopwatch]::StartNew()
while ([Win32]::IsWindow($closing) -and $watch.Elapsed.TotalSeconds -lt 10) {
    if ([Win32]::IsCloaked($closing)) { $cloakedBeforeGone = $true }
    [System.Threading.Thread]::Sleep(1)
}
Test-Check 'closing hides the settings window before it''s taken down' $cloakedBeforeGone
[void] (Wait-Until { (Get-Processes 'PCCompanion').Count -eq 0 } 10)

Start-Process $appExe
$firstShownCloaked = $null
$pageWhenShown = $false
$shownAfter = $null
$watch = [System.Diagnostics.Stopwatch]::StartNew()
while ($watch.Elapsed.TotalSeconds -lt 20) {
    $opening = [Win32]::FindWindow($windowClass, 'PC Companion')
    if ($opening -ne [IntPtr]::Zero -and [Win32]::IsWindowVisible($opening)) {
        $cloaked = [Win32]::IsCloaked($opening)
        if ($null -eq $firstShownCloaked) { $firstShownCloaked = $cloaked; $firstShown = $watch.ElapsedMilliseconds }
        if (-not $cloaked) {
            $shownAfter = $watch.ElapsedMilliseconds - $firstShown
            $condition = New-Object System.Windows.Automation.AndCondition(
                (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, 'Home')),
                (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Text)))
            $titles = @([System.Windows.Automation.AutomationElement]::FromHandle($opening).FindAll([System.Windows.Automation.TreeScope]::Descendants, $condition))
            $pageWhenShown = @($titles | Where-Object { $_.Current.BoundingRectangle.Height -ge 30 }).Count -gt 0
            break
        }
    }
    [System.Threading.Thread]::Sleep(1)
}
Test-Check 'the settings window is out of sight when it first shows' ($firstShownCloaked -eq $true) "cloaked when first shown: $firstShownCloaked"
Test-Check 'the settings window comes into sight within a second' (($null -ne $shownAfter) -and ($shownAfter -le 1000)) "after $shownAfter ms"
Test-Check 'the settings window comes into sight with its page' $pageWhenShown
[void] (Wait-Until { Get-AppWindow })
Start-Sleep -Seconds 2

# One settings app per user session.
Start-Process $appExe
Start-Sleep -Seconds 5
Test-Check 'a second launch keeps one settings app' ((Get-Processes 'PCCompanion').Count -eq 1) "processes: $((Get-Processes 'PCCompanion').Count)"

# The tray icon's menu, and selecting the icon.
Test-Check 'tray menu requested' (Send-TrayEvent $WM_CONTEXTMENU $menuX $menuY)
Start-Sleep -Seconds 2
Save-Screenshot '03-tray-menu-english'
Test-Check 'the tray menu closes' (Close-TrayMenu)

# The tray menu follows the Windows mode, which the host reads each time the menu opens.
$personalize = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize'
$windowsMode = (Get-ItemProperty $personalize -ErrorAction SilentlyContinue).SystemUsesLightTheme
if (-not (Test-Path $personalize)) { [void] (New-Item $personalize -Force) }
foreach ($mode in @{ Name = 'dark'; Light = 0 }, @{ Name = 'light'; Light = 1 }) {
    [void] (New-ItemProperty $personalize -Name SystemUsesLightTheme -Value $mode.Light -PropertyType DWord -Force)
    [void] (Send-TrayEvent $WM_CONTEXTMENU $menuX $menuY)
    $brightness = Get-TrayMenuBrightness
    $passed = if ($mode.Light -eq 0) { ($brightness -ge 0) -and ($brightness -lt 100) } else { $brightness -gt 160 }
    Test-Check "the tray menu is $($mode.Name) in the $($mode.Name) Windows mode" $passed "brightness $brightness"
    Save-Screenshot "03-tray-menu-$($mode.Name)"
    Test-Check "the tray menu closes in the $($mode.Name) Windows mode" (Close-TrayMenu)
}
if ($null -eq $windowsMode) {
    Remove-ItemProperty $personalize -Name SystemUsesLightTheme -ErrorAction SilentlyContinue
}
else {
    [void] (New-ItemProperty $personalize -Name SystemUsesLightTheme -Value $windowsMode -PropertyType DWord -Force)
}

Test-Check 'settings app closes' (Close-SettingsApp)
Test-Check 'host keeps running after the settings app closes' ((Get-Processes 'PCCompanion.Host').Count -eq 1)
Test-Check 'tray icon selected' (Send-TrayEvent $NIN_SELECT)
Test-Check 'selecting the tray icon opens the settings app' (Wait-Until { Get-AppWindow } 20)
Start-Process $hostExe -Wait
Test-Check 'a second host launch keeps one host' ((Get-Processes 'PCCompanion.Host').Count -eq 1)

# Selecting the tray icon brings an open settings window to the front, from minimized and from
# behind another window. When the user clicks the icon, Explorer lets the host bring a window to
# the front; a posted message doesn't, so Select-TrayIcon passes the right on as Explorer does.
# Pressing Alt lets this script do that.
function Select-TrayIcon {
    $hostProcess = Get-Processes 'PCCompanion.Host' | Select-Object -First 1
    if (-not $hostProcess) { return $false }
    [void] [Win32]::Key(0x12, $false)
    [void] [Win32]::Key(0x12, $true)
    $granted = [Win32]::AllowSetForegroundWindow([uint32] $hostProcess.Id)
    $sent = Send-TrayEvent $NIN_SELECT
    return $granted -and $sent
}

$appWindow = [IntPtr] (Get-AppWindow).Current.NativeWindowHandle
[void] [Win32]::ShowWindow($appWindow, 6) # SW_MINIMIZE
Test-Check 'the settings window is minimized' (Wait-Until { [Win32]::IsIconic($appWindow) } 5)
Test-Check 'select the tray icon while the settings window is minimized' (Select-TrayIcon)
Test-Check 'the tray icon brings the minimized settings window to the front' (Wait-Until { -not [Win32]::IsIconic($appWindow) -and [Win32]::GetForegroundWindow() -eq $appWindow } 10) "minimized: $([Win32]::IsIconic($appWindow)); in front: $([Win32]::GetForegroundWindow() -eq $appWindow)"
$other = Start-Process (Join-Path $env:WINDIR 'System32\charmap.exe') -PassThru
[void] (Wait-Until { $other.Refresh(); $other.MainWindowHandle -ne [IntPtr]::Zero } 10)
[void] [Win32]::Key(0x12, $false)
[void] [Win32]::SetForegroundWindow($other.MainWindowHandle)
[void] [Win32]::Key(0x12, $true)
Test-Check 'another window is in front of the settings window' (Wait-Until { [Win32]::GetForegroundWindow() -eq $other.MainWindowHandle } 5)
Test-Check 'select the tray icon while another window is in front' (Select-TrayIcon)
Test-Check 'the tray icon brings the settings window in front of the other window' (Wait-Until { [Win32]::GetForegroundWindow() -eq $appWindow } 10) "in front: $([Win32]::GetForegroundWindow())"
[void] (Close-SettingsApp)
[void] [Win32]::Key(0x12, $false)
[void] [Win32]::SetForegroundWindow($other.MainWindowHandle)
[void] [Win32]::Key(0x12, $true)
Test-Check 'select the tray icon while the settings app is closed and another window is in front' (Select-TrayIcon)
Test-Check 'the tray icon opens the settings window in front of the other window' (Wait-Until { (Get-AppWindow) -and [Win32]::GetForegroundWindow() -eq [IntPtr] (Get-AppWindow).Current.NativeWindowHandle } 15) "in front: $([Win32]::GetForegroundWindow())"

# Starting the host again, as a user does from the Run dialog, brings the open settings window
# in front of another window: the new launch passes its right to do that on to the running host.
[void] [Win32]::Key(0x12, $false)
[void] [Win32]::SetForegroundWindow($other.MainWindowHandle)
[void] [Win32]::Key(0x12, $true)
Test-Check 'another window is in front of the settings window again' (Wait-Until { [Win32]::GetForegroundWindow() -eq $other.MainWindowHandle } 5)
[void] [Win32]::Key(0x5B, $false) # Windows key
Send-Key 0x52                     # R
[void] [Win32]::Key(0x5B, $true)
$runCondition = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, 'Run')
Test-Check 'the Run dialog opens' (Wait-Until { $null -ne [System.Windows.Automation.AutomationElement]::RootElement.FindFirst([System.Windows.Automation.TreeScope]::Children, $runCondition) } 10)
Start-Sleep -Milliseconds 500
[System.Windows.Forms.SendKeys]::SendWait($hostExe)
[System.Windows.Forms.SendKeys]::SendWait('{ENTER}')
Test-Check 'starting the host again brings the settings window in front of the other window' (Wait-Until { (Get-AppWindow) -and [Win32]::GetForegroundWindow() -eq [IntPtr] (Get-AppWindow).Current.NativeWindowHandle } 15) "in front: $([Win32]::GetForegroundWindow())"
Test-Check 'starting the host again keeps one host' (Wait-Until { (Get-Processes 'PCCompanion.Host').Count -eq 1 } 10)
Stop-Process -Id $other.Id -Force -ErrorAction SilentlyContinue
Test-Check 'the settings app is open before Exit' ((Get-Processes 'PCCompanion').Count -eq 1)
Test-Check 'tray menu requested for Exit' (Send-TrayEvent $WM_CONTEXTMENU $menuX $menuY)
Test-Check 'choose Exit in the tray menu' (Select-TrayMenuItem 'Exit')
Test-Check 'Exit in the tray menu stops the host' (Wait-Until { (Get-Processes 'PCCompanion.Host').Count -eq 0 } 15)
Test-Check 'Exit in the tray menu closes the settings app' (Wait-Until { (Get-Processes 'PCCompanion').Count -eq 0 } 15)
$appLog = (Get-ChildItem $logs -Filter 'app-*.log' -ErrorAction SilentlyContinue | ForEach-Object { Get-Content $_.FullName -Raw }) -join "`n"
Test-Check 'the settings app logged that Exit closed it' ($appLog -match 'Asked to close, as Exit was chosen in the tray menu')
Start-Process $hostExe
[void] (Wait-Until { (Get-Processes 'PCCompanion.Host').Count -eq 1 } 10)

# Start with Windows on.
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
