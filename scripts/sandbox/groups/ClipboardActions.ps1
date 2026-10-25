# Clipboard Actions: the Home switch turns it on, and its page adds, edits, moves and deletes rules
# in the rule editor, which says what's wrong with a pattern, an address or a program. Then, with
# rules in the settings, Windows + Alt + V, sent by scan code, opens the menu at the pointer, below
# the text cursor of Character Map's box or of a text area in Edge, and above it at the bottom of
# the screen. For text the checks copy, Open in browser and an address rule open the browser, and a
# rule that runs a program starts Character Map; marked-private, missing and too long text show
# only a note. Esc gives the front back to the window that had it,
# the shortcut again closes the menu, and turned off the shortcut opens no menu. The host's
# --open-web-page opens a long web address it reads from a pipe, and only web addresses. Detailed logging is on for the host's Debug entries.
[void] (Close-SettingsApp)
$detailedUntil = [DateTimeOffset]::Now.AddHours(1).ToString('o')
Start-Sleep -Seconds 2
Start-Process $appExe
[void] (Wait-Until { Get-AppWindow })
Start-Sleep -Seconds 3
function Get-ClipboardActionsSettings { (Get-Content (Join-Path $dataFolder 'settings.json') -Raw | ConvertFrom-Json).features.clipboardActions }

Test-Check 'turn on Clipboard Actions on Home' (Set-Toggle 'Clipboard Actions' $true)
Start-Sleep -Seconds 2
$saved = Get-ClipboardActionsSettings
Test-Check 'the Home switch saves Clipboard Actions as on' ($saved.enabled -eq $true) "saved: $($saved | ConvertTo-Json -Compress)"
Test-Check 'select Clipboard Actions' (Select-NavigationItem 'Clipboard Actions')
Start-Sleep -Seconds 2
Save-Screenshot '13d-clipboard-actions-english'
$text = Get-WindowText
Test-Check 'Clipboard Actions shows its settings' (($text -contains 'Shortcut') -and ($text -contains 'Windows logo key + Alt + V') -and ($text -contains 'Open copied links') -and ($text -contains 'Rules') -and ($text -contains 'Permissions & data')) ($text -join ' | ')
Test-Check 'the page says there are no rules yet' (@($text | Where-Object { $_ -like 'No rules yet.*' }).Count -eq 1)
$openLinks = Find-Toggle 'Open copied links'
Test-Check 'Open copied links is on' (($null -ne $openLinks) -and $openLinks.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern).Current.ToggleState -eq 'On')

# A new rule: Save is off until the rule can be used, Try it says whether the pattern matches.
Test-Check 'Add rule opens the rule editor' ((Invoke-AppElement 'Add rule') -and (Wait-Until { Find-AppElement 'Add a rule' } 5))
Start-Sleep -Seconds 1
Test-Check 'Save is off for an empty rule' (-not (Test-AppEnabled 'Save'))
[void] (Set-AppBox 'Name' 'Track the parcel')
$patternProblem = 'The pattern uses a lookaround, a backreference, an atomic group or a conditional, which can''t be used here.'
Start-Announcements
[void] (Set-AppBox 'Pattern' '(?=1Z)1Z.+')
Test-Check 'a pattern with a lookahead is refused' (Wait-Until { Find-AppText $patternProblem } 3)
Test-Check 'screen readers announce the problem once, and the box reads it' (Test-Announced 'Pattern' $patternProblem) ((Get-Announcements) -join ' | ')
Stop-Announcements
[void] (Set-AppBox 'Pattern' '1Z[0-9A-Z]{16}')
[void] (Set-AppBox 'Try it' '1Z999AA10123456784')
Test-Check 'Try it says the pattern matches' (Wait-Until { Find-AppText 'The pattern matches this text.' } 3)
[void] (Set-AppBox 'Web address' 'file:///C:/{text}')
Test-Check 'an address that isn''t a web address is refused' ((Wait-Until { Find-AppText 'The address must start with https:// or http://.' } 3) -and -not (Test-AppEnabled 'Save'))
[void] (Set-AppBox 'Web address' 'https://tracking.example.com/?number={text}')
Test-Check 'Save is on once the rule can be used' (Wait-Until { Test-AppEnabled 'Save' } 3)
Save-Screenshot '13e-clipboard-actions-rule-editor'
Test-Check 'Save the rule' (Invoke-AppElement 'Save')
Start-Sleep -Seconds 2
$saved = Get-ClipboardActionsSettings
$rule = @($saved.rules)[0]
Test-Check 'the page saves the rule' ((@($saved.rules).Count -eq 1) -and ($rule.name -eq 'Track the parcel') -and ($rule.pattern -eq '1Z[0-9A-Z]{16}') -and ($rule.action -eq 'OpenAddress') -and ($rule.address -eq 'https://tracking.example.com/?number={text}')) "saved: $($saved | ConvertTo-Json -Compress -Depth 5)"
Test-Check 'the rule shows what it does' (Wait-Until { Find-AppText 'Opens tracking.example.com' } 3)

# A rule that runs a program, which refuses command interpreters.
Test-Check 'Add rule opens the rule editor again' ((Invoke-AppElement 'Add rule') -and (Wait-Until { Find-AppElement 'Add a rule' } 5))
Start-Sleep -Seconds 1
[void] (Set-AppBox 'Name' 'Show in Character Map')
[void] (Set-AppBox 'Pattern' '.+\.txt')
Test-Check 'choose Run a program' (Invoke-AppElement 'Run a program')
Start-Sleep -Milliseconds 500
[void] (Set-AppBox 'Program' 'C:\Windows\System32\cmd.exe')
Test-Check 'a command interpreter is refused' ((Wait-Until { Find-AppText 'Programs that run commands, such as cmd or PowerShell, can''t be used: copied text could make them run other commands.' } 3) -and -not (Test-AppEnabled 'Save'))
# Whether the program exists is read off the page's thread, so the problem shows a moment later.
[void] (Set-AppBox 'Program' 'C:\Windows\System32\PCCompanionMissing.exe')
Test-Check 'a program that doesn''t exist is refused' ((Wait-Until { Find-AppText "This program wasn't found." } 5) -and -not (Test-AppEnabled 'Save'))
[void] (Set-AppBox 'Program' 'C:\Windows\System32\charmap.exe')
Test-Check 'a program that exists clears the problem' (Wait-Until { -not (Find-AppText "This program wasn't found.") } 5)
[void] (Set-AppBox 'Arguments' '{text}')
Test-Check 'Save is on for the program rule' (Wait-Until { Test-AppEnabled 'Save' } 3)
Start-Sleep -Seconds 2
Test-Check 'the problem stays away and Save stays on' ((-not (Find-AppText "This program wasn't found.")) -and (Test-AppEnabled 'Save'))
[void] (Invoke-AppElement 'Save')
Start-Sleep -Seconds 2
$saved = Get-ClipboardActionsSettings
Test-Check 'the page saves the program rule after the first' ((@($saved.rules).Count -eq 2) -and (@($saved.rules)[1].action -eq 'RunProgram') -and (@($saved.rules)[1].program -eq 'C:\Windows\System32\charmap.exe')) "saved: $($saved | ConvertTo-Json -Compress -Depth 5)"
# The list looks for the program off its thread; one that exists keeps the rule's summary.
Test-Check 'a rule whose program exists says what it starts' ((Find-AppText 'Starts charmap.exe') -and -not (Find-AppText "Not in the menu. This program wasn't found."))

# Edit, move and delete.
Test-Check 'Edit opens the rule' ((Invoke-AppElement 'Edit Track the parcel') -and (Wait-Until { Find-AppElement 'Edit the rule' } 5))
Start-Sleep -Seconds 1
Test-Check 'the editor shows the rule' ((Get-AppBox 'Name') -eq 'Track the parcel') "name: $(Get-AppBox 'Name')"
[void] (Set-AppBox 'Name' 'Track & trace')
[void] (Invoke-AppElement 'Save')
Start-Sleep -Seconds 2
Test-Check 'the page saves the edited name' ((@((Get-ClipboardActionsSettings).rules)[0].name) -eq 'Track & trace')
Test-Check 'More options opens the rule''s menu' (Invoke-AppElement 'More options for Track & trace')
Start-Sleep -Seconds 1
Test-Check 'Move down moves the rule' (Invoke-AppElement 'Move down')
Start-Sleep -Seconds 2
Test-Check 'the page saves the new order' ((@((Get-ClipboardActionsSettings).rules)[0].name) -eq 'Show in Character Map')
[void] (Invoke-AppElement 'More options for Show in Character Map')
Start-Sleep -Seconds 1
Test-Check 'Delete asks first' ((Invoke-AppElement 'Delete') -and (Wait-Until { Find-AppElement 'Delete Show in Character Map?' } 5))
Start-Sleep -Seconds 1
[void] (Invoke-AppElement 'Delete')
Start-Sleep -Seconds 2
$saved = Get-ClipboardActionsSettings
Test-Check 'the page deletes the rule' ((@($saved.rules).Count -eq 1) -and (@($saved.rules)[0].name -eq 'Track & trace')) "saved: $($saved | ConvertTo-Json -Compress -Depth 5)"
[void] (Close-SettingsApp)

# A rule whose program doesn't exist, as after the program was removed, shows why in the list once
# the page has looked for the program off its thread.
$missingRule = @{ name = 'Missing program'; pattern = '.+'; action = 'RunProgram'; program = 'C:\Windows\System32\PCCompanionMissing.exe'; arguments = '{text}' }
$settings = @{ schemaVersion = 1; general = @{ theme = 'Light' }; features = @{ clipboardActions = @{ enabled = $true; openLinks = $true; rules = @($missingRule) } } }
Write-Settings ($settings | ConvertTo-Json -Depth 6)
Start-Sleep -Seconds 2
Start-Process $appExe
[void] (Wait-Until { Get-AppWindow })
Start-Sleep -Seconds 3
Test-Check 'select Clipboard Actions with a rule whose program doesn''t exist' (Select-NavigationItem 'Clipboard Actions')
Test-Check 'a rule whose program doesn''t exist shows why in the list' (Wait-Until { Find-AppText "Not in the menu. This program wasn't found." } 5)
Save-Screenshot '13e-clipboard-actions-missing-program'
[void] (Close-SettingsApp)

# The menu, with the rules in the settings file.
function Write-ClipboardSettings([bool] $Enabled) {
    $section = @{
        enabled = $Enabled
        openLinks = $true
        rules = @(
            @{ name = 'Track the parcel'; pattern = '1Z[0-9A-Z]{16}'; action = 'OpenAddress'; address = 'https://tracking.example.com/?number={text}' },
            @{ name = 'Show in Character Map'; pattern = '.+\.txt'; action = 'RunProgram'; program = 'C:\Windows\System32\charmap.exe'; arguments = '{text}' },
            @{ name = 'Broken'; pattern = '[0-9'; address = 'https://example.com/{text}' }
        )
    }
    $settings = @{ schemaVersion = 1; general = @{ theme = 'Light' }; diagnostics = @{ detailedLoggingUntil = $detailedUntil }; features = @{ clipboardActions = $section } }
    Write-Settings ($settings | ConvertTo-Json -Depth 6)
    Start-Sleep -Seconds 3
}
function Copy-Text([string] $Text, [string] $Mark) {
    $data = New-Object System.Windows.Forms.DataObject
    if ($null -ne $Text) { $data.SetText($Text) }
    switch ($Mark) {
        'exclude' { $data.SetData('ExcludeClipboardContentFromMonitorProcessing', $false, (New-Object IO.MemoryStream (, [byte[]] @(0)))) }
        'history0' { $data.SetData('CanIncludeInClipboardHistory', $false, (New-Object IO.MemoryStream (, [byte[]] @(0, 0, 0, 0)))) }
        'viewer' { $data.SetData('Clipboard Viewer Ignore', $false, (New-Object IO.MemoryStream (, [byte[]] @(0)))) }
    }
    [System.Windows.Forms.Clipboard]::SetDataObject($data, $true)
    # The Remote Desktop clipboard reads new data at once.
    Start-Sleep -Milliseconds 700
}
function Clear-Clipboard { [System.Windows.Forms.Clipboard]::Clear(); Start-Sleep -Milliseconds 700 }
function Press-Shortcut {
    [void] [Win32]::ScanKey(0x5B, $false, $true); [void] [Win32]::ScanKey(0x38, $false, $false); [void] [Win32]::ScanKey(0x2F, $false, $false)
    Start-Sleep -Milliseconds 60
    [void] [Win32]::ScanKey(0x2F, $true, $false); [void] [Win32]::ScanKey(0x38, $true, $false); [void] [Win32]::ScanKey(0x5B, $true, $true)
}
$menuCondition = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ClassNameProperty, '#32768')
$itemCondition = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::MenuItem)
function Get-Menu { [System.Windows.Automation.AutomationElement]::RootElement.FindFirst([System.Windows.Automation.TreeScope]::Children, $menuCondition) }
# The menu's items, as "name" or "name (dimmed)", with the embedding characters around the preview left out.
function Get-MenuItems {
    $menu = Get-Menu
    if (-not $menu) { return 'no menu' }
    (@($menu.FindAll([System.Windows.Automation.TreeScope]::Descendants, $itemCondition)) | ForEach-Object {
        $name = $_.Current.Name -replace '[\u202A-\u202C]', ''
        if ($_.Current.IsEnabled) { $name } else { "$name (dimmed)" }
    }) -join ' | '
}
function Open-Menu {
    Press-Shortcut
    $opened = Wait-Until { Get-Menu } 5
    Start-Sleep -Milliseconds 500
    return $opened
}
function Close-Menu { if (Get-Menu) { Send-Key 0x1B; Start-Sleep -Milliseconds 500 } }
function Test-CharmapInFront { [Win32]::GetForegroundWindow() -eq $charmap }
function Test-Within([double] $Value, [double] $Low, [double] $High) { ($Value -ge $Low) -and ($Value -le $High) }
# Where the last menu opened, from the host's Debug entry for the press, such as AtTextCursor.
function Get-LastMenuPlace {
    Start-Sleep -Milliseconds 300
    $places = [regex]::Matches((Get-HostLog), 'menu: "?(\w+)"?\.')
    if ($places.Count -gt 0) { $places[$places.Count - 1].Groups[1].Value } else { 'none' }
}

Write-ClipboardSettings $true
if ((Get-Processes 'PCCompanion.Host').Count -eq 0) { Start-Process $hostExe }
Test-Check 'the host uses the rules it can' (Wait-Until { (Get-HostLog) -match '2 of 3 rules can be used' } 10)
Test-Check 'the host leaves out the rule it can''t use, by number' ((Get-HostLog) -match 'Clipboard Actions rule 3 can''t be used: "?InvalidPattern"?\.')
$charmapProcess = Start-Process charmap.exe -PassThru
Test-Check 'Character Map opens to be the window in front' (Wait-Until { $charmapProcess.Refresh(); $charmapProcess.MainWindowHandle -ne [IntPtr]::Zero } 10)
$charmap = $charmapProcess.MainWindowHandle
[void] [Win32]::SetForegroundWindow($charmap)
[void] (Move-Pointer 700 400)

Copy-Text 'https://example.com/copied/link'
Test-Check 'the shortcut opens the menu for a copied link' (Open-Menu)
$items = Get-MenuItems
Test-Check 'the menu shows the link, Open in browser and Settings' ($items -eq 'https://example.com/copied/link (dimmed) | Open in browser | Settings') $items
$menuBounds = (Get-Menu).Current.BoundingRectangle
Test-Check 'the menu opens at the pointer' ((([Math]::Abs($menuBounds.X - 700) -le 10) -or ([Math]::Abs($menuBounds.Right - 700) -le 10)) -and (([Math]::Abs($menuBounds.Y - 400) -le 10) -or ([Math]::Abs($menuBounds.Bottom - 400) -le 10))) "menu at $menuBounds"
Save-Screenshot '13f-clipboard-actions-menu'
Send-Key 0x1B
Start-Sleep -Milliseconds 500
Test-Check 'Esc closes the menu' (-not (Get-Menu))
Test-Check 'the window in front gets the front back' (Test-CharmapInFront) "front: $([Win32]::WindowClass([Win32]::GetForegroundWindow()))"
Test-Check 'with the character grid focused, the host logs the menu at the pointer' ((Get-LastMenuPlace) -eq 'AtPointer') "menu: $(Get-LastMenuPlace)"

# At the text cursor: below the line of Character Map's Characters to copy box, which uses Windows'
# text cursor, and above it with the box at the bottom of the screen.
$editCondition = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Edit)
$charmapBox = [System.Windows.Automation.AutomationElement]::FromHandle($charmap).FindFirst([System.Windows.Automation.TreeScope]::Descendants, $editCondition)
Test-Check 'Character Map has its Characters to copy box' ($null -ne $charmapBox)
$charmapBox.SetFocus()
Start-Sleep -Milliseconds 500
$boxBounds = $charmapBox.Current.BoundingRectangle
Test-Check 'the menu opens with the box focused' (Open-Menu)
$menuBounds = (Get-Menu).Current.BoundingRectangle
Test-Check 'the menu opens below the box''s text cursor, not at the pointer' ((Test-Within $menuBounds.Y $boxBounds.Y $boxBounds.Bottom) -and ((Test-Within $menuBounds.X $boxBounds.X $boxBounds.Right) -or (Test-Within $menuBounds.Right $boxBounds.X $boxBounds.Right))) "menu at $menuBounds, box at $boxBounds"
Test-Check 'the host logs the menu at Windows'' text cursor' ((Get-LastMenuPlace) -eq 'AtTextCursor') "menu: $(Get-LastMenuPlace)"
Save-Screenshot '13g-clipboard-actions-menu-at-text-cursor'
Close-Menu
Test-Check 'Esc gives the front back to Character Map' (Test-CharmapInFront)
$charmapRect = New-Object Win32+RECT
[void] [Win32]::GetWindowRect($charmap, [ref] $charmapRect)
$charmapWidth = $charmapRect.Right - $charmapRect.Left
$charmapHeight = $charmapRect.Bottom - $charmapRect.Top
# Just above the taskbar, with less room below the line than the menu's height.
$lowTop = $screen.Height - 50 - ([int] $boxBounds.Bottom - $charmapRect.Top)
[void] [Win32]::MoveWindow($charmap, $charmapRect.Left, $lowTop, $charmapWidth, $charmapHeight, $true)
Start-Sleep -Milliseconds 500
$boxBounds = $charmapBox.Current.BoundingRectangle
[void] (Open-Menu)
$menuBounds = (Get-Menu).Current.BoundingRectangle
Test-Check 'at the bottom of the screen, the menu opens above the box''s line' (($menuBounds.Bottom -le $boxBounds.Y + ($boxBounds.Height / 2)) -and ($menuBounds.Bottom -ge $boxBounds.Y - 10)) "menu at $menuBounds, box at $boxBounds"
Close-Menu
[void] [Win32]::MoveWindow($charmap, $charmapRect.Left, $charmapRect.Top, $charmapWidth, $charmapHeight, $true)
Start-Sleep -Milliseconds 500

# Edge draws its own text cursor and reports it to accessibility tools only: below the text cursor of
# a page's text area, and at the pointer with the page's plain text clicked.
$policy = 'HKLM:\SOFTWARE\Policies\Microsoft\Edge'
New-Item -Path $policy -Force | Out-Null
Set-ItemProperty -Path $policy -Name HideFirstRunExperience -Value 1 -Type DWord
$page = Join-Path $env:TEMP 'text-cursor.html'
Set-Content -Path $page -Encoding utf8 -Value @'
<!doctype html><html><head><meta charset="utf-8"><title>Text cursor</title></head><body style="margin:0">
<textarea autofocus style="position:fixed;left:0;top:0;width:100vw;height:50vh;margin:0;border:0;padding:8px;font-size:20px"></textarea>
<div style="position:fixed;left:0;top:55vh;width:100vw;height:45vh;font-size:32px">Plain text on the page, which has no text cursor. Plain text on the page, which has no text cursor. Plain text on the page.</div>
</body></html>
'@
$edgeExe = "${env:ProgramFiles(x86)}\Microsoft\Edge\Application\msedge.exe"
Start-Process $edgeExe -ArgumentList @('--no-first-run', '--no-default-browser-check', '--new-window', ([Uri] $page).AbsoluteUri)
$script:edgeWindow = [IntPtr]::Zero
$edgeOpened = Wait-Until {
    $script:edgeWindow = [Win32]::TopLevelWindows() | Where-Object { [Win32]::IsWindowVisible($_) -and ([Win32]::WindowClass($_) -eq 'Chrome_WidgetWin_1') -and ([Win32]::WindowTitle($_) -like 'Text cursor*') } | Select-Object -First 1
    $null -ne $script:edgeWindow
} 60
Test-Check 'Edge opens the page with a text area' $edgeOpened
if ($edgeOpened) {
    Start-Sleep -Seconds 3
    [void] [Win32]::SetForegroundWindow($edgeWindow)
    $edgeRect = New-Object Win32+RECT
    [void] [Win32]::GetWindowRect($edgeWindow, [ref] $edgeRect)
    $middleX = [int] (($edgeRect.Left + $edgeRect.Right) / 2)
    [void] (Move-Pointer $middleX ([int] ($edgeRect.Top + ($edgeRect.Bottom - $edgeRect.Top) * 0.35)))
    [void] [Win32]::LeftButton($true); [void] [Win32]::LeftButton($false)
    Start-Sleep -Milliseconds 500
    Test-Check 'the menu opens in Edge' (Open-Menu)
    $menuBounds = (Get-Menu).Current.BoundingRectangle
    Test-Check 'the menu opens below the text area''s text cursor, at its start, not at the pointer' ((Test-Within $menuBounds.X $edgeRect.Left ($edgeRect.Left + 80)) -and (Test-Within $menuBounds.Y ($edgeRect.Top + 40) ($edgeRect.Top + 200))) "menu at $menuBounds, Edge at $($edgeRect.Left),$($edgeRect.Top),$($edgeRect.Right),$($edgeRect.Bottom), $(Get-PointerText)"
    Test-Check 'the host logs the menu at Edge''s accessibility text cursor' ((Get-LastMenuPlace) -eq 'AtAppTextCursor') "menu: $(Get-LastMenuPlace)"
    Save-Screenshot '13h-clipboard-actions-menu-at-edge-text-cursor'
    Close-Menu
    Test-Check 'Esc gives the front back to Edge' ([Win32]::GetForegroundWindow() -eq $edgeWindow) "front: $([Win32]::WindowClass([Win32]::GetForegroundWindow()))"
    $textY = [int] ($edgeRect.Top + ($edgeRect.Bottom - $edgeRect.Top) * 0.75)
    [void] (Move-Pointer ($edgeRect.Left + 120) $textY)
    [void] [Win32]::LeftButton($true); [void] [Win32]::LeftButton($false)
    Start-Sleep -Milliseconds 500
    [void] (Open-Menu)
    $menuBounds = (Get-Menu).Current.BoundingRectangle
    Test-Check 'with plain text clicked, the menu opens at the pointer' ((Get-LastMenuPlace) -eq 'AtPointer') "menu: $(Get-LastMenuPlace), menu at $menuBounds, $(Get-PointerText)"
    Close-Menu
}
Get-Processes 'msedge' | ForEach-Object { Stop-Process -Id $_.Id -Force -ErrorAction SilentlyContinue }
Start-Sleep -Seconds 2
[void] [Win32]::SetForegroundWindow($charmap)
[void] (Move-Pointer 700 400)

$edgeBefore = (Get-Processes 'msedge').Count
Test-Check 'the menu opens again' (Open-Menu)
Test-Check 'Open in browser opens the browser' ((Select-TrayMenuItem 'Open in browser') -and (Wait-Until { (Get-Processes 'msedge').Count -gt $edgeBefore } 20))
Test-Check 'the host logs the opened link without it' (Wait-Until { (Get-HostLog) -match 'Clipboard Actions opened the address of rule 0' } 5)
Start-Sleep -Seconds 3
Get-Processes 'msedge' | ForEach-Object { Stop-Process -Id $_.Id -Force -ErrorAction SilentlyContinue }
Start-Sleep -Seconds 2
[void] [Win32]::SetForegroundWindow($charmap)

Copy-Text "  1Z999AA10123456784`r`n"
Test-Check 'the shortcut opens the menu for a parcel number' (Open-Menu)
$items = Get-MenuItems
Test-Check 'the menu offers the matching rule' ($items -eq '1Z999AA10123456784 (dimmed) | Track the parcel | Settings') $items
$edgeBefore = (Get-Processes 'msedge').Count
Test-Check 'the rule opens its address in the browser' ((Select-TrayMenuItem 'Track the parcel') -and (Wait-Until { (Get-Processes 'msedge').Count -gt $edgeBefore } 20))
Test-Check 'the host logs the rule by number' (Wait-Until { (Get-HostLog) -match 'Clipboard Actions opened the address of rule 1' } 5)
$edgeCommandLines = (Get-CimInstance Win32_Process -Filter "Name = 'msedge.exe'" | ForEach-Object { $_.CommandLine }) -join ' '
Test-Check 'the browser got the address with the number' ($edgeCommandLines -match 'tracking\.example\.com/\?number=1Z999AA10123456784') $edgeCommandLines
Start-Sleep -Seconds 2
Get-Processes 'msedge' | ForEach-Object { Stop-Process -Id $_.Id -Force -ErrorAction SilentlyContinue }
Start-Sleep -Seconds 2
[void] [Win32]::SetForegroundWindow($charmap)

Copy-Text 'notes.txt'
Test-Check 'the shortcut opens the menu for a file name' (Open-Menu)
$items = Get-MenuItems
Test-Check 'the menu offers the rule that runs a program' ($items -eq 'notes.txt (dimmed) | Show in Character Map | Settings') $items
$charmapBefore = @(Get-Processes 'charmap' | ForEach-Object { $_.Id })
Test-Check 'the rule starts its program' ((Select-TrayMenuItem 'Show in Character Map') -and (Wait-Until { @(Get-Processes 'charmap').Count -gt $charmapBefore.Count } 10)) "charmap processes before: $($charmapBefore.Count), after: $(@(Get-Processes 'charmap').Count)"
Test-Check 'the host logs the program by its file name' (Wait-Until { (Get-HostLog) -match 'Clipboard Actions rule 2 started charmap\.exe\.' } 5)
Get-Processes 'charmap' | Where-Object { $charmapBefore -notcontains $_.Id } | ForEach-Object { Stop-Process -Id $_.Id -Force -ErrorAction SilentlyContinue }
Start-Sleep -Seconds 2
[void] [Win32]::SetForegroundWindow($charmap)

Copy-Text 'PC-12'
[void] (Open-Menu)
$items = Get-MenuItems
Test-Check 'text that matches no rule shows a note' ($items -eq 'PC-12 (dimmed) | No actions for the copied text (dimmed) | Settings') $items
Close-Menu

foreach ($mark in 'exclude', 'history0', 'viewer') {
    Copy-Text 'secret-4711' $mark
    [void] (Open-Menu)
    $items = Get-MenuItems
    Test-Check "text marked private ($mark) isn't shown" ($items -eq 'The copied text is private (dimmed) | Settings') $items
    Close-Menu
}

Copy-Text ('a' * 5000)
[void] (Open-Menu)
$items = Get-MenuItems
Test-Check 'text longer than 4,096 characters shows a note' ($items -eq 'The copied text is too long (dimmed) | Settings') $items
Close-Menu

Clear-Clipboard
[void] (Open-Menu)
$items = Get-MenuItems
Test-Check 'an empty clipboard shows a note' ($items -eq 'The clipboard has no text (dimmed) | Settings') $items
Close-Menu

Copy-Text 'PC-12'
[void] [Win32]::SetForegroundWindow($charmap)
[void] (Open-Menu)
Press-Shortcut
Test-Check 'the shortcut again closes the menu' (Wait-Until { -not (Get-Menu) } 3)
Start-Sleep -Milliseconds 500
Test-Check 'and gives the front back' (Test-CharmapInFront) "front: $([Win32]::WindowClass([Win32]::GetForegroundWindow()))"
Test-Check 'the log never holds the copied text' (((Get-HostLog) -notmatch 'secret-4711') -and ((Get-HostLog) -notmatch '1Z999AA') -and ((Get-HostLog) -notmatch 'tracking\.example'))

# Turning Clipboard Actions off while its menu is open closes the menu and ends the thread.
Copy-Text 'PC-12'
[void] [Win32]::SetForegroundWindow($charmap)
Test-Check 'the menu is open before Clipboard Actions turns off' (Open-Menu)
Write-ClipboardSettings $false
Test-Check 'turning it off with its menu open ends its thread' (Wait-Until { (Get-HostLog) -match "Clipboard Actions' thread ended\." } 5)
Test-Check 'turning it off closes its menu' (Wait-Until { -not (Get-Menu) } 5) (Get-MenuItems)
Close-Menu
Write-ClipboardSettings $true
Test-Check 'turning it on again starts its thread' (Wait-Until { (Get-HostLog) -match 'Clipboard Actions started on its own thread\.' } 10)
Write-ClipboardSettings $false
Test-Check 'and turning it off again ends it' (Wait-Until { (Get-MatchCount (Get-HostLog) "Clipboard Actions' thread ended\.") -ge 2 } 10)
Press-Shortcut
Start-Sleep -Seconds 2
Test-Check 'turned off, the shortcut opens no menu' (-not (Get-Menu))
Close-Menu
Get-Processes 'charmap' | ForEach-Object { Stop-Process -Id $_.Id -Force -ErrorAction SilentlyContinue }

# The copy a host running as administrator starts to open a web page reads the address from a
# one-time pipe, as the host hands it over, and opens only web addresses, also long ones. Returns
# the copy's exit code, or $null when it didn't exit.
function Invoke-WebPageCopy([string] $Address) {
    $name = 'PCCompanion.OpenWebPage.Check.' + [Guid]::NewGuid().ToString('N')
    $server = New-Object IO.Pipes.NamedPipeServerStream($name, [IO.Pipes.PipeDirection]::Out, 1, [IO.Pipes.PipeTransmissionMode]::Byte, [IO.Pipes.PipeOptions]::Asynchronous)
    $waiting = $server.WaitForConnectionAsync()
    $copy = Start-Process $hostExe -ArgumentList @('--open-web-page', $name) -PassThru
    if ($waiting.Wait(15000)) {
        $bytes = [Text.Encoding]::UTF8.GetBytes($Address)
        $server.Write($bytes, 0, $bytes.Length)
        $server.Flush()
    }

    $server.Dispose()
    if (-not $copy.WaitForExit(30000)) { return $null }
    return $copy.ExitCode
}

$edgeBefore = (Get-Processes 'msedge').Count
$exitCode = Invoke-WebPageCopy ('https://example.com/opened-by-the-copy?text=' + ('a' * 2000))
Test-Check '--open-web-page opens a long web address from its pipe in the browser' (($exitCode -eq 0) -and (Wait-Until { (Get-Processes 'msedge').Count -gt $edgeBefore } 20)) "exit code: $exitCode"
Start-Sleep -Seconds 2
$edgeCommandLines = (Get-CimInstance Win32_Process -Filter "Name = 'msedge.exe'" | ForEach-Object { $_.CommandLine }) -join ' '
$received = if ($edgeCommandLines -match 'opened-by-the-copy\?text=(a*)') { $Matches[1].Length } else { 0 }
Test-Check 'the browser got the whole address' ($received -eq 2000) "characters of text received: $received"
Get-Processes 'msedge' | ForEach-Object { Stop-Process -Id $_.Id -Force -ErrorAction SilentlyContinue }
Start-Sleep -Seconds 2
$calcBefore = (Get-Processes 'CalculatorApp').Count + (Get-Processes 'calc').Count
$exitCode = Invoke-WebPageCopy 'file:///C:/Windows/System32/calc.exe'
Start-Sleep -Seconds 3
Test-Check '--open-web-page opens nothing for another kind of address' (($exitCode -eq 13) -and (Get-Processes 'msedge').Count -eq 0 -and ((Get-Processes 'CalculatorApp').Count + (Get-Processes 'calc').Count) -eq $calcBefore) "exit code: $exitCode"
Stop-All
