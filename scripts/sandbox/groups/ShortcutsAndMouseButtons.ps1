# Shortcuts & Mouse Buttons: the Home switch turns it on with an empty list, and its page lists the
# triggers in the settings with their keys or button and their action, says at each one why it does
# nothing (an action whose feature is off or whose profile is gone, a shortcut against the rules,
# the same shortcut as a row above, a shortcut another program has), lists the other pages'
# shortcuts with links to them, deletes a trigger after asking and saves the full-screen pause. A
# shortcut runs Hide Apps' Hide app on a test program. Never Lock, Turn off displays or Start the
# screen saver, which would stop the checks, and never a lock key, which the Sandbox passes to the PC.
[void] (Close-SettingsApp)

# Detailed logging is on for the host's Debug entries.
$detailedUntil = [DateTimeOffset]::Now.AddHours(1).ToString('o')
function Write-ShortcutSettings([string] $Section, [string] $Others = '', [string] $General = '"theme": "Light"') {
    Write-Settings "{ ""schemaVersion"": 1, ""general"": { $General }, ""diagnostics"": { ""detailedLoggingUntil"": ""$detailedUntil"" }, ""features"": { ""shortcutsAndMouseButtons"": $Section$Others } }"
    Start-Sleep -Seconds 3
}
function Get-ShortcutSettings { (Get-Content (Join-Path $dataFolder 'settings.json') -Raw | ConvertFrom-Json).features.shortcutsAndMouseButtons }
function Test-Logged([string] $Pattern, [int] $Seconds = 5) { Wait-Until { (Get-HostLog) -match $Pattern } $Seconds }
function Start-Host {
    if ((Get-Processes 'PCCompanion.Host').Count -eq 0) {
        Start-Process $hostExe
        [void] (Wait-Until { [Win32]::FindWindow('PCCompanion.Host', $null) -ne [IntPtr]::Zero } 30)
    }
    [void] (Wait-Until { [Win32]::FindWindow('PCCompanion.ShortcutsAndMouseButtons', $null) -ne [IntPtr]::Zero } 20)
    Start-Sleep -Seconds 1
}
function Stop-Host {
    [void] [Win32]::PostMessage([Win32]::FindWindow('PCCompanion.Host', $null), 0x0010, [UIntPtr]::Zero, [IntPtr]::Zero)
    [void] (Wait-Until { (Get-Processes 'PCCompanion.Host').Count -eq 0 } 15)
}
function Open-Page([string] $Name = 'Shortcuts & Mouse Buttons') {
    Start-Process $appExe
    [void] (Wait-Until { Get-AppWindow })
    Start-Sleep -Seconds 3
    $selected = Select-NavigationItem $Name
    Start-Sleep -Seconds 2
    return $selected
}
function Test-Toggled([string] $Name) {
    $toggle = Find-Toggle $Name
    ($null -ne $toggle) -and ($toggle.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern).Current.ToggleState -eq [System.Windows.Automation.ToggleState]::On)
}

# A test program with one window, which Hide app minimizes.
$shortcutFolder = Join-Path $env:TEMP 'PCCompanionShortcuts'
New-Item -ItemType Directory -Force -Path $shortcutFolder | Out-Null
$shortcutExe = Join-Path $shortcutFolder 'shortcut-app.exe'
Add-Type -OutputAssembly $shortcutExe -OutputType WindowsApplication -ReferencedAssemblies System.Windows.Forms, System.Drawing -TypeDefinition @'
using System;
using System.Drawing;
using System.Windows.Forms;
public static class ShortcutApp {
    [STAThread]
    public static void Main() {
        Application.Run(new Form { Text = "ShortcutApp", StartPosition = FormStartPosition.Manual, Bounds = new Rectangle(200, 200, 480, 320) });
    }
}
'@

# Brings a window to the front: a press of a key that has no use first gives this script the right
# to, as in the Hide Apps checks.
function Select-Window([IntPtr] $Window) {
    for ($attempt = 1; $attempt -le 5; $attempt++) {
        [void] [Win32]::Key(0xE8, $false)
        [void] [Win32]::Key(0xE8, $true)
        [void] [Win32]::SetForegroundWindow($Window)
        Start-Sleep -Milliseconds 700
        if ([Win32]::GetForegroundWindow() -eq $Window) { return }
    }
    Write-Report "[NOTE] $([Win32]::WindowTitle($Window)) didn't come to the front; $([Win32]::WindowTitle([Win32]::GetForegroundWindow())) is."
}
$LeftControl = 0x1D; $LeftAlt = 0x38; $LeftShift = 0x2A; $LeftWindows = 0x5B
$KeyA = 0x1E; $KeyH = 0x23; $KeyJ = 0x24; $KeyU = 0x16; $KeyY = 0x15; $KeyF4 = 0x3E
function Send-Keys([int[]] $Keys, [int] $Wait = 1500) {
    foreach ($key in $Keys) { [void] [Win32]::ScanKey($key, $false, $key -eq $LeftWindows) }
    [array]::Reverse($Keys)
    foreach ($key in $Keys) { [void] [Win32]::ScanKey($key, $true, $key -eq $LeftWindows) }
    Start-Sleep -Milliseconds $Wait
}

# The editor's box, by its name, given the focus so that it listens; the host then pauses PC
# Companion's shortcuts.
function Select-ShortcutBox([string] $Name) {
    $box = Find-AppBox $Name
    if (-not $box) { return $false }
    $box.SetFocus()
    Start-Sleep -Seconds 1
    return $true
}
function Get-ProblemText { @(Get-WindowText | Where-Object { $_ -like '*shortcut*' -or $_ -like 'Add Ctrl*' -or $_ -like '* already has *' }) -join ' | ' }
# A button of the settings app by its name, where a text has the same name, such as the page's Add.
function Invoke-AppButton([string] $Name) {
    $window = Get-AppWindow
    if (-not $window) { return $false }
    $condition = New-Object System.Windows.Automation.AndCondition(
        (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, $Name)),
        (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Button)))
    $button = $window.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
    if (-not $button) { return $false }
    $button.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    return $true
}
# A running app in the Running app dialog, by a pattern of its name.
function Find-RunningApp([string] $Like) {
    $window = Get-AppWindow
    if (-not $window) { return $null }
    $condition = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::ListItem)
    @($window.FindAll([System.Windows.Automation.TreeScope]::Descendants, $condition) | Where-Object { $_.Current.Name -like $Like }) | Select-Object -First 1
}

# Turned on from Home, which saves only that it's on: the list is empty, the full-screen pause is
# on, and no other feature that's on has a shortcut.
Write-Settings '{ "schemaVersion": 1, "general": { "theme": "Light" } }'
Start-Sleep -Seconds 2
Start-Process $appExe
[void] (Wait-Until { Get-AppWindow })
Start-Sleep -Seconds 3
Test-Check 'turn on Shortcuts & Mouse Buttons on Home' (Set-Toggle 'Shortcuts & Mouse Buttons' $true)
Start-Sleep -Seconds 2
$saved = Get-ShortcutSettings
Test-Check 'the Home switch saves it as on, with no triggers' (($saved.enabled -eq $true) -and ($null -eq $saved.triggers)) "saved: $($saved | ConvertTo-Json -Compress)"
Test-Check 'select Shortcuts & Mouse Buttons' (Select-NavigationItem 'Shortcuts & Mouse Buttons')
Start-Sleep -Seconds 2
Save-Screenshot '20a-shortcuts-and-mouse-buttons-empty'
$text = Get-WindowText
$missing = @('Shortcuts and mouse buttons', 'Pause mouse buttons while an app is full screen', 'Shortcuts on other pages', 'Permissions & data') | Where-Object { $text -notcontains $_ }
Test-Check 'the page shows its settings' (@($missing).Count -eq 0) ($text -join ' | ')
Test-Check 'the empty list says so' (@($text | Where-Object { $_ -like 'No shortcuts or mouse buttons yet.*' }).Count -eq 1) ($text -join ' | ')
Test-Check 'no other feature that''s on has a shortcut' ($text -contains 'No other feature that''s on has a shortcut.') ($text -join ' | ')
Test-Check 'the full-screen pause is on' (Test-Toggled 'Pause mouse buttons while an app is full screen')
# Windows Sandbox runs PC Companion as administrator, so the page says shortcuts and buttons work with every app.
Test-Check 'it says what works with apps running as administrator' ($text -contains 'PC Companion runs as administrator, so shortcuts and mouse buttons also work while an app running as administrator is in front.') ($text -join ' | ')
Test-Check 'turn the full-screen pause off' (Set-Toggle 'Pause mouse buttons while an app is full screen' $false)
Start-Sleep -Seconds 2
$saved = Get-ShortcutSettings
Test-Check 'the page saves the full-screen pause' ($saved.pauseMouseButtonsInFullScreen -eq $false) "saved: $($saved | ConvertTo-Json -Compress)"
[void] (Close-SettingsApp)

# The list, with Hide Apps on and End App off: the host registers the usable shortcut whose action
# can run, and the page says why the others do nothing. Network Profiles has no profile named
# Desk-Test, and the last two rows break the rules.
$hideAppOnJ = '{ "action": { "feature": "hideApps", "id": "hideApp" }, "shortcut": { "modifiers": [ "Control", "Alt" ], "key": "KeyJ" } }'
$triggers = @(
    $hideAppOnJ,
    '{ "action": { "feature": "endApp", "id": "chooseApp" }, "shortcut": { "modifiers": [ "Control", "Alt" ], "key": "KeyK" } }',
    '{ "action": { "feature": "networkProfiles", "id": "use", "target": "Desk-Test" }, "shortcut": { "modifiers": [ "Control", "Alt" ], "key": "KeyL" } }',
    '{ "action": { "feature": "hideApps", "id": "showAll" }, "mouseButton": { "button": "Back", "modifiers": [ "Control" ] } }',
    '{ "action": { "feature": "shortcutsAndMouseButtons", "id": "lock" }, "shortcut": { "modifiers": [ "Shift" ], "key": "KeyA" } }',
    '{ "action": { "feature": "hideApps", "id": "hideOthers" }, "shortcut": { "modifiers": [ "Control", "Alt" ], "key": "KeyJ" } }'
) -join ', '
$list = "{ ""enabled"": true, ""triggers"": [ $triggers ] }"
$hideApps = ', "hideApps": { "enabled": true }'
Write-ShortcutSettings $list $hideApps
Start-Host
Test-Check 'the host registers Ctrl + Alt + J' (Test-Logged 'Shortcuts & Mouse Buttons registered [^\r\n]* scan code 24' 10)
Test-Check 'it leaves out the triggers that break the rules, with a warning, once' (((Get-MatchCount (Get-HostLog) 'Trigger 5 can''t be used: "?NeedsModifier') -eq 1) -and ((Get-MatchCount (Get-HostLog) 'Trigger 6 can''t be used: "?SameAsEarlier') -eq 1)) ((Get-HostLog) -split "`n" | Where-Object { $_ -like '*can''t be used*' } | Out-String)
Test-Check 'it leaves out the triggers whose actions can''t run' (Test-Logged 'Shortcuts & Mouse Buttons has 2 triggers whose actions can run, and 2 whose features are off')

Test-Check 'select Shortcuts & Mouse Buttons with a list' (Open-Page)
Save-Screenshot '20b-shortcuts-and-mouse-buttons-list'
$text = Get-WindowText
$missing = @(
    'Ctrl + Alt + J', 'Hide Apps: Hide app',
    'Ctrl + Alt + K', 'End App: Choose an app to end', 'End App is off, so this does nothing.', 'Open End App',
    'Ctrl + Alt + L', 'Network Profiles: Desk-Test', 'Network Profiles no longer has Desk-Test, so this does nothing.', 'Open Network Profiles',
    'Ctrl + Back button', 'Hide Apps: Show all hidden windows',
    'Shift + A', 'Shortcuts & Mouse Buttons: Lock', 'This shortcut needs Ctrl, Alt or the Windows logo key, so it does nothing.',
    'Hide Apps: Hide others', 'A row above has the same shortcut or button, so this does nothing.',
    'Windows logo key + Ctrl + H', 'Windows logo key + Ctrl + Shift + H'
) | Where-Object { $text -notcontains $_ }
Test-Check 'each row shows its keys or button, its action and why it does nothing' (@($missing).Count -eq 0) "missing: $($missing -join ' | ')"
Test-Check 'Shortcuts on other pages lists Hide Apps'' shortcuts' (@($text | Where-Object { $_ -eq 'Hide Apps: Hide app' }).Count -ge 2) ($text -join ' | ')
$warnings = @($text | Where-Object { $_ -like '*so this does nothing*' -or $_ -like '*so it does nothing*' } | Sort-Object -Unique)
Test-Check 'the rows whose feature is on show no warning' ($warnings.Count -eq 4) ($warnings -join ' | ')

# The warning's button and a row of Shortcuts on other pages open the feature's page.
Test-Check 'Open End App opens End App''s page' ((Invoke-AppElement 'Open End App') -and (Wait-Until { (Get-WindowText) -contains 'Choosing an app' } 5))
[void] (Select-NavigationItem 'Shortcuts & Mouse Buttons')
Start-Sleep -Seconds 2
Test-Check 'Open Hide Apps in Shortcuts on other pages opens its page' ((Invoke-AppElement 'Open Hide Apps') -and (Wait-Until { (Get-WindowText) -contains 'Hide others only on the display in front' } 5))
[void] (Select-NavigationItem 'Shortcuts & Mouse Buttons')
Start-Sleep -Seconds 2

# Delete asks first, then saves the list without the row.
Test-Check 'More options opens the row''s menu' (Invoke-AppElement 'More options for Shift + A')
Start-Sleep -Seconds 1
Test-Check 'Delete asks first' ((Invoke-AppElement 'Delete') -and (Wait-Until { Find-AppElement 'Delete Shift + A?' } 5))
Start-Sleep -Seconds 1
[void] (Invoke-AppElement 'Delete')
Start-Sleep -Seconds 2
$saved = Get-ShortcutSettings
Test-Check 'the page deletes the row' ((@($saved.triggers).Count -eq 5) -and (@($saved.triggers | Where-Object { $_.action.id -eq 'lock' }).Count -eq 0)) "saved: $($saved | ConvertTo-Json -Compress -Depth 6)"
Test-Check 'the row is gone from the page' (-not ((Get-WindowText) -contains 'Shift + A'))

# The editor: Save waits for an action and a shortcut that can be used; the box records the keys
# pressed in it, and says why a shortcut can't be used: one without Ctrl, Alt or the Windows logo
# key, another feature's, another row's and another program's. PC Companion's own shortcut is
# recorded, not run, since the host pauses them while the box listens. Windows logo key + Y is
# allowed with a warning, and Choose keys picks keys that aren't pressed.
$holder = Start-ShortcutHolder 0x3 0x59
Test-Check 'a test program takes Ctrl + Alt + Y' ($null -ne $holder)
Test-Check 'Add opens the editor' ((Invoke-AppElement 'Add a shortcut or mouse button') -and (Wait-Until { Find-AppText 'Add a shortcut or mouse button' } 5))
Start-Sleep -Seconds 1
Test-Check 'Save waits for an action and a shortcut' (-not (Test-AppEnabled 'Save'))
Test-Check 'choose Hide Apps: Hide others' (Select-ListItem 'Action' 'Hide Apps: Hide others')
$boxName = 'Shortcut for Hide others'
Test-Check 'the box is named after the action' (Select-ShortcutBox $boxName)
Test-Check 'the box waits for keys' ((Get-AppBox $boxName) -eq 'Press the keys') "box: $(Get-AppBox $boxName)"
Send-Keys @($LeftShift, $KeyA)
Test-Check 'Shift + A needs Ctrl, Alt or the Windows logo key' (((Get-AppBox $boxName) -eq 'Shift + A') -and (Find-AppText 'Add Ctrl, Alt or the Windows logo key.') -and -not (Test-AppEnabled 'Save')) "box: $(Get-AppBox $boxName); $(Get-ProblemText)"
Send-Keys @($LeftWindows, $LeftControl, $KeyH)
Test-Check 'Windows logo key + Ctrl + H is Hide Apps''' (((Get-AppBox $boxName) -eq 'Windows logo key + Ctrl + H') -and (Find-AppText 'Hide Apps uses this shortcut. Choose another, or change it on the Hide Apps page.')) "box: $(Get-AppBox $boxName); $(Get-ProblemText)"
$appWindow = [IntPtr] (Get-AppWindow).Current.NativeWindowHandle
Send-Keys @($LeftControl, $LeftAlt, $KeyJ)
Test-Check 'Ctrl + Alt + J is recorded, and is another row''s' (((Get-AppBox $boxName) -eq 'Ctrl + Alt + J') -and (Find-AppText 'Hide app already has this shortcut.')) "box: $(Get-AppBox $boxName); $(Get-ProblemText)"
Test-Check 'the paused host doesn''t run Hide app on the settings window' (-not [Win32]::IsIconic($appWindow))
Send-Keys @($LeftControl, $LeftAlt, $KeyY)
Test-Check 'another program''s Ctrl + Alt + Y is taken' (((Get-AppBox $boxName) -eq 'Ctrl + Alt + Y') -and (Find-AppText 'Windows or another program uses this shortcut.')) "box: $(Get-AppBox $boxName); $(Get-ProblemText)"
Send-Keys @($LeftWindows, $KeyY)
Test-Check 'Windows logo key + Y is allowed with a warning' (((Get-AppBox $boxName) -eq 'Windows logo key + Y') -and (Find-AppText 'Windows or apps use this shortcut. This action takes its place.') -and (Test-AppEnabled 'Save')) "box: $(Get-AppBox $boxName); $(Get-ProblemText)"
Test-Check 'Choose keys opens' (Invoke-AppElement 'Choose keys')
Start-Sleep -Seconds 1
Test-Check 'its check boxes follow the keys pressed' (Test-Toggled 'Windows logo key')
[void] (Set-Toggle 'Windows logo key' $false)
[void] (Set-Toggle 'Ctrl' $true)
[void] (Set-Toggle 'Alt' $true)
Test-Check 'choose U' (Select-ListItem 'Key' 'U')
Start-Sleep -Seconds 1
Test-Check 'Choose keys sets Ctrl + Alt + U' (((Get-AppBox $boxName) -eq 'Ctrl + Alt + U') -and (Test-AppEnabled 'Save')) "box: $(Get-AppBox $boxName); $(Get-ProblemText)"
Save-Screenshot '20e-shortcuts-and-mouse-buttons-editor'
[void] (Invoke-AppElement 'Save')
Start-Sleep -Seconds 2
$saved = Get-ShortcutSettings
$added = @($saved.triggers)[-1]
Test-Check 'Save adds the shortcut' ((@($saved.triggers).Count -eq 6) -and ($added.action.id -eq 'hideOthers') -and ($added.shortcut.key -eq 'KeyU') -and (($added.shortcut.modifiers -join ',') -eq 'Control,Alt')) "saved: $($saved | ConvertTo-Json -Compress -Depth 6)"
Test-Check 'the page lists it' ((Get-WindowText) -contains 'Ctrl + Alt + U')
if ($holder) { Stop-Process -Id $holder.Id -Force -ErrorAction SilentlyContinue }

# Edit shows the shortcut, and Not in these apps adds the test program through Running app, which
# opens in the editor's place and gives it back.
$app = Start-Process $shortcutExe -PassThru
[void] (Wait-Until { [Win32]::FindWindow([NullString]::Value, 'ShortcutApp') -ne [IntPtr]::Zero } 15)
Test-Check 'Edit opens the editor with the shortcut' ((Invoke-AppElement 'Edit Ctrl + Alt + U') -and (Wait-Until { (Get-AppBox $boxName) -eq 'Ctrl + Alt + U' } 5)) "box: $(Get-AppBox $boxName)"
Test-Check 'Add an app offers Running app' ((Invoke-AppElement 'Add an app') -and (Wait-Until { Find-AppElement 'Running app…' } 5))
[void] (Invoke-AppElement 'Running app…')
Test-Check 'Running app lists the test program' (Wait-Until { $null -ne (Find-RunningApp 'shortcut-app*') } 15)
$running = Find-RunningApp 'shortcut-app*'
$runningName = if ($running) { $running.Current.Name } else { '' }
if ($running) { $running.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select(); Start-Sleep -Milliseconds 300 }
Test-Check 'Running app''s Add adds it' (Invoke-AppButton 'Add')
Test-Check 'the editor comes back with the test program listed' (Wait-Until { (Find-AppElement "Remove $runningName") -and ((Get-AppBox $boxName) -eq 'Ctrl + Alt + U') } 10) "listed: $runningName"
[void] (Invoke-AppElement 'Save')
Start-Sleep -Seconds 2
$listed = @(@(Get-ShortcutSettings).triggers)[-1].excludedApps
Test-Check 'Save keeps the test program in Not in these apps' ((@($listed).Count -eq 1) -and (@($listed)[0].program -like '*shortcut-app.exe')) "saved: $($listed | ConvertTo-Json -Compress)"

# Alt + F4 in the box is recorded rather than closing the window, and Esc without Ctrl, Alt or the
# Windows logo key closes the editor.
[void] (Invoke-AppElement 'Add a shortcut or mouse button')
Start-Sleep -Seconds 1
# Alt + F4 and Esc go to the window in front, so only to the settings window.
$listening = (Select-ShortcutBox 'Shortcut') -and ([Win32]::GetForegroundWindow() -eq $appWindow)
Test-Check 'the editor''s box listens in the window in front' $listening
if ($listening) { Send-Keys @($LeftAlt, $KeyF4) }
Test-Check 'Alt + F4 in the box is recorded, and the window stays' (((Get-AppBox 'Shortcut') -eq 'Alt + F4') -and (Get-AppWindow)) "box: $(Get-AppBox 'Shortcut')"
if ([Win32]::GetForegroundWindow() -eq $appWindow) { Send-Key 0x1B }
Test-Check 'Esc closes the editor' (Wait-Until { -not (Find-AppBox 'Shortcut') } 5)
Test-Check 'nothing is added' (@((Get-ShortcutSettings).triggers).Count -eq 6)
[void] (Close-SettingsApp)

# Ctrl + Alt + U is left to the test program, which Not in these apps lists, and Ctrl + Alt + J runs
# Hide app on it.
$window = [Win32]::FindWindow([NullString]::Value, 'ShortcutApp')
Test-Check 'the test program shows its window' ($window -ne [IntPtr]::Zero)
Select-Window $window
Test-Check 'the host leaves Ctrl + Alt + U to the listed app in front' (Test-Logged 'Shortcuts & Mouse Buttons left 1 shortcuts to the app in front' 5)
Send-Keys @($LeftControl, $LeftAlt, $KeyU)
Test-Check 'Ctrl + Alt + U reaches the listed app, and Hide others doesn''t run' ((-not [Win32]::IsIconic($window)) -and -not (Test-Logged 'runs hideApps''s action hideOthers' 2))
Send-Keys @($LeftControl, $LeftAlt, $KeyJ)
Test-Check 'Ctrl + Alt + J runs Hide app on the test program' (Wait-Until { [Win32]::IsIconic($window) } 5)
Test-Check 'the host logs which action ran' (Test-Logged "Shortcuts & Mouse Buttons runs hideApps's action hideApp, for Control\+Alt\+KeyJ\.")
Stop-Process -Id $app.Id -Force -ErrorAction SilentlyContinue
Stop-Host

# Another program has Ctrl + Alt + J: the row says so until the program lets it go.
$onlyJ = "{ ""enabled"": true, ""triggers"": [ $hideAppOnJ ] }"
$onlyJPaused = "{ ""enabled"": true, ""triggers"": [ $hideAppOnJ ], ""pauseMouseButtonsInFullScreen"": false }"
Start-Host
Test-ShortcutTakenNote 'Ctrl + Alt + J' 0x3 0x4A { Write-ShortcutSettings '{ "enabled": false }' $hideApps } { Write-ShortcutSettings $onlyJ $hideApps } { Write-ShortcutSettings $onlyJPaused $hideApps } `
    'Shortcuts & Mouse Buttons' 'Another program uses Ctrl + Alt + J, so this shortcut does nothing here. PC Companion tries again when a setting here changes.' '20c-shortcuts-and-mouse-buttons-taken'

# The page in Arabic with the dark theme.
Stop-Host
Write-ShortcutSettings $list $hideApps '"language": "ar", "theme": "Dark"'
Test-Check 'select Shortcuts & Mouse Buttons in Arabic' (Open-Page 'الاختصارات وأزرار الماوس')
Save-Screenshot '20d-shortcuts-and-mouse-buttons-arabic-dark'
$text = Get-WindowText
Test-Check 'the page is in Arabic' ($text -contains 'الاختصارات في الصفحات الأخرى') ($text -join ' | ')
Test-Check 'the editor opens in Arabic' ((Invoke-AppElement 'إضافة اختصار أو زر ماوس') -and (Wait-Until { Find-AppText 'إضافة اختصار أو زر ماوس' } 5))
[void] (Invoke-AppElement 'اختيار المفاتيح')
Start-Sleep -Seconds 1
Save-Screenshot '20f-shortcuts-and-mouse-buttons-editor-arabic-dark'
[void] (Invoke-AppButton 'إلغاء الأمر')
[void] (Close-SettingsApp)

Remove-Item $shortcutFolder -Recurse -Force -ErrorAction SilentlyContinue
Write-ShortcutSettings '{ "enabled": false }'
