# Window Rules: the Home switch turns it on with no rules, and the rule editor saves a rule filled in
# from Pick a window and one whose app comes from Running app, saying how many open windows each
# matches; a window open as its rule is saved gets the rule's held actions without being moved. Apply
# now places an open test window, and leaves a snapped one where it is. Test windows open at their
# rule's place, one that moves itself back is placed again, and others open maximized, with restoring
# going to the rule's place, and minimized, without the front; File Explorer, Settings and Edge open
# at their rule's place. On top, opacity and taskbar hiding apply, the taskbar button comes back
# while the window is minimized, and turning the rule off puts them back. Open as a tab of groups two
# test windows, with Window Tabs on. Where I last put it opens a window where the last one was left
# as it closed, an opacity set with Title Bar Actions' wheel is kept for the next window, and
# window-rules.json holds both without a title. Exit puts the windows back, the next start gives an
# open window its held actions without moving it, a host that was ended leaves them, and the next
# start with the feature off puts them back and deletes the file. With the host's file labeled
# Medium integrity, a window running as administrator is left alone.
[void] (Close-SettingsApp)

# Detailed logging is on for the host's Debug entries.
$detailedUntil = [DateTimeOffset]::Now.AddHours(1).ToString('o')
function Write-RulesSettings([string] $Section, [string] $Others = '') {
    Write-Settings "{ ""schemaVersion"": 1, ""general"": { ""theme"": ""Light"" }, ""diagnostics"": { ""detailedLoggingUntil"": ""$detailedUntil"" }, ""features"": { ""windowRules"": $Section$Others } }"
    Start-Sleep -Seconds 3
}
function Start-Host([switch] $Off) {
    if ((Get-Processes 'PCCompanion.Host').Count -eq 0) {
        Start-Process $hostExe
        [void] (Wait-Until { [Win32]::FindWindow('PCCompanion.Host', $null) -ne [IntPtr]::Zero } 30)
    }
    if (-not $Off) { [void] (Wait-Until { [Win32]::FindWindow('PCCompanion.WindowRules', $null) -ne [IntPtr]::Zero } 20) }
    Start-Sleep -Seconds 1
}
function Stop-Host {
    [void] [Win32]::PostMessage([Win32]::FindWindow('PCCompanion.Host', $null), 0x0010, [UIntPtr]::Zero, [IntPtr]::Zero)
    [void] (Wait-Until { (Get-Processes 'PCCompanion.Host').Count -eq 0 } 15)
}
function Get-RulesSettings { (Get-Content (Join-Path $dataFolder 'settings.json') -Raw | ConvertFrom-Json).features.windowRules }
function Test-Logged([string] $Pattern, [int] $Seconds = 5) { Wait-Until { (Get-HostLog) -match $Pattern } $Seconds }

# The test program, built once and copied under other names, which are other programs: a window
# titled after its file at the place in its first argument. With "late" second, it moves itself back
# there 0.4 s and 1.2 s after it shows, as apps that restore their own place late do.
$rulesFolder = Join-Path $env:TEMP 'PCCompanionRuleApp'
New-Item -ItemType Directory -Force -Path $rulesFolder | Out-Null
$ruleExe = @{}
foreach ($name in 'rule-a', 'rule-b', 'rule-c', 'rule-d', 'rule-e', 'rule-f', 'rule-g', 'rule-late') { $ruleExe[$name] = Join-Path $rulesFolder "$name.exe" }
Add-Type -OutputAssembly $ruleExe['rule-a'] -OutputType WindowsApplication -ReferencedAssemblies System.Windows.Forms, System.Drawing -TypeDefinition @'
using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
public static class RuleApp {
    [DllImport("user32.dll")] private static extern bool SetProcessDPIAware();

    [STAThread]
    public static void Main(string[] arguments) {
        SetProcessDPIAware();
        var name = Path.GetFileNameWithoutExtension(Application.ExecutablePath);
        var place = arguments[0].Split(',').Select(int.Parse).ToArray();
        var start = new Rectangle(place[0], place[1], place[2], place[3]);
        var window = new Form { Text = name, BackColor = Color.FromArgb(70, 120, 180), StartPosition = FormStartPosition.Manual, Bounds = start };
        if (arguments.Length > 1 && arguments[1] == "late") {
            window.Shown += delegate {
                var ticks = 0;
                var timer = new Timer { Interval = 400 };
                timer.Tick += delegate {
                    ticks++;
                    if (ticks == 1 || ticks == 3) { window.Bounds = start; }
                    if (ticks == 3) { timer.Stop(); }
                };
                timer.Start();
            };
        }
        Application.Run(window);
    }
}
'@
foreach ($name in @($ruleExe.Keys | Where-Object { $_ -ne 'rule-a' })) { Copy-Item $ruleExe['rule-a'] $ruleExe[$name] -Force }
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class RuleChecks {
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr window, int attribute, out RECT value, int size);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetProp(IntPtr window, string name);
    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr window, int index);
    [DllImport("user32.dll")] private static extern bool GetLayeredWindowAttributes(IntPtr window, out uint key, out byte alpha, out uint flags);
    [DllImport("user32.dll")] public static extern bool IsZoomed(IntPtr window);
    [DllImport("user32.dll")] public static extern bool IsWindowArranged(IntPtr window);
    [DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint count, INPUT[] inputs, int size);
    [StructLayout(LayoutKind.Sequential)] private struct MOUSEINPUT { public int dx, dy; public uint mouseData, dwFlags, time; public IntPtr extra; }
    [StructLayout(LayoutKind.Explicit)] private struct INPUT { [FieldOffset(0)] public uint type; [FieldOffset(8)] public MOUSEINPUT mi; }

    // The part of the window that shows, without its invisible borders (DWMWA_EXTENDED_FRAME_BOUNDS).
    public static RECT Visible(IntPtr window) {
        RECT bounds;
        DwmGetWindowAttribute(window, 9, out bounds, Marshal.SizeOf(typeof(RECT)));
        return bounds;
    }

    public static bool IsTopmost(IntPtr window) { return (GetWindowLong(window, -20) & 0x8) != 0; } // GWL_EXSTYLE, WS_EX_TOPMOST

    // The window's opacity in percent, rounded; 100 without the layered style.
    public static int Opacity(IntPtr window) {
        uint key, flags;
        byte alpha;
        if ((GetWindowLong(window, -20) & 0x80000) == 0 || !GetLayeredWindowAttributes(window, out key, out alpha, out flags) || (flags & 2) == 0) return 100;
        return (int)Math.Round(alpha / 2.55);
    }

    public static long Mark(IntPtr window) { return GetProp(window, "PCCompanion.WindowRules").ToInt64(); }

    public static long TabMark(IntPtr window) { return GetProp(window, "PCCompanion.WindowTabs").ToInt64(); }

    // A notch of the mouse wheel, as a mouse sends it (MOUSEEVENTF_WHEEL).
    public static bool Wheel(int data) {
        var input = new INPUT { type = 0 };
        input.mi.dwFlags = 0x0800;
        input.mi.mouseData = unchecked((uint)data);
        return SendInput(1, new[] { input }, Marshal.SizeOf(typeof(INPUT))) == 1;
    }
}
'@
function Find-RuleWindow([string] $Name) { [Win32]::FindWindow([NullString]::Value, $Name) }
function Start-RuleApp([string] $Name, [string] $Place, [string] $Kind = '') {
    $arguments = @($Place)
    if ($Kind) { $arguments += $Kind }
    $process = Start-Process $ruleExe[$Name] -ArgumentList $arguments -PassThru
    [void] (Wait-Until { (Find-RuleWindow $Name) -ne [IntPtr]::Zero } 30)
    Start-Sleep -Seconds 1
    return $process
}
function Stop-RuleApps {
    Get-Process 'rule-*' -ErrorAction SilentlyContinue | Stop-Process -Force
    Start-Sleep -Seconds 1
}
function Close-Window([IntPtr] $Window) { [void] [Win32]::PostMessage($Window, 0x0010, [UIntPtr]::Zero, [IntPtr]::Zero) }
function Find-TopWindow([string] $Class, [string] $Like) {
    [Win32]::TopLevelWindows() | Where-Object { ([Win32]::WindowClass($_) -eq $Class) -and [Win32]::IsWindowVisible($_) -and ([Win32]::WindowTitle($_) -like $Like) } | Select-Object -First 1
}

# Places in the main display's work area, and whether a window's frame is at one.
$work = [System.Windows.Forms.Screen]::PrimaryScreen.WorkingArea
function Get-Part([double] $Left, [double] $Top, [double] $Width, [double] $Height) {
    New-Object System.Drawing.Rectangle(([int] ($work.Left + $work.Width * $Left)), ([int] ($work.Top + $work.Height * $Top)), ([int] ($work.Width * $Width)), ([int] ($work.Height * $Height)))
}
function Test-SameFrame($Frame, [System.Drawing.Rectangle] $Want) {
    ([math]::Abs($Frame.Left - $Want.Left) -le 2) -and ([math]::Abs($Frame.Top - $Want.Top) -le 2) -and
        ([math]::Abs($Frame.Right - $Want.Right) -le 2) -and ([math]::Abs($Frame.Bottom - $Want.Bottom) -le 2)
}
function Test-Frame([IntPtr] $Window, [System.Drawing.Rectangle] $Want) { ($Window -ne [IntPtr]::Zero) -and (Test-SameFrame ([RuleChecks]::Visible($Window)) $Want) }
function ConvertTo-Rectangle($Frame) { New-Object System.Drawing.Rectangle($Frame.Left, $Frame.Top, ($Frame.Right - $Frame.Left), ($Frame.Bottom - $Frame.Top)) }
function Get-RuleState([IntPtr] $Window, $Want = $null) {
    if ($Window -eq [IntPtr]::Zero) { return '(no window)' }
    $frame = [RuleChecks]::Visible($Window)
    $state = "$([Win32]::WindowTitle($Window)): frame $($frame.Left),$($frame.Top),$($frame.Right),$($frame.Bottom), visible $([Win32]::IsWindowVisible($Window)), minimized $([Win32]::IsIconic($Window)), maximized $([RuleChecks]::IsZoomed($Window)), on top $([RuleChecks]::IsTopmost($Window)), opacity $([RuleChecks]::Opacity($Window)), mark $([RuleChecks]::Mark($Window)), tab mark $([RuleChecks]::TabMark($Window)), front $([Win32]::GetForegroundWindow() -eq $Window)"
    if ($Want) { $state += "; want $($Want.Left),$($Want.Top),$($Want.Right),$($Want.Bottom)" }
    return $state
}

# Whether an app has a button on the taskbar, by the name UI Automation gives it.
function Get-Taskbar {
    $condition = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ClassNameProperty, 'Shell_TrayWnd')
    [System.Windows.Automation.AutomationElement]::RootElement.FindFirst([System.Windows.Automation.TreeScope]::Children, $condition)
}
function Get-ButtonNames([string] $Like) {
    $taskbar = Get-Taskbar
    if (-not $taskbar) { return @() }
    $condition = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Button)
    @($taskbar.FindAll([System.Windows.Automation.TreeScope]::Descendants, $condition) | ForEach-Object { $_.Current.Name -replace "`r?`n", ' / ' } | Where-Object { $_ -like $Like })
}
function Test-OnTaskbar([string] $Name) { (Get-ButtonNames "$Name *").Count -gt 0 }

# Brings a window to the front: a press of a key that has no use first gives this script the right to.
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
$LeftWindows = 0x5B; $KeyLeft = 0x4B
function Send-Keys([int[]] $Keys, [int] $Wait = 1500) {
    foreach ($key in $Keys) { [void] [Win32]::ScanKey($key, $false, $true) }
    [array]::Reverse($Keys)
    foreach ($key in $Keys) { [void] [Win32]::ScanKey($key, $true, $true) }
    Start-Sleep -Milliseconds $Wait
}

# The settings app's dialogs, list items, and buttons in a dialog.
function Get-DialogText([string] $Title) {
    $dialog = Find-AppDialog $Title
    if (-not $dialog) { return "(no dialog $Title)" }
    @($dialog.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition) | ForEach-Object { $_.Current.Name } | Where-Object { $_ }) -join ' | '
}
function Invoke-DialogButton([string] $Dialog, [string] $Name) {
    $found = Find-AppDialog $Dialog
    if (-not $found) { return $false }
    $condition = New-Object System.Windows.Automation.AndCondition(
        (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, $Name)),
        (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Button)))
    $button = $found.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
    if (-not $button) { return $false }
    $button.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    return $true
}
function Find-ListItemLike([string] $Like) {
    $window = Get-AppWindow
    if (-not $window) { return $null }
    $condition = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::ListItem)
    @($window.FindAll([System.Windows.Automation.TreeScope]::Descendants, $condition)) | Where-Object { $_.Current.Name -like $Like } | Select-Object -First 1
}
function Select-Element($Element) {
    if (-not $Element) { return $false }
    $Element.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    Start-Sleep -Milliseconds 300
    return $true
}
function Get-RulesText { "saved: $(Get-RulesSettings | ConvertTo-Json -Compress -Depth 6)" }

# A rule for a test program, with its actions.
function Get-RuleJson([string] $Name, [string] $Actions, [string] $Id = '') {
    $idPart = if ($Id) { """id"": ""$Id"", " } else { '' }
    "{ $idPart""name"": ""$Name"", ""app"": { ""name"": ""$Name"", ""program"": ""$($ruleExe[$Name] -replace '\\', '\\')"" }, $Actions }"
}

# Turned on from Home, which saves only that it's on, the page has no rules. A rule filled in from
# Pick a window, and one whose app comes from Running app, are saved with what the editor shows, and
# the editor says how many open windows each matches. Detailed logging is on for Apply now's entries.
Write-Settings "{ ""schemaVersion"": 1, ""general"": { ""theme"": ""Light"" }, ""diagnostics"": { ""detailedLoggingUntil"": ""$detailedUntil"" } }"
Start-Sleep -Seconds 2
[void] (Start-RuleApp 'rule-a' '100,100,500,350')
[void] (Start-RuleApp 'rule-b' '160,520,500,300')
$a = Find-RuleWindow 'rule-a'
$b = Find-RuleWindow 'rule-b'
$aFrame = [RuleChecks]::Visible($a)
Start-Process $appExe
[void] (Wait-Until { Get-AppWindow })
Start-Sleep -Seconds 3
Test-Check 'turn on Window Rules on Home' (Set-Toggle 'Window Rules' $true)
Start-Sleep -Seconds 2
$saved = Get-RulesSettings
Test-Check 'the Home switch saves Window Rules as on' ($saved.enabled -eq $true) "saved: $($saved | ConvertTo-Json -Compress)"
Start-Host
Test-Check 'select Window Rules' (Select-NavigationItem 'Window Rules')
Start-Sleep -Seconds 2
Save-Screenshot '22a-window-rules-english'
$text = Get-WindowText
Test-Check 'Window Rules shows its settings, with no rules yet' (($text -contains 'Rules') -and ($text -contains 'Add rule') -and ($text -contains "No rules yet. Add one, and use Pick a window to fill it in from a window that's open.") -and ($text -contains 'Permissions & data')) ($text -join ' | ')

Test-Check 'Add rule opens the rule editor' ((Invoke-AppElement 'Add rule') -and (Wait-Until { Find-AppDialog 'Add rule' } 5))
Test-Check 'name the rule' (Set-AppBox 'Name' 'Rule A')
Test-Check 'Choose app offers Pick a window' ((Invoke-AppElement 'Choose app') -and (Wait-Until { Find-AppElement 'Pick a window' } 5))
[void] (Invoke-AppElement 'Pick a window')
$script:item = $null
Test-Check 'Pick a window lists the open test windows' (Wait-Until { (Find-AppDialog 'Pick a window') -and (($script:item = Find-ListItemLike 'rule-a, *') -ne $null) -and ((Find-ListItemLike 'rule-b, *') -ne $null) } 10) (Get-DialogText 'Pick a window')
Save-Screenshot '22b-window-rules-pick'
[void] (Select-Element $script:item)
Test-Check 'Choose fills in the rule from the window' ((Invoke-DialogButton 'Pick a window' 'Choose') -and (Wait-Until { (Get-DialogText 'Add rule') -like '*rule-a*' } 10)) (Get-DialogText 'Add rule')
Test-Check 'the editor says it matches the open window' (Wait-Until { (Get-DialogText 'Add rule') -like '*Matches 1 open window.*' } 5) (Get-DialogText 'Add rule')
Test-Check 'it keeps the name' ((Get-AppBox 'Name') -eq 'Rule A') "name: $(Get-AppBox 'Name')"
Test-Check 'turn on Place and size' (Set-Toggle 'Place and size' $true)
Start-Sleep -Milliseconds 500
Test-Check 'choose Right half' (Select-ListItem 'Place and size' 'Right half')
Test-Check 'turn on Always on top' (Set-Toggle 'Always on top' $true)
Save-Screenshot '22c-window-rules-editor'
Test-Check 'Save saves the rule with the window''s app, a place, on top and an ID' ((Invoke-DialogButton 'Add rule' 'Save') -and (Wait-Until { $r = @((Get-RulesSettings).rules); ($r.Count -eq 1) -and ($r[0].name -eq 'Rule A') -and ($r[0].app.program -eq $ruleExe['rule-a']) -and ($r[0].place.which -eq 'RightHalf') -and ($r[0].onTop -eq $true) -and ($r[0].id.Length -eq 32) -and ($null -eq $r[0].title) } 5)) (Get-RulesText)
Test-Check 'the open window gets the rule''s held action, always on top' (Wait-Until { [RuleChecks]::IsTopmost($a) } 5) (Get-RuleState $a)
Test-Check 'but isn''t moved' (Test-SameFrame ([RuleChecks]::Visible($a)) (ConvertTo-Rectangle $aFrame)) (Get-RuleState $a (ConvertTo-Rectangle $aFrame))

Test-Check 'Add rule opens the editor again' ((Invoke-AppElement 'Add rule') -and (Wait-Until { Find-AppDialog 'Add rule' } 5))
[void] (Set-AppBox 'Name' 'Rule B')
Test-Check 'Choose app offers Running app' ((Invoke-AppElement 'Choose app') -and (Wait-Until { Find-AppElement 'Running app…' } 5))
[void] (Invoke-AppElement 'Running app…')
$script:item = $null
Test-Check 'Running app lists the other test program' (Wait-Until { ($script:item = Find-AppListItem 'rule-b') -ne $null } 15)
[void] (Select-Element $script:item)
Test-Check 'Add gives the rule its app' ((Invoke-DialogButton 'Choose a running app' 'Add') -and (Wait-Until { (Get-DialogText 'Add rule') -like '*Matches 1 open window.*' } 10)) (Get-DialogText 'Add rule')
Test-Check 'turn on Place and size' (Set-Toggle 'Place and size' $true)
Start-Sleep -Milliseconds 500
Test-Check 'choose Bottom half' (Select-ListItem 'Place and size' 'Bottom half')
Test-Check 'turn on Opacity' (Set-Toggle 'Opacity' $true)
Start-Sleep -Milliseconds 500
Test-Check 'choose 60%' (Select-ListItem 'Opacity' '60%')
Test-Check 'Save saves the second rule' ((Invoke-DialogButton 'Add rule' 'Save') -and (Wait-Until { $r = @((Get-RulesSettings).rules); ($r.Count -eq 2) -and ($r[1].name -eq 'Rule B') -and ($r[1].app.program -eq $ruleExe['rule-b']) -and ($r[1].place.which -eq 'BottomHalf') -and ($r[1].opacity.percent -eq 60) } 5)) (Get-RulesText)
Test-Check 'the open window gets the rule''s opacity' (Wait-Until { [RuleChecks]::Opacity($b) -eq 60 } 5) (Get-RuleState $b)
$text = Get-WindowText
Test-Check 'the page lists both rules with what they do' (($text -contains 'Rule A') -and ($text -contains 'rule-a: Right half, always on top') -and ($text -contains 'Rule B') -and ($text -contains 'rule-b: Bottom half, 60% opacity')) ($text -join ' | ')
Save-Screenshot '22d-window-rules-list'

# Apply now places the open window, and the page says how many windows it applied the rule to.
$want = Get-Part 0 0.5 1 0.5
Test-Check 'More options offers Apply now' ((Invoke-AppElement 'More options for Rule B') -and (Wait-Until { Find-AppElement 'Apply now' } 5))
[void] (Invoke-AppElement 'Apply now')
Test-Check 'Apply now places the open window' (Wait-Until { Test-Frame $b $want } 5) (Get-RuleState $b $want)
Test-Check 'the page says it applied the rule to one window' (Wait-Until { (Get-WindowText) -contains 'The rule was applied to 1 open window.' } 5) ((Get-WindowText) -join ' | ')

# A snapped window is left where it is.
Select-Window $a
Send-Keys @($LeftWindows, $KeyLeft)
Send-Key 0x1B
Start-Sleep -Seconds 1
Test-Check 'Windows + Left snaps the test window' ([RuleChecks]::IsWindowArranged($a)) (Get-RuleState $a)
$snapped = ConvertTo-Rectangle ([RuleChecks]::Visible($a))
$appliedNow = Get-MatchCount (Get-HostLog) 'applied a rule now to 1 open windows'
Select-Window ([IntPtr] (Get-AppWindow).Current.NativeWindowHandle)
[void] (Invoke-AppElement 'More options for Rule A')
Test-Check 'Apply now applies the rule to the snapped window' ((Invoke-AppElement 'Apply now') -and (Wait-Until { (Get-MatchCount (Get-HostLog) 'applied a rule now to 1 open windows') -gt $appliedNow } 5))
Test-Check 'but leaves it where it is' ((Test-Frame $a $snapped) -and [RuleChecks]::IsTopmost($a)) (Get-RuleState $a $snapped)
[void] (Close-SettingsApp)
Stop-RuleApps

# Test windows open at their rule's place, and one whose app moves it back is placed again. A window
# opens maximized, with the rule's place as its normal place, and another minimized, without the front.
$edgeExe = "${env:ProgramFiles(x86)}\Microsoft\Edge\Application\msedge.exe"
$rules = @(
    (Get-RuleJson 'rule-a' '"place": { "which": "RightHalf" }, "onTop": true, "opacity": { "percent": 60 }, "hideFromTaskbar": true'),
    (Get-RuleJson 'rule-late' '"place": { "which": "LeftHalf" }'),
    (Get-RuleJson 'rule-c' '"place": { "which": "LeftHalf" }, "open": "Maximized"'),
    (Get-RuleJson 'rule-d' '"open": "Minimized"'),
    '{ "name": "Explorer", "app": { "name": "File Explorer", "program": "C:\\Windows\\explorer.exe" }, "windowClass": "CabinetWClass", "place": { "which": "BottomRight" } }',
    '{ "name": "Settings", "app": { "name": "Settings", "appId": "windows.immersivecontrolpanel_cw5n1h2txyewy!microsoft.windows.immersivecontrolpanel" }, "place": { "which": "LeftHalf" } }',
    "{ ""name"": ""Edge"", ""app"": { ""name"": ""Edge"", ""program"": ""$($edgeExe -replace '\\', '\\')"" }, ""place"": { ""which"": ""RightHalf"" } }"
) -join ', '
Write-RulesSettings "{ ""enabled"": true, ""rules"": [ $rules ] }"
Start-Host
$applied = Get-MatchCount (Get-HostLog) 'applied rules "?1"? to a window'
[void] (Start-RuleApp 'rule-a' '100,100,500,350')
$a = Find-RuleWindow 'rule-a'
$want = Get-Part 0.5 0 0.5 1
Test-Check 'a test window opens at its rule''s place' (Wait-Until { Test-Frame $a $want } 5) (Get-RuleState $a $want)
Save-Screenshot '22e-window-rules-placed'
Test-Check 'the host logs which actions, without the app or title' (Wait-Until { ((Get-MatchCount (Get-HostLog) 'applied rules "?1"? to a window: "?place, on top, opacity, off the taskbar') -gt $applied) -and ((Get-HostLog) -notmatch 'rule-a') } 5)

[void] (Start-RuleApp 'rule-late' '200,200,500,350' 'late')
$late = Find-RuleWindow 'rule-late'
$want = Get-Part 0 0 0.5 1
Start-Sleep -Seconds 3
Test-Check 'a window whose app moves it back is placed again' (Test-Frame $late $want) (Get-RuleState $late $want)
Test-Check 'the host logs it' (Test-Logged 'moved it back, so Window Rules placed it again')

[void] (Start-RuleApp 'rule-c' '300,150,500,350')
$c = Find-RuleWindow 'rule-c'
Test-Check 'a window opens maximized' (Wait-Until { [RuleChecks]::IsZoomed($c) } 5) (Get-RuleState $c)
[void] [Win32]::ShowWindow($c, 9) # SW_RESTORE
Test-Check 'and restoring it goes to the rule''s place' (Wait-Until { (-not [RuleChecks]::IsZoomed($c)) -and (Test-Frame $c $want) } 5) (Get-RuleState $c $want)

[void] (Start-RuleApp 'rule-d' '300,300,500,350')
$d = Find-RuleWindow 'rule-d'
Test-Check 'a window opens minimized, without the front' ((Wait-Until { [Win32]::IsIconic($d) } 5) -and ([Win32]::GetForegroundWindow() -ne $d)) (Get-RuleState $d)

# File Explorer, Settings and Edge open at their rules' places.
Start-Process explorer.exe 'C:\Windows'
$script:explorer = [IntPtr]::Zero
[void] (Wait-Until { ($script:explorer = Find-TopWindow 'CabinetWClass' '*') -ne $null } 20)
$want = Get-Part 0.5 0.5 0.5 0.5
Test-Check 'File Explorer opens at its rule''s place' (($null -ne $explorer) -and (Wait-Until { Test-Frame $explorer $want } 10)) (Get-RuleState ([IntPtr] $explorer) $want)
if ($explorer) { Close-Window $explorer }
Start-Process 'ms-settings:about'
$script:settingsWindow = [IntPtr]::Zero
[void] (Wait-Until { ($script:settingsWindow = Find-TopWindow 'ApplicationFrameWindow' 'Settings') -ne $null } 30)
$want = Get-Part 0 0 0.5 1
Test-Check 'Settings opens at its rule''s place' (($null -ne $settingsWindow) -and (Wait-Until { Test-Frame $settingsWindow $want } 10)) (Get-RuleState ([IntPtr] $settingsWindow) $want)
if ($settingsWindow) { Close-Window $settingsWindow }
$edgeData = Join-Path $env:TEMP 'PCCompanionRuleEdge'
Start-Process $edgeExe -ArgumentList '--no-first-run', "--user-data-dir=$edgeData", 'about:blank'
$script:edge = [IntPtr]::Zero
[void] (Wait-Until { ($script:edge = Find-TopWindow 'Chrome_WidgetWin_1' '*Edge*') -ne $null } 30)
$want = Get-Part 0.5 0 0.5 1
Test-Check 'Edge opens at its rule''s place' (($null -ne $edge) -and (Wait-Until { Test-Frame $edge $want } 10)) (Get-RuleState ([IntPtr] $edge) $want)
Get-Processes 'msedge' | ForEach-Object { Stop-Process -Id $_.Id -Force -ErrorAction SilentlyContinue }

# On top, opacity and taskbar hiding hold; the button comes back while the window is minimized, and
# turning the rule off puts them all back, though not the place.
Test-Check 'the window is on top, 60% opaque and marked' ([RuleChecks]::IsTopmost($a) -and ([RuleChecks]::Opacity($a) -eq 60) -and ([RuleChecks]::Mark($a) -ne 0)) (Get-RuleState $a)
Test-Check 'and has no taskbar button' (Wait-Until { -not (Test-OnTaskbar 'rule-a') } 5) ((Get-ButtonNames 'rule-*') -join ' | ')
[void] [Win32]::ShowWindow($a, 6) # SW_MINIMIZE
Test-Check 'minimized, it gets its button back' (Wait-Until { Test-OnTaskbar 'rule-a' } 5) ((Get-ButtonNames 'rule-*') -join ' | ')
[void] [Win32]::ShowWindow($a, 9) # SW_RESTORE
Test-Check 'restored, it loses it again' (Wait-Until { -not (Test-OnTaskbar 'rule-a') } 5) ((Get-ButtonNames 'rule-*') -join ' | ')
$rulesOff = $rules -replace '^\{ "name": "rule-a", ', '{ "name": "rule-a", "enabled": false, '
Write-RulesSettings "{ ""enabled"": true, ""rules"": [ $rulesOff ] }"
$want = Get-Part 0.5 0 0.5 1
Test-Check 'turning the rule off puts back on top, opacity and the taskbar button' ((Wait-Until { (-not [RuleChecks]::IsTopmost($a)) -and ([RuleChecks]::Opacity($a) -eq 100) -and ([RuleChecks]::Mark($a) -eq 0) } 5) -and (Wait-Until { Test-OnTaskbar 'rule-a' } 5)) "$(Get-RuleState $a); $((Get-ButtonNames 'rule-*') -join ' | ')"
Test-Check 'but leaves the window where it is' (Test-Frame $a $want) (Get-RuleState $a $want)

# With Window Tabs on, a window opens as a tab of another app's window: that window's tab is hidden
# and the new one shows in its place.
$tabOf = "{ ""name"": ""rule-c"", ""program"": ""$($ruleExe['rule-c'] -replace '\\', '\\')"" }"
$tabRule = Get-RuleJson 'rule-e' """tabOf"": $tabOf"
Write-RulesSettings "{ ""enabled"": true, ""rules"": [ $tabRule ] }" ', "windowTabs": { "enabled": true }'
[void] (Wait-Until { [Win32]::FindWindow('PCCompanion.WindowTabs', $null) -ne [IntPtr]::Zero } 20)
Select-Window $c
$groupFrame = ConvertTo-Rectangle ([RuleChecks]::Visible($c))
[void] (Start-RuleApp 'rule-e' '700,400,500,300')
$e = Find-RuleWindow 'rule-e'
Test-Check 'a window opens as a tab of another app''s window, in its place' (Wait-Until { (-not [Win32]::IsWindowVisible($c)) -and ([RuleChecks]::TabMark($c) -eq 1) -and [Win32]::IsWindowVisible($e) -and (Test-Frame $e $groupFrame) } 5) "$(Get-RuleState $c); $(Get-RuleState $e $groupFrame)"
Test-Check 'the host logs it' (Test-Logged 'asked Window Tabs to open a window as a tab')
Save-Screenshot '22f-window-rules-tab'
Stop-RuleApps

# Where I last put it opens a window where the last one was left as it closed, and an opacity set
# with Title Bar Actions' wheel is kept for the next window; window-rules.json holds both, by the
# rules' IDs, and no title.
$rules = @(
    (Get-RuleJson 'rule-f' '"place": { "which": "Remembered" }' 'kept-place'),
    (Get-RuleJson 'rule-g' '"opacity": { "which": "Wheel" }' 'wheel-opacity')
) -join ', '
Write-RulesSettings "{ ""enabled"": true, ""rules"": [ $rules ] }" ', "titleBarActions": { "enabled": true, "wheel": "Opacity" }'
$keptFile = Join-Path $dataFolder 'window-rules.json'
function Get-KeptText { if (Test-Path $keptFile) { Get-Content $keptFile -Raw } else { '(no file)' } }
$process = Start-RuleApp 'rule-f' '100,100,500,350'
$f = Find-RuleWindow 'rule-f'
[void] [Win32]::MoveWindow($f, 600, 250, 640, 420, $true)
Start-Sleep -Seconds 1
$left = ConvertTo-Rectangle ([RuleChecks]::Visible($f))
Close-Window $f
[void] (Wait-Until { $process.HasExited } 5)
[void] (Start-RuleApp 'rule-f' '100,100,500,350')
$f = Find-RuleWindow 'rule-f'
Test-Check 'the next window opens where the last one was left' (Wait-Until { Test-Frame $f $left } 5) (Get-RuleState $f $left)

$process = Start-RuleApp 'rule-g' '120,520,520,320'
$g = Find-RuleWindow 'rule-g'
$frame = [RuleChecks]::Visible($g)
[void] (Move-Pointer ($frame.Left + 200) ($frame.Top + 15))
for ($notch = 1; $notch -le 3; $notch++) {
    [void] [RuleChecks]::Wheel(-120)
    Start-Sleep -Milliseconds 400
}
Test-Check 'Title Bar Actions'' wheel makes the window 70% opaque' (Wait-Until { [RuleChecks]::Opacity($g) -eq 70 } 3) (Get-RuleState $g)
Close-Window $g
[void] (Wait-Until { $process.HasExited } 5)
[void] (Start-RuleApp 'rule-g' '120,520,520,320')
$g = Find-RuleWindow 'rule-g'
Test-Check 'the next window gets the opacity the wheel set last' (Wait-Until { [RuleChecks]::Opacity($g) -eq 70 } 5) (Get-RuleState $g)
Test-Check 'the host saves window-rules.json 5 seconds after the last change' (Wait-Until { (Get-KeptText) -match '"opacity":70' } 10) (Get-KeptText)
$kept = if (Test-Path $keptFile) { Get-Content $keptFile -Raw | ConvertFrom-Json } else { $null }
$place = @(@($kept.rules) | Where-Object { $_.rule -eq 'kept-place' })
$opacity = @(@($kept.rules) | Where-Object { $_.rule -eq 'wheel-opacity' })
Test-Check 'it holds the place and the opacity, by the rules'' IDs' (($place.Count -eq 1) -and (@($place[0].places).Count -eq 1) -and ($opacity.Count -eq 1) -and ($opacity[0].opacity -eq 70)) (Get-KeptText)
Test-Check 'and no title' ((Get-KeptText) -notmatch 'rule-') (Get-KeptText)
Stop-RuleApps

# Exit puts the windows back; the next start gives an open window its held actions without moving
# it. A host that was ended leaves them, and the next start, with the feature off, puts them back and
# deletes the file.
$rules = Get-RuleJson 'rule-a' '"place": { "which": "RightHalf" }, "onTop": true, "opacity": { "percent": 60 }, "hideFromTaskbar": true'
Write-RulesSettings "{ ""enabled"": true, ""rules"": [ $rules ] }"
[void] (Start-RuleApp 'rule-a' '100,100,500,350')
$a = Find-RuleWindow 'rule-a'
[void] (Wait-Until { [RuleChecks]::IsTopmost($a) -and ([RuleChecks]::Opacity($a) -eq 60) } 5)
Stop-Host
Test-Check 'when PC Companion exits, the window gets back what the rule gave it' ((-not [RuleChecks]::IsTopmost($a)) -and ([RuleChecks]::Opacity($a) -eq 100) -and ([RuleChecks]::Mark($a) -eq 0) -and (Wait-Until { Test-OnTaskbar 'rule-a' } 5)) "$(Get-RuleState $a); $((Get-ButtonNames 'rule-*') -join ' | ')"
[void] [Win32]::MoveWindow($a, 100, 100, 500, 350, $true)
Start-Sleep -Seconds 1
$frame = ConvertTo-Rectangle ([RuleChecks]::Visible($a))
Start-Host
Test-Check 'at the next start, the open window gets its held actions again' (Wait-Until { [RuleChecks]::IsTopmost($a) -and ([RuleChecks]::Opacity($a) -eq 60) -and -not (Test-OnTaskbar 'rule-a') } 5) (Get-RuleState $a)
Test-Check 'without being moved' (Test-Frame $a $frame) (Get-RuleState $a $frame)
Get-Processes 'PCCompanion.Host' | Stop-Process -Force
Start-Sleep -Seconds 2
Test-Check 'a host that''s ended leaves the window changed and marked' ([RuleChecks]::IsTopmost($a) -and ([RuleChecks]::Mark($a) -ne 0)) (Get-RuleState $a)
$putBack = Get-MatchCount (Get-HostLog) 'put back 1 windows left changed'
Write-RulesSettings "{ ""enabled"": false, ""rules"": [ $rules ] }"
Start-Host -Off
Test-Check 'the next start, with the feature off, puts the window back' (Wait-Until { (-not [RuleChecks]::IsTopmost($a)) -and ([RuleChecks]::Opacity($a) -eq 100) -and ([RuleChecks]::Mark($a) -eq 0) } 10) (Get-RuleState $a)
Test-Check 'the host logs it' (Wait-Until { (Get-MatchCount (Get-HostLog) 'put back 1 windows left changed') -gt $putBack } 5)
Test-Check 'turning the feature off deletes window-rules.json' (Wait-Until { -not (Test-Path $keptFile) } 5) (Get-KeptText)
Stop-Host
Stop-RuleApps

# Everything in Windows Sandbox runs as administrator. A host whose file is labeled Medium integrity
# runs without admin rights, and leaves alone the test window, which has them.
Write-RulesSettings "{ ""enabled"": true, ""rules"": [ $rules ] }"
& icacls.exe $hostExe /setintegritylevel medium | Out-Null
Start-Host
[void] (Start-RuleApp 'rule-a' '100,100,500,350')
$a = Find-RuleWindow 'rule-a'
Start-Sleep -Seconds 2
Test-Check 'a host without admin rights leaves a window of an app running as administrator alone' ((-not [RuleChecks]::IsTopmost($a)) -and ([RuleChecks]::Opacity($a) -eq 100) -and ([RuleChecks]::Mark($a) -eq 0) -and -not (Test-Frame $a (Get-Part 0.5 0 0.5 1))) (Get-RuleState $a)
Test-Check 'the host logs why' (Test-Logged 'left a window of an app running as administrator alone')
Stop-Host
& icacls.exe $hostExe /setintegritylevel high | Out-Null

Stop-RuleApps
Remove-Item $rulesFolder, $edgeData -Recurse -Force -ErrorAction SilentlyContinue
Write-RulesSettings '{ "enabled": false }'
