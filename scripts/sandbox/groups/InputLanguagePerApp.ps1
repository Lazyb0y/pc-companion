# Input Language per App, with German, US-International and Japanese added inside Windows Sandbox,
# whose languages stay in it, as the spike showed. Windows + Space is never pressed: inside the
# Sandbox, it brought the Sandbox's window to the front on the PC that ran it. Before the languages
# are added, the page says there's only one and opens Language & region. Turned on from Home, the
# page lists the input methods by Windows' names and saves the identifiers Windows lists, a listed
# app added with Running app, the behavior for other apps and the notice switch, and shows Windows'
# own setting with its Open. With test programs, which are other apps: a rule switches an app when
# it comes to the front, as a typed key shows, with the notice in the light and dark Windows modes
# and in Arabic; Remember gives an app back its language, also after the host restarts; Keep
# switches nothing; Use one language switches an app without a rule; with Windows' setting on,
# rules work the same; File Explorer, Settings and a console switch; a full-screen window skips the
# notice; a host without admin rights is refused by an app with them; and turned off, the file of
# remembered languages goes and nothing switches. The user's language list stays as the group set
# it. Detailed logging is on for the Debug entries.
$detailedUntil = [DateTimeOffset]::Now.AddHours(1).ToString('o')
$noticeClass = 'PCCompanion.Notice'
$rememberedFile = Join-Path $dataFolder 'input-language-per-app.json'
$personalize = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize'
$lightModeAtStart = (Get-ItemProperty $personalize -Name SystemUsesLightTheme -ErrorAction SilentlyContinue).SystemUsesLightTheme
$languagesAtStart = Get-WinUserLanguageList
$work = [System.Windows.Forms.Screen]::PrimaryScreen.WorkingArea
$graphics = [System.Drawing.Graphics]::FromHwnd([IntPtr]::Zero)
$margin = [int] [Math]::Round(24 * $graphics.DpiX / 96)
$graphics.Dispose()

# Handles of the input methods, as GetKeyboardLayout gives them; Japanese ends in 0411.
$englishLayout = '04090409'
$germanLayout = '04070407'

Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class LanguageChecks {
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
    [DllImport("user32.dll")] private static extern IntPtr GetKeyboardLayout(uint thread);
    [DllImport("user32.dll")] private static extern bool SystemParametersInfo(uint action, uint parameter, IntPtr value, uint update);

    // The input method the window's thread types with, as 8 hexadecimal digits.
    public static string Layout(IntPtr window) {
        uint processId;
        var thread = GetWindowThreadProcessId(window, out processId);
        return ((uint)(long)GetKeyboardLayout(thread)).ToString("X8");
    }

    // Let me use a different input method for each app window (SPI_SETTHREADLOCALINPUTSETTINGS).
    public static bool SetPerWindow(bool on) { return SystemParametersInfo(0x104F, 0, on ? (IntPtr)1 : IntPtr.Zero, 2); }
}
'@

# The test program, run as copies with other names, which are other apps: a window titled after its
# first argument, at the place in its second argument, with a box that has the focus; the title
# shows what's typed in it after a |. A posted WM_APP + 1 empties the box.
$languageFolder = Join-Path $env:TEMP 'PCCompanionInputLanguages'
New-Item -ItemType Directory -Force -Path $languageFolder | Out-Null
$languageExe = Join-Path $languageFolder 'lang-app.exe'
Add-Type -OutputAssembly $languageExe -OutputType WindowsApplication -ReferencedAssemblies System.Windows.Forms, System.Drawing -TypeDefinition @'
using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
public static class LanguageApp {
    [DllImport("user32.dll")] private static extern bool SetProcessDPIAware();

    private sealed class Window : Form {
        public readonly TextBox Box = new TextBox { Dock = DockStyle.Fill, Multiline = true };
        protected override void WndProc(ref Message message) {
            if (message.Msg == 0x8001) { Box.Clear(); return; }
            base.WndProc(ref message);
        }
    }

    [STAThread]
    public static void Main(string[] args) {
        SetProcessDPIAware();
        var place = args[1].Split(',');
        var window = new Window { Text = args[0], StartPosition = FormStartPosition.Manual, Bounds = new Rectangle(int.Parse(place[0]), int.Parse(place[1]), 420, 260) };
        window.Controls.Add(window.Box);
        window.Box.TextChanged += delegate { window.Text = args[0] + "|" + window.Box.Text; };
        window.Shown += delegate { window.Box.Focus(); };
        Application.Run(window);
    }
}
'@
$processes = @()
function Start-LanguageApp([string] $Name, [string] $Place) {
    $exe = Join-Path $languageFolder "$Name.exe"
    Copy-Item $languageExe $exe -Force
    $script:processes += Start-Process $exe -ArgumentList "PCCompanionLanguage-$Name", $Place -PassThru
    [void] (Wait-Until { [Win32]::FindWindow([NullString]::Value, "PCCompanionLanguage-$Name") -ne [IntPtr]::Zero } 20)
    return [Win32]::FindWindow([NullString]::Value, "PCCompanionLanguage-$Name")
}

# Brings a window to the front, as Hide Apps' checks do: a press of a key that has no use first
# gives this script the right to.
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

# Switches a window's input method as the user's own switch does, with the request Windows posts.
function Switch-ByHand([IntPtr] $Window, [string] $Layout) {
    [void] [Win32]::PostMessage($Window, 0x0050, [UIntPtr]::Zero, [IntPtr] [Convert]::ToInt32($Layout, 16))
    [void] (Wait-Until { [LanguageChecks]::Layout($Window) -eq $Layout } 5)
}

# What the key that types y in US English and z in German types in the test program in front.
function Get-Typed([IntPtr] $Window) {
    [void] [Win32]::PostMessage($Window, 0x8001, [UIntPtr]::Zero, [IntPtr]::Zero)
    Start-Sleep -Milliseconds 300
    [void] [Win32]::ScanKey(0x15, $false, $false)
    [void] [Win32]::ScanKey(0x15, $true, $false)
    Start-Sleep -Milliseconds 500
    return ([Win32]::WindowTitle($Window) -split '\|', 2)[1]
}

# App B gets English by hand, then app A comes to the front from it, as from any other app.
function Switch-ToFromB([IntPtr] $Window) {
    Select-Window $b
    Switch-ByHand $b $englishLayout
    Select-Window $Window
}

function Get-Languages { (Get-WinUserLanguageList | ForEach-Object { "$($_.LanguageTag) $($_.InputMethodTips -join ',')" }) -join '; ' }
function Get-LanguageSettings { (Get-Content (Join-Path $dataFolder 'settings.json') -Raw | ConvertFrom-Json).features.inputLanguagePerApp }
# The requests the host made: each is held, not taken, refused, not asked for or asked of a console unread.
function Get-Asked { Get-MatchCount (Get-HostLog) '(held after|didn''t take a switch|refused a switch|couldn''t be asked for|asked a console for a switch)' }
function Get-Held([string] $Source) { Get-MatchCount (Get-HostLog) "a switch from ""?$Source""? held after" }
function Test-Logged([string] $Pattern, [int] $Seconds = 5) { Wait-Until { (Get-HostLog) -match $Pattern } $Seconds }
function Find-Notice { [Win32]::FindWindow($noticeClass, [NullString]::Value) }
function Test-NoticeShown { $notice = Find-Notice; ($notice -ne [IntPtr]::Zero) -and [Win32]::IsWindowVisible($notice) }

function Write-LanguageSettings($Section, [string] $Language = '') {
    $general = [ordered] @{ theme = 'Light' }
    if ($Language) { $general.language = $Language }
    $applied = Get-MatchCount (Get-HostLog) 'Input Language per App (applied its settings|stopped)'
    Write-Settings ([ordered] @{
        schemaVersion = 1
        general = $general
        updates = @{ checkAutomatically = $false }
        diagnostics = @{ detailedLoggingUntil = $detailedUntil }
        features = @{ inputLanguagePerApp = $Section }
    } | ConvertTo-Json -Depth 8)
    [void] (Wait-Until { (Get-MatchCount (Get-HostLog) 'Input Language per App (applied its settings|stopped)') -gt $applied } 10)
    Start-Sleep -Milliseconds 500
}
function Rule([string] $Name, [string] $InputMethod, [string] $Program = '', [string] $AppId = '') {
    $rule = [ordered] @{ name = $Name; inputMethod = $InputMethod }
    if ($Program) { $rule.program = $Program } else { $rule.appId = $AppId }
    return $rule
}
function Start-Host {
    if ((Get-Processes 'PCCompanion.Host').Count -eq 0) {
        Start-Process $hostExe
        [void] (Wait-Until { [Win32]::FindWindow('PCCompanion.Host', $null) -ne [IntPtr]::Zero } 30)
    }
    [void] (Wait-Until { [Win32]::FindWindow('PCCompanion.InputLanguagePerApp', $null) -ne [IntPtr]::Zero } 20)
    Start-Sleep -Seconds 2
}
function Stop-Host {
    [void] [Win32]::PostMessage([Win32]::FindWindow('PCCompanion.Host', $null), 0x0010, [UIntPtr]::Zero, [IntPtr]::Zero)
    [void] (Wait-Until { (Get-Processes 'PCCompanion.Host').Count -eq 0 } 15)
}

# Waits for the notice, then checks its name, its place and colors, and that it goes after its time.
function Test-LanguageNotice([string] $When, [string] $Screenshot, [bool] $Dark = $false) {
    $shown = Wait-Until { Test-NoticeShown } 5
    Test-Check "$When shows the notice" $shown
    if (-not $shown) { return }
    $shownAt = Get-Date
    Start-Sleep -Milliseconds 400
    $title = [Win32]::WindowTitle((Find-Notice))
    Test-Check "$When, the notice shows the language's and the layout's names" ($title -eq 'German (Germany), German') "name: $title"
    $area = New-Object Win32+RECT
    [void] [Win32]::GetWindowRect((Find-Notice), [ref] $area)
    $centered = [Math]::Abs([int] (($area.Left + $area.Right) / 2) - [int] ($work.Left + $work.Width / 2)) -le 1
    Test-Check "$When, the notice is at the bottom center of the work area" ($centered -and ($area.Bottom -eq $work.Bottom - $margin)) "notice at $($area.Left), $($area.Top), $($area.Right), $($area.Bottom); work area $work; margin $margin"
    $bitmap = New-Object System.Drawing.Bitmap 1, 1
    $pixels = [System.Drawing.Graphics]::FromImage($bitmap)
    $pixels.CopyFromScreen($area.Left + 4, [int] (($area.Top + $area.Bottom) / 2), 0, 0, $bitmap.Size)
    $background = $bitmap.GetPixel(0, 0)
    $pixels.Dispose()
    $bitmap.Dispose()
    $looks = if ($Dark) { $background.R -lt 80 -and $background.G -lt 80 -and $background.B -lt 80 } else { $background.R -gt 200 -and $background.G -gt 200 -and $background.B -gt 200 }
    Test-Check "$When, the notice has the Windows mode's colors" $looks "background $background"
    Save-Screenshot $Screenshot
    $gone = Wait-Until { -not (Test-NoticeShown) } 10
    $seconds = ((Get-Date) - $shownAt).TotalSeconds
    Test-Check "$When, the notice goes after 2 seconds" ($gone -and $seconds -lt 6) "gone: $gone after $([Math]::Round($seconds, 1)) s"
}

# Before more languages are added: turned on from Home, the page says there's only one.
[void] (Close-SettingsApp)
Write-Settings (@{ schemaVersion = 1; general = @{ theme = 'Light' }; updates = @{ checkAutomatically = $false }; diagnostics = @{ detailedLoggingUntil = $detailedUntil } } | ConvertTo-Json -Depth 4)
Start-Sleep -Seconds 2
Start-Process $appExe
[void] (Wait-Until { Get-AppWindow })
Start-Sleep -Seconds 3
Test-Check 'turn on Input Language per App on Home' (Set-Toggle 'Input Language per App' $true)
Start-Sleep -Seconds 2
$saved = Get-LanguageSettings
Test-Check 'the Home switch saves Input Language per App as on, with its defaults' (($saved.enabled -eq $true) -and ($null -eq $saved.otherApps) -and ($null -eq $saved.apps)) ($saved | ConvertTo-Json -Compress)
Test-Check 'the host starts Input Language per App' (Test-Logged 'Input Language per App started\.' 15)
Test-Check 'select Input Language per App' (Select-NavigationItem 'Input Language per App')
Start-Sleep -Seconds 2
$text = Get-WindowText
Test-Check 'Input Language per App shows its settings' ((@('Apps with a language', 'Other apps', 'Show a notice when the language changes', "Windows' own setting", 'Permissions & data') | Where-Object { $text -notcontains $_ }).Count -eq 0) ($text -join ' | ')
Test-Check 'with one input method, the page says so' (Wait-Until { Find-AppText 'You have only one keyboard or language. Add another in Language & region to give apps their own.' } 5)
Save-Screenshot '18a-input-language-one-language'
Test-Check 'its Open opens Language & region' ((Invoke-AppElement 'Open Language & region') -and (Wait-Until { Get-Processes 'SystemSettings' } 15))
Start-Sleep -Seconds 2
Get-Processes 'SystemSettings' | Stop-Process -Force -ErrorAction SilentlyContinue
[void] (Close-SettingsApp)

# German, US-International and Japanese, inside the Sandbox only.
$list = New-WinUserLanguageList en-US
$list[0].InputMethodTips.Add('0409:00020409')
$list.Add('de-DE')
$list.Add('ja-JP')
Set-WinUserLanguageList $list -Force
Start-Sleep -Seconds 3
$languages = Get-Languages
Test-Check 'the Sandbox has German, US-International and Japanese' (($languages -match '0407:00000407') -and ($languages -match '0409:00020409') -and ($languages -match '0411:')) $languages
Stop-Host
Start-Host

# The page lists the input methods by Windows' names and saves the identifiers Windows lists.
$a = Start-LanguageApp 'lang-a' '60,60'
$b = Start-LanguageApp 'lang-b' '560,60'
Start-Process $appExe
[void] (Wait-Until { Get-AppWindow })
Start-Sleep -Seconds 3
[void] (Select-NavigationItem 'Input Language per App')
Start-Sleep -Seconds 2
Test-Check 'with several input methods, the note goes' (-not (Find-AppText 'You have only one keyboard or language. Add another in Language & region to give apps their own.'))
Test-Check 'choose Use one language' (Select-ListItem 'Other apps' 'Use one language')
Start-Sleep -Seconds 1
$saved = Get-LanguageSettings
Test-Check 'the page saves Use one language, with the first input method' (($saved.otherApps -eq 'Use') -and ($saved.otherAppsInputMethod -eq '0409:00000409')) ($saved | ConvertTo-Json -Compress)
$tips = @((Get-WinUserLanguageList).InputMethodTips)
foreach ($choice in @(@{ Name = 'German (Germany) – German'; Id = '0407:00000407' }, @{ Name = 'English (United States) – United States-International'; Id = '0409:00020409' }, @{ Name = 'Japanese (Japan)'; Id = '0411' })) {
    $chosen = Select-ListItem 'Language for other apps' $choice.Name
    Start-Sleep -Seconds 1
    $saved = (Get-LanguageSettings).otherAppsInputMethod
    $listed = ($choice.Id.Length -eq 4 -and ($tips | Where-Object { $_ -like "$($choice.Id):*" })) -or ($tips -contains $choice.Id)
    Test-Check "the page lists $($choice.Name) and saves the identifier Windows lists" ($chosen -and ($saved -eq $choice.Id) -and $listed) "saved: $saved; Windows lists: $($tips -join ', ')"
}
Test-Check 'choose Remember each app''s language again' (Select-ListItem 'Other apps' "Remember each app's language")

Test-Check 'Add an app offers Running app' ((Invoke-AppElement 'Add an app') -and (Wait-Until { Find-AppElement 'Running app…' } 5))
[void] (Invoke-AppElement 'Running app…')
$appItem = $null
Test-Check 'Running app lists a test program' (Wait-Until { ($script:appItem = Find-AppListItem 'lang-a') -ne $null } 15)
if ($appItem) { $appItem.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select(); Start-Sleep -Milliseconds 300 }
[void] (Invoke-AppElement 'Add')
$langA = Join-Path $languageFolder 'lang-a.exe'
$langB = Join-Path $languageFolder 'lang-b.exe'
Test-Check 'Running app adds it with the first input method' (Wait-Until { $apps = @((Get-LanguageSettings).apps); ($apps.Count -eq 1) -and ($apps[0].program -eq $langA) -and ($apps[0].inputMethod -eq '0409:00000409') } 5) "saved: $(Get-LanguageSettings | ConvertTo-Json -Compress)"
Start-Sleep -Seconds 1
$focused = [System.Windows.Automation.AutomationElement]::FocusedElement
Test-Check 'the focus goes to the new app''s language list' ($focused.Current.Name -eq 'Language for lang-a') "focused: $($focused.Current.Name)"
Test-Check 'choose German for the app' (Select-ListItem 'Language for lang-a' 'German (Germany) – German')
Start-Sleep -Seconds 1
Test-Check 'the page saves the app''s input method' (@((Get-LanguageSettings).apps)[0].inputMethod -eq '0407:00000407') "saved: $(Get-LanguageSettings | ConvertTo-Json -Compress)"
Save-Screenshot '18b-input-language-page'
Test-Check 'turn off the notice' (Set-Toggle 'Show a notice when the language changes' $false)
Start-Sleep -Seconds 1
$off = (Get-LanguageSettings).showNotice -eq $false
[void] (Set-Toggle 'Show a notice when the language changes' $true)
Start-Sleep -Seconds 1
Test-Check 'the page saves the notice switch' ($off -and ((Get-LanguageSettings).showNotice -eq $true))
Test-Check 'the page says Windows'' own setting is off' (Wait-Until { Find-AppText 'Let me use a different input method for each app window is off: with Keep the language in use, the language in use follows you into every app. It''s in Typing, under Advanced keyboard settings.' } 3)
Test-Check 'its Open opens Windows'' Typing settings' ((Invoke-AppElement 'Open') -and (Wait-Until { Get-Processes 'SystemSettings' } 15))
Start-Sleep -Seconds 2
Get-Processes 'SystemSettings' | Stop-Process -Force -ErrorAction SilentlyContinue
[void] (Close-SettingsApp)

# After Settings was ended and the settings app closed, the host didn't hear of windows coming to
# the front for about 13 seconds in Windows Sandbox, so the checks wait until it does, and say how long.
$heard = Get-MatchCount (Get-HostLog) 'another app came to the front'
$waited = Measure-Command { [void] (Wait-Until { Select-Window $b; Select-Window $a; (Get-MatchCount (Get-HostLog) 'another app came to the front') -gt $heard } 30) }
Write-Report "[NOTE] After Settings was ended, the host heard of an app coming to the front again after $([Math]::Round($waited.TotalSeconds)) s."

# A rule switches its app when it comes to the front, and the notice shows the new language.
$languagesSet = Get-Languages
Write-LanguageSettings ([ordered] @{ enabled = $true; apps = @(Rule 'A' '0407:00000407' $langA); otherApps = 'Keep' })
$held = Get-Held 'Rule'
Switch-ToFromB $a
Test-Check 'a rule switches its app when it comes to the front' ((Wait-Until { (Get-Held 'Rule') -gt $held } 5) -and ([LanguageChecks]::Layout($a) -eq $germanLayout)) "layout $([LanguageChecks]::Layout($a))"
Test-LanguageNotice 'a switch' '18c-input-language-notice-light'
$typed = Get-Typed $a
Test-Check 'a typed key goes in German' ($typed -eq 'z') "typed: $typed"
$asked = Get-Asked
Select-Window $b
Select-Window $a
Start-Sleep -Seconds 1
Test-Check 'an app that has its language already isn''t asked again' (((Get-Asked) -eq $asked) -and ([LanguageChecks]::Layout($a) -eq $germanLayout)) "layout $([LanguageChecks]::Layout($a))"

Set-ItemProperty $personalize -Name SystemUsesLightTheme -Value 0 -Type DWord
Switch-ToFromB $a
Test-LanguageNotice 'in the dark Windows mode' '18d-input-language-notice-dark' -Dark $true
Set-ItemProperty $personalize -Name SystemUsesLightTheme -Value $(if ($null -eq $lightModeAtStart) { 1 } else { $lightModeAtStart }) -Type DWord

Write-LanguageSettings ([ordered] @{ enabled = $true; apps = @(Rule 'A in Arabic' '0407:00000407' $langA); otherApps = 'Keep' }) 'ar'
Switch-ToFromB $a
Test-Check 'in Arabic, the notice shows' (Wait-Until { Test-NoticeShown } 5)
Start-Sleep -Milliseconds 400
Save-Screenshot '18e-input-language-notice-arabic'
[void] (Wait-Until { -not (Test-NoticeShown) } 10)

# Keep switches nothing.
Write-LanguageSettings ([ordered] @{ enabled = $true; otherApps = 'Keep' })
$all = Get-Asked
Select-Window $a
Switch-ByHand $a $germanLayout
Switch-ToFromB $a
Start-Sleep -Seconds 1
Test-Check 'Keep switches nothing' (((Get-Asked) -eq $all) -and ([LanguageChecks]::Layout($a) -eq $englishLayout)) "layout $([LanguageChecks]::Layout($a))"

# Use one language switches an app without a rule.
Write-LanguageSettings ([ordered] @{ enabled = $true; otherApps = 'Use'; otherAppsInputMethod = '0411' })
$held = Get-Held 'OneLanguage'
Switch-ToFromB $a
Test-Check 'Use one language switches an app without a rule' ((Wait-Until { (Get-Held 'OneLanguage') -gt $held } 5) -and ([LanguageChecks]::Layout($a) -like '*0411')) "layout $([LanguageChecks]::Layout($a))"
[void] (Wait-Until { -not (Test-NoticeShown) } 10)

# Remember gives an app back the language it had when the user left it, also after the host restarts.
Write-LanguageSettings ([ordered] @{ enabled = $true })
Select-Window $a
Switch-ByHand $a $germanLayout
$held = Get-Held 'Remembered'
Switch-ToFromB $a
Test-Check 'Remember gives an app back its language' ((Wait-Until { (Get-Held 'Remembered') -gt $held } 5) -and ([LanguageChecks]::Layout($a) -eq $germanLayout)) "layout $([LanguageChecks]::Layout($a))"
$typed = Get-Typed $a
Test-Check 'a typed key goes in the remembered language' ($typed -eq 'z') "typed: $typed"
[void] (Wait-Until { -not (Test-NoticeShown) } 10)
Select-Window $b
Switch-ByHand $b $englishLayout
Select-Window $a
Start-Sleep -Seconds 1
Stop-Host
$file = if (Test-Path $rememberedFile) { Get-Content $rememberedFile -Raw | ConvertFrom-Json } else { $null }
Test-Check 'the host writes the remembered languages as it exits' (($null -ne $file) -and ((@($file.apps) | Where-Object { $_.program -eq $langB }).inputMethod -eq '0409:00000409') -and ((@($file.apps) | Where-Object { $_.program -eq $langA }).inputMethod -eq '0407:00000407')) "file: $(if ($file) { $file | ConvertTo-Json -Compress -Depth 4 } else { 'none' })"
Start-Host
Test-Check 'the host reads them when it starts' (Test-Logged 'Input Language per App read [1-9][0-9]* remembered input methods\.' 10)
$held = Get-Held 'Remembered'
Select-Window $a
Select-Window $b
Test-Check 'after the restart, an app gets back its language' ((Wait-Until { (Get-Held 'Remembered') -gt $held } 5) -and ([LanguageChecks]::Layout($b) -eq $englishLayout)) "layout $([LanguageChecks]::Layout($b))"
[void] (Wait-Until { -not (Test-NoticeShown) } 10)

# With Windows' setting on, changed only in the Sandbox, a rule works the same.
Test-Check 'turn on Windows'' setting in the Sandbox' ([LanguageChecks]::SetPerWindow($true))
Write-LanguageSettings ([ordered] @{ enabled = $true; apps = @(Rule 'A' '0407:00000407' $langA); otherApps = 'Keep' })
Select-Window $a
Switch-ByHand $a $englishLayout
$held = Get-Held 'Rule'
Select-Window $b
Select-Window $a
Test-Check 'with Windows'' setting on, a rule switches its app' ((Wait-Until { (Get-Held 'Rule') -gt $held } 5) -and ([LanguageChecks]::Layout($a) -eq $germanLayout)) "layout $([LanguageChecks]::Layout($a))"
[void] ([LanguageChecks]::SetPerWindow($false))
[void] (Wait-Until { -not (Test-NoticeShown) } 10)

# File Explorer, Settings and a console window switch.
$explorerProgram = Join-Path $env:windir 'explorer.exe'
$consoleProgram = Join-Path $env:windir 'System32\cmd.exe'
$settingsAppId = 'windows.immersivecontrolpanel_cw5n1h2txyewy!microsoft.windows.immersivecontrolpanel'
Write-LanguageSettings ([ordered] @{ enabled = $true; apps = @((Rule 'File Explorer' '0407:00000407' $explorerProgram), (Rule 'Settings' '0407:00000407' '' $settingsAppId), (Rule 'Command Prompt' '0407:00000407' $consoleProgram)); otherApps = 'Keep' })
Start-Process explorer.exe $env:windir
[void] (Wait-Until { ($script:explorer = @([Win32]::TopLevelWindows() | Where-Object { [Win32]::WindowClass($_) -eq 'CabinetWClass' })[0]) -ne $null } 15)
$held = Get-Held 'Rule'
if ($explorer) { Switch-ToFromB $explorer }
Test-Check 'File Explorer switches' ($explorer -and (Wait-Until { (Get-Held 'Rule') -gt $held } 5))
[void] (Wait-Until { -not (Test-NoticeShown) } 10)
Start-Process 'ms-settings:'
[void] (Wait-Until { ($script:settingsWindow = @([Win32]::TopLevelWindows() | Where-Object { ([Win32]::WindowClass($_) -eq 'ApplicationFrameWindow') -and ([Win32]::WindowTitle($_) -eq 'Settings') })[0]) -ne $null } 20)
Start-Sleep -Seconds 2
$held = Get-Held 'Rule'
if ($settingsWindow) { Switch-ToFromB $settingsWindow }
Test-Check 'Settings switches' ($settingsWindow -and (Wait-Until { (Get-Held 'Rule') -gt $held } 5))
[void] (Wait-Until { -not (Test-NoticeShown) } 10)
# The console comes to the front from an app with English, so it needs a switch.
Select-Window $b
Switch-ByHand $b $englishLayout
$consoleProcess = Start-Process $consoleProgram -PassThru
[void] (Wait-Until { ($script:console = @([Win32]::TopLevelWindows() | Where-Object { ([Win32]::WindowClass($_) -eq 'ConsoleWindowClass') -and ([Win32]::WindowTitle($_) -like '*cmd.exe') })[0]) -ne $null } 15)
if ($console) { Switch-ToFromB $console }
Test-Check 'a console switches without reads until its console host is found' ($console -and (Test-Logged 'asked a console for a switch from "?Rule"? without reading it' 5) -and (Test-Logged 'console host found: "?true"?' 5))
$held = Get-Held 'Rule'
if ($console) { Switch-ToFromB $console }
Test-Check 'then a console switches through its console host' ($console -and (Wait-Until { (Get-Held 'Rule') -gt $held } 5))
[void] (Wait-Until { -not (Test-NoticeShown) } 10)
Stop-Process -Id $consoleProcess.Id -Force -ErrorAction SilentlyContinue
foreach ($window in $script:explorer, $script:settingsWindow) { if ($window) { [void] [Win32]::PostMessage($window, 0x0010, [UIntPtr]::Zero, [IntPtr]::Zero) } }

# A borderless window covering the display skips the notice, and the host logs why.
Write-LanguageSettings ([ordered] @{ enabled = $true; apps = @(Rule 'A' '0407:00000407' $langA); otherApps = 'Keep' })
$fullScreen = Start-TestWindow 'FullScreen'
$held = Get-Held 'Rule'
Switch-ToFromB $a
Test-Check 'with a full-screen window, the app still switches' (Wait-Until { (Get-Held 'Rule') -gt $held } 5)
Test-Check 'a full-screen app skips the notice, and the host logs why' ((Test-Logged 'Input Language per App showed no notice, since an app was full screen\.' 5) -and -not (Test-NoticeShown))
Stop-Process -Id $fullScreen.Id -Force -ErrorAction SilentlyContinue
[void] (Wait-Until { (Find-TestWindow 'FullScreen') -eq [IntPtr]::Zero } 10)

# Everything in Windows Sandbox runs as administrator; a host whose file is labeled Medium integrity
# runs without admin rights, and the test programs, which have them, refuse its requests.
Stop-Host
& icacls.exe $hostExe /setintegritylevel medium | Out-Null
Start-Host
Switch-ToFromB $a
Test-Check 'an app running as administrator refuses a host without admin rights, which logs it' ((Test-Logged 'Windows refused a switch from "?Rule"?, as for an app running as administrator\.' 5) -and ([LanguageChecks]::Layout($a) -eq $englishLayout)) "layout $([LanguageChecks]::Layout($a))"
Stop-Host
& icacls.exe $hostExe /setintegritylevel high | Out-Null
Start-Host

# Turned off, the remembered languages go and nothing switches.
Write-LanguageSettings ([ordered] @{ enabled = $true })
Switch-ToFromB $a
Write-LanguageSettings ([ordered] @{ enabled = $false })
Test-Check 'the host stops Input Language per App' (Test-Logged 'Input Language per App stopped\.' 10)
Test-Check 'turned off, the remembered languages are deleted' (Wait-Until { -not (Test-Path $rememberedFile) } 5)
$all = Get-Asked
Select-Window $a
Switch-ByHand $a $germanLayout
Switch-ToFromB $a
Start-Sleep -Seconds 1
Test-Check 'turned off, nothing switches' ((Get-Asked) -eq $all)
Test-Check 'switching left the user''s language list as it was' ((Get-Languages) -eq $languagesSet) "before: $languagesSet; after: $(Get-Languages)"

foreach ($process in $processes) { Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue }
Remove-Item $languageFolder -Recurse -Force -ErrorAction SilentlyContinue
Set-WinUserLanguageList $languagesAtStart -Force
