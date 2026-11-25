# Window Tabs: the Home switch turns it on and its page saves Group by dropping with Ctrl, the switch
# and group shortcuts and Keep groups after a restart, with a note while another program has
# Windows + Ctrl + Page Down or Windows + Ctrl + T. Test windows,
# painted in solid colors and dragged by their title bars with simulated input, make groups: one
# dropped with Ctrl on another's title bar, and one dropped on a strip without a key, while one
# dropped without Ctrl doesn't group. The strip shows right above the group; a click on a tab and the
# shortcuts show each tab in the group's place and in front, and with Shift a tab moves; screen
# readers get a tab list. A tab dragged off the strip and Move out of group in a tab's menu take a
# window out, and a group of one ends. Windows + Ctrl + T adds the window in front to the group of the
# window behind it, and with Shift takes it out again. Minimizing the group hides its strip, an app showing a hidden
# tab brings it to the front, and closing the tab in front shows the other. A window with a dialog
# open and a fixed-size window can't be tabs, nor, with the host's file labeled Medium integrity, a
# window running as administrator. Minimize to Tray's shortcut and Drop-down Apps leave a tab alone.
# Exit brings hidden tabs back on the taskbar, minimized, and keeps the group in window-tabs.json, so
# the next start groups the windows again; the next start after a host was ended brings back the tabs
# it left and groups them again. Turning Keep groups after a restart off deletes the file.
[void] (Close-SettingsApp)

# Detailed logging is on for the host's Debug entries.
$detailedUntil = [DateTimeOffset]::Now.AddHours(1).ToString('o')
function Write-TabsSettings([string] $Section, [string] $Others = '') {
    Write-Settings "{ ""schemaVersion"": 1, ""general"": { ""theme"": ""Light"" }, ""diagnostics"": { ""detailedLoggingUntil"": ""$detailedUntil"" }, ""features"": { ""windowTabs"": $Section$Others } }"
    Start-Sleep -Seconds 3
}
function Start-Host {
    if ((Get-Processes 'PCCompanion.Host').Count -eq 0) {
        Start-Process $hostExe
        [void] (Wait-Until { [Win32]::FindWindow('PCCompanion.Host', $null) -ne [IntPtr]::Zero } 30)
    }
    [void] (Wait-Until { [Win32]::FindWindow('PCCompanion.WindowTabs', $null) -ne [IntPtr]::Zero } 20)
    Start-Sleep -Seconds 1
}
function Stop-Host {
    [void] [Win32]::PostMessage([Win32]::FindWindow('PCCompanion.Host', $null), 0x0010, [UIntPtr]::Zero, [IntPtr]::Zero)
    [void] (Wait-Until { (Get-Processes 'PCCompanion.Host').Count -eq 0 } 15)
}

# The test program, built once and copied under other names, which are other programs: a window
# titled after its file, painted in a color of its own, at the place in its first argument; with
# "fixed" second, it can't be resized. Posted messages make it stop responding for the milliseconds
# in wParam (WM_APP + 3) and open a message box (+ 5).
$tabsFolder = Join-Path $env:TEMP 'PCCompanionTabApp'
New-Item -ItemType Directory -Force -Path $tabsFolder | Out-Null
$tabExe = @{}
foreach ($name in 'tab-a', 'tab-b', 'tab-c', 'tab-fixed') { $tabExe[$name] = Join-Path $tabsFolder "$name.exe" }
Add-Type -OutputAssembly $tabExe['tab-a'] -OutputType WindowsApplication -ReferencedAssemblies System.Windows.Forms, System.Drawing -TypeDefinition @'
using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Runtime.InteropServices;
using System.Windows.Forms;
public static class TabApp {
    [DllImport("user32.dll")] private static extern void DisableProcessWindowsGhosting();
    [DllImport("user32.dll")] private static extern bool SetProcessDPIAware();

    private sealed class Window : Form {
        protected override void WndProc(ref Message message) {
            switch (message.Msg) {
                case 0x8003: Thread.Sleep((int)message.WParam); return;
                case 0x8005: BeginInvoke((MethodInvoker)delegate { MessageBox.Show(this, "Box", Text + "-Box"); }); return;
            }
            base.WndProc(ref message);
        }
    }

    [STAThread]
    public static void Main(string[] arguments) {
        // Otherwise Windows puts a window of its own in place of one that doesn't respond for 5 seconds.
        DisableProcessWindowsGhosting();
        SetProcessDPIAware();
        var name = Path.GetFileNameWithoutExtension(Application.ExecutablePath);
        var place = arguments[0].Split(',').Select(int.Parse).ToArray();
        var color = name == "tab-a" ? Color.FromArgb(200, 40, 40) : name == "tab-b" ? Color.FromArgb(40, 90, 200) : name == "tab-c" ? Color.FromArgb(40, 160, 70) : Color.FromArgb(150, 150, 150);
        var window = new Window { Text = name, BackColor = color, StartPosition = FormStartPosition.Manual, Bounds = new Rectangle(place[0], place[1], place[2], place[3]) };
        if (arguments.Length > 1 && arguments[1] == "fixed") {
            window.FormBorderStyle = FormBorderStyle.FixedSingle;
            window.MaximizeBox = false;
        }
        Application.Run(window);
    }
}
'@
foreach ($name in 'tab-b', 'tab-c', 'tab-fixed') { Copy-Item $tabExe['tab-a'] $tabExe[$name] -Force }
$tabColor = @{ 'tab-a' = [System.Drawing.Color]::FromArgb(200, 40, 40); 'tab-b' = [System.Drawing.Color]::FromArgb(40, 90, 200); 'tab-c' = [System.Drawing.Color]::FromArgb(40, 160, 70) }
Add-Type -ReferencedAssemblies System.Drawing -TypeDefinition @'
using System;
using System.Drawing;
using System.Runtime.InteropServices;
public static class TabChecks {
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr window, int attribute, out RECT value, int size);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetProp(IntPtr window, string name);

    // The part of the window that shows, without its invisible borders (DWMWA_EXTENDED_FRAME_BOUNDS).
    public static RECT Visible(IntPtr window) {
        RECT bounds;
        DwmGetWindowAttribute(window, 9, out bounds, Marshal.SizeOf(typeof(RECT)));
        return bounds;
    }

    public static long Mark(IntPtr window) { return GetProp(window, "PCCompanion.WindowTabs").ToInt64(); }

    public static bool IsInTray(IntPtr window) { return GetProp(window, "PCCompanion.MinimizeToTray") != IntPtr.Zero; }

    // The color on the screen at a point.
    public static Color ColorAt(int x, int y) {
        using (var bitmap = new Bitmap(1, 1))
        using (var graphics = Graphics.FromImage(bitmap)) {
            graphics.CopyFromScreen(x, y, 0, 0, new Size(1, 1));
            return bitmap.GetPixel(0, 0);
        }
    }
}
'@
function Find-TabWindow([string] $Name) { [Win32]::FindWindow([NullString]::Value, $Name) }
function Start-TabApp([string] $Name, [string] $Place, [string] $Kind = '') {
    $arguments = @($Place)
    if ($Kind) { $arguments += $Kind }
    $process = Start-Process $tabExe[$Name] -ArgumentList $arguments -PassThru
    [void] (Wait-Until { (Find-TabWindow $Name) -ne [IntPtr]::Zero } 30)
    Start-Sleep -Seconds 1
    return $process
}
function Get-TabState([IntPtr] $Window) {
    $visible = [TabChecks]::Visible($Window)
    "$([Win32]::WindowTitle($Window)): visible $([Win32]::IsWindowVisible($Window)), minimized $([Win32]::IsIconic($Window)), mark $([TabChecks]::Mark($Window)), front $([Win32]::GetForegroundWindow() -eq $Window), frame $($visible.Left),$($visible.Top),$($visible.Right),$($visible.Bottom)"
}
function Get-States { ($args | ForEach-Object { Get-TabState $_ }) -join '; ' }
function Test-Hidden([IntPtr] $Window) { (-not [Win32]::IsWindowVisible($Window)) -and ([TabChecks]::Mark($Window) -eq 1) }
function Test-Shown([IntPtr] $Window) { [Win32]::IsWindowVisible($Window) -and -not [Win32]::IsIconic($Window) -and ([TabChecks]::Mark($Window) -eq 0) }
function Test-SameFrame($First, $Second) {
    ([math]::Abs($First.Left - $Second.Left) -le 2) -and ([math]::Abs($First.Top - $Second.Top) -le 2) -and
        ([math]::Abs($First.Right - $Second.Right) -le 2) -and ([math]::Abs($First.Bottom - $Second.Bottom) -le 2)
}

# Whether the screen shows the window's color in the middle of the frame.
function Test-ShowsColor($Frame, [System.Drawing.Color] $Want) {
    $color = [TabChecks]::ColorAt([int] (($Frame.Left + $Frame.Right) / 2), [int] (($Frame.Top + $Frame.Bottom) / 2))
    ([math]::Abs($color.R - $Want.R) -le 8) -and ([math]::Abs($color.G - $Want.G) -le 8) -and ([math]::Abs($color.B - $Want.B) -le 8)
}

# The groups' strips that show, as the host's windows.
function Get-Strips { @([Win32]::TopLevelWindows() | Where-Object { [Win32]::WindowClass($_) -like 'PCCompanion.WindowTabs.Strip.*' }) }
function Get-StripRect([IntPtr] $Strip) {
    $rect = New-Object Win32+RECT
    [void] [Win32]::GetWindowRect($Strip, [ref] $rect)
    return $rect
}

# A strip's tab list, as screen readers get it: its name and its tabs' names, the selected one in brackets.
function Get-TabList([IntPtr] $Strip) {
    try {
        $list = [System.Windows.Automation.AutomationElement]::FromHandle($Strip)
        # A tooltip the strip shows is its child too, as Windows lists owned windows.
        $condition = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::TabItem)
        $tabs = $list.FindAll([System.Windows.Automation.TreeScope]::Children, $condition) | ForEach-Object {
            $selected = $_.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Current.IsSelected
            if ($selected) { "[$($_.Current.Name)]" } else { $_.Current.Name }
        }
        return "$($list.Current.ControlType.ProgrammaticName) ""$($list.Current.Name)"": $($tabs -join ', ')"
    }
    catch {
        return "UI Automation failed: $($_.Exception.Message)"
    }
}

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

# A point on a window's title bar, left of its buttons.
function Get-TitlePoint([IntPtr] $Window) {
    $frame = [TabChecks]::Visible($Window)
    return New-Object System.Drawing.Point(($frame.Left + 120), ($frame.Top + 14))
}

# Drags with the left mouse button from one point to another, as a user does, holding Ctrl from just
# after the press until just after the release when asked to.
function Invoke-Drag([System.Drawing.Point] $From, [System.Drawing.Point] $To, [bool] $Control = $false) {
    [void] (Move-Pointer $From.X $From.Y)
    [void] [Win32]::LeftButton($true)
    Start-Sleep -Milliseconds 300
    if ($Control) { [void] [Win32]::ScanKey($LeftControl, $false, $false) }
    [void] (Move-Pointer $To.X $To.Y)
    [void] [Win32]::LeftButton($false)
    Start-Sleep -Milliseconds 300
    if ($Control) { [void] [Win32]::ScanKey($LeftControl, $true, $false) }
    Start-Sleep -Milliseconds 1500
}
function Invoke-Click([int] $X, [int] $Y, [switch] $Right) {
    [void] (Move-Pointer $X $Y)
    if ($Right) {
        [Win32]::mouse_event(0x0008, 0, 0, 0, [UIntPtr]::Zero) # MOUSEEVENTF_RIGHTDOWN
        [Win32]::mouse_event(0x0010, 0, 0, 0, [UIntPtr]::Zero) # MOUSEEVENTF_RIGHTUP
    } else {
        [void] [Win32]::LeftButton($true)
        [void] [Win32]::LeftButton($false)
    }
    Start-Sleep -Milliseconds 1500
}

# The center of a tab on a strip of the given number of tabs.
function Get-TabPoint([IntPtr] $Strip, [int] $Index, [int] $Count) {
    $rect = Get-StripRect $Strip
    $width = ($rect.Right - $rect.Left) / $Count
    return New-Object System.Drawing.Point(([int] ($rect.Left + ($Index + 0.5) * $width)), ([int] (($rect.Top + $rect.Bottom) / 2)))
}

$LeftWindows = 0x5B; $LeftControl = 0x1D; $LeftShift = 0x2A; $KeyPageDown = 0x51; $KeyPageUp = 0x49; $KeyZ = 0x2C; $KeyAboveTab = 0x29; $KeyT = 0x14
function Send-Keys([int[]] $Keys, [int] $Wait = 1500) {
    $extended = @($LeftWindows, $KeyPageDown, $KeyPageUp)
    foreach ($key in $Keys) { [void] [Win32]::ScanKey($key, $false, $extended -contains $key) }
    [array]::Reverse($Keys)
    foreach ($key in $Keys) { [void] [Win32]::ScanKey($key, $true, $extended -contains $key) }
    Start-Sleep -Milliseconds $Wait
}
function Test-Logged([string] $Pattern, [int] $Seconds = 5) { Wait-Until { (Get-HostLog) -match $Pattern } $Seconds }
function Get-TabsSettings { (Get-Content (Join-Path $dataFolder 'settings.json') -Raw | ConvertFrom-Json).features.windowTabs }

# Turned on from Home, which saves only that it's on, the page shows its settings and saves them.
Write-Settings '{ "schemaVersion": 1, "general": { "theme": "Light" } }'
Start-Sleep -Seconds 2
Start-Process $appExe
[void] (Wait-Until { Get-AppWindow })
Start-Sleep -Seconds 3
Test-Check 'turn on Window Tabs on Home' (Set-Toggle 'Window Tabs' $true)
Start-Sleep -Seconds 2
$saved = Get-TabsSettings
Test-Check 'the Home switch saves Window Tabs as on' ($saved.enabled -eq $true) "saved: $($saved | ConvertTo-Json -Compress)"
Test-Check 'select Window Tabs' (Select-NavigationItem 'Window Tabs')
Start-Sleep -Seconds 2
Save-Screenshot '13a-window-tabs-english'
$text = Get-WindowText
Test-Check 'Window Tabs shows its settings' (($text -contains 'Making a group') -and ($text -contains 'Group by dropping with Ctrl') -and ($text -contains 'Switch tabs') -and ($text -contains 'Group from the keyboard') -and ($text -contains 'Keep groups after a restart') -and ($text -contains 'Permissions & data')) ($text -join ' | ')
Test-Check 'it says which windows can''t be tabs' (@($text | Where-Object { $_ -like "Some windows can't be tabs*" }).Count -eq 1) ($text -join ' | ')
Test-Check 'Switch tabs starts with Windows logo key + Ctrl' ((Get-ListSelection 'Switch tabs') -eq 'Windows logo key + Ctrl + Page Down / Page Up') "selected: $(Get-ListSelection 'Switch tabs')"
Test-Check 'turn off Group by dropping with Ctrl' (Set-Toggle 'Group by dropping with Ctrl' $false)
Test-Check 'the page saves it' (Wait-Until { (Get-TabsSettings).groupByDropping -eq $false } 5) "saved: $(Get-TabsSettings | ConvertTo-Json -Compress)"
Test-Check 'choose Windows logo key + Alt' (Select-ListItem 'Switch tabs' 'Windows logo key + Alt + Page Down / Page Up')
Test-Check 'the page saves the shortcuts' (Wait-Until { (Get-TabsSettings).switchShortcuts -eq 'WindowsAltPage' } 5) "saved: $(Get-TabsSettings | ConvertTo-Json -Compress)"
$selected = Get-ListSelection 'Group from the keyboard'
Test-Check 'Group from the keyboard starts with Windows logo key + Ctrl + T' ($selected -eq 'Windows logo key + Ctrl + T') "selected: $selected"
Test-Check 'choose None for Group from the keyboard' (Select-ListItem 'Group from the keyboard' 'None')
Test-Check 'the page saves the group shortcuts' (Wait-Until { (Get-TabsSettings).groupShortcuts -eq 'None' } 5) "saved: $(Get-TabsSettings | ConvertTo-Json -Compress)"
Test-Check 'turn off Keep groups after a restart' (Set-Toggle 'Keep groups after a restart' $false)
Test-Check 'the page saves that too' (Wait-Until { (Get-TabsSettings).keepGroups -eq $false } 5) "saved: $(Get-TabsSettings | ConvertTo-Json -Compress)"
[void] (Close-SettingsApp)

# Another program that has Windows + Ctrl + Page Down, or Windows + Ctrl + T: the page says so.
Test-ShortcutTakenNote 'Windows + Ctrl + Page Down' 0xA 0x22 { Write-TabsSettings '{ "enabled": false }' } { Write-TabsSettings '{ "enabled": true }' } { Write-TabsSettings '{ "enabled": true, "groupByDropping": false }' } 'Window Tabs' 'Another program uses Windows logo key + Ctrl + Page Down / Page Up, so this shortcut does nothing here. PC Companion tries again when a setting here changes.' '13b-window-tabs-shortcut-taken'
Test-ShortcutTakenNote 'Windows + Ctrl + T' 0xA 0x54 { Write-TabsSettings '{ "enabled": false }' } { Write-TabsSettings '{ "enabled": true }' } { Write-TabsSettings '{ "enabled": true, "groupByDropping": false }' } 'Window Tabs' 'Another program uses Windows logo key + Ctrl + T, so this shortcut does nothing here. PC Companion tries again when a setting here changes.' '13e-window-tabs-group-shortcut-taken'

# A window dropped with Ctrl on another's title bar makes a group: it takes the other's place, in
# front, with the strip right above it, and the other is hidden and marked.
Write-TabsSettings '{ "enabled": true }'
Start-Host
$processes = @(
    (Start-TabApp 'tab-a' '40,150,460,300'),
    (Start-TabApp 'tab-b' '600,150,520,360'),
    (Start-TabApp 'tab-c' '40,480,460,220'),
    (Start-TabApp 'tab-fixed' '520,560,300,160' 'fixed'))
$a = Find-TabWindow 'tab-a'
$b = Find-TabWindow 'tab-b'
$c = Find-TabWindow 'tab-c'
$fixed = Find-TabWindow 'tab-fixed'
Select-Window $b
Select-Window $a
$groupFrame = [TabChecks]::Visible($b)
Invoke-Drag (Get-TitlePoint $a) (New-Object System.Drawing.Point(($groupFrame.Left + 200), ($groupFrame.Top + 20))) -Control $true
Save-Screenshot '13c-window-tabs-group'
Test-Check 'a window dropped with Ctrl on another''s title bar makes a group' (Wait-Until { (Test-Hidden $b) -and (Test-Shown $a) } 5) (Get-States $a $b)
Test-Check 'it takes the other''s place, in front' ((Test-SameFrame ([TabChecks]::Visible($a)) $groupFrame) -and ([Win32]::GetForegroundWindow() -eq $a)) (Get-States $a $b)
Test-Check 'the host logs the group' ((Test-Logged 'made a group') -and (Test-Logged 'which has 2 tabs now'))
[void] (Wait-Until { (Get-Strips).Count -eq 1 } 5)
$strips = Get-Strips
$strip = if ($strips.Count -eq 1) { $strips[0] } else { [IntPtr]::Zero }
$rect = Get-StripRect $strip
Test-Check 'the strip shows right above the group, with a tab for each window' (($strips.Count -eq 1) -and ([math]::Abs($rect.Left - $groupFrame.Left) -le 1) -and ([math]::Abs($rect.Bottom - $groupFrame.Top) -le 1) -and (($rect.Bottom - $rect.Top) -eq 32) -and (($rect.Right - $rect.Left) -eq 400)) "strips: $($strips.Count); strip $($rect.Left),$($rect.Top),$($rect.Right),$($rect.Bottom); group $($groupFrame.Left),$($groupFrame.Top),$($groupFrame.Right),$($groupFrame.Bottom)"
Test-Check 'screen readers get a tab list named after the tab in front' ((Get-TabList $strip) -eq 'ControlType.Tab "Tabs: tab-a": tab-b, [tab-a]') (Get-TabList $strip)

# A click on a tab shows its window in the group's place, in front, and hides the other.
$point = Get-TabPoint $strip 0 2
Invoke-Click $point.X $point.Y
Test-Check 'a click on a tab shows its window in front' (Wait-Until { (Test-Shown $b) -and (Test-Hidden $a) -and ([Win32]::GetForegroundWindow() -eq $b) } 5) (Get-States $a $b)
Test-Check 'in the group''s place, with nothing behind showing through' ((Test-SameFrame ([TabChecks]::Visible($b)) $groupFrame) -and (Test-ShowsColor $groupFrame $tabColor['tab-b'])) (Get-States $b)
Test-Check 'the host logs the switch' (Test-Logged 'showed tab 1 of 2')

# The shortcuts show the next and previous tabs, and with Shift move the tab in front.
Send-Keys @($LeftWindows, $LeftControl, $KeyPageDown)
Test-Check 'Windows + Ctrl + Page Down shows the next tab' ((Wait-Until { (Test-Shown $a) -and (Test-Hidden $b) } 5) -and (Test-ShowsColor $groupFrame $tabColor['tab-a'])) (Get-States $a $b)
Send-Keys @($LeftWindows, $LeftControl, $KeyPageUp)
Test-Check 'Windows + Ctrl + Page Up shows the previous one' (Wait-Until { (Test-Shown $b) -and (Test-Hidden $a) } 5) (Get-States $a $b)
Send-Keys @($LeftWindows, $LeftControl, $LeftShift, $KeyPageDown)
Test-Check 'with Shift, the tab in front moves along the strip' (Wait-Until { (Get-TabList $strip) -eq 'ControlType.Tab "Tabs: tab-b": tab-a, [tab-b]' } 5) (Get-TabList $strip)
Test-Check 'and stays in front' ((Test-Shown $b) -and (Test-Hidden $a)) (Get-States $a $b)

# A window dropped on the strip without a key joins the group there.
$rect = Get-StripRect $strip
Invoke-Drag (Get-TitlePoint $c) (New-Object System.Drawing.Point(($rect.Right - 10), ($rect.Top + 16)))
Save-Screenshot '13d-window-tabs-three'
Test-Check 'a window dropped on the strip without a key joins the group, in front' (Wait-Until { (Test-Shown $c) -and (Test-Hidden $a) -and (Test-Hidden $b) } 5) (Get-States $a $b $c)
Test-Check 'at the end of the strip' (Wait-Until { (Get-TabList $strip) -eq 'ControlType.Tab "Tabs: tab-c": tab-a, tab-b, [tab-c]' } 5) (Get-TabList $strip)
Test-Check 'the host logs it' (Test-Logged 'which has 3 tabs now')

# A tab dragged off the strip leaves the group, and the group shows another tab.
$point = Get-TabPoint $strip 2 3
Invoke-Drag $point (New-Object System.Drawing.Point($point.X, ($point.Y + 250)))
Test-Check 'a tab dragged off the strip leaves the group' (Wait-Until { (Test-Shown $c) -and ((Get-TabList $strip) -like '*tab-a*tab-b*') -and ((Get-TabList $strip) -notlike '*tab-c*') } 5) "$(Get-States $a $b $c); $(Get-TabList $strip)"
Test-Check 'the group shows another tab' (Wait-Until { (Test-Shown $a) -or (Test-Shown $b) } 5) (Get-States $a $b)
Test-Check 'the host logs it' (Test-Logged '"?the user took it out')

# A window dropped on a title bar without Ctrl doesn't group.
$front = if ([Win32]::IsWindowVisible($a)) { $a } else { $b }
$joined = Get-MatchCount (Get-HostLog) 'added a window'
$frame = [TabChecks]::Visible($front)
Select-Window $c
Invoke-Drag (Get-TitlePoint $c) (New-Object System.Drawing.Point(($frame.Left + 200), ($frame.Top + 20)))
Test-Check 'a window dropped on a title bar without Ctrl doesn''t group' ((Test-Shown $c) -and ((Get-MatchCount (Get-HostLog) 'added a window') -eq $joined)) (Get-States $a $b $c)
[void] [Win32]::MoveWindow($c, 40, 480, 460, 220, $true)

# Move out of group in a tab's menu takes its window out, and a group of one ends.
Select-Window $front
$point = Get-TabPoint $strip 0 2
Invoke-Click $point.X $point.Y -Right
Test-Check 'Move out of group in a tab''s menu' (Select-TrayMenuItem 'Move out of group')
Test-Check 'takes the window out, and the group of one ends' (Wait-Until { (Test-Shown $a) -and (Test-Shown $b) -and ((Get-Strips).Count -eq 0) } 5) "$(Get-States $a $b); strips: $((Get-Strips).Count)"
Test-Check 'the host logs it' (Test-Logged 'ended a group: "?one window was left')

# Windows + Ctrl + T adds the window in front to the group of the window behind it, at that window's
# place, and with Shift takes it out again, beside the group, which ends with one window left.
[void] [Win32]::MoveWindow($a, 40, 150, 460, 300, $true)
[void] [Win32]::MoveWindow($b, 600, 150, 520, 360, $true)
Start-Sleep -Seconds 1
Select-Window $b
Select-Window $a
$behindFrame = [TabChecks]::Visible($b)
Send-Keys @($LeftWindows, $LeftControl, $KeyT)
Test-Check 'Windows + Ctrl + T adds the window in front to the group of the window behind it' (Wait-Until { (Test-Hidden $b) -and (Test-Shown $a) -and ((Get-Strips).Count -eq 1) } 5) "$(Get-States $a $b); strips: $((Get-Strips).Count)"
Test-Check 'at that window''s place, in front' ((Test-SameFrame ([TabChecks]::Visible($a)) $behindFrame) -and ([Win32]::GetForegroundWindow() -eq $a)) (Get-States $a $b)
$tookOut = Get-MatchCount (Get-HostLog) 'the user took it out'
Send-Keys @($LeftWindows, $LeftControl, $LeftShift, $KeyT)
Test-Check 'Windows + Ctrl + Shift + T takes it out again, and the group of one ends' (Wait-Until { (Test-Shown $a) -and (Test-Shown $b) -and ((Get-Strips).Count -eq 0) } 5) "$(Get-States $a $b); strips: $((Get-Strips).Count)"
$frame = [TabChecks]::Visible($a)
Test-Check 'it shows beside the other window, in front' (([math]::Abs($frame.Left - $behindFrame.Left - 32) -le 2) -and ([math]::Abs($frame.Top - $behindFrame.Top - 32) -le 2) -and ([Win32]::GetForegroundWindow() -eq $a) -and (Wait-Until { [Win32]::IsInFrontOf($a, $b) } 3)) "$(Get-States $a $b); frame $($frame.Left),$($frame.Top); behind $($behindFrame.Left),$($behindFrame.Top); in front of the other: $([Win32]::IsInFrontOf($a, $b))"
Test-Check 'the host logs it' (Wait-Until { (Get-MatchCount (Get-HostLog) 'the user took it out') -gt $tookOut } 5)

# Minimizing the group hides its strip, and restoring it brings the strip back.
[void] [Win32]::MoveWindow($a, 40, 150, 460, 300, $true)
[void] [Win32]::MoveWindow($b, 600, 150, 520, 360, $true)
Start-Sleep -Seconds 1
Select-Window $a
Invoke-Drag (Get-TitlePoint $a) (New-Object System.Drawing.Point(($groupFrame.Left + 200), ($groupFrame.Top + 20))) -Control $true
[void] (Wait-Until { (Test-Hidden $b) -and (Test-Shown $a) } 5)
[void] [Win32]::ShowWindow($a, 6) # SW_MINIMIZE
Test-Check 'minimizing the group hides its strip' (Wait-Until { [Win32]::IsIconic($a) -and ((Get-Strips).Count -eq 0) } 5) "$(Get-States $a $b); strips: $((Get-Strips).Count)"
[void] [Win32]::ShowWindow($a, 9) # SW_RESTORE
Test-Check 'restoring it brings the strip back' (Wait-Until { (Test-Shown $a) -and ((Get-Strips).Count -eq 1) } 5) "$(Get-States $a $b); strips: $((Get-Strips).Count)"

# An app showing its hidden tab brings it to the front of the group.
[void] [Win32]::ShowWindow($b, 5) # SW_SHOW
Test-Check 'an app showing a hidden tab makes it the tab in front' (Wait-Until { (Test-Shown $b) -and (Test-Hidden $a) } 5) (Get-States $a $b)
Test-Check 'the host logs it' (Test-Logged 'an app showed its hidden tab')

# Closing the tab in front shows the other, and the group of one ends.
$bProcess = $processes[1]
[void] [Win32]::PostMessage($b, 0x0010, [UIntPtr]::Zero, [IntPtr]::Zero)
Test-Check 'closing the tab in front shows the other' (Wait-Until { (Test-Shown $a) -and ((Get-Strips).Count -eq 0) } 5) "$(Get-States $a); strips: $((Get-Strips).Count)"
Test-Check 'the host logs it' (Test-Logged 'which has 1 tabs now: "?(its app hid it|it closed)')
[void] (Wait-Until { $bProcess.HasExited } 5)
$processes[1] = Start-TabApp 'tab-b' '600,150,520,360'
$b = Find-TabWindow 'tab-b'
[void] [Win32]::MoveWindow($a, 40, 150, 460, 300, $true)

# A window with a dialog open can't take a tab, and a window that can't be resized can't be one.
[void] [Win32]::PostMessage($b, 0x8005, [UIntPtr]::Zero, [IntPtr]::Zero)
[void] (Wait-Until { [Win32]::FindWindow('#32770', 'tab-b-Box') -ne [IntPtr]::Zero } 10)
Select-Window $a
Invoke-Drag (Get-TitlePoint $a) (New-Object System.Drawing.Point(($groupFrame.Left + 200), ($groupFrame.Top + 20))) -Control $true
Test-Check 'a window with a dialog open can''t take a tab' ((Test-Shown $a) -and (Test-Shown $b) -and ((Get-Strips).Count -eq 0)) (Get-States $a $b)
Test-Check 'the host logs why' (Test-Logged 'refused a window: "?DialogOpen')
[void] [Win32]::PostMessage([Win32]::FindWindow('#32770', 'tab-b-Box'), 0x0010, [UIntPtr]::Zero, [IntPtr]::Zero)
Start-Sleep -Seconds 1
[void] [Win32]::MoveWindow($a, 40, 150, 460, 300, $true)
Select-Window $a
$frame = [TabChecks]::Visible($a)
Select-Window $fixed
Invoke-Drag (Get-TitlePoint $fixed) (New-Object System.Drawing.Point(($frame.Left + 200), ($frame.Top + 20))) -Control $true
Test-Check 'a window that can''t be resized can''t be a tab' ((Test-Shown $a) -and (Test-Shown $fixed) -and ((Get-Strips).Count -eq 0)) (Get-States $a $fixed)
Test-Check 'the host logs why' (Test-Logged 'refused a window: "?NotResizable')

# Minimize to Tray's shortcut and Drop-down Apps leave a tab alone.
$dropApp = "{ ""name"": ""tab-a"", ""program"": ""$($tabExe['tab-a'] -replace '\\', '\\')"", ""shortcut"": ""WindowsControlAboveTab"" }"
Write-TabsSettings '{ "enabled": true }' ", ""minimizeToTray"": { ""enabled"": true }, ""dropDownApps"": { ""enabled"": true, ""apps"": [ $dropApp ] }"
Select-Window $b
Select-Window $a
Invoke-Drag (Get-TitlePoint $a) (New-Object System.Drawing.Point(($groupFrame.Left + 200), ($groupFrame.Top + 20))) -Control $true
Test-Check 'the test windows make a group again' (Wait-Until { (Test-Hidden $b) -and (Test-Shown $a) } 5) (Get-States $a $b)
Select-Window $a
Send-Keys @($LeftWindows, $LeftShift, $KeyZ)
Test-Check 'Minimize to Tray''s shortcut leaves a tab' ((Test-Shown $a) -and -not [TabChecks]::IsInTray($a)) (Get-States $a)
Test-Check 'Minimize to Tray logs why' (Test-Logged '"?Shortcut"?, "?WindowTab')
Send-Keys @($LeftWindows, $LeftControl, $KeyAboveTab)
Test-Check 'Drop-down Apps leaves a tab' ((Test-Shown $a) -and (Test-SameFrame ([TabChecks]::Visible($a)) $groupFrame)) (Get-States $a)
Test-Check 'Drop-down Apps logs why' (Test-Logged 'app 0: "?WindowTab')
Write-TabsSettings '{ "enabled": true }'

# Exit brings a hidden tab back on the taskbar, minimized, and keeps the group in window-tabs.json:
# each tab's app, by its program here, in the strip's order, and the tab in front, and nothing else.
# The next start groups the windows again, and a tab made again from a minimized window shows restored.
$keptFile = Join-Path $dataFolder 'window-tabs.json'
function Get-KeptText { if (Test-Path $keptFile) { Get-Content $keptFile -Raw } else { '(no file)' } }
Stop-Host
Test-Check 'when PC Companion exits, a hidden tab comes back on the taskbar, minimized' ([Win32]::IsWindowVisible($b) -and [Win32]::IsIconic($b) -and ([TabChecks]::Mark($b) -eq 0)) (Get-States $b)
$kept = if (Test-Path $keptFile) { Get-Content $keptFile -Raw | ConvertFrom-Json } else { $null }
$keptTabs = @($kept.groups | ForEach-Object { $_.tabs })
Test-Check 'it keeps the group in window-tabs.json, with its tabs'' programs in order and the tab in front' ((@($kept.groups).Count -eq 1) -and ($keptTabs.Count -eq 2) -and ($keptTabs[0].program -like '*\tab-b.exe') -and ($keptTabs[1].program -like '*\tab-a.exe') -and ($kept.groups[0].front -eq 1)) (Get-KeptText)
Test-Check 'and nothing else, such as titles' ((@($keptTabs | ForEach-Object { $_.PSObject.Properties.Name -join ',' } | Select-Object -Unique) -join ';') -eq 'appId,program') (Get-KeptText)
$made = Get-MatchCount (Get-HostLog) 'made a kept group again, with 2 tabs'
Start-Host
Test-Check 'at the next start, the windows are grouped again, with the same tab in front' (Wait-Until { (Test-Hidden $b) -and (Test-Shown $a) -and ((Get-Strips).Count -eq 1) } 10) "$(Get-States $a $b); strips: $((Get-Strips).Count)"
$strip = Get-Strips | Select-Object -First 1
Test-Check 'in their order' ((Get-TabList $strip) -eq 'ControlType.Tab "Tabs: tab-a": tab-b, [tab-a]') (Get-TabList $strip)
Test-Check 'the host logs it' (Wait-Until { (Get-MatchCount (Get-HostLog) 'made a kept group again, with 2 tabs') -gt $made } 5)
$frame = [TabChecks]::Visible($a)
$point = Get-TabPoint $strip 0 2
Invoke-Click $point.X $point.Y
Test-Check 'the tab made again from a minimized window shows restored, in the group''s place' (Wait-Until { (Test-Shown $b) -and (Test-Hidden $a) -and (Test-SameFrame ([TabChecks]::Visible($b)) $frame) } 5) "$(Get-States $a $b); group $($frame.Left),$($frame.Top),$($frame.Right),$($frame.Bottom)"
$saves = Get-MatchCount (Get-HostLog) 'saved 1 kept groups'
$point = Get-TabPoint $strip 1 2
Invoke-Click $point.X $point.Y
[void] (Wait-Until { (Test-Shown $a) -and (Test-Hidden $b) } 5)
Test-Check 'the host saves the change after 5 seconds' (Wait-Until { (Get-MatchCount (Get-HostLog) 'saved 1 kept groups') -gt $saves } 10)

# Ended, the host leaves the tab hidden and marked; its next start brings the tab back on the
# taskbar and groups it again.
$brought = Get-MatchCount (Get-HostLog) 'brought back 1 windows left hidden'
$made = Get-MatchCount (Get-HostLog) 'made a kept group again, with 2 tabs'
Get-Processes 'PCCompanion.Host' | Stop-Process -Force
Start-Sleep -Seconds 2
Test-Check 'a host that''s ended leaves the tab hidden and marked' (Test-Hidden $b) (Get-States $b)
Start-Host
Test-Check 'at the next start, the tab comes back and is grouped again' (Wait-Until { (Test-Hidden $b) -and (Test-Shown $a) -and ((Get-Strips).Count -eq 1) } 10) "$(Get-States $a $b); strips: $((Get-Strips).Count)"
Test-Check 'the host logs both' (Wait-Until { ((Get-MatchCount (Get-HostLog) 'brought back 1 windows left hidden') -gt $brought) -and ((Get-MatchCount (Get-HostLog) 'made a kept group again, with 2 tabs') -gt $made) } 5)

# Turning Keep groups after a restart off deletes the file.
Write-TabsSettings '{ "enabled": true, "keepGroups": false }'
Test-Check 'turning Keep groups after a restart off deletes window-tabs.json' (Wait-Until { -not (Test-Path $keptFile) } 5) (Get-KeptText)

# Everything in Windows Sandbox runs as administrator. A host whose file is labeled Medium integrity
# runs without admin rights, as do copies of the test program labeled the same way, which it groups;
# Windows keeps the test window that runs as administrator from such a host, which refuses it when
# it's dropped on a strip. Windows doesn't tell such a host whether Ctrl is held while a window of a
# program with admin rights is in front, so a drop with Ctrl isn't checked.
Stop-Host
Get-Process 'tab-b', 'tab-c' -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Seconds 1
& icacls.exe $hostExe /setintegritylevel medium | Out-Null
foreach ($name in 'tab-b', 'tab-c') { & icacls.exe $tabExe[$name] /setintegritylevel medium | Out-Null }
Start-Host
[void] (Start-TabApp 'tab-b' '600,150,520,360')
[void] (Start-TabApp 'tab-c' '40,480,460,220')
$b = Find-TabWindow 'tab-b'
$c = Find-TabWindow 'tab-c'
if ([Win32]::IsIconic($a)) { [void] [Win32]::ShowWindow($a, 9) }
[void] [Win32]::MoveWindow($a, 40, 150, 460, 300, $true)
Select-Window $b
Select-Window $c
Invoke-Drag (Get-TitlePoint $c) (New-Object System.Drawing.Point(($groupFrame.Left + 200), ($groupFrame.Top + 20))) -Control $true
Test-Check 'a host without admin rights groups windows of programs without them' (Wait-Until { (Test-Hidden $b) -and (Test-Shown $c) -and ((Get-Strips).Count -eq 1) } 5) (Get-States $b $c)
$rect = Get-StripRect (Get-Strips | Select-Object -First 1)
Select-Window $a
Invoke-Drag (Get-TitlePoint $a) (New-Object System.Drawing.Point(($rect.Right - 10), ($rect.Top + 16)))
Test-Check 'but refuses a window of a program with them, dropped on the strip' ((Test-Shown $a) -and (Test-Shown $c) -and (Test-Hidden $b)) (Get-States $a $b $c)
Test-Check 'the host logs why' (Test-Logged 'refused a window: "?RunsAsAdministrator')
Stop-Host
& icacls.exe $hostExe /setintegritylevel high | Out-Null

Get-Process 'tab-a', 'tab-b', 'tab-c', 'tab-fixed' -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Seconds 1
Remove-Item $tabsFolder -Recurse -Force -ErrorAction SilentlyContinue
Write-TabsSettings '{ "enabled": false }'
