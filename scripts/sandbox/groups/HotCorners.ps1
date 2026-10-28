# Hot Corners: the Home switch turns it on; a corner shows the desktop when the pointer reaches
# it, not while a mouse button is held, an app is full screen or has captured the pointer, or a
# listed app is in front, and only with the key it requires.
[void] (Close-SettingsApp)
Write-Settings '{ "schemaVersion": 1, "general": { "theme": "Light" } }'
Start-Sleep -Seconds 2
Start-Process $appExe
[void] (Wait-Until { Get-AppWindow })
Start-Sleep -Seconds 3
Test-Check 'turn on Hot Corners on Home' (Set-Toggle 'Hot Corners' $true)
Start-Sleep -Seconds 2
$saved = (Get-Content (Join-Path $dataFolder 'settings.json') -Raw | ConvertFrom-Json).features.hotCorners
Test-Check 'the Home switch saves Hot Corners as on' ($saved.enabled -eq $true) "saved: $($saved | ConvertTo-Json -Compress)"
Test-Check 'select Hot Corners' (Select-NavigationItem 'Hot Corners')
Start-Sleep -Seconds 2
Save-Screenshot '09-hot-corners-english'
$text = Get-WindowText
Test-Check 'Hot Corners shows its settings' (($text -contains 'Top left') -and ($text -contains 'Activation delay') -and ($text -contains 'Permissions & data')) ($text -join ' | ')
$selected = Get-SelectedItemName 'Top left action'
Test-Check 'the top-left corner shows Task View when first turned on' ($selected -eq 'Task View') "selected: $selected"

# The activation delay's slider, named after its header, shows the delay and saves a new one.
$sliderCondition = New-Object System.Windows.Automation.AndCondition(
    (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, 'Activation delay')),
    (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::IsRangeValuePatternAvailableProperty, $true)))
$slider = (Get-AppWindow).FindFirst([System.Windows.Automation.TreeScope]::Descendants, $sliderCondition)
$range = if ($slider) { $slider.GetCurrentPattern([System.Windows.Automation.RangeValuePattern]::Pattern) } else { $null }
Test-Check 'the activation delay starts at 150 ms' (($null -ne $range) -and ($range.Current.Value -eq 150) -and ((Get-WindowText) -contains '150 ms')) "slider: $(if ($range) { $range.Current.Value } else { 'not found' })"
if ($range) { $range.SetValue(300) }
Start-Sleep -Seconds 1
$saved = (Get-Content (Join-Path $dataFolder 'settings.json') -Raw | ConvertFrom-Json).features.hotCorners
Test-Check 'the activation delay slider saves 300 ms and shows it' (($saved.activationDelayMilliseconds -eq 300) -and ((Get-WindowText) -contains '300 ms')) "saved: $($saved.activationDelayMilliseconds)"
[void] (Close-SettingsApp)

# The top-left corner shows the desktop; detailed logging is on for the host's Debug entries.
$detailedUntil = [DateTimeOffset]::Now.AddHours(1).ToString('o')
function Write-HotCornersSettings([bool] $Enabled, [string] $Key) {
    Write-Settings "{ ""schemaVersion"": 1, ""general"": { ""theme"": ""Light"" }, ""diagnostics"": { ""detailedLoggingUntil"": ""$detailedUntil"" }, ""features"": { ""hotCorners"": { ""enabled"": $($Enabled.ToString().ToLowerInvariant()), ""topLeft"": { ""action"": ""ShowDesktop"", ""key"": ""$Key"" } } } }"
    Start-Sleep -Seconds 3
}
$middleX = [int]($screen.Width / 2)
$middleY = [int]($screen.Height / 2)
$ran = 'The "?TopLeft"? corner ran "?ShowDesktop"?\.'
Write-HotCornersSettings $true 'None'
Start-Process $appExe
[void] (Wait-Until { Get-AppWindow })
Start-Sleep -Seconds 3
$appWindow = (Get-Processes 'PCCompanion' | Select-Object -First 1).MainWindowHandle
Test-Check 'the pointer can be moved in the Sandbox' (Move-Pointer 100 100) "at $([System.Windows.Forms.Cursor]::Position); keep the Sandbox window open and the mouse outside it"
[void] (Move-Pointer $middleX $middleY)
[void] (Move-IntoTopLeftCorner)
Test-Check 'the top-left corner shows the desktop' (Wait-Until { [Win32]::IsIconic($appWindow) } 5) (Get-PointerText)
Test-Check 'the host logs which corner ran which action' (Wait-Until { (Get-MatchCount (Get-HostLog) $ran) -eq 1 } 5) (Get-PointerText)
Save-Screenshot '10-hot-corners-show-desktop'

# A mouse button held as the pointer reaches the corner, as when dragging a window there.
[void] (Move-Pointer $middleX $middleY)
[void] [Win32]::LeftButton($true)
[void] (Move-IntoTopLeftCorner)
Start-Sleep -Seconds 1
[void] [Win32]::LeftButton($false)
Test-Check 'a held mouse button skips the corner' (Wait-Until { (Get-HostLog) -match "The `"?TopLeft`"? corner didn't run `"?ShowDesktop`"?: `"?MouseButtonHeld`"?\." } 5) (Get-PointerText)

# The corner requires Ctrl. Holding it must not change the shortcut: Ctrl + Windows + D adds a virtual desktop.
Write-HotCornersSettings $true 'Control'
[void] (Move-Pointer $middleX $middleY)
[void] (Move-IntoTopLeftCorner)
Test-Check 'without the required key the corner is skipped' (Wait-Until { (Get-HostLog) -match "The `"?TopLeft`"? corner didn't run `"?ShowDesktop`"?: `"?KeyNotHeld`"?\." } 5) (Get-PointerText)
$desktops = Get-VirtualDesktopCount
[void] (Move-Pointer $middleX $middleY)
[void] [Win32]::Key(0xA2, $false)
[void] (Move-IntoTopLeftCorner)
Start-Sleep -Seconds 1
[void] [Win32]::Key(0xA2, $true)
Test-Check 'with the required key the corner runs' (Wait-Until { (Get-MatchCount (Get-HostLog) $ran) -eq 2 } 5) (Get-PointerText)
Test-Check 'the held key adds no virtual desktop' ((Get-VirtualDesktopCount) -eq $desktops) "before: $desktops, after: $(Get-VirtualDesktopCount)"

# Keep displays on: while the pointer stays in the corner, powercfg lists the host's display and
# system requests with their reason; once it leaves, neither. Windows Sandbox runs no screen saver,
# so whether it starts is checked by hand.
function Get-HostPowerRequests {
    $requests = @{}
    $section = ''
    foreach ($line in @(& powercfg.exe /requests)) {
        if ($line -match '^([A-Z]+):$') { $section = $Matches[1]; $requests[$section] = '' }
        elseif ($section) { $requests[$section] += "$line`n" }
    }
    $reason = [regex]::Escape('Hot Corners: the pointer is in a corner that keeps the displays on.')
    return @{
        Display = $requests['DISPLAY'] -match "PCCompanion\.Host\.exe\s*\n$reason"
        System = $requests['SYSTEM'] -match "PCCompanion\.Host\.exe\s*\n$reason"
        Any = ($requests['DISPLAY'] + $requests['SYSTEM']) -match 'PCCompanion\.Host\.exe'
        Text = (($requests.Keys | ForEach-Object { "$_ $($requests[$_].Trim() -replace '\s*\n\s*', ' ')" }) -join ' | ')
    }
}
Write-Settings "{ ""schemaVersion"": 1, ""general"": { ""theme"": ""Light"" }, ""diagnostics"": { ""detailedLoggingUntil"": ""$detailedUntil"" }, ""features"": { ""hotCorners"": { ""enabled"": true, ""topLeft"": { ""action"": ""KeepDisplaysOn"", ""key"": ""None"" } } } }"
Start-Sleep -Seconds 3
[void] (Move-Pointer $middleX $middleY)
[void] (Move-IntoTopLeftCorner)
Test-Check 'the top-left corner keeps the displays on' (Wait-Until { (Get-HostLog) -match 'The "?TopLeft"? corner ran "?KeepDisplaysOn"?\.' } 5) (Get-PointerText)
$requests = Get-HostPowerRequests
Test-Check 'powercfg lists the host''s display request with its reason' $requests.Display $requests.Text
Test-Check 'powercfg lists the host''s system request with its reason' $requests.System $requests.Text
[void] (Move-Pointer $middleX $middleY)
Test-Check 'leaving the corner ends Keep displays on' (Wait-Until { (Get-HostLog) -match 'Keep displays on ended: "?PointerLeft"?\.' } 5) (Get-PointerText)
$requests = Get-HostPowerRequests
Test-Check 'powercfg lists no request of the host once the pointer leaves' (-not $requests.Any) $requests.Text

# Full screen and a captured pointer: a borderless window covering the display skips the corner,
# which runs again once the window closes, and so do a pointer confined with ClipCursor and a
# window in the corner that hides the pointer. The windows run in a process of their own, like
# another app's.
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class PointerClip {
    [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] private static extern bool ClipCursor(ref RECT rect);
    [DllImport("user32.dll")] private static extern bool ClipCursor(IntPtr rect);
    public static bool Confine(int left, int top, int right, int bottom) {
        var rect = new RECT { Left = left, Top = top, Right = right, Bottom = bottom };
        return ClipCursor(ref rect);
    }
    public static bool Release() { return ClipCursor(IntPtr.Zero); }
}
'@
function Get-SkippedPattern([string] $Reason) { "The `"?TopLeft`"? corner didn't run `"?ShowDesktop`"?: `"?$Reason`"?\." }

Write-HotCornersSettings $true 'None'
$window = Start-TestWindow 'FullScreen'
$found = Find-TestWindow 'FullScreen'
$area = New-Object Win32+RECT
$covers = ($found -ne [IntPtr]::Zero) -and [Win32]::GetWindowRect($found, [ref] $area) -and ($area.Left -le 0) -and ($area.Top -le 0) -and ($area.Right -ge $screen.Width) -and ($area.Bottom -ge $screen.Height)
Test-Check 'the full-screen test window covers the display' $covers "window $found at $($area.Left), $($area.Top), $($area.Right), $($area.Bottom); display $($screen.Width) x $($screen.Height)"
[void] (Move-Pointer $middleX $middleY)
[void] (Move-IntoTopLeftCorner)
Test-Check 'a borderless window covering the display skips the corner' (Wait-Until { (Get-HostLog) -match (Get-SkippedPattern 'FullScreenApp') } 5) (Get-PointerText)
Stop-Process -Id $window.Id -Force -ErrorAction SilentlyContinue
[void] (Wait-Until { (Find-TestWindow 'FullScreen') -eq [IntPtr]::Zero } 10)
[void] (Move-Pointer $middleX $middleY)
[void] (Move-IntoTopLeftCorner)
Test-Check 'the corner runs again once the full-screen window closes' (Wait-Until { (Get-MatchCount (Get-HostLog) $ran) -eq 3 } 5) (Get-PointerText)

[void] (Move-Pointer 400 300)
Test-Check 'the pointer can be confined to part of the screen' ([PointerClip]::Confine(0, 0, 800, 600))
[void] (Move-IntoTopLeftCorner)
Test-Check 'a pointer confined to part of the screen skips the corner' (Wait-Until { (Get-HostLog) -match (Get-SkippedPattern 'PointerConfined') } 5) (Get-PointerText)
[void] [PointerClip]::Release()

[void] (Move-Pointer $middleX $middleY)
$window = Start-TestWindow 'HidesPointer'
[void] (Move-IntoTopLeftCorner)
Test-Check 'a window in the corner that hides the pointer skips the corner' (Wait-Until { (Get-HostLog) -match (Get-SkippedPattern 'PointerHidden') } 5) (Get-PointerText)
Stop-Process -Id $window.Id -Force -ErrorAction SilentlyContinue

# Apps that turn corners off: with Character Map listed and in front, the corner is skipped and the
# host logs why. The page shows the listed app; Remove removes it, Running app lists Character Map
# and adds it again, and Program file adds a program through the Open dialog, each saved.
$charmap = Join-Path $env:WINDIR 'System32\charmap.exe'
$commandPrompt = Join-Path $env:WINDIR 'System32\cmd.exe'
function Get-ExcludedApps {
    $apps = (Get-Content (Join-Path $dataFolder 'settings.json') -Raw | ConvertFrom-Json).features.hotCorners.excludedApps
    if ($null -eq $apps) { return @() }
    return @($apps)
}
function Find-AppListItem([string] $Name) {
    $window = Get-AppWindow
    if (-not $window) { return $null }
    $condition = New-Object System.Windows.Automation.AndCondition(
        (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, $Name)),
        (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::ListItem)))
    return $window.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
}
function Find-InElement($Element, [string] $AutomationId, $ControlType) {
    $condition = New-Object System.Windows.Automation.AndCondition(
        (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::AutomationIdProperty, $AutomationId)),
        (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, $ControlType)))
    return $Element.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
}
Write-Settings "{ ""schemaVersion"": 1, ""general"": { ""theme"": ""Light"" }, ""diagnostics"": { ""detailedLoggingUntil"": ""$detailedUntil"" }, ""features"": { ""hotCorners"": { ""enabled"": true, ""topLeft"": { ""action"": ""ShowDesktop"", ""key"": ""None"" }, ""excludedApps"": [ { ""name"": ""Character Map"", ""program"": ""$($charmap.Replace('\', '\\'))"" } ] } } }"
Start-Sleep -Seconds 3
$characterMap = Start-Process $charmap -PassThru
[void] (Wait-Until { $characterMap.Refresh(); $characterMap.MainWindowHandle -ne [IntPtr]::Zero } 15)
[void] [Win32]::SetForegroundWindow($characterMap.MainWindowHandle)
Start-Sleep -Seconds 1
Test-Check 'Character Map is in front' ([Win32]::GetForegroundWindow() -eq $characterMap.MainWindowHandle)
[void] (Move-Pointer $middleX $middleY)
[void] (Move-IntoTopLeftCorner)
Test-Check 'a listed app in front skips the corner' (Wait-Until { (Get-HostLog) -match (Get-SkippedPattern 'AppExcluded') } 5) (Get-PointerText)
[void] (Move-Pointer $middleX $middleY)

[void] (Close-SettingsApp)
Start-Process $appExe
[void] (Wait-Until { Get-AppWindow })
Start-Sleep -Seconds 3
Test-Check 'select Hot Corners for its apps' (Select-NavigationItem 'Hot Corners')
Start-Sleep -Seconds 2
Test-Check 'the page lists Character Map' ($null -ne (Find-AppElement 'Remove Character Map'))
Save-Screenshot '10b-hot-corners-apps'
Test-Check 'Remove removes Character Map' ((Invoke-AppElement 'Remove Character Map') -and (Wait-Until { @(Get-ExcludedApps).Count -eq 0 } 5)) "saved: $(Get-ExcludedApps | ConvertTo-Json -Compress)"
Test-Check 'Add an app offers Running app' ((Invoke-AppElement 'Add an app') -and (Wait-Until { Find-AppElement 'Running app…' } 5))
[void] (Invoke-AppElement 'Running app…')
$appItem = $null
Test-Check 'Running app lists Character Map' (Wait-Until { ($script:appItem = Find-AppListItem 'Character Map') -ne $null } 15)
Save-Screenshot '10c-hot-corners-running-app'
if ($appItem) { $appItem.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select(); Start-Sleep -Milliseconds 300 }
[void] (Invoke-AppElement 'Add')
Test-Check 'Running app adds Character Map with its name' (Wait-Until { $apps = @(Get-ExcludedApps); ($apps.Count -eq 1) -and ($apps[0].program -eq $charmap) -and ($apps[0].name -eq 'Character Map') } 5) "saved: $(Get-ExcludedApps | ConvertTo-Json -Compress)"
Test-Check 'Add an app offers Program file' ((Invoke-AppElement 'Add an app') -and (Wait-Until { Find-AppElement 'Program file…' } 5))
# Invoked through UI Automation, the item would wait for the Open dialog to close, so it's chosen
# with the keyboard, as Browse is.
$programFileItem = Find-AppElement 'Program file…'
if ($programFileItem) { $programFileItem.SetFocus(); Start-Sleep -Milliseconds 300; Send-Key 0x20 }
$openDialog = $null
Test-Check 'Program file opens the Open dialog' (Wait-Until { ($script:openDialog = Find-AppDialog 'Choose a program') -ne $null } 10)
# The file name box has the keyboard focus when the dialog opens; its window shows before the box
# does. Its type differs between Windows versions, so it's found by its ID alone, and typed into
# when it can't be set. Enter chooses Open.
$fileNameBox = $null
$idCondition = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::AutomationIdProperty, '1148')
[void] (Wait-Until { ($script:fileNameBox = $openDialog.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $idCondition)) -ne $null } 10)
Start-Sleep -Seconds 1
$valuePattern = $null
if ($fileNameBox -and $fileNameBox.TryGetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern, [ref] $valuePattern)) {
    $fileNameBox.SetFocus()
    $valuePattern.SetValue($commandPrompt)
} else {
    [System.Windows.Forms.SendKeys]::SendWait($commandPrompt)
}
Start-Sleep -Milliseconds 500
Send-Key 0x0D
Test-Check 'Program file adds the program with its description as its name' (Wait-Until { $apps = @(Get-ExcludedApps); ($apps.Count -eq 2) -and ($apps[1].program -eq $commandPrompt) -and $apps[1].name -and ($apps[1].name -ne 'cmd') } 10) "saved: $(Get-ExcludedApps | ConvertTo-Json -Compress)"
# An Open dialog left open would hold the later checks.
if (Find-AppDialog 'Choose a program') { Send-Key 0x1B; [void] (Wait-Until { -not (Find-AppDialog 'Choose a program') } 5) }
$removeButton = Find-AppElement 'Remove Character Map'
$scrollItem = $null
if ($removeButton -and $removeButton.TryGetCurrentPattern([System.Windows.Automation.ScrollItemPattern]::Pattern, [ref] $scrollItem)) { $scrollItem.ScrollIntoView(); Start-Sleep -Seconds 1 }
Save-Screenshot '10d-hot-corners-apps-added'
[void] (Close-SettingsApp)
Stop-Process -Id $characterMap.Id -Force -ErrorAction SilentlyContinue

# Recent results: with the page open, the pointer leaving a corner before the delay ends, and a
# corner skipped by a full-screen window, show on the page as they happen. Turning Hot Corners off
# first clears the results of the checks above.
Write-HotCornersSettings $false 'None'
Write-Settings "{ ""schemaVersion"": 1, ""general"": { ""theme"": ""Light"" }, ""diagnostics"": { ""detailedLoggingUntil"": ""$detailedUntil"" }, ""features"": { ""hotCorners"": { ""enabled"": true, ""topLeft"": { ""action"": ""ShowDesktop"", ""key"": ""None"" }, ""activationDelayMilliseconds"": 500 } } }"
Start-Sleep -Seconds 3
Start-Process $appExe
[void] (Wait-Until { Get-AppWindow })
Start-Sleep -Seconds 3
Test-Check 'select Hot Corners for its recent results' (Select-NavigationItem 'Hot Corners')
Start-Sleep -Seconds 2
Test-Check 'the page says that no corner has been reached yet' (Wait-Until { (Get-WindowText) -contains 'No corner has been reached yet.' } 5)
# In and out of the corner at once, as when the pointer passes through it.
[void] (Move-Pointer $middleX $middleY)
[void] [Win32]::MovePointer(0, 0, $screen.Width, $screen.Height)
[void] [Win32]::PushPointer(-10, -10)
Start-Sleep -Milliseconds 100
[void] [Win32]::MovePointer($middleX, $middleY, $screen.Width, $screen.Height)
$leftEarly = 'Top left, Show desktop: skipped because the pointer left before the delay ended'
Test-Check 'the page shows that the pointer left before the delay ended' (Wait-Until { (Get-WindowText) -contains $leftEarly } 5) (Get-PointerText)
$window = Start-TestWindow 'FullScreen'
[void] (Move-IntoTopLeftCorner)
$fullScreen = 'Top left, Show desktop: skipped because an app was full screen'
Test-Check 'the page shows that an app was full screen' (Wait-Until { (Get-WindowText) -contains $fullScreen } 5) (Get-PointerText)
Stop-Process -Id $window.Id -Force -ErrorAction SilentlyContinue
[void] (Wait-Until { (Find-TestWindow 'FullScreen') -eq [IntPtr]::Zero } 10)
[void] (Move-Pointer $middleX $middleY)
# The results are at the end of the page, which scrolls apart from the navigation pane.
$scroller = Find-AppText $fullScreen
while ($scroller -and -not [bool] $scroller.GetCurrentPropertyValue([System.Windows.Automation.AutomationElement]::IsScrollPatternAvailableProperty)) {
    $scroller = [System.Windows.Automation.TreeWalker]::ControlViewWalker.GetParent($scroller)
}
if ($scroller) { $scroller.GetCurrentPattern([System.Windows.Automation.ScrollPattern]::Pattern).SetScrollPercent([System.Windows.Automation.ScrollPattern]::NoScroll, 100); Start-Sleep -Seconds 1 }
Save-Screenshot '10e-hot-corners-recent-results'
[void] (Close-SettingsApp)
Remove-Item $testWindowScript -ErrorAction SilentlyContinue

# Turned off, Hot Corners stops reading the pointer.
Write-HotCornersSettings $false 'None'
Test-Check 'turning Hot Corners off stops it' ((Get-HostLog) -match 'Hot Corners stopped\.')
[void] (Move-Pointer $middleX $middleY)
[void] (Move-IntoTopLeftCorner)
Start-Sleep -Seconds 1
Test-Check 'a corner does nothing while Hot Corners is off' ((Get-MatchCount (Get-HostLog) $ran) -eq 3)
[void] (Move-Pointer $middleX $middleY)
Save-Screenshot '11-hot-corners-off'

# Run a program: the page shows the program, and Browse opens the Open dialog. Everything in the
# Sandbox runs as administrator with UAC off, so the host starts the program itself. Handing it to a
# copy of the host without admin rights, with UAC on, is checked by hand.
[void] (Close-SettingsApp)
$program = Join-Path $env:WINDIR 'System32\charmap.exe'
Test-Check 'the test program exists' (Test-Path $program) $program
Write-Settings "{ ""schemaVersion"": 1, ""general"": { ""theme"": ""Light"" }, ""diagnostics"": { ""detailedLoggingUntil"": ""$detailedUntil"" }, ""features"": { ""hotCorners"": { ""enabled"": true, ""topLeft"": { ""action"": ""RunProgram"", ""key"": ""None"", ""program"": ""$($program.Replace('\', '\\'))"", ""arguments"": ""/A"" } } } }"
Start-Sleep -Seconds 3
Start-Process $appExe
[void] (Wait-Until { Get-AppWindow })
Start-Sleep -Seconds 3
Test-Check 'select Hot Corners for Run a program' (Select-NavigationItem 'Hot Corners')
Start-Sleep -Seconds 2
$selected = Get-SelectedItemName 'Top left action'
Test-Check 'the top-left corner shows Run a program' ($selected -eq 'Run a program') "selected: $selected"
$shown = Get-BoxText 'Top left program'
Test-Check 'the program box shows the program' ($shown -eq $program) "shown: $shown"
# Whether the program exists is read off the page's thread, so a problem would show a moment later.
$programProblems = @('Choose a program to run.', 'Enter the full path, such as C:\Program Files\App\App.exe.', 'Choose a program file (.exe).', "This program wasn't found.")
function Find-ProgramProblem { @($programProblems | Where-Object { Find-AppText $_ }) }
Test-Check 'a program that exists shows no problem below the box' (-not (Wait-Until { @(Find-ProgramProblem).Count -gt 0 } 3)) "shown: $(@(Find-ProgramProblem) -join ' | ')"
Test-Check 'the arguments box shows the arguments' ((Get-BoxText 'Top left arguments') -eq '/A')
Save-Screenshot '12-hot-corners-run-program'
# A path that doesn't fit shows its end after an ellipsis; the tooltip and the box while editing show all of it.
$programBox = Find-AppElement 'Top left program'
if ($programBox) {
    $bounds = $programBox.Current.BoundingRectangle
    [void] (Move-Pointer ([int]($bounds.X + $bounds.Width / 2)) ([int]($bounds.Y + $bounds.Height / 2)))
}
Test-Check 'the program box shows the whole path in its tooltip' (Wait-Until { Find-AppText $program } 5)
Save-Screenshot '12b-hot-corners-program-tooltip'
if ($programBox) { $programBox.SetFocus() }
Start-Sleep -Seconds 1
Save-Screenshot '12c-hot-corners-program-editing'
[void] (Move-Pointer $middleX $middleY)
$browse = Find-AppElement 'Top left program: Browse'
if ($browse) { $browse.SetFocus(); Start-Sleep -Milliseconds 300; Send-Key 0x20 }
Test-Check 'Browse opens the Open dialog' (Wait-Until { Find-AppDialog 'Choose a program' } 10)
Save-Screenshot '13-hot-corners-open-dialog'
Send-Key 0x1B
Test-Check 'the Open dialog closes' (Wait-Until { -not (Find-AppDialog 'Choose a program') } 10)
Test-Check 'cancelling the Open dialog keeps the program' ((Get-BoxText 'Top left program') -eq $program)
$running = (Get-Processes 'charmap').Count
[void] (Move-Pointer $middleX $middleY)
[void] (Move-IntoTopLeftCorner)
Test-Check 'Run a program starts the program' (Wait-Until { (Get-Processes 'charmap').Count -gt $running } 10) "charmap processes before: $running, after: $((Get-Processes 'charmap').Count); $(Get-PointerText)"
Test-Check 'the host logs the program by its file name' (Wait-Until { (Get-HostLog) -match 'Started charmap\.exe\.' } 5)
[void] (Move-Pointer $middleX $middleY)
Get-Processes 'charmap' | Stop-Process -Force -ErrorAction SilentlyContinue
[void] (Wait-Until { (Get-Processes 'charmap').Count -eq 0 } 10)

# A program that doesn't exist shows its problem below the box once the box is left. Whether the
# file exists is read off the page's thread, so the problem shows a moment later.
$missing = Join-Path $env:WINDIR 'System32\PCCompanionMissing.exe'
$programBox = Find-AppBox 'Top left program'
if ($programBox) { $programBox.SetFocus() }
[void] (Set-AppBox 'Top left program' $missing)
$argumentsBox = Find-AppBox 'Top left arguments'
if ($argumentsBox) { $argumentsBox.SetFocus() }
$savedProgram = { (Get-Content (Join-Path $dataFolder 'settings.json') -Raw | ConvertFrom-Json).features.hotCorners.topLeft.program }
Test-Check 'a program that doesn''t exist shows its problem below the box' (Wait-Until { (& $savedProgram) -eq $missing -and (Find-AppText "This program wasn't found.") } 10) "saved: $(& $savedProgram)"
Save-Screenshot '12d-hot-corners-program-missing'
# The program that exists again clears the problem: a late answer about the missing one is dropped.
if ($programBox) { $programBox.SetFocus() }
[void] (Set-AppBox 'Top left program' $program)
if ($argumentsBox) { $argumentsBox.SetFocus() }
Test-Check 'a program that exists again clears the problem below the box' (Wait-Until { (& $savedProgram) -eq $program -and @(Find-ProgramProblem).Count -eq 0 } 10) "saved: $(& $savedProgram); shown: $(@(Find-ProgramProblem) -join ' | ')"
Start-Sleep -Seconds 2
Test-Check 'the problem stays away' (@(Find-ProgramProblem).Count -eq 0) "shown: $(@(Find-ProgramProblem) -join ' | ')"
Save-Screenshot '12d2-hot-corners-program-found-again'

# With Hot Corners on and the page open, a host that no longer answers makes the page say, after a
# few reads, that recent results aren't available. It's the last check, since it ends the host.
Get-Processes 'PCCompanion.Host' | Stop-Process -Force -ErrorAction SilentlyContinue
$notAvailable = 'Recent results aren''t available right now. If corners don''t work either, restart PC Companion.'
Test-Check 'without the host, the page says that recent results aren''t available' (Wait-Until { (Get-WindowText) -contains $notAvailable } 10)
Save-Screenshot '12e-hot-corners-results-not-available'
