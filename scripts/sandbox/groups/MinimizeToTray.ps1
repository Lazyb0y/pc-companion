# Minimize to Tray: the shortcut, Shift + Minimize and a listed app send a test program's window to
# the tray, where its app has one icon, named after the app and never after a window; selecting the
# icon or choosing from its menu brings windows back. A window leaves the tray when its app shows it,
# closes it or ends; a program that stopped responding doesn't hold the host; Show desktop sends no
# listed app's window; Settings and a window with a dialog open stay; File Explorer windows go; the
# icons come back after Explorer restarts; windows come back on the taskbar when the host exits, and
# after the host is ended, at its next start; and a host without admin rights leaves the window of a
# program with them alone.
[void] (Close-SettingsApp)

# Detailed logging is on for the host's Debug entries.
$detailedUntil = [DateTimeOffset]::Now.AddHours(1).ToString('o')
function Write-TraySettings([string] $Section) {
    Write-Settings "{ ""schemaVersion"": 1, ""general"": { ""theme"": ""Light"" }, ""diagnostics"": { ""detailedLoggingUntil"": ""$detailedUntil"" }, ""features"": { ""minimizeToTray"": $Section } }"
    Start-Sleep -Seconds 3
}
function Start-Host {
    if ((Get-Processes 'PCCompanion.Host').Count -eq 0) {
        Start-Process $hostExe
        [void] (Wait-Until { [Win32]::FindWindow('PCCompanion.Host', $null) -ne [IntPtr]::Zero } 30)
    }
    [void] (Wait-Until { [Win32]::FindWindow('PCCompanion.MinimizeToTray', $null) -ne [IntPtr]::Zero } 20)
    Start-Sleep -Seconds 1
}

# The test program: two windows, the first with a palette it owns. Posted messages make it show its
# first window again (WM_APP + 1), change that window's title (+ 2), stop responding for the
# milliseconds in wParam (+ 3), and open a message box on its first window (+ 5).
$trayFolder = Join-Path $env:TEMP 'PCCompanionTrayApp'
New-Item -ItemType Directory -Force -Path $trayFolder | Out-Null
$trayExe = Join-Path $trayFolder 'tray-app.exe'
Add-Type -OutputAssembly $trayExe -OutputType WindowsApplication -ReferencedAssemblies System.Windows.Forms, System.Drawing -TypeDefinition @'
using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
public static class TrayApp {
    [DllImport("user32.dll")] private static extern void DisableProcessWindowsGhosting();
    [DllImport("user32.dll")] private static extern bool SetProcessDPIAware();
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr window, int command);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr window);

    private sealed class Window : Form {
        protected override void WndProc(ref Message message) {
            switch (message.Msg) {
                case 0x8001: ShowWindow(Handle, 9); SetForegroundWindow(Handle); return;
                case 0x8002: Text = Text + " changed"; return;
                case 0x8003: Thread.Sleep((int)message.WParam); return;
                case 0x8005: BeginInvoke((MethodInvoker)delegate { MessageBox.Show(this, "Box", "PCCompanionTrayApp-Box"); }); return;
            }
            base.WndProc(ref message);
        }
    }

    [STAThread]
    public static void Main() {
        // Otherwise Windows puts a window of its own in place of one that doesn't respond for 5 seconds.
        DisableProcessWindowsGhosting();
        SetProcessDPIAware();
        var first = new Window { Text = "PCCompanionTrayApp-1", StartPosition = FormStartPosition.Manual, Bounds = new Rectangle(60, 60, 480, 320) };
        var second = new Window { Text = "PCCompanionTrayApp-2", StartPosition = FormStartPosition.Manual, Bounds = new Rectangle(600, 60, 420, 300) };
        var palette = new Form { Text = "PCCompanionTrayApp-Palette", FormBorderStyle = FormBorderStyle.FixedToolWindow, ShowInTaskbar = false, StartPosition = FormStartPosition.Manual, Bounds = new Rectangle(90, 140, 200, 120) };
        first.Shown += delegate { palette.Show(first); };
        second.Show();
        Application.Run(first);
    }
}
'@
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class TrayChecks {
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr window, int attribute, out RECT value, int size);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr window, out RECT rect);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr GetProp(IntPtr window, string name);

    // The middle of the window's Minimize button, the first of its caption buttons.
    public static int[] MinimizeButton(IntPtr window) {
        RECT buttons, bounds;
        DwmGetWindowAttribute(window, 5, out buttons, Marshal.SizeOf(typeof(RECT))); // DWMWA_CAPTION_BUTTON_BOUNDS
        GetWindowRect(window, out bounds);
        return new[] { bounds.Left + buttons.Left + (buttons.Right - buttons.Left) / 6, bounds.Top + (buttons.Top + buttons.Bottom) / 2 };
    }

    public static bool IsMarked(IntPtr window) { return GetProp(window, "PCCompanion.MinimizeToTray") != IntPtr.Zero; }
}
'@
function Find-TrayWindow([string] $Name) { [Win32]::FindWindow([NullString]::Value, "PCCompanionTrayApp-$Name") }
function Start-TrayApp {
    $process = Start-Process $trayExe -PassThru
    [void] (Wait-Until { ((Find-TrayWindow 1) -ne [IntPtr]::Zero) -and ((Find-TrayWindow 2) -ne [IntPtr]::Zero) -and ((Find-TrayWindow 'Palette') -ne [IntPtr]::Zero) } 30)
    Start-Sleep -Seconds 1
    return $process
}
function Get-WindowState([IntPtr] $Window) { "visible $([Win32]::IsWindowVisible($Window)), minimized $([Win32]::IsIconic($Window)), marked $([TrayChecks]::IsMarked($Window)), front $([Win32]::GetForegroundWindow() -eq $Window)" }
function Test-InTray([IntPtr] $Window) { (-not [Win32]::IsWindowVisible($Window)) -and [Win32]::IsIconic($Window) -and [TrayChecks]::IsMarked($Window) }
function Test-Back([IntPtr] $Window) { [Win32]::IsWindowVisible($Window) -and (-not [Win32]::IsIconic($Window)) -and (-not [TrayChecks]::IsMarked($Window)) }

# Clicks the middle of a window's title bar, as a user brings it to the front.
function Select-Window([IntPtr] $Window) {
    $area = New-Object Win32+RECT
    [void] [Win32]::GetWindowRect($Window, [ref] $area)
    [void] (Move-Pointer ([int](($area.Left + $area.Right) / 2)) ($area.Top + 12))
    [void] [Win32]::LeftButton($true)
    [void] [Win32]::LeftButton($false)
    Start-Sleep -Seconds 1
}
$LeftWindows = 0x5B; $LeftShift = 0x2A; $KeyZ = 0x2C; $KeyD = 0x20
function Send-Shortcut {
    [void] [Win32]::ScanKey($LeftWindows, $false, $true)
    [void] [Win32]::ScanKey($LeftShift, $false, $false)
    [void] [Win32]::ScanKey($KeyZ, $false, $false)
    [void] [Win32]::ScanKey($KeyZ, $true, $false)
    [void] [Win32]::ScanKey($LeftShift, $true, $false)
    [void] [Win32]::ScanKey($LeftWindows, $true, $true)
    Start-Sleep -Milliseconds 500
}
function Send-ShowDesktop {
    [void] [Win32]::ScanKey($LeftWindows, $false, $true)
    [void] [Win32]::ScanKey($KeyD, $false, $false)
    [void] [Win32]::ScanKey($KeyD, $true, $false)
    Start-Sleep -Milliseconds 150
    [void] [Win32]::ScanKey($LeftWindows, $true, $true)
    Start-Sleep -Milliseconds 1500
}
# Clicks the window's Minimize button, holding Shift when asked, as long as a user does.
function Click-Minimize([IntPtr] $Window, [bool] $Shift) {
    $point = [TrayChecks]::MinimizeButton($Window)
    [void] (Move-Pointer $point[0] $point[1])
    if ($Shift) { [void] [Win32]::ScanKey($LeftShift, $false, $false) }
    [void] [Win32]::LeftButton($true)
    [void] [Win32]::LeftButton($false)
    Start-Sleep -Milliseconds 300
    if ($Shift) { [void] [Win32]::ScanKey($LeftShift, $true, $false) }
    Start-Sleep -Seconds 1
}

# What Windows keeps of the host's app icons: the ID and the tooltip each was added with.
function Get-TrayIconSettings {
    @(Get-ChildItem 'HKCU:\Control Panel\NotifyIconSettings' -ErrorAction SilentlyContinue | ForEach-Object {
        $values = Get-ItemProperty $_.PSPath
        if ($values.ExecutablePath -like '*\PCCompanion.Host.exe' -and $values.UID -ge 0x100) {
            [pscustomobject] @{ Id = [int] $values.UID; Name = $values.InitialTooltip }
        }
    })
}
function Get-TrayIconId([string] $Name) {
    $script:trayIcon = $null
    [void] (Wait-Until { $script:trayIcon = Get-TrayIconSettings | Where-Object { $_.Name -eq $Name } | Select-Object -First 1; $null -ne $script:trayIcon } 15)
    if ($script:trayIcon) { $script:trayIcon.Id } else { 0 }
}
function Select-TrayIcon([int] $Id) {
    [void] (Send-TrayEvent $NIN_SELECT -IconId $Id -WindowClass 'PCCompanion.MinimizeToTray')
    Start-Sleep -Seconds 2
}

function Get-Taskbar {
    $condition = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ClassNameProperty, 'Shell_TrayWnd')
    [System.Windows.Automation.AutomationElement]::RootElement.FindFirst([System.Windows.Automation.TreeScope]::Children, $condition)
}
function Get-ButtonNames($Element, [string] $Like) {
    if (-not $Element) { return @() }
    $condition = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Button)
    @($Element.FindAll([System.Windows.Automation.TreeScope]::Descendants, $condition) | ForEach-Object { $_.Current.Name -replace "`r?`n", ' / ' } | Where-Object { $_ -like $Like })
}
# The names of the icons in the hidden icons menu, which this opens and closes.
function Get-HiddenIconNames([string] $Like, [string] $Screenshot = '') {
    $condition = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, 'Show Hidden Icons')
    $chevron = (Get-Taskbar).FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
    if (-not $chevron) { return @() }
    $chevron.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    Start-Sleep -Seconds 2
    if ($Screenshot) { Save-Screenshot $Screenshot }
    $names = @([System.Windows.Automation.AutomationElement]::RootElement.FindAll([System.Windows.Automation.TreeScope]::Children, [System.Windows.Automation.Condition]::TrueCondition) |
        Where-Object { $_.Current.ClassName -like '*Overflow*' } | ForEach-Object { Get-ButtonNames $_ $Like })
    Send-Key 0x1B
    Start-Sleep -Seconds 1
    return $names
}
# Clicks an icon in the hidden icons menu with the mouse, as a user does: Explorer then lets the icon's
# program bring a window to the front, which a posted message doesn't.
function Click-HiddenIcon([string] $Like) {
    $condition = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, 'Show Hidden Icons')
    $chevron = (Get-Taskbar).FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
    if (-not $chevron) { return $false }
    $chevron.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    Start-Sleep -Seconds 2
    $buttonCondition = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Button)
    $button = @([System.Windows.Automation.AutomationElement]::RootElement.FindAll([System.Windows.Automation.TreeScope]::Children, [System.Windows.Automation.Condition]::TrueCondition) |
        Where-Object { $_.Current.ClassName -like '*Overflow*' } |
        ForEach-Object { $_.FindAll([System.Windows.Automation.TreeScope]::Descendants, $buttonCondition) } |
        Where-Object { $_.Current.Name -like $Like }) | Select-Object -First 1
    if (-not $button) { Send-Key 0x1B; return $false }
    $rectangle = $button.Current.BoundingRectangle
    if (-not (Move-Pointer ([int] ($rectangle.X + $rectangle.Width / 2)) ([int] ($rectangle.Y + $rectangle.Height / 2)))) { return $false }
    [void] [Win32]::LeftButton($true)
    [void] [Win32]::LeftButton($false)
    Start-Sleep -Seconds 2
    return $true
}
function Get-MenuItemNames {
    $menuCondition = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ClassNameProperty, '#32768')
    $script:menu = $null
    [void] (Wait-Until { $script:menu = [System.Windows.Automation.AutomationElement]::RootElement.FindFirst([System.Windows.Automation.TreeScope]::Children, $menuCondition); $null -ne $script:menu } 10)
    if (-not $script:menu) { return '' }
    $itemCondition = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::MenuItem)
    (@($script:menu.FindAll([System.Windows.Automation.TreeScope]::Descendants, $itemCondition)) | ForEach-Object { $_.Current.Name }) -join ', '
}
function Test-Logged([string] $Pattern, [int] $Seconds = 5) { Wait-Until { (Get-HostLog) -match $Pattern } $Seconds }

Write-TraySettings '{ "enabled": true }'
Start-Host
$trayProcess = Start-TrayApp
$first = Find-TrayWindow 1
$second = Find-TrayWindow 2
$palette = Find-TrayWindow 'Palette'
Test-Check 'the test program shows its two windows and the palette' (($first -ne [IntPtr]::Zero) -and ($second -ne [IntPtr]::Zero) -and ($palette -ne [IntPtr]::Zero))
Test-Check 'the test program is on the taskbar with two windows' (Wait-Until { (Get-ButtonNames (Get-Taskbar) 'tray-app*') -like '*2 running windows*' } 10) ((Get-ButtonNames (Get-Taskbar) 'tray-app*') -join ' | ')

# The shortcut sends the window in front, with its palette.
Select-Window $first
Send-Shortcut
Test-Check 'Windows + Shift + Z sends the window in front to the tray' (Wait-Until { Test-InTray $first } 5) (Get-WindowState $first)
Test-Check 'its palette goes with it' (-not [Win32]::IsWindowVisible($palette))
Test-Check 'it leaves the taskbar' (Wait-Until { (Get-ButtonNames (Get-Taskbar) 'tray-app*') -like '*1 running window*' } 10) ((Get-ButtonNames (Get-Taskbar) 'tray-app*') -join ' | ')
Test-Check 'the host logs the shortcut' (Test-Logged 'sent a window to the tray: "?Shortcut"?; 1 windows are in the tray')
$appId = Get-TrayIconId 'tray-app'
Test-Check 'the app gets an icon named after the app' ($appId -ge 0x100) ((Get-TrayIconSettings | ForEach-Object { "$($_.Id) $($_.Name)" }) -join ', ')
Test-Check 'Windows keeps no window title as an icon''s name' (-not (Get-TrayIconSettings | Where-Object { $_.Name -like '*PCCompanionTrayApp*' }))
$hidden = Get-HiddenIconNames 'tray-app*' '13t-minimize-to-tray-hidden-icon'
Test-Check 'its icon is in the hidden icons menu, with the window''s title in its tooltip' ($hidden -like '*PCCompanionTrayApp-1*') ($hidden -join ' | ')

# The window comes back with its last active popup in front, as switching to an app does: here its palette.
Test-Check 'click the app''s icon in the hidden icons menu' (Click-HiddenIcon 'tray-app*')
Save-Screenshot '13w-minimize-to-tray-brought-back'
Test-Check 'selecting the icon brings the window back, with its palette, in front' ((Wait-Until { Test-Back $first } 5) -and (@($first, $palette) -contains [Win32]::GetForegroundWindow()) -and [Win32]::IsWindowVisible($palette)) "$(Get-WindowState $first); front: $([Win32]::WindowTitle([Win32]::GetForegroundWindow()))"
Test-Check 'the host logs it' (Test-Logged 'A window left the tray: "?BroughtBack"?; 0 windows')

# Shift + Minimize sends it; Minimize alone doesn't.
Click-Minimize $first $true
Test-Check 'Shift + Minimize sends the window to the tray' (Wait-Until { Test-InTray $first } 5) (Get-WindowState $first)
Select-TrayIcon $appId
Test-Check 'and the icon brings it back' (Wait-Until { Test-Back $first } 5) (Get-WindowState $first)
Click-Minimize $first $false
Start-Sleep -Seconds 1
Test-Check 'Minimize without Shift leaves the window on the taskbar' ([Win32]::IsWindowVisible($first) -and [Win32]::IsIconic($first) -and -not [TrayChecks]::IsMarked($first)) (Get-WindowState $first)
[void] [Win32]::ShowWindow($first, 9)
Start-Sleep -Seconds 1

# Both windows share the app's icon, whose menu lists them.
Select-Window $first
Send-Shortcut
Select-Window $second
Send-Shortcut
Test-Check 'both windows of the app go to the tray' ((Wait-Until { (Test-InTray $first) -and (Test-InTray $second) } 5)) "first: $(Get-WindowState $first); second: $(Get-WindowState $second)"
Test-Check 'they share one icon' (@(Get-TrayIconSettings | Where-Object { $_.Name -eq 'tray-app' }).Count -eq 1) ((Get-TrayIconSettings | ForEach-Object { "$($_.Id) $($_.Name)" }) -join ', ')
$hidden = Get-HiddenIconNames 'tray-app*'
Test-Check 'its tooltip counts the windows and lists their titles' (($hidden -like '*2 windows*') -and ($hidden -like '*PCCompanionTrayApp-2*') -and ($hidden -like '*PCCompanionTrayApp-1*')) ($hidden -join ' | ')
Test-Check 'the icon''s menu requested' (Send-TrayEvent $WM_CONTEXTMENU $menuX $menuY -IconId $appId -WindowClass 'PCCompanion.MinimizeToTray')
$items = Get-MenuItemNames
Test-Check 'the menu lists the windows, the last one first, then Bring all back and the settings' ($items -eq 'PCCompanionTrayApp-2, PCCompanionTrayApp-1, Bring all back, Minimize to Tray settings') $items
Save-Screenshot '13u-minimize-to-tray-menu'
Test-Check 'choose Bring all back' (Select-TrayMenuItem 'Bring all back')
Test-Check 'Bring all back brings both windows back' (Wait-Until { (Test-Back $first) -and (Test-Back $second) } 5) "first: $(Get-WindowState $first); second: $(Get-WindowState $second)"

# A title change shows in the tooltip; the app showing its window takes it out of the tray.
Select-Window $first
Send-Shortcut
[void] [Win32]::PostMessage($first, 0x8002, [UIntPtr]::Zero, [IntPtr]::Zero)
Start-Sleep -Seconds 2
$hidden = Get-HiddenIconNames 'tray-app*'
Test-Check 'a title change while in the tray shows in the tooltip' ($hidden -like '*PCCompanionTrayApp-1 changed*') ($hidden -join ' | ')
$first = [Win32]::FindWindow([NullString]::Value, 'PCCompanionTrayApp-1 changed')
[void] [Win32]::PostMessage($first, 0x8001, [UIntPtr]::Zero, [IntPtr]::Zero)
Test-Check 'the app showing its window takes it out of the tray' ((Wait-Until { [Win32]::IsWindowVisible($first) -and -not [TrayChecks]::IsMarked($first) } 5) -and (Test-Logged '"?AppShowedIt"?')) (Get-WindowState $first)

# A program that stopped responding doesn't hold the host: the window goes once it responds.
Select-Window $first
[void] [Win32]::PostMessage($first, 0x8003, [UIntPtr]::new([uint32] 6000), [IntPtr]::Zero)
Start-Sleep -Milliseconds 300
Test-Check 'the test program stopped responding' (-not [Win32]::Responds($first, 200))
$pressed = Get-MatchCount (Get-HostLog) 'shortcut was pressed: "?Sent'
Send-Shortcut
Test-Check 'the shortcut doesn''t wait for a program that stopped responding' (Wait-Until { (Get-MatchCount (Get-HostLog) 'shortcut was pressed: "?Sent') -gt $pressed } 2)
[void] (Wait-Until { [Win32]::Responds($first, 500) } 20)
Test-Check 'once the program responds, its window is in the tray' (Wait-Until { Test-InTray $first } 5) (Get-WindowState $first)
Select-TrayIcon $appId
[void] (Wait-Until { Test-Back $first } 5)

# A listed app's window goes when it's minimized alone, not with Show desktop.
Write-TraySettings "{ ""enabled"": true, ""minimizedApps"": [ { ""name"": ""Tray app"", ""program"": ""$($trayExe -replace '\\', '\\')"" } ] }"
[void] [Win32]::ShowWindow($first, 6) # SW_MINIMIZE
Test-Check 'a listed app''s window goes to the tray when it''s minimized' (Wait-Until { Test-InTray $first } 5) (Get-WindowState $first)
Select-TrayIcon $appId
[void] (Wait-Until { Test-Back $first } 5)
Select-Window $first
Send-ShowDesktop
Test-Check 'Show desktop minimizes the listed app''s windows to the taskbar, not the tray' ([Win32]::IsIconic($first) -and [Win32]::IsWindowVisible($first) -and [Win32]::IsWindowVisible($second) -and -not [TrayChecks]::IsMarked($first)) "first: $(Get-WindowState $first); second: $(Get-WindowState $second)"
Test-Check 'the host logs why' (Test-Logged '"?ListedApp"?, "?(WindowsKeyHeld|MinimizedWithOthers)')
Send-ShowDesktop
Write-TraySettings '{ "enabled": true }'
foreach ($window in $first, $second) { if ([Win32]::IsIconic($window)) { [void] [Win32]::ShowWindow($window, 9) } }

# A window with a dialog open stays, as does Settings, a Store app in Windows' shared frame.
[void] [Win32]::PostMessage($first, 0x8005, [UIntPtr]::Zero, [IntPtr]::Zero)
[void] (Wait-Until { [Win32]::FindWindow('#32770', 'PCCompanionTrayApp-Box') -ne [IntPtr]::Zero } 10)
$box = [Win32]::FindWindow('#32770', 'PCCompanionTrayApp-Box')
Select-Window $box
Send-Shortcut
Test-Check 'a window with a dialog open stays on the taskbar' ([Win32]::IsWindowVisible($first) -and [Win32]::IsWindowVisible($box) -and -not [TrayChecks]::IsMarked($first)) (Get-WindowState $first)
Test-Check 'the host logs that it has a dialog open' (Test-Logged '"?Shortcut"?, "?DialogOpen')
[void] [Win32]::PostMessage($box, 0x0010, [UIntPtr]::Zero, [IntPtr]::Zero)
Start-Sleep -Seconds 1
$before = [Win32]::TopLevelWindows()
Start-Process 'ms-settings:display'
$script:settingsWindow = [IntPtr]::Zero
[void] (Wait-Until { $script:settingsWindow = [Win32]::TopLevelWindows() | Where-Object { [Win32]::WindowClass($_) -eq 'ApplicationFrameWindow' -and [Win32]::WindowTitle($_) -eq 'Settings' } | Select-Object -First 1; $null -ne $script:settingsWindow } 30)
Start-Sleep -Seconds 3
if ($script:settingsWindow) {
    Select-Window $script:settingsWindow
    Send-Shortcut
    Test-Check 'Settings, a Store app in Windows'' shared frame, stays' ([Win32]::IsWindowVisible($script:settingsWindow) -and -not [TrayChecks]::IsMarked($script:settingsWindow))
    Test-Check 'the host logs that it''s in the shared frame' (Test-Logged '"?Shortcut"?, "?SharedAppFrame')
    [void] [Win32]::PostMessage($script:settingsWindow, 0x0010, [UIntPtr]::Zero, [IntPtr]::Zero)
} else {
    Test-Check 'Settings opens' $false
}

# File Explorer windows go to the tray and come back.
$before = [Win32]::TopLevelWindows()
Start-Process explorer.exe 'C:\Windows'
$script:explorer = [IntPtr]::Zero
[void] (Wait-Until { $script:explorer = [Win32]::TopLevelWindows() | Where-Object { [Win32]::WindowClass($_) -eq 'CabinetWClass' } | Select-Object -First 1; $null -ne $script:explorer } 20)
Start-Sleep -Seconds 2
Select-Window $script:explorer
Send-Shortcut
Test-Check 'a File Explorer window goes to the tray' (Wait-Until { Test-InTray $script:explorer } 5) (Get-WindowState $script:explorer)
$explorerId = (Get-TrayIconSettings | Where-Object { $_.Name -like '*Explorer*' } | Select-Object -First 1).Id
Test-Check 'File Explorer gets an icon of its own' ($explorerId -ge 0x100 -and $explorerId -ne $appId) ((Get-TrayIconSettings | ForEach-Object { "$($_.Id) $($_.Name)" }) -join ', ')
Select-TrayIcon $explorerId
Test-Check 'and comes back' (Wait-Until { Test-Back $script:explorer } 5) (Get-WindowState $script:explorer)
[void] [Win32]::PostMessage($script:explorer, 0x0010, [UIntPtr]::Zero, [IntPtr]::Zero)

# The icons come back after Explorer restarts.
Select-Window $first
Send-Shortcut
[void] (Wait-Until { Test-InTray $first } 5)
$taskbarBefore = [Win32]::FindWindow('Shell_TrayWnd', $null)
Get-Process explorer -ErrorAction SilentlyContinue | Stop-Process -Force
if (-not (Wait-Until { $taskbar = [Win32]::FindWindow('Shell_TrayWnd', $null); ($taskbar -ne [IntPtr]::Zero) -and ($taskbar -ne $taskbarBefore) } 20)) { Start-Process explorer.exe }
[void] (Wait-Until { $taskbar = [Win32]::FindWindow('Shell_TrayWnd', $null); ($taskbar -ne [IntPtr]::Zero) -and ($taskbar -ne $taskbarBefore) } 30)
Start-Sleep -Seconds 8
[void] (Get-HiddenIconNames 'tray-app*' '13v-minimize-to-tray-after-explorer-restart')
Test-Check 'after Explorer restarts, the window is still in the tray' (Test-InTray $first) (Get-WindowState $first)
Select-TrayIcon $appId
Test-Check 'and its icon still brings it back' (Wait-Until { Test-Back $first } 5) (Get-WindowState $first)

# Closing a window in the tray, or ending its program, takes it out.
Select-Window $second
Send-Shortcut
[void] (Wait-Until { Test-InTray $second } 5)
[void] [Win32]::PostMessage($second, 0x0010, [UIntPtr]::Zero, [IntPtr]::Zero)
Test-Check 'a window in the tray that closes leaves it' ((Wait-Until { -not [Win32]::IsWindow($second) } 5) -and (Test-Logged '"?Closed"?; 0 windows'))
Select-Window $first
Send-Shortcut
[void] (Wait-Until { Test-InTray $first } 5)
$closed = Get-MatchCount (Get-HostLog) '"?Closed"?; 0 windows'
Stop-Process -Id $trayProcess.Id -Force
Test-Check 'a window whose program ends leaves the tray' (Wait-Until { (Get-MatchCount (Get-HostLog) '"?Closed"?; 0 windows') -gt $closed } 5)

# PC Companion exiting brings the windows back on the taskbar, minimized; ended, it brings them back at its next start.
$trayProcess = Start-TrayApp
$first = Find-TrayWindow 1
Select-Window $first
Send-Shortcut
[void] (Wait-Until { Test-InTray $first } 5)
[void] [Win32]::PostMessage([Win32]::FindWindow('PCCompanion.Host', $null), 0x0010, [UIntPtr]::Zero, [IntPtr]::Zero)
[void] (Wait-Until { (Get-Processes 'PCCompanion.Host').Count -eq 0 } 15)
Test-Check 'when PC Companion exits, the window comes back on the taskbar, minimized' ([Win32]::IsWindowVisible($first) -and [Win32]::IsIconic($first) -and -not [TrayChecks]::IsMarked($first)) (Get-WindowState $first)
[void] [Win32]::ShowWindow($first, 9)
Start-Host
Select-Window $first
Send-Shortcut
[void] (Wait-Until { Test-InTray $first } 5)
Get-Processes 'PCCompanion.Host' | Stop-Process -Force
Start-Sleep -Seconds 2
Test-Check 'a host that''s ended leaves the window hidden and marked' (Test-InTray $first) (Get-WindowState $first)
Start-Host
Test-Check 'at the next start, the window comes back on the taskbar, minimized' (Wait-Until { [Win32]::IsWindowVisible($first) -and [Win32]::IsIconic($first) -and -not [TrayChecks]::IsMarked($first) } 10) (Get-WindowState $first)
Test-Check 'the host logs it' (Test-Logged 'brought back 1 windows left in the tray')
[void] [Win32]::ShowWindow($first, 9)
Start-Sleep -Seconds 1

# Everything in Windows Sandbox runs as administrator; a host whose file is labeled Medium integrity
# runs without admin rights, and Windows keeps the test program's window, which has them, from it.
[void] [Win32]::PostMessage([Win32]::FindWindow('PCCompanion.Host', $null), 0x0010, [UIntPtr]::Zero, [IntPtr]::Zero)
[void] (Wait-Until { (Get-Processes 'PCCompanion.Host').Count -eq 0 } 15)
& icacls.exe $hostExe /setintegritylevel medium | Out-Null
Start-Host
Select-Window $first
$pressed = Get-MatchCount (Get-HostLog) 'shortcut was pressed'
Send-Shortcut
Start-Sleep -Seconds 1
Test-Check 'a host without admin rights leaves the window of a program with them alone' ([Win32]::IsWindowVisible($first) -and -not [Win32]::IsIconic($first) -and -not [TrayChecks]::IsMarked($first)) (Get-WindowState $first)
if ((Get-MatchCount (Get-HostLog) 'shortcut was pressed') -gt $pressed) {
    Test-Check 'the host logs that Windows refused' (Test-Logged '"?Shortcut"?, "?Refused')
} else {
    Write-Report '[NOTE] Without admin rights, the host got no shortcut while a window with admin rights was in front.'
}
# Windows hides the keys from a program without admin rights while one with them is in front, so the
# host doesn't see Shift held, and the window minimizes as usual.
$shiftEntries = Get-MatchCount (Get-HostLog) '"?ShiftMinimize"?'
Click-Minimize $first $true
Test-Check 'Shift + Minimize on it leaves it minimized on the taskbar' ([Win32]::IsWindowVisible($first) -and [Win32]::IsIconic($first) -and -not [TrayChecks]::IsMarked($first)) (Get-WindowState $first)
Start-Sleep -Seconds 1
Test-Check 'the host tries nothing with it' ((Get-MatchCount (Get-HostLog) '"?ShiftMinimize"?') -eq $shiftEntries)
[void] [Win32]::PostMessage([Win32]::FindWindow('PCCompanion.Host', $null), 0x0010, [UIntPtr]::Zero, [IntPtr]::Zero)
[void] (Wait-Until { (Get-Processes 'PCCompanion.Host').Count -eq 0 } 15)
& icacls.exe $hostExe /setintegritylevel high | Out-Null

Stop-Process -Id $trayProcess.Id -Force -ErrorAction SilentlyContinue
Remove-Item $trayFolder -Recurse -Force -ErrorAction SilentlyContinue
Write-TraySettings '{ "enabled": false }'
