# Clipboard Actions: the Home switch turns it on, and its page adds, edits, moves and deletes rules
# in the rule editor, which says what's wrong with a pattern, an address or a program. The list
# says why a rule whose program doesn't exist, a rule after the first 50 and a rule from a newer
# version aren't in the menu, and the editor keeps the newer rule's action. Then, with rules in the
# settings, Windows + Alt + V, sent by scan code, opens the menu at the pointer, below the text
# cursor of Character Map's box or of a text area in Edge, and above it at the bottom of the
# screen. For text the checks copy, Open in browser and an address rule open the browser, and a
# rule that runs a program starts Character Map; a rule whose program was removed gives the front
# back, and so does a key pressed after a rule starts a program without a window; marked-private,
# missing and too long text show only a note. Esc gives the front back to the window that had it,
# the shortcut again closes the menu, turning the feature off closes its open menu, and turned off
# the shortcut opens no menu. The host's --open-web-page opens a long web address it reads from a
# pipe, and only web addresses. Selected text: the page's switch saves it and Home keeps it, and
# Windows + Alt + S shows the actions for text selected in Edge, its PDF viewer, Character Map, the
# Run dialog and a WPF window, below the selection, and a note for a password box, nothing
# selected, an app that stops responding and one with admin rights, without changing the
# clipboard; with another program holding the shortcut, the page says so. Detailed logging is on
# for the host's Debug entries.
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
$selectedText = Find-Toggle 'Selected text'
Test-Check 'Selected text is off' (($null -ne $selectedText) -and $selectedText.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern).Current.ToggleState -eq 'Off')
Test-Check 'turn on Selected text' (Set-Toggle 'Selected text' $true)
Start-Sleep -Seconds 2
Test-Check 'the switch saves Selected text as on' ((Get-ClipboardActionsSettings).selectedText -eq $true) "saved: $(Get-ClipboardActionsSettings | ConvertTo-Json -Compress)"
Test-Check 'select Home' (Select-NavigationItem 'Home')
Start-Sleep -Seconds 1
[void] (Set-Toggle 'Clipboard Actions' $false)
Start-Sleep -Seconds 2
$saved = Get-ClipboardActionsSettings
Test-Check 'the Home switch keeps Selected text' (($saved.enabled -eq $false) -and ($saved.selectedText -eq $true)) "saved: $($saved | ConvertTo-Json -Compress)"
[void] (Set-Toggle 'Clipboard Actions' $true)
Start-Sleep -Seconds 2
Test-Check 'select Clipboard Actions again' (Select-NavigationItem 'Clipboard Actions')
Start-Sleep -Seconds 2

# A new rule: Save is off until the rule can be used, Try it says whether the pattern matches.
Test-Check 'Add rule opens the rule editor' ((Invoke-AppElement 'Add rule') -and (Wait-Until { Find-AppElement 'Add a rule' } 5))
Start-Sleep -Seconds 1
Test-Check 'Save is off for an empty rule' (-not (Test-AppEnabled 'Save'))
[void] (Set-AppBox 'Name' 'Track the parcel')
$patternProblem = 'The pattern uses a lookaround, a backreference, an atomic group or a conditional, or repeats a part too many times, such as {1,4096}. These can''t be used here.'
Start-Announcements
[void] (Set-AppBox 'Pattern' '(?=1Z)1Z.+')
Test-Check 'a pattern with a lookahead is refused' (Wait-Until { Find-AppText $patternProblem } 3)
Test-Check 'screen readers announce the problem once, and the box reads it' (Test-Announced 'Pattern' $patternProblem) ((Get-Announcements) -join ' | ')
Stop-Announcements
# Non-backtracking matching also refuses a part repeated so often that its matcher would grow too large.
[void] (Set-AppBox 'Pattern' '1Z.+')
[void] (Wait-Until { -not (Find-AppText $patternProblem) } 3)
[void] (Set-AppBox 'Pattern' '.{1,4096}')
Test-Check 'a pattern that repeats a part too many times is refused' (Wait-Until { Find-AppText $patternProblem } 3)
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

# The menu uses only the first 50 rules, so the list says so for each rule after them.
$manyRules = @(1..51 | ForEach-Object { @{ name = "Rule $_"; pattern = "rule-$_"; action = 'OpenAddress'; address = 'https://example.com/?q={text}' } })
$settings = @{ schemaVersion = 1; general = @{ theme = 'Light' }; features = @{ clipboardActions = @{ enabled = $true; openLinks = $true; rules = $manyRules } } }
Write-Settings ($settings | ConvertTo-Json -Depth 6)
Start-Sleep -Seconds 2
Start-Process $appExe
[void] (Wait-Until { Get-AppWindow })
Start-Sleep -Seconds 3
Test-Check 'select Clipboard Actions with 51 rules' (Select-NavigationItem 'Clipboard Actions')
$beyond = 'Not in the menu. The menu uses only the first 50 rules.'
Test-Check 'the rule after the first 50 says it isn''t in the menu' (Wait-Until { Find-AppText $beyond } 5)
$beyondCount = @((Get-AppWindow).FindAll([System.Windows.Automation.TreeScope]::Descendants, (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, $beyond)))).Count
$opensCount = @((Get-AppWindow).FindAll([System.Windows.Automation.TreeScope]::Descendants, (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, 'Opens example.com')))).Count
Test-Check 'and the first 50 rules say what they open' (($beyondCount -gt 0) -and ($opensCount -eq 50 * $beyondCount)) "rules after the first 50: $beyondCount, rules that open example.com: $opensCount"
[void] (Close-SettingsApp)

# A rule from a newer version, with an action this version doesn't know, keeps its action: the
# editor chooses neither action and says why, so Save stays off until the user chooses one.
function Test-AppSelected([string] $Name) {
    $element = Find-AppElement $Name
    $pattern = $null
    return ($null -ne $element) -and $element.TryGetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern, [ref] $pattern) -and $pattern.Current.IsSelected
}
$newerRule = @{ name = 'From a newer version'; pattern = 'x'; action = 'Translate'; address = 'https://example.com/?q={text}' }
$settings = @{ schemaVersion = 1; general = @{ theme = 'Light' }; features = @{ clipboardActions = @{ enabled = $true; openLinks = $true; rules = @($newerRule) } } }
Write-Settings ($settings | ConvertTo-Json -Depth 6)
Start-Sleep -Seconds 2
Start-Process $appExe
[void] (Wait-Until { Get-AppWindow })
Start-Sleep -Seconds 3
Test-Check 'select Clipboard Actions with a rule from a newer version' (Select-NavigationItem 'Clipboard Actions')
Test-Check 'a rule from a newer version says why it isn''t in the menu' (Wait-Until { Find-AppText 'Not in the menu. Its action needs a newer version of PC Companion.' } 5)
Test-Check 'Edit opens the rule from a newer version' ((Invoke-AppElement 'Edit From a newer version') -and (Wait-Until { Find-AppElement 'Edit the rule' } 5))
Start-Sleep -Seconds 1
Test-Check 'the editor chooses neither action for it' ((-not (Test-AppSelected 'Open a web address')) -and (-not (Test-AppSelected 'Run a program'))) "web address: $(Test-AppSelected 'Open a web address'), program: $(Test-AppSelected 'Run a program')"
Test-Check 'the editor says its action needs a newer version' ($null -ne (Find-AppText 'Its action needs a newer version of PC Companion.'))
[void] (Set-AppBox 'Name' 'Renamed')
Test-Check 'Save stays off for it' (-not (Test-AppEnabled 'Save'))
Save-Screenshot '13e-clipboard-actions-newer-rule'
[void] (Invoke-AppElement 'Cancel')
Start-Sleep -Seconds 2
Test-Check 'the rule keeps its action' ((@((Get-ClipboardActionsSettings).rules)[0].action) -eq 'Translate')
[void] (Close-SettingsApp)

# The menu, with the rules in the settings file. One rule starts a program without a window of its
# own, which leaves the front with the feature's hidden window.
$noWindowExe = Join-Path $env:TEMP 'PCCompanionNoWindow.exe'
Add-Type -OutputType WindowsApplication -OutputAssembly $noWindowExe -TypeDefinition 'public static class NoWindow { public static void Main() { System.Threading.Thread.Sleep(20000); } }'
function Write-ClipboardSettings([bool] $Enabled, [bool] $SelectedText = $false, [bool] $OpenLinks = $true) {
    $section = @{
        enabled = $Enabled
        openLinks = $OpenLinks
        selectedText = $SelectedText
        rules = @(
            @{ name = 'Track the parcel'; pattern = '1Z[0-9A-Z]{16}'; action = 'OpenAddress'; address = 'https://tracking.example.com/?number={text}' },
            @{ name = 'Show in Character Map'; pattern = '.+\.txt'; action = 'RunProgram'; program = 'C:\Windows\System32\charmap.exe'; arguments = '{text}' },
            @{ name = 'Broken'; pattern = '[0-9'; address = 'https://example.com/{text}' },
            @{ name = 'Run a removed program'; pattern = '.+\.gone'; action = 'RunProgram'; program = 'C:\Windows\System32\PCCompanionMissing.exe'; arguments = '{text}' },
            @{ name = 'Run a program without a window'; pattern = '.+\.nowindow'; action = 'RunProgram'; program = $noWindowExe; arguments = '{text}' }
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
Test-Check 'the host uses the rules it can' (Wait-Until { (Get-HostLog) -match '4 of 5 rules can be used' } 10)
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

# A rule whose program was removed starts nothing, and the window that was in front gets the front
# back, so typing goes on there rather than to the feature's hidden window.
Copy-Text 'old.gone'
Test-Check 'the shortcut opens the menu for a rule whose program was removed' (Open-Menu)
Test-Check 'the rule whose program was removed starts nothing' ((Select-TrayMenuItem 'Run a removed program') -and (Wait-Until { (Get-HostLog) -match 'Clipboard Actions rule 4 didn''t start PCCompanionMissing\.exe: "?NotFound"?\.' } 5))
Start-Sleep -Milliseconds 500
Test-Check 'and the window in front gets the front back' (Test-CharmapInFront) "front: $([Win32]::WindowClass([Win32]::GetForegroundWindow()))"
[void] [Win32]::SetForegroundWindow($charmap)

# A rule whose program shows no window starts it, and nothing else comes to the front: the first key
# pressed gives the front back, so typing goes on in the window that had it.
Copy-Text 'job.nowindow'
Test-Check 'the shortcut opens the menu for a rule whose program shows no window' (Open-Menu)
Test-Check 'the rule starts its program without a window' ((Select-TrayMenuItem 'Run a program without a window') -and (Wait-Until { (Get-HostLog) -match 'Clipboard Actions rule 5 started PCCompanionNoWindow\.exe\.' } 10))
Start-Sleep -Seconds 1
Send-Key 0x10
Test-Check 'a key pressed then gives the front back' (Wait-Until { Test-CharmapInFront } 3) "front: $([Win32]::WindowClass([Win32]::GetForegroundWindow()))"
Get-Processes 'PCCompanionNoWindow' | ForEach-Object { Stop-Process -Id $_.Id -Force -ErrorAction SilentlyContinue }
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

# Selected text: Windows + Alt + S, sent by scan code, reads the text selected in the app in front
# with UI Automation and shows the same actions, below the selection, without the clipboard. A
# password box, nothing selected, an app that stops responding and one with admin rights give a
# note and Show actions for the copied text. Windows + Alt + V still shows the copied text's actions.
Add-Type -TypeDefinition @'
using System.Runtime.InteropServices;
public static class SelectionChecks {
    [DllImport("user32.dll")] public static extern uint GetClipboardSequenceNumber();
}
'@
function Press-SelectionShortcut {
    [void] [Win32]::ScanKey(0x5B, $false, $true); [void] [Win32]::ScanKey(0x38, $false, $false); [void] [Win32]::ScanKey(0x1F, $false, $false)
    Start-Sleep -Milliseconds 60
    [void] [Win32]::ScanKey(0x1F, $true, $false); [void] [Win32]::ScanKey(0x38, $true, $false); [void] [Win32]::ScanKey(0x5B, $true, $true)
}
function Open-SelectionMenu {
    Press-SelectionShortcut
    $opened = Wait-Until { Get-Menu } 5
    Start-Sleep -Milliseconds 500
    return $opened
}
# Selects all the text in the box with the keyboard focus: End, then Shift + Home.
function Select-BoxText {
    [void] [Win32]::ScanKey(0x4F, $false, $true); [void] [Win32]::ScanKey(0x4F, $true, $true)
    [void] [Win32]::ScanKey(0x2A, $false, $false); [void] [Win32]::ScanKey(0x47, $false, $true)
    [void] [Win32]::ScanKey(0x47, $true, $true); [void] [Win32]::ScanKey(0x2A, $true, $false)
    Start-Sleep -Milliseconds 300
}
$parcelMenu = '1Z999AA10123456784 (dimmed) | Track the parcel | Settings'
$copiedMenu = 'PC-12 (dimmed) | No actions for the copied text (dimmed) | Settings'
$showCopied = 'Show actions for the copied text'

Write-ClipboardSettings $true $true
Test-Check 'the host turns on Selected text' (Wait-Until { (Get-HostLog) -match 'Selected text "?True"?\.' } 10)
Copy-Text 'PC-12'
$sequence = [SelectionChecks]::GetClipboardSequenceNumber()

# One page for each selection, by the part of its address after #, which says when it's ready.
$selectionPage = Join-Path $env:TEMP 'selection.html'
Set-Content -Path $selectionPage -Encoding utf8 -Value @'
<!doctype html><html><head><meta charset="utf-8"><title>Selection</title></head><body style="margin:20px;font-size:24px">
<p id="p">Track 1Z999AA10123456784 مرحبا 你好 end of paragraph.</p>
<textarea id="t" rows="2" cols="40">Text area: 1Z999AA10123456784 and more</textarea>
<div id="d" contenteditable="true">Editable: 1Z999AA10123456784 here</div>
<p>Password: <input id="w" type="password" value="Secret-1Z999AA10123456784"></p>
<iframe id="f" style="width:600px;height:80px" srcdoc="&lt;p id=q&gt;Frame: 1Z999AA10123456784 here&lt;/p&gt;&lt;script&gt;function s(){const t=document.getElementById('q').firstChild;const r=document.createRange();r.setStart(t,7);r.setEnd(t,25);const x=getSelection();x.removeAllRanges();x.addRange(r);}addEventListener('load',s);addEventListener('focus',s);&lt;/script&gt;"></iframe>
<script>
function select(node, start, end) { const r = document.createRange(); r.setStart(node, start); r.setEnd(node, end); const s = getSelection(); s.removeAllRanges(); s.addRange(r); }
const cases = {
  paragraph: () => { const t = document.getElementById('p').firstChild; select(t, t.data.indexOf('1Z'), t.data.indexOf(' end')); },
  textarea: () => { const t = document.getElementById('t'); t.focus(); t.setSelectionRange(11, 29); },
  editable: () => { const d = document.getElementById('d'); d.focus(); select(d.firstChild, 10, 28); },
  password: () => { const w = document.getElementById('w'); w.focus(); w.select(); },
  frame: () => { document.getElementById('f').focus(); },
  nothing: () => { getSelection().removeAllRanges(); },
};
const name = location.hash.slice(1);
addEventListener('load', () => { cases[name](); document.title = 'SEL-' + name + ' ready'; });
addEventListener('focus', () => cases[name]());
</script></body></html>
'@
function Open-SelectionPage([string] $Case) {
    Start-Process $edgeExe -ArgumentList @('--no-first-run', '--no-default-browser-check', (([Uri] $selectionPage).AbsoluteUri + '#' + $Case))
    $script:selectionWindow = [IntPtr]::Zero
    $opened = Wait-Until {
        $script:selectionWindow = [Win32]::TopLevelWindows() | Where-Object { [Win32]::IsWindowVisible($_) -and ([Win32]::WindowClass($_) -eq 'Chrome_WidgetWin_1') -and ([Win32]::WindowTitle($_) -like "SEL-$Case ready*") } | Select-Object -First 1
        $null -ne $script:selectionWindow
    } 60
    if ($opened) {
        [void] [Win32]::SetForegroundWindow($script:selectionWindow)
        Start-Sleep -Seconds 1
    }
    return $opened
}

$selectionCases = [ordered] @{
    paragraph = '1Z999AA10123456784 مرحبا 你好 (dimmed) | No actions for the selected text (dimmed) | Settings'
    textarea = $parcelMenu
    editable = $parcelMenu
    frame = $parcelMenu
    password = "PC Companion doesn't read password boxes (dimmed) | $showCopied | Settings"
    nothing = "No text is selected (dimmed) | $showCopied | Settings"
}
foreach ($case in $selectionCases.Keys) {
    if (-not (Open-SelectionPage $case)) {
        Test-Check "Edge opens the selection page ($case)" $false
        continue
    }

    Test-Check "Windows + Alt + S opens the menu in Edge ($case)" (Open-SelectionMenu)
    $items = Get-MenuItems
    Test-Check "the menu is the one for the selection ($case)" ($items -eq $selectionCases[$case]) $items
    switch ($case) {
        'textarea' {
            Test-Check 'the host logs the menu below the selection' ((Get-LastMenuPlace) -eq 'BelowSelection') "menu: $(Get-LastMenuPlace)"
            $menuBounds = (Get-Menu).Current.BoundingRectangle
            $selectionRect = New-Object Win32+RECT
            [void] [Win32]::GetWindowRect($script:selectionWindow, [ref] $selectionRect)
            Test-Check 'the menu opens below the text area''s first line' ((Test-Within $menuBounds.Y ($selectionRect.Top + 100) ($selectionRect.Top + 400)) -and (Test-Within $menuBounds.X $selectionRect.Left ($selectionRect.Left + 400))) "menu at $menuBounds, Edge at $($selectionRect.Left),$($selectionRect.Top)"
            Save-Screenshot '13i-clipboard-actions-selected-text-menu'
            $opened = Get-MatchCount (Get-HostLog) 'Clipboard Actions opened the address of rule 1'
            Test-Check 'a rule runs on the selected text' ((Select-TrayMenuItem 'Track the parcel') -and (Wait-Until { (Get-MatchCount (Get-HostLog) 'Clipboard Actions opened the address of rule 1') -gt $opened } 10))
        }
        'editable' {
            Close-Menu
            Test-Check 'Windows + Alt + V still shows the copied text''s actions with text selected' (Open-Menu)
            $items = Get-MenuItems
            Test-Check 'and only the copied text''s' ($items -eq $copiedMenu) $items
            Close-Menu
        }
        'nothing' {
            Test-Check 'Show actions for the copied text opens their menu' ((Select-TrayMenuItem $showCopied) -and (Wait-Until { (Get-MenuItems) -eq $copiedMenu } 5)) (Get-MenuItems)
            Close-Menu
            Test-Check 'Esc in that menu gives the front back to Edge' (Wait-Until { [Win32]::GetForegroundWindow() -eq $script:selectionWindow } 3) "front: $([Win32]::WindowClass([Win32]::GetForegroundWindow()))"
        }
        default { Close-Menu }
    }
}
# A PDF in Edge's viewer, whose text is one element above the focus: Ctrl + A selects it.
$pdfContent = 'BT /F1 24 Tf 72 700 Td (1Z999AA10123456784) Tj ET'
$pdfObjects = @(
    '<< /Type /Catalog /Pages 2 0 R >>',
    '<< /Type /Pages /Kids [3 0 R] /Count 1 >>',
    '<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Contents 4 0 R /Resources << /Font << /F1 5 0 R >> >> >>',
    "<< /Length $($pdfContent.Length) >>`nstream`n$pdfContent`nendstream",
    '<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>'
)
$pdf = New-Object Text.StringBuilder
[void] $pdf.Append("%PDF-1.4`n")
$pdfOffsets = @()
for ($index = 0; $index -lt $pdfObjects.Count; $index++) {
    $pdfOffsets += $pdf.Length
    [void] $pdf.Append("$($index + 1) 0 obj`n$($pdfObjects[$index])`nendobj`n")
}
$pdfTable = $pdf.Length
[void] $pdf.Append("xref`n0 $($pdfObjects.Count + 1)`n0000000000 65535 f `n")
foreach ($offset in $pdfOffsets) { [void] $pdf.Append(("{0:D10} 00000 n `n" -f $offset)) }
[void] $pdf.Append("trailer`n<< /Size $($pdfObjects.Count + 1) /Root 1 0 R >>`nstartxref`n$pdfTable`n%%EOF`n")
$selectionPdf = Join-Path $env:TEMP 'selection.pdf'
[IO.File]::WriteAllText($selectionPdf, $pdf.ToString(), [Text.Encoding]::ASCII)
Start-Process $edgeExe -ArgumentList @('--no-first-run', '--no-default-browser-check', ([Uri] $selectionPdf).AbsoluteUri)
$script:pdfWindow = [IntPtr]::Zero
if (Wait-Until { $script:pdfWindow = [Win32]::TopLevelWindows() | Where-Object { [Win32]::IsWindowVisible($_) -and ([Win32]::WindowClass($_) -eq 'Chrome_WidgetWin_1') -and ([Win32]::WindowTitle($_) -like 'selection.pdf*') } | Select-Object -First 1; $null -ne $script:pdfWindow } 30) {
    [void] [Win32]::SetForegroundWindow($script:pdfWindow)
    Start-Sleep -Seconds 4
    [void] [Win32]::ScanKey(0x1D, $false, $false); [void] [Win32]::ScanKey(0x1E, $false, $false)
    [void] [Win32]::ScanKey(0x1E, $true, $false); [void] [Win32]::ScanKey(0x1D, $true, $false)
    Start-Sleep -Seconds 1
    Test-Check 'Windows + Alt + S opens the menu in Edge''s PDF viewer' (Open-SelectionMenu)
    $items = Get-MenuItems
    Test-Check 'the menu is the one for the PDF''s selected text' ($items -eq $parcelMenu) $items
    Close-Menu
} else {
    Test-Check 'Edge opens the PDF' $false
}
Get-Processes 'msedge' | ForEach-Object { Stop-Process -Id $_.Id -Force -ErrorAction SilentlyContinue }
Start-Sleep -Seconds 2

[void] [Win32]::SetForegroundWindow($charmap)
$charmapBox.SetFocus()
$charmapBox.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue('1Z999AA10123456784')
Select-BoxText
Test-Check 'Windows + Alt + S opens the menu for Character Map''s box' (Open-SelectionMenu)
$items = Get-MenuItems
Test-Check 'the menu is the one for the box''s selected text' ($items -eq $parcelMenu) $items
Test-Check 'and opens below the selection' ((Get-LastMenuPlace) -eq 'BelowSelection') "menu: $(Get-LastMenuPlace)"
Close-Menu

Start-Process explorer.exe -ArgumentList 'shell:::{2559a1f3-21d7-11d4-bdaf-00c04f60b9f0}'
$script:runDialog = [IntPtr]::Zero
if (Wait-Until { $script:runDialog = [Win32]::FindWindow('#32770', 'Run'); $script:runDialog -ne [IntPtr]::Zero } 15) {
    [void] [Win32]::SetForegroundWindow($script:runDialog)
    Start-Sleep -Seconds 1
    $runBox = [System.Windows.Automation.AutomationElement]::FromHandle($script:runDialog).FindFirst([System.Windows.Automation.TreeScope]::Descendants, $editCondition)
    $runBox.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue('https://example.com/run-dialog')
    $runBox.SetFocus()
    Select-BoxText
    Test-Check 'Windows + Alt + S opens the menu for the Run dialog''s box' (Open-SelectionMenu)
    $items = Get-MenuItems
    Test-Check 'the menu offers Open in browser for the selected link' ($items -eq 'https://example.com/run-dialog (dimmed) | Open in browser | Settings') $items
    Close-Menu
    Send-Key 0x1B
} else {
    Test-Check 'the Run dialog opens' $false
}

# A WPF test window with a box whose text is selected, as a password box, or whose thread stops
# responding 1.5 seconds after it shows. WPF answers UI Automation on that thread, so a read waits
# for it, unlike Windows' own boxes, which Windows answers for.
$selectionWindowScript = Join-Path $env:TEMP 'PCCompanion-selection-window.ps1'
Set-Content -Path $selectionWindowScript -Encoding utf8 -Value @'
param([string] $Kind)
Add-Type -AssemblyName PresentationFramework
$window = New-Object System.Windows.Window
$window.Title = "PCCompanionSelection-$Kind"
$window.Width = 600; $window.Height = 200; $window.WindowStartupLocation = 'CenterScreen'
if ($Kind -eq 'password') {
    $script:box = New-Object System.Windows.Controls.PasswordBox
    $script:box.Password = '1Z999AA10123456784'
} else {
    $script:box = New-Object System.Windows.Controls.TextBox
    $script:box.Text = '1Z999AA10123456784'
}
$script:box.FontSize = 20
$script:box.Margin = New-Object System.Windows.Thickness 20
$script:box.VerticalAlignment = 'Top'
$window.Content = $script:box
$window.Add_ContentRendered({
    [void] $script:box.Focus(); $script:box.SelectAll()
    $window.Title = "PCCompanionSelection-$Kind ready"
    if ($Kind -eq 'hang') {
        $script:timer = New-Object System.Windows.Threading.DispatcherTimer
        $script:timer.Interval = [TimeSpan]::FromMilliseconds(1500)
        $script:timer.Add_Tick({ $script:timer.Stop(); Start-Sleep -Seconds 40 })
        $script:timer.Start()
    }
})
$window.Add_Activated({ [void] $script:box.Focus(); $script:box.SelectAll() })
[void] $window.ShowDialog()
'@
function Start-SelectionWindow([string] $Kind) {
    $process = Start-Process powershell.exe -ArgumentList '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $selectionWindowScript, '-Kind', $Kind -WindowStyle Hidden -PassThru
    $script:selectionTestWindow = [IntPtr]::Zero
    [void] (Wait-Until { $script:selectionTestWindow = [Win32]::FindWindow([NullString]::Value, "PCCompanionSelection-$Kind ready"); $script:selectionTestWindow -ne [IntPtr]::Zero } 30)
    [void] [Win32]::SetForegroundWindow($script:selectionTestWindow)
    return $process
}

$boxProcess = Start-SelectionWindow 'plain'
Start-Sleep -Seconds 1
Test-Check 'Windows + Alt + S opens the menu for a WPF box' (Open-SelectionMenu)
$items = Get-MenuItems
Test-Check 'the menu is the one for the WPF box''s selected text' ($items -eq $parcelMenu) $items
Close-Menu
Stop-Process -Id $boxProcess.Id -Force -ErrorAction SilentlyContinue

$boxProcess = Start-SelectionWindow 'password'
Start-Sleep -Seconds 1
Test-Check 'Windows + Alt + S opens the menu for a password box' (Open-SelectionMenu)
$items = Get-MenuItems
Test-Check 'a password box isn''t read' ($items -eq "PC Companion doesn't read password boxes (dimmed) | $showCopied | Settings") $items
Close-Menu
Stop-Process -Id $boxProcess.Id -Force -ErrorAction SilentlyContinue

$boxProcess = Start-SelectionWindow 'hang'
Start-Sleep -Seconds 3
$pressedAt = Get-Date
Press-SelectionShortcut
$opened = Wait-Until { Get-Menu } 10
$waited = ((Get-Date) - $pressedAt).TotalSeconds
Start-Sleep -Milliseconds 500
$items = Get-MenuItems
# The host logs how long the read took; finding the menu with UI Automation here can itself wait for
# the window that stopped responding.
$reads = [regex]::Matches((Get-HostLog), 'selected text: "?(\w+)"? in (\d+) ms')
$lastRead = if ($reads.Count -gt 0) { $reads[$reads.Count - 1] } else { $null }
$inTime = ($null -ne $lastRead) -and ($lastRead.Groups[1].Value -eq 'NoAnswer') -and ([int] $lastRead.Groups[2].Value -le 1500)
Test-Check 'an app that stops responding gives its note within the time limit' ($opened -and $inTime -and ($items -eq "This app didn't answer in time (dimmed) | $showCopied | Settings")) "host: $($lastRead.Value); menu found after $([Math]::Round($waited, 1)) s: $items"
Close-Menu
# About 5 seconds after an app stops responding, Windows shows a window of its own in its place.
if (Wait-Until { [Win32]::WindowClass([Win32]::GetForegroundWindow()) -eq 'Ghost' } 10) {
    [void] (Open-SelectionMenu)
    $items = Get-MenuItems
    Test-Check 'the window Windows shows in its place says the app isn''t responding' ($items -eq "This app isn't responding (dimmed) | $showCopied | Settings") $items
    Close-Menu
} else {
    Write-Report "[NOTE] Windows showed no window in place of the test window that stopped responding; front: $([Win32]::WindowClass([Win32]::GetForegroundWindow()))."
}
Stop-Process -Id $boxProcess.Id -Force -ErrorAction SilentlyContinue

Test-Check 'reading the selected text never changed the clipboard' ([SelectionChecks]::GetClipboardSequenceNumber() -eq $sequence) "sequence before: $sequence, after: $([SelectionChecks]::GetClipboardSequenceNumber())"
Test-Check 'the log never holds the selected text' (((Get-HostLog) -notmatch 'Secret-') -and ((Get-HostLog) -notmatch 'run-dialog') -and ((Get-HostLog) -notmatch '1Z999AA'))

# Everything in Windows Sandbox runs as administrator; a host whose file is labeled Medium integrity
# runs without admin rights, so it doesn't read the test window, which has them.
function Stop-Host {
    [void] [Win32]::PostMessage([Win32]::FindWindow('PCCompanion.Host', $null), 0x0010, [UIntPtr]::Zero, [IntPtr]::Zero)
    [void] (Wait-Until { (Get-Processes 'PCCompanion.Host').Count -eq 0 } 15)
}
function Start-HostAgain {
    $started = Get-MatchCount (Get-HostLog) 'Clipboard Actions started on its own thread\.'
    Start-Process $hostExe
    [void] (Wait-Until { (Get-MatchCount (Get-HostLog) 'Clipboard Actions started on its own thread\.') -gt $started } 15)
    Start-Sleep -Seconds 2
}
Stop-Host
& icacls.exe $hostExe /setintegritylevel medium | Out-Null
Start-HostAgain
$boxProcess = Start-SelectionWindow 'plain'
Start-Sleep -Seconds 1
$pressed = Get-MatchCount (Get-HostLog) 'shortcut for the selected text was pressed'
Press-SelectionShortcut
[void] (Wait-Until { Get-Menu } 5)
Start-Sleep -Milliseconds 500
if ((Get-MatchCount (Get-HostLog) 'shortcut for the selected text was pressed') -gt $pressed) {
    $items = Get-MenuItems
    Test-Check 'a host without admin rights doesn''t read an app with them, and says so' ($items -eq "This app runs as administrator, so its selected text can't be read (dimmed) | $showCopied | Settings") $items
} else {
    Write-Report '[NOTE] Without admin rights, the host got no shortcut while a window with admin rights was in front.'
}
Close-Menu
Stop-Process -Id $boxProcess.Id -Force -ErrorAction SilentlyContinue
Stop-Host
& icacls.exe $hostExe /setintegritylevel high | Out-Null
Start-HostAgain

# Another program that has Windows + Alt + S: Windows refuses it to the host, and the page says so
# until the program lets it go and a setting changes.
Write-ClipboardSettings $true $false
Test-Check 'turning Selected text off removes its shortcut' (Wait-Until { (Get-HostLog) -match 'Selected text "?False"?\.' } 10)
$holderResult = Join-Path $env:TEMP 'PCCompanion-hotkey-holder.txt'
Remove-Item $holderResult -Force -ErrorAction SilentlyContinue
$holderScript = Join-Path $env:TEMP 'PCCompanion-hotkey-holder.ps1'
Set-Content -Path $holderScript -Encoding utf8 -Value @"
Add-Type -TypeDefinition 'using System; using System.Runtime.InteropServices; public static class Holder { [DllImport("user32.dll")] public static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint key); }'
Set-Content -Path '$holderResult' -Value ([Holder]::RegisterHotKey([IntPtr]::Zero, 1, 0x4009, 0x53))
Start-Sleep -Seconds 120
"@
$holder = Start-Process powershell.exe -ArgumentList '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $holderScript -WindowStyle Hidden -PassThru
Test-Check 'a test program takes Windows + Alt + S' (Wait-Until { (Test-Path $holderResult) -and ((Get-Content $holderResult) -eq 'True') } 15)
[void] [Win32]::SetForegroundWindow($charmap)
Press-SelectionShortcut
Start-Sleep -Seconds 2
Test-Check 'with Selected text off, Windows + Alt + S opens no menu' (-not (Get-Menu))
Close-Menu
Write-ClipboardSettings $true $true
Test-Check 'the host logs that Windows refused Windows + Alt + S' (Wait-Until { (Get-HostLog) -match 'Windows refused Windows \+ Alt \+ S' } 10)
Start-Process $appExe
[void] (Wait-Until { Get-AppWindow })
Start-Sleep -Seconds 3
[void] (Select-NavigationItem 'Clipboard Actions')
$taken = "Another program uses Windows logo key + Alt + S, so this shortcut doesn't open the menu. PC Companion tries again when a setting here changes."
Test-Check 'the page says another program has Windows + Alt + S' (Wait-Until { Find-AppText $taken } 10)
Test-Check 'and nothing about Windows + Alt + V' (-not (Find-AppText ($taken -replace '\+ S,', '+ V,')))
Save-Screenshot '13j-clipboard-actions-shortcut-taken'
Stop-Process -Id $holder.Id -Force -ErrorAction SilentlyContinue
Start-Sleep -Seconds 1
Write-ClipboardSettings $true $true $false
Test-Check 'once the program lets it go and a setting changes, the note goes away' (Wait-Until { -not (Find-AppText $taken) } 15)
[void] (Close-SettingsApp)
Write-ClipboardSettings $true
[void] [Win32]::SetForegroundWindow($charmap)

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
