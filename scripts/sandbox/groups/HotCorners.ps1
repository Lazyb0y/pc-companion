# Hot Corners.

# 9. Hot Corners: the Home switch turns it on; a corner shows the desktop when the pointer reaches
#    it, not while a mouse button is held, and only with the key it requires.
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

# Turned off, Hot Corners stops reading the pointer.
Write-HotCornersSettings $false 'None'
Test-Check 'turning Hot Corners off stops it' ((Get-HostLog) -match 'Hot Corners stopped\.')
[void] (Move-Pointer $middleX $middleY)
[void] (Move-IntoTopLeftCorner)
Start-Sleep -Seconds 1
Test-Check 'a corner does nothing while Hot Corners is off' ((Get-MatchCount (Get-HostLog) $ran) -eq 2)
[void] (Move-Pointer $middleX $middleY)
Save-Screenshot '11-hot-corners-off'

# Run a program: the page shows the program, and Browse opens the Open dialog. Everything in the
# Sandbox runs as administrator, so the program must not start; the host logs why.
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
Test-Check 'Run a program logs that it doesn''t run as administrator' (Wait-Until { (Get-HostLog) -match "Run a program doesn't run while PC Companion runs as administrator\." } 5) (Get-PointerText)
Test-Check 'no program starts while PC Companion runs as administrator' ((Get-Processes 'charmap').Count -eq $running) "charmap processes before: $running, after: $((Get-Processes 'charmap').Count)"
[void] (Move-Pointer $middleX $middleY)
