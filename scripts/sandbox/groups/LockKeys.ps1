# Lock Keys, without pressing a lock key: Windows Sandbox copies its lock keys to the PC that runs
# it. Turned on from Home, the page shows its settings and saves each, and says when no key is
# chosen; Open opens Windows' keyboard accessibility settings. A change of the position or the
# duration while the feature is on shows the notice once, with Caps Lock's state: the check finds
# its window, with its name, at the top center and the bottom center of the work area, in the
# light and dark Windows modes and in Arabic, with screenshots, and gone after its time. A
# borderless window covering the display skips it, and the host logs why. Turned off, a change
# shows nothing. With the session's lock report, a change shows nothing until the unlock report.
# The lock keys stay as they were. Detailed logging is on for the Debug entries.
$detailedUntil = [DateTimeOffset]::Now.AddHours(1).ToString('o')
$noticeClass = 'PCCompanion.LockKeys.Notice'
$personalize = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize'
$lightModeAtStart = (Get-ItemProperty $personalize -Name SystemUsesLightTheme -ErrorAction SilentlyContinue).SystemUsesLightTheme
$capsAtStart = [Console]::CapsLock
$numAtStart = [Console]::NumberLock
$capsText = if ($capsAtStart) { 'Caps Lock on' } else { 'Caps Lock off' }
$work = [System.Windows.Forms.Screen]::PrimaryScreen.WorkingArea
$graphics = [System.Drawing.Graphics]::FromHwnd([IntPtr]::Zero)
$margin = [int] [Math]::Round(24 * $graphics.DpiX / 96)
$graphics.Dispose()

function Get-LockKeysSettings { (Get-Content (Join-Path $dataFolder 'settings.json') -Raw | ConvertFrom-Json).features.lockKeys }
function Find-Notice { [Win32]::FindWindow($noticeClass, [NullString]::Value) }
function Test-NoticeShown { $notice = Find-Notice; ($notice -ne [IntPtr]::Zero) -and [Win32]::IsWindowVisible($notice) }
function Get-NoticeBounds {
    $area = New-Object Win32+RECT
    if ([Win32]::GetWindowRect((Find-Notice), [ref] $area)) { $area } else { $null }
}
function Get-NoticeText([Win32+RECT] $Area) { "notice at $($Area.Left), $($Area.Top), $($Area.Right), $($Area.Bottom); work area $work; margin $margin" }

# The color just inside the notice's left edge, halfway down: its background.
function Get-NoticeBackground([Win32+RECT] $Area) {
    $bitmap = New-Object System.Drawing.Bitmap 1, 1
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.CopyFromScreen($Area.Left + 4, [int] (($Area.Top + $Area.Bottom) / 2), 0, 0, $bitmap.Size)
    $color = $bitmap.GetPixel(0, 0)
    $graphics.Dispose()
    $bitmap.Dispose()
    return $color
}

function Write-LockKeysSettings([bool] $Enabled, [string] $Position, [int] $Duration = 2000, [string] $Language = '') {
    $general = [ordered] @{ theme = 'Light' }
    if ($Language) { $general.language = $Language }
    $settings = [ordered] @{
        schemaVersion = 1
        general = $general
        updates = @{ checkAutomatically = $false }
        diagnostics = @{ detailedLoggingUntil = $detailedUntil }
        features = @{ lockKeys = [ordered] @{ enabled = $Enabled; position = $Position; durationMilliseconds = $Duration } }
    }
    Write-Settings ($settings | ConvertTo-Json -Depth 6)
}

# Waits for the sample notice after a change, then checks where it is and that it goes after its time.
function Test-SampleNotice([string] $When, [string] $Position, [string] $Screenshot, [bool] $Dark = $false) {
    $shown = Wait-Until { Test-NoticeShown } 10
    Test-Check "$When shows the notice" $shown
    if (-not $shown) { return }
    $shownAt = Get-Date
    Start-Sleep -Milliseconds 400
    $area = Get-NoticeBounds
    $middle = [int] (($area.Left + $area.Right) / 2)
    $centered = [Math]::Abs($middle - [int] ($work.Left + $work.Width / 2)) -le 1
    $placed = switch ($Position) {
        'TopCenter' { $centered -and ($area.Top -eq $work.Top + $margin) }
        'TopLeft' { ($area.Left -eq $work.Left + $margin) -and ($area.Top -eq $work.Top + $margin) }
        default { $centered -and ($area.Bottom -eq $work.Bottom - $margin) }
    }
    Test-Check "$When, the notice is at the chosen place in the work area" $placed (Get-NoticeText $area)
    $background = Get-NoticeBackground $area
    $looks = if ($Dark) { $background.R -lt 80 -and $background.G -lt 80 -and $background.B -lt 80 } else { $background.R -gt 200 -and $background.G -gt 200 -and $background.B -gt 200 }
    Test-Check "$When, the notice has the Windows mode's colors" $looks "background $background"
    Save-Screenshot $Screenshot
    $gone = Wait-Until { -not (Test-NoticeShown) } 10
    $seconds = ((Get-Date) - $shownAt).TotalSeconds
    Test-Check "$When, the notice goes after its time" ($gone -and $seconds -lt 8) "gone: $gone after $([Math]::Round($seconds, 1)) s"
}

# Turned on from Home, with its settings on the page.
[void] (Close-SettingsApp)
Write-Settings (@{ schemaVersion = 1; general = @{ theme = 'Light' }; updates = @{ checkAutomatically = $false }; diagnostics = @{ detailedLoggingUntil = $detailedUntil } } | ConvertTo-Json -Depth 4)
Start-Sleep -Seconds 2
Start-Process $appExe
[void] (Wait-Until { Get-AppWindow })
Start-Sleep -Seconds 3
Test-Check 'turn on Lock Keys on Home' (Set-Toggle 'Lock Keys' $true)
Start-Sleep -Seconds 2
$saved = Get-LockKeysSettings
Test-Check 'the Home switch saves Lock Keys as on' ($saved.enabled -eq $true) ($saved | ConvertTo-Json -Compress)
Test-Check 'the host starts Lock Keys' (Wait-Until { (Get-HostLog) -match 'Lock Keys started\.' } 15)
Test-Check 'turning it on shows no notice' (-not (Test-NoticeShown))
Test-Check 'select Lock Keys' (Select-NavigationItem 'Lock Keys')
Start-Sleep -Seconds 2
Save-Screenshot '17a-lock-keys-english'
$text = Get-WindowText
Test-Check 'Lock Keys shows its settings' ((@('Keys', 'Caps Lock', 'Num Lock', 'Scroll Lock', 'Show when a key turns off', 'Position', 'Show for', '2 s', "Don't show while an app is full screen", 'Keep showing while Caps Lock is on', 'Sounds and screen readers', 'Permissions & data') | Where-Object { $text -notcontains $_ }).Count -eq 0) ($text -join ' | ')
$selected = Get-ListSelection 'Position'
Test-Check 'the position starts at the bottom center' ($selected -eq 'Bottom center') "selected: $selected"

# The switches save, and with no key chosen the page says so.
Test-Check 'turn on Scroll Lock' (Set-Toggle 'Scroll Lock' $true)
Start-Sleep -Seconds 1
Test-Check 'the page saves Scroll Lock' ((Get-LockKeysSettings).scrollLock -eq $true)
foreach ($key in 'Caps Lock', 'Num Lock', 'Scroll Lock') { [void] (Set-Toggle $key $false) }
Start-Sleep -Seconds 1
$saved = Get-LockKeysSettings
Test-Check 'with no key chosen, the page says no notices show' ((-not $saved.capsLock) -and (-not $saved.numLock) -and (-not $saved.scrollLock) -and (Wait-Until { Find-AppText 'No key is chosen, so no notices show.' } 3)) ($saved | ConvertTo-Json -Compress)
foreach ($key in 'Caps Lock', 'Num Lock') { [void] (Set-Toggle $key $true) }
Start-Sleep -Seconds 1
Test-Check 'with a key chosen, the note goes' (-not (Find-AppText 'No key is chosen, so no notices show.'))
foreach ($switch in @(@{ Name = 'Show when a key turns off'; Property = 'showWhenOff'; Default = $true }, @{ Name = "Don't show while an app is full screen"; Property = 'ignoreFullScreenApps'; Default = $true }, @{ Name = 'Keep showing while Caps Lock is on'; Property = 'keepCapsLockBadge'; Default = $false })) {
    [void] (Set-Toggle $switch.Name (-not $switch.Default))
    Start-Sleep -Seconds 1
    $changed = (Get-LockKeysSettings).($switch.Property) -eq (-not $switch.Default)
    [void] (Set-Toggle $switch.Name $switch.Default)
    Start-Sleep -Seconds 1
    Test-Check "the page saves $($switch.Name)" ($changed -and ((Get-LockKeysSettings).($switch.Property) -eq $switch.Default))
}

# A new position shows the notice once, there, with Caps Lock's state as its name.
Test-Check 'choose Top center' (Select-ListItem 'Position' 'Top center')
Start-Sleep -Seconds 1
Test-Check 'the page saves the position' ((Get-LockKeysSettings).position -eq 'TopCenter')
Test-SampleNotice 'a new position' 'TopCenter' '17b-lock-keys-notice-top-light'
$title = [Win32]::WindowTitle((Find-Notice))
Test-Check 'the notice is named after Caps Lock''s state' ($title -eq $capsText) "name: $title"
Test-Check 'the host logs the notice' (Wait-Until { (Get-HostLog) -match 'Lock Keys showed its notice after a settings change\.' } 5)

# The duration's slider, named after its header, saves a new duration and shows the notice at the bottom.
Test-Check 'choose Bottom center' (Select-ListItem 'Position' 'Bottom center')
Test-SampleNotice 'the bottom center' 'BottomCenter' '17c-lock-keys-notice-bottom-light'
$sliderCondition = New-Object System.Windows.Automation.AndCondition(
    (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, 'Show for')),
    (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::IsRangeValuePatternAvailableProperty, $true)))
$slider = (Get-AppWindow).FindFirst([System.Windows.Automation.TreeScope]::Descendants, $sliderCondition)
$range = if ($slider) { $slider.GetCurrentPattern([System.Windows.Automation.RangeValuePattern]::Pattern) } else { $null }
Test-Check 'the duration starts at 2 seconds' (($null -ne $range) -and ($range.Current.Value -eq 2000)) "slider: $(if ($range) { $range.Current.Value } else { 'not found' })"
if ($range) { $range.SetValue(1500) }
Start-Sleep -Seconds 1
Test-Check 'the duration slider saves 1.5 seconds and shows it' (((Get-LockKeysSettings).durationMilliseconds -eq 1500) -and ((Get-WindowText) -contains '1.5 s')) "saved: $((Get-LockKeysSettings).durationMilliseconds)"
Test-SampleNotice 'a new duration' 'BottomCenter' '17d-lock-keys-notice-duration'

# Open opens Windows' keyboard accessibility settings.
Test-Check 'Open opens the keyboard accessibility settings' ((Invoke-AppElement 'Open') -and (Wait-Until { Get-Processes 'SystemSettings' } 15))
Start-Sleep -Seconds 2
Get-Processes 'SystemSettings' | Stop-Process -Force -ErrorAction SilentlyContinue
[void] (Close-SettingsApp)

# The dark Windows mode, read each time a notice shows.
Set-ItemProperty $personalize -Name SystemUsesLightTheme -Value 0 -Type DWord
Write-LockKeysSettings $true 'TopLeft'
Test-SampleNotice 'in the dark Windows mode' 'TopLeft' '17e-lock-keys-notice-dark' -Dark $true
Set-ItemProperty $personalize -Name SystemUsesLightTheme -Value $(if ($null -eq $lightModeAtStart) { 1 } else { $lightModeAtStart }) -Type DWord

# Arabic: the host follows the language, and the notice is mirrored, with the key's name in it.
Write-LockKeysSettings $true 'TopLeft' 2000 'ar'
Start-Sleep -Seconds 3
Write-LockKeysSettings $true 'TopCenter' 2000 'ar'
Test-SampleNotice 'in Arabic' 'TopCenter' '17f-lock-keys-notice-arabic'
$title = [Win32]::WindowTitle((Find-Notice))
Test-Check 'in Arabic, the notice is named in Arabic with the key''s name' (($title -like '*Caps Lock*') -and ($title -ne $capsText) -and ($title -match '[؀-ۿ]')) "name: $title"

# With the session's lock report sent to the feature's window, as Windows sends it, a change shows
# nothing; after the unlock report, the lock keys are read again without a notice.
$lockKeysWindow = [Win32]::FindWindow('PCCompanion.LockKeys', [NullString]::Value)
Test-Check 'the feature has its hidden window' ($lockKeysWindow -ne [IntPtr]::Zero)
$samples = Get-MatchCount (Get-HostLog) 'Lock Keys showed its notice after a settings change\.'
[void] [Win32]::PostMessage($lockKeysWindow, 0x02B1, [UIntPtr] [uint32] 7, [IntPtr]::Zero)
Test-Check 'the lock report pauses Lock Keys' (Wait-Until { (Get-HostLog) -match 'Lock Keys paused: "?Locked"?\.' } 5)
Write-LockKeysSettings $true 'TopLeft'
Start-Sleep -Seconds 3
Test-Check 'while locked, a change shows no notice' ((-not (Test-NoticeShown)) -and ((Get-MatchCount (Get-HostLog) 'Lock Keys showed its notice after a settings change\.') -eq $samples))
[void] [Win32]::PostMessage($lockKeysWindow, 0x02B1, [UIntPtr] [uint32] 8, [IntPtr]::Zero)
Test-Check 'after the unlock report, the lock keys are read again without a notice' ((Wait-Until { (Get-HostLog) -match 'Lock Keys read the lock keys again without a notice: "?Locked"? ended\.' } 5) -and -not (Test-NoticeShown))
# A borderless window covering the display skips the notice, and the host logs why.
$window = Start-TestWindow 'FullScreen'
$found = Find-TestWindow 'FullScreen'
$area = New-Object Win32+RECT
$covers = ($found -ne [IntPtr]::Zero) -and [Win32]::GetWindowRect($found, [ref] $area) -and ($area.Left -le 0) -and ($area.Top -le 0) -and ($area.Right -ge $screen.Width) -and ($area.Bottom -ge $screen.Height)
Test-Check 'the full-screen test window covers the display' $covers "window $found at $($area.Left), $($area.Top), $($area.Right), $($area.Bottom); display $($screen.Width) x $($screen.Height)"
Write-LockKeysSettings $true 'BottomCenter'
Test-Check 'a full-screen app skips the notice, and the host logs why' (Wait-Until { (Get-HostLog) -match '"?CapsLock"? changed, now on: (true|false); no notice, since an app was full screen\.' } 10)
Test-Check 'no notice shows over the full-screen window' (-not (Test-NoticeShown))
Stop-Process -Id $window.Id -Force -ErrorAction SilentlyContinue
[void] (Wait-Until { (Find-TestWindow 'FullScreen') -eq [IntPtr]::Zero } 10)

# Turned off, a change shows nothing.
Write-LockKeysSettings $false 'BottomCenter'
Test-Check 'the host stops Lock Keys' (Wait-Until { (Get-HostLog) -match 'Lock Keys stopped\.' } 10)
Write-LockKeysSettings $false 'TopCenter'
Start-Sleep -Seconds 3
Test-Check 'turned off, a change shows no notice' ((Find-Notice) -eq [IntPtr]::Zero)

Test-Check 'the lock keys are as they were' (([Console]::CapsLock -eq $capsAtStart) -and ([Console]::NumberLock -eq $numAtStart)) "Caps Lock $([Console]::CapsLock), Num Lock $([Console]::NumberLock)"
