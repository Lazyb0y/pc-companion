# Hide Apps: the Home switch turns it on and its page saves its settings. Hide app minimizes both
# windows of a test program and its palette; when one comes back, the other comes back behind it.
# Hide others keeps the app in front and its focus, minimizes other apps' windows, Settings and
# File Explorer's among them, and leaves an always-on-top window, a window without a Minimize button
# and a window with a dialog open; pressed again, it brings them back in their order, maximized as
# they were, and a window opened meanwhile changes nothing. Show all hidden windows in the PC
# Companion icon's menu brings windows back; windows on another virtual desktop are left alone and
# come back; a program that stopped responding doesn't hold the host; a listed
# Minimize to Tray app's windows stay on the taskbar; ending the host leaves the windows on the
# taskbar; and a host without admin rights asks a program with them to minimize and come back.
[void] (Close-SettingsApp)

# Detailed logging is on for the host's Debug entries.
$detailedUntil = [DateTimeOffset]::Now.AddHours(1).ToString('o')
function Write-HideSettings([string] $Section, [string] $Others = '') {
    Write-Settings "{ ""schemaVersion"": 1, ""general"": { ""theme"": ""Light"" }, ""diagnostics"": { ""detailedLoggingUntil"": ""$detailedUntil"" }, ""features"": { ""hideApps"": $Section$Others } }"
    Start-Sleep -Seconds 3
}
function Start-Host {
    if ((Get-Processes 'PCCompanion.Host').Count -eq 0) {
        Start-Process $hostExe
        [void] (Wait-Until { [Win32]::FindWindow('PCCompanion.Host', $null) -ne [IntPtr]::Zero } 30)
    }
    [void] (Wait-Until { [Win32]::FindWindow('PCCompanion.HideApps', $null) -ne [IntPtr]::Zero } 20)
    Start-Sleep -Seconds 1
}
function Stop-Host {
    [void] [Win32]::PostMessage([Win32]::FindWindow('PCCompanion.Host', $null), 0x0010, [UIntPtr]::Zero, [IntPtr]::Zero)
    [void] (Wait-Until { (Get-Processes 'PCCompanion.Host').Count -eq 0 } 15)
}

# The test program, run as copies with other names, which are other apps: two windows titled after
# its first argument, at the place in its second argument, the first with a palette it owns. "max"
# maximizes the second, "top" keeps both always on top and "nomin" takes their Minimize buttons away. Posted messages make it restore its
# window (WM_APP + 1), stop responding for the milliseconds in wParam (+ 3), and open a message box
# on its first window (+ 5).
$hideFolder = Join-Path $env:TEMP 'PCCompanionHideApps'
New-Item -ItemType Directory -Force -Path $hideFolder | Out-Null
$hideExe = Join-Path $hideFolder 'hide-app.exe'
Add-Type -OutputAssembly $hideExe -OutputType WindowsApplication -ReferencedAssemblies System.Windows.Forms, System.Drawing -TypeDefinition @'
using System;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
public static class HideApp {
    [DllImport("user32.dll")] private static extern void DisableProcessWindowsGhosting();
    [DllImport("user32.dll")] private static extern bool SetProcessDPIAware();
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr window, int command);

    private sealed class Window : Form {
        protected override void WndProc(ref Message message) {
            switch (message.Msg) {
                case 0x8001: ShowWindow(Handle, 9); return;
                case 0x8003: Thread.Sleep((int)message.WParam); return;
                case 0x8005: BeginInvoke((MethodInvoker)delegate { MessageBox.Show(this, "Box", Text + "-Box"); }); return;
            }
            base.WndProc(ref message);
        }
    }

    [STAThread]
    public static void Main(string[] args) {
        // Otherwise Windows puts a window of its own in place of one that doesn't respond for 5 seconds.
        DisableProcessWindowsGhosting();
        SetProcessDPIAware();
        var name = args[0];
        var place = args[1].Split(',');
        int x = int.Parse(place[0]), y = int.Parse(place[1]);
        var top = args.Contains("top");
        var minimize = !args.Contains("nomin");
        var first = new Window { Text = name + "-1", StartPosition = FormStartPosition.Manual, Bounds = new Rectangle(x, y, 480, 320), TopMost = top, MinimizeBox = minimize };
        var second = new Window { Text = name + "-2", StartPosition = FormStartPosition.Manual, Bounds = new Rectangle(x + 40, y + 40, 420, 280), TopMost = top, MinimizeBox = minimize };
        var palette = new Form { Text = name + "-Palette", FormBorderStyle = FormBorderStyle.FixedToolWindow, ShowInTaskbar = false, StartPosition = FormStartPosition.Manual, Bounds = new Rectangle(x + 30, y + 80, 200, 120) };
        first.Shown += delegate { palette.Show(first); };
        second.Shown += delegate { if (args.Contains("max")) second.WindowState = FormWindowState.Maximized; };
        second.Show();
        Application.Run(first);
    }
}
'@
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class HideChecks {
    [DllImport("user32.dll")] public static extern bool IsZoomed(IntPtr window);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr GetProp(IntPtr window, string name);
    public static bool IsInTray(IntPtr window) { return GetProp(window, "PCCompanion.MinimizeToTray") != IntPtr.Zero; }
}
'@
function Find-HideWindow([string] $App, [string] $Name) { [Win32]::FindWindow([NullString]::Value, "PCCompanion$App-$Name") }
function Start-HideApp([string] $App, [string] $Place, [string[]] $Flags = @()) {
    $exe = Join-Path $hideFolder "$($App.ToLowerInvariant()).exe"
    if (-not (Test-Path $exe)) { Copy-Item $hideExe $exe }
    $process = Start-Process $exe -ArgumentList (@("PCCompanion$App", $Place) + $Flags) -PassThru
    [void] (Wait-Until { ((Find-HideWindow $App 1) -ne [IntPtr]::Zero) -and ((Find-HideWindow $App 2) -ne [IntPtr]::Zero) } 30)
    Start-Sleep -Seconds 1
    return $process
}
function Get-State([IntPtr] $Window) { "visible $([Win32]::IsWindowVisible($Window)), minimized $([Win32]::IsIconic($Window)), maximized $([HideChecks]::IsZoomed($Window)), front $([Win32]::GetForegroundWindow() -eq $Window)" }
# On the taskbar, minimized: Hide Apps minimizes and never hides.
function Test-Minimized([IntPtr] $Window) { [Win32]::IsWindowVisible($Window) -and [Win32]::IsIconic($Window) }
function Test-Shown([IntPtr] $Window) { [Win32]::IsWindowVisible($Window) -and -not [Win32]::IsIconic($Window) }
# The windows' order, front to back.
function Get-Order([IntPtr[]] $Windows) { @([Win32]::TopLevelWindows() | Where-Object { $Windows -contains $_ }) }
function Restore-All([IntPtr[]] $Windows) {
    foreach ($window in $Windows) { if ([Win32]::IsIconic($window)) { [void] [Win32]::ShowWindow($window, 9) } }
    Start-Sleep -Seconds 1
}

# Brings a window to the front. Not with a click: the test windows overlap, and the maximized and
# always-on-top ones cover others' title bars. Windows Sandbox doesn't apply the foreground lock.
# Brings a window to the front. The test windows overlap, so a click can land on another one, and
# SetForegroundWindow alone didn't always take the front from File Explorer: a press of a key that
# has no use, as Hot Corners presses, first gives this script the right to.
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
$LeftWindows = 0x5B; $LeftControl = 0x1D; $LeftShift = 0x2A; $KeyH = 0x23; $KeyD = 0x20; $KeyF4 = 0x3E
function Send-Keys([int[]] $Keys, [int] $Wait = 1500) {
    foreach ($key in $Keys) { [void] [Win32]::ScanKey($key, $false, $key -eq $LeftWindows) }
    [array]::Reverse($Keys)
    foreach ($key in $Keys) { [void] [Win32]::ScanKey($key, $true, $key -eq $LeftWindows) }
    Start-Sleep -Milliseconds $Wait
}
function Send-HideApp { Send-Keys @($LeftWindows, $LeftControl, $KeyH) }
function Send-HideOthers { Send-Keys @($LeftWindows, $LeftControl, $LeftShift, $KeyH) }
# The names of the items of the open popup menu, joined by commas.
function Get-MenuItemNames {
    $menuCondition = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ClassNameProperty, '#32768')
    $script:menu = $null
    [void] (Wait-Until { $script:menu = [System.Windows.Automation.AutomationElement]::RootElement.FindFirst([System.Windows.Automation.TreeScope]::Children, $menuCondition); $null -ne $script:menu } 10)
    if (-not $script:menu) { return '' }
    $itemCondition = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::MenuItem)
    (@($script:menu.FindAll([System.Windows.Automation.TreeScope]::Descendants, $itemCondition)) | ForEach-Object { $_.Current.Name }) -join ', '
}
function Test-Logged([string] $Pattern, [int] $Seconds = 5) { Wait-Until { (Get-HostLog) -match $Pattern } $Seconds }
function Get-Presses([string] $Pattern) { Get-MatchCount (Get-HostLog) $Pattern }

# Turned on from Home, which saves only that it's on, the page shows the defaults and saves each
# setting.
function Get-HideSettings { (Get-Content (Join-Path $dataFolder 'settings.json') -Raw | ConvertFrom-Json).features.hideApps }
Write-Settings '{ "schemaVersion": 1, "general": { "theme": "Light" } }'
Start-Sleep -Seconds 2
Start-Process $appExe
[void] (Wait-Until { Get-AppWindow })
Start-Sleep -Seconds 3
Test-Check 'turn on Hide Apps on Home' (Set-Toggle 'Hide Apps' $true)
Start-Sleep -Seconds 2
$saved = Get-HideSettings
Test-Check 'the Home switch saves Hide Apps as on' ($saved.enabled -eq $true) "saved: $($saved | ConvertTo-Json -Compress)"
Test-Check 'select Hide Apps' (Select-NavigationItem 'Hide Apps')
Start-Sleep -Seconds 2
Save-Screenshot '13x-hide-apps-english'
$text = Get-WindowText
Test-Check 'Hide Apps shows its settings' (($text -contains 'Hide app') -and ($text -contains 'Hide others') -and ($text -contains 'Hide others only on the display in front') -and ($text -contains "Bring back an app's other windows with it") -and ($text -contains 'Permissions & data')) ($text -join ' | ')
Test-Check 'it says which windows stay' (@($text | Where-Object { $_ -like 'Some windows stay*' }).Count -eq 1) ($text -join ' | ')
$selected = Get-ListSelection 'Hide app'
Test-Check 'Hide app is Windows logo key + Ctrl + H' ($selected -eq 'Windows logo key + Ctrl + H') "selected: $selected"
$selected = Get-ListSelection 'Hide others'
Test-Check 'Hide others is Windows logo key + Ctrl + Shift + H' ($selected -eq 'Windows logo key + Ctrl + Shift + H') "selected: $selected"
Test-Check 'choose no Hide app shortcut' (Select-ListItem 'Hide app' 'None')
Start-Sleep -Seconds 1
Test-Check 'the page saves the Hide app shortcut' ((Get-HideSettings).hideAppShortcut -eq 'None') "saved: $(Get-HideSettings | ConvertTo-Json -Compress)"
Test-Check 'turn on Hide others only on the display in front' (Set-Toggle 'Hide others only on the display in front' $true)
Start-Sleep -Seconds 1
Test-Check 'the page saves it' ((Get-HideSettings).frontDisplayOnly -eq $true) "saved: $(Get-HideSettings | ConvertTo-Json -Compress)"
Test-Check 'turn off bringing back an app''s other windows' (Set-Toggle "Bring back an app's other windows with it" $false)
Start-Sleep -Seconds 1
Test-Check 'the page saves that' ((Get-HideSettings).bringBackWholeApp -eq $false) "saved: $(Get-HideSettings | ConvertTo-Json -Compress)"
[void] (Close-SettingsApp)

Write-HideSettings '{ "enabled": true }'
Start-Host
Start-Process explorer.exe 'C:\Windows'
$script:explorer = [IntPtr]::Zero
[void] (Wait-Until { $script:explorer = [Win32]::TopLevelWindows() | Where-Object { [Win32]::WindowClass($_) -eq 'CabinetWClass' } | Select-Object -First 1; $null -ne $script:explorer } 20)
Start-Process 'ms-settings:display'
$script:settingsWindow = [IntPtr]::Zero
[void] (Wait-Until { $script:settingsWindow = [Win32]::TopLevelWindows() | Where-Object { [Win32]::WindowClass($_) -eq 'ApplicationFrameWindow' -and [Win32]::WindowTitle($_) -eq 'Settings' } | Select-Object -First 1; $null -ne $script:settingsWindow } 30)
Start-Sleep -Seconds 5
$processes = @(
    (Start-HideApp 'HideApp' '40,40'),
    (Start-HideApp 'OtherApp' '60,420' @('max')),
    (Start-HideApp 'TopApp' '1000,470' @('top')),
    (Start-HideApp 'FixedApp' '700,300' @('nomin')))
$a1 = Find-HideWindow 'HideApp' 1; $a2 = Find-HideWindow 'HideApp' 2; $palette = Find-HideWindow 'HideApp' 'Palette'
$b1 = Find-HideWindow 'OtherApp' 1; $b2 = Find-HideWindow 'OtherApp' 2
$top1 = Find-HideWindow 'TopApp' 1; $fixed1 = Find-HideWindow 'FixedApp' 1
$stayers = @($top1, (Find-HideWindow 'TopApp' 2), $fixed1, (Find-HideWindow 'FixedApp' 2))
Test-Check 'the test programs show their windows' (@($a1, $a2, $palette, $b1, $b2, $top1, $fixed1) -notcontains [IntPtr]::Zero)
Test-Check 'the second window of OtherApp is maximized' ([HideChecks]::IsZoomed($b2))
$others = @($b1, $b2, $script:explorer, $script:settingsWindow | Where-Object { $_ -and $_ -ne [IntPtr]::Zero })
$all = @($a1, $a2) + $others + $stayers

# Hide app minimizes both of the app's windows and its palette, and the window behind them comes to the front.
Select-Window $a1
Send-HideApp
Test-Check 'Hide app minimizes both windows of the app in front' (Wait-Until { (Test-Minimized $a1) -and (Test-Minimized $a2) } 5) "first: $(Get-State $a1); second: $(Get-State $a2)"
Test-Check 'its palette goes with it' (-not [Win32]::IsWindowVisible($palette))
Test-Check 'another window comes to the front' (@($a1, $a2, $palette) -notcontains [Win32]::GetForegroundWindow())
Test-Check 'the host logs it' (Test-Logged 'Hide Apps: "?HideApp"?, "?Hid"?, 2 windows')

# The app restoring one window brings the other back right behind it.
[void] [Win32]::PostMessage($a2, 0x8001, [UIntPtr]::Zero, [IntPtr]::Zero)
Test-Check 'when one window comes back, the app''s other window comes back too' (Wait-Until { (Test-Shown $a1) -and (Test-Shown $a2) } 5) "first: $(Get-State $a1); second: $(Get-State $a2)"
Test-Check 'right behind it' (Wait-Until { [Win32]::IsInFrontOf($a2, $a1) } 3)
Test-Check 'the host logs it' (Test-Logged 'brought back 1 more windows')

# Hide others keeps the app in front and its focus, and leaves the always-on-top window and the
# window without a Minimize button.
Restore-All $all
foreach ($window in @($others[($others.Count - 1)..0]) + @($a2, $a1)) { Select-Window $window }
$before = Get-Order $others
Send-HideOthers
Test-Check 'Hide others minimizes the other apps'' windows, Settings and File Explorer''s too' (Wait-Until { @($others | Where-Object { -not (Test-Minimized $_) }).Count -eq 0 } 5) (($others | ForEach-Object { "$([Win32]::WindowTitle($_)): $(Get-State $_)" }) -join '; ')
Test-Check 'the app in front keeps its windows and the focus' ((Test-Shown $a1) -and (Test-Shown $a2) -and ([Win32]::GetForegroundWindow() -eq $a1)) "first: $(Get-State $a1); second: $(Get-State $a2)"
Test-Check 'the always-on-top window and the window without a Minimize button stay' ((Test-Shown $top1) -and (Test-Shown $fixed1)) "always on top: $(Get-State $top1); no Minimize button: $(Get-State $fixed1)"
Test-Check 'the host logs why they stay' (Test-Logged 'Hide Apps: "?HideOthers"?, "?Hid"?, \d+ windows; \d+ stayed \((?=[^)]*Topmost)(?=[^)]*NoMinimizeButton)')
Save-Screenshot '13y-hide-apps-others'

# Pressed again, Hide others brings them back in their order, maximized as they were, and the app keeps the front.
Send-HideOthers
Test-Check 'pressed again, Hide others brings the windows back' (Wait-Until { @($others | Where-Object { -not (Test-Shown $_) }).Count -eq 0 } 5) (($others | ForEach-Object { "$([Win32]::WindowTitle($_)): $(Get-State $_)" }) -join '; ')
Start-Sleep -Seconds 1
$after = Get-Order $others
Test-Check 'in their order' (($after -join ',') -eq ($before -join ',')) "before: $(($before | ForEach-Object { [Win32]::WindowTitle($_) }) -join ', '); after: $(($after | ForEach-Object { [Win32]::WindowTitle($_) }) -join ', ')"
Test-Check 'behind the app in front, which keeps the focus' ([Win32]::IsInFrontOf($a2, $others[0]) -and ([Win32]::GetForegroundWindow() -eq $a1)) "front: $([Win32]::WindowTitle([Win32]::GetForegroundWindow()))"
Test-Check 'the maximized window comes back maximized' ([HideChecks]::IsZoomed($b2)) (Get-State $b2)
Test-Check 'the host logs how long putting them in order took, with every window moved in time' (Test-Logged 'brought back \d+ windows in order in [\d.]+ ms; 0 skipped, 0 not moved in time')

# A window opened between the presses changes nothing.
Send-HideOthers
[void] (Wait-Until { Test-Minimized $b1 } 5)
Start-Process explorer.exe 'C:\Users'
$script:newExplorer = [IntPtr]::Zero
[void] (Wait-Until { $script:newExplorer = [Win32]::TopLevelWindows() | Where-Object { [Win32]::WindowClass($_) -eq 'CabinetWClass' -and $_ -ne $script:explorer } | Select-Object -First 1; $null -ne $script:newExplorer } 20)
Start-Sleep -Seconds 2
Select-Window $a1
Send-HideOthers
Test-Check 'a window opened between the presses doesn''t change which windows come back' ((Wait-Until { (Test-Shown $b1) -and (Test-Shown $b2) } 5) -and (Test-Shown $script:newExplorer)) "OtherApp: $(Get-State $b1); new File Explorer window: $(Get-State $script:newExplorer)"
[void] [Win32]::PostMessage($script:newExplorer, 0x0010, [UIntPtr]::Zero, [IntPtr]::Zero)
Start-Sleep -Seconds 1

# A window with a dialog open stays, with the warning sound, and the host says why.
[void] [Win32]::PostMessage($b1, 0x8005, [UIntPtr]::Zero, [IntPtr]::Zero)
[void] (Wait-Until { [Win32]::FindWindow('#32770', 'PCCompanionOtherApp-1-Box') -ne [IntPtr]::Zero } 10)
$box = [Win32]::FindWindow('#32770', 'PCCompanionOtherApp-1-Box')
Select-Window $a1
Send-HideOthers
Test-Check 'a window with a dialog open stays' ((Test-Shown $b1) -and [Win32]::IsWindowVisible($box)) (Get-State $b1)
# A Windows Forms message box disables every window of its app, so both of the program's windows stay.
Test-Check 'the host logs that it has a dialog open' (Test-Logged 'stayed \([^)]*DialogOpen \d')
[void] [Win32]::PostMessage($box, 0x0010, [UIntPtr]::Zero, [IntPtr]::Zero)
Start-Sleep -Seconds 1

# Show all hidden windows, in the PC Companion icon's menu, brings back every window Hide Apps minimized.
Test-Check 'the PC Companion icon''s menu requested' (Send-TrayEvent $WM_CONTEXTMENU $menuX $menuY)
$items = Get-MenuItemNames
Test-Check 'the menu offers Show all hidden windows' ($items -like '*Show all hidden windows*') $items
Save-Screenshot '13z-hide-apps-show-all'
Test-Check 'choose Show all hidden windows' (Select-TrayMenuItem 'Show all hidden windows')
Test-Check 'Show all brings back every window Hide Apps minimized' (Wait-Until { @($others | Where-Object { $_ -ne $b1 -and -not (Test-Shown $_) }).Count -eq 0 } 5) (($others | ForEach-Object { "$([Win32]::WindowTitle($_)): $(Get-State $_)" }) -join '; ')
Test-Check 'the host logs it' (Test-Logged 'Hide Apps: "?ShowAll"?, "?BroughtBack"?')
Restore-All $all

# On another virtual desktop, the first desktop's windows are left alone, and Show all brings them back on their own desktop.
Select-Window $a1
Send-HideOthers
[void] (Wait-Until { Test-Minimized $b1 } 5)
$desktops = Get-VirtualDesktopCount
Send-Keys @($LeftControl, $LeftWindows, $KeyD) 2500
$deskProcess = Start-HideApp 'DeskApp' '200,200'
$desk1 = Find-HideWindow 'DeskApp' 1
Test-Check 'a second virtual desktop opens' ((Get-VirtualDesktopCount) -gt $desktops -and [Win32]::IsCloaked($a1)) "desktops: $(Get-VirtualDesktopCount); first desktop's window cloaked: $([Win32]::IsCloaked($a1))"
Select-Window $desk1
$nothing = Get-Presses '"?HideOthers"?, "?NothingToDo"?'
Send-HideOthers
Test-Check 'Hide others leaves the first desktop''s windows alone' ((Test-Shown $a1) -and (Test-Minimized $b1) -and ((Get-Presses '"?HideOthers"?, "?NothingToDo"?') -gt $nothing)) "first desktop: $(Get-State $a1); minimized there: $(Get-State $b1)"
[void] (Send-TrayEvent $WM_CONTEXTMENU $menuX $menuY)
[void] (Select-TrayMenuItem 'Show all hidden windows')
$fronts = @()
$watch = [Diagnostics.Stopwatch]::StartNew()
while ($watch.ElapsedMilliseconds -lt 3000) {
    $front = [Win32]::GetForegroundWindow()
    $name = "$([Win32]::WindowClass($front))|$([Win32]::WindowTitle($front))"
    if ($fronts.Count -eq 0 -or $fronts[-1] -notlike "*$name") { $fronts += "$($watch.ElapsedMilliseconds) ms $name" }
    Start-Sleep -Milliseconds 20
}
Write-Report "[NOTE] The front after Show all on the second desktop: $($fronts -join '; ')"
Test-Check 'Show all brings the first desktop''s windows back' (Wait-Until { -not [Win32]::IsIconic($b1) } 5) (Get-State $b1)
# Known: Windows sometimes switches to the first desktop then, before Hide Apps brings the windows
# back (see Not included in the spec), so it's reported rather than checked.
$stayed = [Win32]::IsCloaked($b1) -and -not [Win32]::IsCloaked($desk1)
Write-Report "[NOTE] After Show all from the menu, Windows $(if ($stayed) { 'stayed on the second desktop' } else { 'switched to the first desktop' })."

Stop-Process -Id $deskProcess.Id -Force -ErrorAction SilentlyContinue
Send-Keys @($LeftControl, $LeftWindows, $KeyF4) 2500
Restore-All $all

# A program that stopped responding doesn't hold the host: its window minimizes once it responds.
Select-Window $a1
[void] [Win32]::PostMessage($b1, 0x8003, [UIntPtr]::new([uint32] 6000), [IntPtr]::Zero)
Start-Sleep -Milliseconds 300
Test-Check 'the test program stopped responding' (-not [Win32]::Responds($b1, 200))
$hid = Get-Presses '"?HideOthers"?, "?Hid"?'
Send-Keys @($LeftWindows, $LeftControl, $LeftShift, $KeyH) 0
Test-Check 'Hide others doesn''t wait for a program that stopped responding' (Wait-Until { (Get-Presses '"?HideOthers"?, "?Hid"?') -gt $hid } 2)
[void] (Wait-Until { [Win32]::Responds($b1, 500) } 20)
Test-Check 'once the program responds, its window is minimized' (Wait-Until { Test-Minimized $b1 } 5) (Get-State $b1)
Send-HideOthers
Restore-All $all

# A listed Minimize to Tray app's windows stay on the taskbar, so Hide others can bring them back.
Write-HideSettings '{ "enabled": true }' ", ""minimizeToTray"": { ""enabled"": true, ""shortcut"": ""None"", ""shiftMinimize"": false, ""minimizedApps"": [ { ""name"": ""Other app"", ""program"": ""$((Join-Path $hideFolder 'otherapp.exe') -replace '\\', '\\')"" } ] }"
[void] (Wait-Until { [Win32]::FindWindow('PCCompanion.MinimizeToTray', $null) -ne [IntPtr]::Zero } 20)
Select-Window $a1
Send-HideOthers
Start-Sleep -Seconds 1
Test-Check 'a listed Minimize to Tray app''s windows stay on the taskbar' ((Test-Minimized $b1) -and -not [HideChecks]::IsInTray($b1)) (Get-State $b1)
Test-Check 'Minimize to Tray logs why' (Test-Logged '"?MinimizedByHideApps"?')
Send-HideOthers
Test-Check 'and Hide others brings them back' (Wait-Until { Test-Shown $b1 } 5) (Get-State $b1)
Write-HideSettings '{ "enabled": true }'
Restore-All $all

# Ended, the host leaves the windows minimized on the taskbar.
Select-Window $a1
Send-HideOthers
[void] (Wait-Until { Test-Minimized $b1 } 5)
Get-Processes 'PCCompanion.Host' | Stop-Process -Force
Start-Sleep -Seconds 2
Test-Check 'a host that''s ended leaves the windows minimized on the taskbar' ((Test-Minimized $b1) -and (Test-Minimized $b2)) "first: $(Get-State $b1); second: $(Get-State $b2)"
Restore-All $all

# Everything in Windows Sandbox runs as administrator; a host whose file is labeled Medium integrity
# runs without admin rights, and asks the test programs, which have them, to minimize and come back.
& icacls.exe $hostExe /setintegritylevel medium | Out-Null
Start-Host
Select-Window $a1
$hid = Get-Presses '"?HideOthers"?, "?Hid"?'
Send-HideOthers
if ((Get-Presses '"?HideOthers"?, "?Hid"?') -gt $hid) {
    Test-Check 'a host without admin rights asks a program with them to minimize' (Wait-Until { Test-Minimized $b1 } 5) (Get-State $b1)
    Send-HideOthers
    Test-Check 'and to come back, then gives the front back' ((Wait-Until { Test-Shown $b1 } 5) -and (Wait-Until { [Win32]::GetForegroundWindow() -eq $a1 } 5)) "$(Get-State $b1); front: $([Win32]::WindowTitle([Win32]::GetForegroundWindow()))"
} else {
    Write-Report '[NOTE] Without admin rights, the host got no shortcut while a window with admin rights was in front.'
}
Stop-Host
& icacls.exe $hostExe /setintegritylevel high | Out-Null

foreach ($process in $processes) { Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue }
foreach ($window in $script:explorer, $script:settingsWindow) { if ($window) { [void] [Win32]::PostMessage($window, 0x0010, [UIntPtr]::Zero, [IntPtr]::Zero) } }
Remove-Item $hideFolder -Recurse -Force -ErrorAction SilentlyContinue
Write-HideSettings '{ "enabled": false }'
