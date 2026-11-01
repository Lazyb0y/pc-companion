# Drop-down Apps: the Home switch turns it on and its page saves its list of apps and each app's
# shortcut. With animation effects on, the shortcut slides a test program's window down to the top
# of the work area, in front, and back up, giving the front back; without them, the window shows and
# hides at once. Another app coming to the front hides it; a window with a dialog open stays; Keep on
# top puts it on top only while shown; a program that stopped responding doesn't hold the host; a
# window shown on another virtual desktop comes to this one; a listed program that isn't running
# starts, as does Edge by its app ID, and an app ID that isn't in Start's Apps folder opens nothing.
# A maximized window drops down unmaximized and comes back maximized after Exit. Minimize to Tray's
# shortcut and Hide others leave a drop-down window alone, and a window in Minimize to Tray's tray
# drops down. Exit brings hidden windows back on the taskbar, and the next start brings back those
# a host that was ended left. A host without admin rights leaves the window of a program with them
# alone, and Settings, in Windows' shared frame, doesn't drop down. The page shows recent results.
[void] (Close-SettingsApp)

# Detailed logging is on for the host's Debug entries.
$detailedUntil = [DateTimeOffset]::Now.AddHours(1).ToString('o')
function Write-DropSettings([string] $Section, [string] $Others = '') {
    Write-Settings "{ ""schemaVersion"": 1, ""general"": { ""theme"": ""Light"" }, ""diagnostics"": { ""detailedLoggingUntil"": ""$detailedUntil"" }, ""features"": { ""dropDownApps"": $Section$Others } }"
    Start-Sleep -Seconds 3
}
function Start-Host {
    if ((Get-Processes 'PCCompanion.Host').Count -eq 0) {
        Start-Process $hostExe
        [void] (Wait-Until { [Win32]::FindWindow('PCCompanion.Host', $null) -ne [IntPtr]::Zero } 30)
    }
    [void] (Wait-Until { [Win32]::FindWindow('PCCompanion.DropDownApps', $null) -ne [IntPtr]::Zero } 20)
    Start-Sleep -Seconds 1
}
function Stop-Host {
    [void] [Win32]::PostMessage([Win32]::FindWindow('PCCompanion.Host', $null), 0x0010, [UIntPtr]::Zero, [IntPtr]::Zero)
    [void] (Wait-Until { (Get-Processes 'PCCompanion.Host').Count -eq 0 } 15)
}

# The test program, built twice: drop-app is the listed app and other-app another app. Each has two
# windows titled after its file. Posted messages make it stop responding for the milliseconds in
# wParam (WM_APP + 3) and open a message box on its first window (+ 5).
$dropFolder = Join-Path $env:TEMP 'PCCompanionDropApp'
New-Item -ItemType Directory -Force -Path $dropFolder | Out-Null
$dropExe = Join-Path $dropFolder 'drop-app.exe'
$otherExe = Join-Path $dropFolder 'other-app.exe'
Add-Type -OutputAssembly $dropExe -OutputType WindowsApplication -ReferencedAssemblies System.Windows.Forms, System.Drawing -TypeDefinition @'
using System;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
public static class DropApp {
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
    public static void Main() {
        // Otherwise Windows puts a window of its own in place of one that doesn't respond for 5 seconds.
        DisableProcessWindowsGhosting();
        SetProcessDPIAware();
        var name = Path.GetFileNameWithoutExtension(Application.ExecutablePath);
        var first = new Window { Text = name + "-1", StartPosition = FormStartPosition.Manual, Bounds = new Rectangle(60, 160, 480, 320) };
        var second = new Window { Text = name + "-2", StartPosition = FormStartPosition.Manual, Bounds = new Rectangle(600, 160, 420, 300) };
        second.Show();
        Application.Run(first);
    }
}
'@
Copy-Item $dropExe $otherExe -Force
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class DropChecks {
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr window, int attribute, out RECT value, int size);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr GetProp(IntPtr window, string name);
    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr window, int index);
    [DllImport("user32.dll")] public static extern bool IsZoomed(IntPtr window);
    [DllImport("user32.dll")] private static extern bool SystemParametersInfo(uint action, uint parameter, IntPtr value, uint flags);

    // The part of the window that shows, without its invisible borders (DWMWA_EXTENDED_FRAME_BOUNDS).
    public static RECT Visible(IntPtr window) {
        RECT bounds;
        DwmGetWindowAttribute(window, 9, out bounds, Marshal.SizeOf(typeof(RECT)));
        return bounds;
    }

    public static bool IsMarked(IntPtr window) { return GetProp(window, "PCCompanion.DropDownApps") != IntPtr.Zero; }

    public static bool IsInTray(IntPtr window) { return GetProp(window, "PCCompanion.MinimizeToTray") != IntPtr.Zero; }

    public static bool IsTopmost(IntPtr window) { return (GetWindowLong(window, -20) & 0x8) != 0; } // GWL_EXSTYLE, WS_EX_TOPMOST

    // Windows' animation effects (SPI_SETCLIENTAREAANIMATION), which the Sandbox has off.
    public static bool SetAnimations(bool on) { return SystemParametersInfo(0x1043, 0, new IntPtr(on ? 1 : 0), 2); }
}
'@
function Find-DropWindow([string] $Name) { [Win32]::FindWindow([NullString]::Value, $Name) }
function Start-DropApp([string] $Exe) {
    $name = [IO.Path]::GetFileNameWithoutExtension($Exe)
    $process = Start-Process $Exe -PassThru
    [void] (Wait-Until { ((Find-DropWindow "$name-1") -ne [IntPtr]::Zero) -and ((Find-DropWindow "$name-2") -ne [IntPtr]::Zero) } 30)
    Start-Sleep -Seconds 1
    return $process
}
function Get-DropState([IntPtr] $Window) {
    $visible = [DropChecks]::Visible($Window)
    "visible $([Win32]::IsWindowVisible($Window)), minimized $([Win32]::IsIconic($Window)), maximized $([DropChecks]::IsZoomed($Window)), marked $([DropChecks]::IsMarked($Window)), topmost $([DropChecks]::IsTopmost($Window)), cloaked $([Win32]::IsCloaked($Window)), front $([Win32]::GetForegroundWindow() -eq $Window), frame $($visible.Left),$($visible.Top),$($visible.Right),$($visible.Bottom)"
}

# Whether the window shows at the top of the work area, at the page's width and height in percent.
function Test-DroppedDown([IntPtr] $Window, [int] $Width = 100, [int] $Height = 50) {
    $work = [System.Windows.Forms.Screen]::PrimaryScreen.WorkingArea
    $visible = [DropChecks]::Visible($Window)
    $wantWidth = [math]::Round($work.Width * $Width / 100)
    $wantHeight = [math]::Round($work.Height * $Height / 100)
    $wantLeft = $work.Left + [math]::Floor(($work.Width - $wantWidth) / 2)
    [Win32]::IsWindowVisible($Window) -and -not [Win32]::IsIconic($Window) -and -not [DropChecks]::IsMarked($Window) -and
        ([math]::Abs($visible.Left - $wantLeft) -le 2) -and ([math]::Abs($visible.Top - $work.Top) -le 2) -and
        ([math]::Abs(($visible.Right - $visible.Left) - $wantWidth) -le 2) -and ([math]::Abs(($visible.Bottom - $visible.Top) - $wantHeight) -le 2)
}
function Test-Hidden([IntPtr] $Window) { (-not [Win32]::IsWindowVisible($Window)) -and [DropChecks]::IsMarked($Window) }

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
$LeftWindows = 0x5B; $LeftControl = 0x1D; $LeftAlt = 0x38; $LeftShift = 0x2A; $KeyAboveTab = 0x29; $Key1 = 0x02; $KeyD = 0x20; $KeyF4 = 0x3E; $KeyH = 0x23; $KeyZ = 0x2C
function Send-Keys([int[]] $Keys, [int] $Wait = 1500) {
    foreach ($key in $Keys) { [void] [Win32]::ScanKey($key, $false, $key -eq $LeftWindows) }
    [array]::Reverse($Keys)
    foreach ($key in $Keys) { [void] [Win32]::ScanKey($key, $true, $key -eq $LeftWindows) }
    Start-Sleep -Milliseconds $Wait
}
function Send-DropShortcut([int] $Wait = 1500) { Send-Keys @($LeftWindows, $LeftControl, $KeyAboveTab) $Wait }
function Test-Logged([string] $Pattern, [int] $Seconds = 5) { Wait-Until { (Get-HostLog) -match $Pattern } $Seconds }
function Get-DropSettings { (Get-Content (Join-Path $dataFolder 'settings.json') -Raw | ConvertFrom-Json).features.dropDownApps }
$dropApp = "{ ""name"": ""drop-app"", ""program"": ""$($dropExe -replace '\\', '\\')"", ""shortcut"": ""WindowsControlAboveTab"" }"

# Turned on from Home, which saves only that it's on, the page shows its settings, and saves the
# list: Running app adds the test program with the first free shortcut, its row's list changes it,
# and Remove takes it out. Program file is the same list as Hot Corners', whose checks cover it.
Write-Settings '{ "schemaVersion": 1, "general": { "theme": "Light" } }'
Start-Sleep -Seconds 2
$dropProcess = Start-DropApp $dropExe
Start-Process $appExe
[void] (Wait-Until { Get-AppWindow })
Start-Sleep -Seconds 3
Test-Check 'turn on Drop-down Apps on Home' (Set-Toggle 'Drop-down Apps' $true)
Start-Sleep -Seconds 2
$saved = Get-DropSettings
Test-Check 'the Home switch saves Drop-down Apps as on' ($saved.enabled -eq $true) "saved: $($saved | ConvertTo-Json -Compress)"
Test-Check 'select Drop-down Apps' (Select-NavigationItem 'Drop-down Apps')
Start-Sleep -Seconds 2
Save-Screenshot '13x-drop-down-apps-english'
$text = Get-WindowText
Test-Check 'Drop-down Apps shows its settings' (($text -contains 'Apps that drop down') -and ($text -contains 'Display') -and ($text -contains 'Width') -and ($text -contains 'Height') -and ($text -contains 'Slide in and out') -and ($text -contains 'Hide when another app comes to the front') -and ($text -contains 'Keep on top while shown') -and ($text -contains 'Recent results') -and ($text -contains 'Permissions & data')) ($text -join ' | ')
Test-Check 'no app is listed yet' ($text -contains 'No apps are listed.') ($text -join ' | ')
Test-Check 'it says which windows can''t drop down' (@($text | Where-Object { $_ -like "Some windows can't drop down*" }).Count -eq 1) ($text -join ' | ')
Test-Check 'Add an app offers Running app' ((Invoke-AppElement 'Add an app') -and (Wait-Until { Find-AppElement 'Running app…' } 5))
[void] (Invoke-AppElement 'Running app…')
$appItem = $null
Test-Check 'Running app lists the test program' (Wait-Until { ($script:appItem = Find-AppListItem 'drop-app') -ne $null } 15)
if ($appItem) { $appItem.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select(); Start-Sleep -Milliseconds 300 }
[void] (Invoke-AppElement 'Add')
Test-Check 'Running app adds it with the first shortcut' (Wait-Until { $apps = @((Get-DropSettings).apps); ($apps.Count -eq 1) -and ($apps[0].program -eq $dropExe) -and ($apps[0].shortcut -eq 'WindowsControlAboveTab') } 5) "saved: $(Get-DropSettings | ConvertTo-Json -Compress)"
Save-Screenshot '13y-drop-down-apps-list'
Test-Check 'choose another shortcut in its row' (Select-ListItem 'Shortcut for drop-app' 'Windows + Ctrl + Alt + 1')
Test-Check 'the page saves the app''s shortcut' (Wait-Until { @((Get-DropSettings).apps)[0].shortcut -eq 'WindowsControlAlt1' } 5) "saved: $(Get-DropSettings | ConvertTo-Json -Compress)"
Test-Check 'turn on Keep on top while shown' (Set-Toggle 'Keep on top while shown' $true)
Test-Check 'the page saves it' (Wait-Until { (Get-DropSettings).keepOnTop -eq $true } 5) "saved: $(Get-DropSettings | ConvertTo-Json -Compress)"
Test-Check 'Remove takes the app out' ((Invoke-AppElement 'Remove drop-app') -and (Wait-Until { @((Get-DropSettings).apps).Count -eq 0 } 5)) "saved: $(Get-DropSettings | ConvertTo-Json -Compress)"
[void] (Close-SettingsApp)

# The shortcut slides the window in, at the top of the work area, in front, and back up, giving the
# front back to the window that had it.
Write-DropSettings "{ ""enabled"": true, ""apps"": [ $dropApp ] }"
Start-Host
$otherProcess = Start-DropApp $otherExe
$drop = Find-DropWindow 'drop-app-1'
$other = Find-DropWindow 'other-app-1'
Select-Window $drop
Select-Window $other
Test-Check 'turn on animation effects, which the Sandbox has off' ([DropChecks]::SetAnimations($true))
Send-DropShortcut
Save-Screenshot '13z-drop-down-apps-shown'
Test-Check 'the shortcut drops the window down at the top of the work area' (Wait-Until { Test-DroppedDown $drop } 5) (Get-DropState $drop)
Test-Check 'it''s in front' ([Win32]::GetForegroundWindow() -eq $drop) "front: $([Win32]::WindowTitle([Win32]::GetForegroundWindow()))"
Test-Check 'the host logs the slide''s frames' (Test-Logged 'slid a window "?in"?: \d+ frames') ([regex]::Match((Get-HostLog), 'slid a window.*').Value)
Test-Check 'the app''s other window stays where it was' ([Win32]::IsWindowVisible((Find-DropWindow 'drop-app-2')) -and -not [DropChecks]::IsMarked((Find-DropWindow 'drop-app-2')))
Send-DropShortcut
Test-Check 'pressed again, it slides up and hides' (Wait-Until { Test-Hidden $drop } 5) (Get-DropState $drop)
Test-Check 'the window that had the front gets it back' ([Win32]::GetForegroundWindow() -eq $other) "front: $([Win32]::WindowTitle([Win32]::GetForegroundWindow()))"
Test-Check 'the host logs both slides' (Test-Logged 'slid a window "?out"?: \d+ frames')

# Without animation effects, the window shows and hides at once.
[void] [DropChecks]::SetAnimations($false)
$slides = Get-MatchCount (Get-HostLog) 'slid a window'
Send-DropShortcut
Test-Check 'without animation effects, the window shows at once' (Wait-Until { Test-DroppedDown $drop } 5) (Get-DropState $drop)
Send-DropShortcut
Test-Check 'and hides at once' ((Wait-Until { Test-Hidden $drop } 5) -and ((Get-MatchCount (Get-HostLog) 'slid a window') -eq $slides)) (Get-DropState $drop)
[void] [DropChecks]::SetAnimations($true)

# Another app coming to the front hides it; a window with a dialog open stays.
Send-DropShortcut
[void] (Wait-Until { Test-DroppedDown $drop } 5)
Select-Window $other
Test-Check 'another app coming to the front hides it' (Wait-Until { Test-Hidden $drop } 5) (Get-DropState $drop)
Send-DropShortcut
[void] (Wait-Until { Test-DroppedDown $drop } 5)
[void] [Win32]::PostMessage($drop, 0x8005, [UIntPtr]::Zero, [IntPtr]::Zero)
[void] (Wait-Until { [Win32]::FindWindow('#32770', 'drop-app-1-Box') -ne [IntPtr]::Zero } 10)
$box = [Win32]::FindWindow('#32770', 'drop-app-1-Box')
Send-DropShortcut
Test-Check 'a window with a dialog open stays' ([Win32]::IsWindowVisible($drop) -and [Win32]::IsWindowVisible($box) -and -not [DropChecks]::IsMarked($drop)) (Get-DropState $drop)
Test-Check 'the host logs that it has a dialog open' (Test-Logged '"?DialogOpen')
[void] [Win32]::PostMessage($box, 0x0010, [UIntPtr]::Zero, [IntPtr]::Zero)
Start-Sleep -Seconds 1
Select-Window $drop
Send-DropShortcut
[void] (Wait-Until { Test-Hidden $drop } 5)

# Keep on top puts it on top only while it's shown.
Write-DropSettings "{ ""enabled"": true, ""keepOnTop"": true, ""apps"": [ $dropApp ] }"
Send-DropShortcut
Test-Check 'with Keep on top, the shown window is on top' ((Wait-Until { Test-DroppedDown $drop } 5) -and [DropChecks]::IsTopmost($drop)) (Get-DropState $drop)
Send-DropShortcut
Test-Check 'and not once it hides' ((Wait-Until { Test-Hidden $drop } 5) -and -not [DropChecks]::IsTopmost($drop)) (Get-DropState $drop)
Write-DropSettings "{ ""enabled"": true, ""apps"": [ $dropApp ] }"

# A program that stopped responding doesn't hold the host: the window goes once it responds.
Send-DropShortcut
[void] (Wait-Until { Test-DroppedDown $drop } 5)
[void] [Win32]::PostMessage($drop, 0x8003, [UIntPtr]::new([uint32] 4000), [IntPtr]::Zero)
Start-Sleep -Milliseconds 300
Test-Check 'the test program stopped responding' (-not [Win32]::Responds($drop, 200))
$hidden = Get-MatchCount (Get-HostLog) 'app 0: "?Hidden'
Send-DropShortcut 100
Test-Check 'the shortcut doesn''t wait for a program that stopped responding' (Wait-Until { (Get-MatchCount (Get-HostLog) 'app 0: "?Hidden') -gt $hidden } 2)
[void] (Wait-Until { [Win32]::Responds($drop, 500) } 20)
Test-Check 'once it responds, the window is hidden' (Wait-Until { Test-Hidden $drop } 5) (Get-DropState $drop)

# A window shown on another virtual desktop comes to this one, without a switch.
Send-DropShortcut
[void] (Wait-Until { Test-DroppedDown $drop } 5)
Write-DropSettings "{ ""enabled"": true, ""hideWhenInactive"": ""Never"", ""apps"": [ $dropApp ] }"
$desktops = Get-VirtualDesktopCount
Send-Keys @($LeftControl, $LeftWindows, $KeyD) 2500
Test-Check 'a second virtual desktop opens' ((Get-VirtualDesktopCount) -gt $desktops -and [Win32]::IsCloaked($drop)) "desktops: $(Get-VirtualDesktopCount); $(Get-DropState $drop)"
Send-DropShortcut 2500
Test-Check 'the shortcut brings the window to this desktop, in front' ((Wait-Until { (Test-DroppedDown $drop) -and -not [Win32]::IsCloaked($drop) } 5) -and ([Win32]::GetForegroundWindow() -eq $drop)) (Get-DropState $drop)
Test-Check 'the host logs it' (Test-Logged '"?BroughtFromOtherDesktop')
Save-Screenshot '13za-drop-down-apps-other-desktop'
Send-Keys @($LeftControl, $LeftWindows, $KeyF4) 2500
Send-DropShortcut
[void] (Wait-Until { Test-Hidden $drop } 5)
Write-DropSettings "{ ""enabled"": true, ""apps"": [ $dropApp ] }"

# Minimize to Tray's shortcut and Hide others leave a drop-down window alone.
Write-DropSettings "{ ""enabled"": true, ""hideWhenInactive"": ""Never"", ""apps"": [ $dropApp ] }" ', "minimizeToTray": { "enabled": true }, "hideApps": { "enabled": true }'
Send-DropShortcut
[void] (Wait-Until { Test-DroppedDown $drop } 5)
Send-Keys @($LeftWindows, $LeftShift, $KeyZ)
Test-Check 'Minimize to Tray''s shortcut leaves a drop-down window' ((Test-DroppedDown $drop) -and -not [DropChecks]::IsInTray($drop)) (Get-DropState $drop)
Test-Check 'Minimize to Tray logs why' (Test-Logged '"?Shortcut"?, "?DropDownWindow')
Select-Window $other
Send-Keys @($LeftWindows, $LeftControl, $LeftShift, $KeyH)
Test-Check 'Hide others leaves a shown drop-down window' ((Test-DroppedDown $drop) -and -not [Win32]::IsIconic($drop)) (Get-DropState $drop)
Send-Keys @($LeftWindows, $LeftControl, $LeftShift, $KeyH)
Select-Window $drop
Send-DropShortcut
[void] (Wait-Until { Test-Hidden $drop } 5)

# A window in Minimize to Tray's tray drops down, and leaves the tray.
Stop-Host
foreach ($window in $drop, (Find-DropWindow 'drop-app-2')) { if (-not [Win32]::IsWindowVisible($window) -or [Win32]::IsIconic($window)) { [void] [Win32]::ShowWindow($window, 9) } }
Start-Host
[void] [Win32]::PostMessage((Find-DropWindow 'drop-app-2'), 0x0010, [UIntPtr]::Zero, [IntPtr]::Zero)
Select-Window $drop
Send-Keys @($LeftWindows, $LeftShift, $KeyZ)
Test-Check 'the test program''s window goes to Minimize to Tray''s tray' (Wait-Until { (-not [Win32]::IsWindowVisible($drop)) -and [DropChecks]::IsInTray($drop) } 5) (Get-DropState $drop)
Send-DropShortcut
Test-Check 'its shortcut drops it down from the tray' (Wait-Until { Test-DroppedDown $drop } 5) (Get-DropState $drop)
Test-Check 'Minimize to Tray lets it go' (Wait-Until { -not [DropChecks]::IsInTray($drop) } 5)
Write-DropSettings "{ ""enabled"": true, ""apps"": [ $dropApp ] }"

# PC Companion exiting brings a hidden window back on the taskbar, minimized; ended, it brings it back at its next start.
Send-DropShortcut
[void] (Wait-Until { Test-Hidden $drop } 5)
Stop-Host
Test-Check 'when PC Companion exits, a hidden window comes back on the taskbar, minimized' ([Win32]::IsWindowVisible($drop) -and [Win32]::IsIconic($drop) -and -not [DropChecks]::IsMarked($drop)) (Get-DropState $drop)
[void] [Win32]::ShowWindow($drop, 9)
Start-Host
Select-Window $other
Send-DropShortcut
[void] (Wait-Until { Test-DroppedDown $drop } 5)
Send-DropShortcut
[void] (Wait-Until { Test-Hidden $drop } 5)
Get-Processes 'PCCompanion.Host' | Stop-Process -Force
Start-Sleep -Seconds 2
Test-Check 'a host that''s ended leaves the window hidden and marked' (Test-Hidden $drop) (Get-DropState $drop)
Start-Host
Test-Check 'at the next start, the window comes back on the taskbar, minimized' (Wait-Until { [Win32]::IsWindowVisible($drop) -and [Win32]::IsIconic($drop) -and -not [DropChecks]::IsMarked($drop) } 10) (Get-DropState $drop)
Test-Check 'the host logs it' (Test-Logged 'brought back 1 windows left hidden')

# A maximized window drops down unmaximized, and after Exit comes back to be maximized again.
Stop-Process -Id $dropProcess.Id -Force -ErrorAction SilentlyContinue
$dropProcess = Start-DropApp $dropExe
$drop = Find-DropWindow 'drop-app-1'
Select-Window $drop
[void] [Win32]::ShowWindow($drop, 3) # SW_MAXIMIZE
Start-Sleep -Seconds 1
Select-Window $other
Send-DropShortcut
Test-Check 'a maximized window drops down at the top of the work area' (Wait-Until { (Test-DroppedDown $drop) -and -not [DropChecks]::IsZoomed($drop) } 5) (Get-DropState $drop)
Send-DropShortcut
[void] (Wait-Until { Test-Hidden $drop } 5)
Stop-Host
[void] [Win32]::ShowWindow($drop, 9) # SW_RESTORE
Start-Sleep -Seconds 1
Test-Check 'after Exit, it comes back maximized' ([DropChecks]::IsZoomed($drop)) (Get-DropState $drop)
[void] [Win32]::ShowWindow($drop, 9)

# A listed program that isn't running starts, and its window drops down; so does Edge, by its app
# ID. An app ID that isn't in Start's Apps folder opens nothing.
Start-Host
Stop-Process -Id $dropProcess.Id -Force -ErrorAction SilentlyContinue
[void] (Wait-Until { (Find-DropWindow 'drop-app-1') -eq [IntPtr]::Zero } 10)
Select-Window $other
Send-DropShortcut
Test-Check 'the shortcut starts a listed program that isn''t running' (Wait-Until { (Find-DropWindow 'drop-app-1') -ne [IntPtr]::Zero -or (Find-DropWindow 'drop-app-2') -ne [IntPtr]::Zero } 20)
Test-Check 'and drops down its first window' (Wait-Until { @((Find-DropWindow 'drop-app-1'), (Find-DropWindow 'drop-app-2')) | Where-Object { $_ -ne [IntPtr]::Zero -and (Test-DroppedDown $_) } } 15) "first: $(Get-DropState (Find-DropWindow 'drop-app-1')); second: $(Get-DropState (Find-DropWindow 'drop-app-2'))"
Test-Check 'the host logs that it started' (Test-Logged 'app 0: "?Started')
$dropProcess = Get-Process 'drop-app' -ErrorAction SilentlyContinue | Select-Object -First 1
Write-DropSettings "{ ""enabled"": true, ""apps"": [ { ""name"": ""Edge"", ""appId"": ""MSEdge"", ""shortcut"": ""WindowsAltAboveTab"" }, { ""name"": ""Nothing"", ""appId"": ""PCCompanion.NoSuchApp"", ""shortcut"": ""WindowsControlAlt1"" } ] }"
Select-Window $other
Send-Keys @($LeftWindows, $LeftAlt, $KeyAboveTab)
$script:edge = [IntPtr]::Zero
Test-Check 'Edge starts by its app ID and drops down' (Wait-Until { $script:edge = [Win32]::TopLevelWindows() | Where-Object { [Win32]::WindowClass($_) -eq 'Chrome_WidgetWin_1' -and [Win32]::IsWindowVisible($_) -and (Test-DroppedDown $_) } | Select-Object -First 1; $null -ne $script:edge } 30) (@([regex]::Matches((Get-HostLog), 'app 0: [^\r\n]*')) | Select-Object -Last 1).Value
Save-Screenshot '13zb-drop-down-apps-edge'
Get-Process msedge -ErrorAction SilentlyContinue | Stop-Process -Force
$explorers = @([Win32]::TopLevelWindows() | Where-Object { [Win32]::WindowClass($_) -eq 'CabinetWClass' }).Count
Send-Keys @($LeftWindows, $LeftControl, $LeftAlt, $Key1) 4000
Test-Check 'an app ID that isn''t in the Apps folder opens nothing' (@([Win32]::TopLevelWindows() | Where-Object { [Win32]::WindowClass($_) -eq 'CabinetWClass' }).Count -eq $explorers)
Test-Check 'the host logs why' (Test-Logged 'app 1: "?AppIdNotFound')

# The page shows the recent results, newest first.
Start-Process $appExe
[void] (Wait-Until { Get-AppWindow })
Start-Sleep -Seconds 2
[void] (Select-NavigationItem 'Drop-down Apps')
Start-Sleep -Seconds 3
$text = Get-WindowText
Test-Check 'the page shows the recent results' ((@($text | Where-Object { $_ -like "Nothing: the app isn't in Start's list of apps" }).Count -eq 1) -and (@($text | Where-Object { $_ -like 'Edge: started' }).Count -eq 1)) ($text -join ' | ')
Save-Screenshot '13zc-drop-down-apps-results'
[void] (Close-SettingsApp)

# Everything in Windows Sandbox runs as administrator; a host whose file is labeled Medium integrity
# runs without admin rights, and Windows keeps the test program's window, which has them, from it.
# Settings, in Windows' shared frame, doesn't drop down either.
Write-DropSettings "{ ""enabled"": true, ""apps"": [ $dropApp, { ""name"": ""Settings"", ""appId"": ""windows.immersivecontrolpanel_cw5n1h2txyewy!microsoft.windows.immersivecontrolpanel"", ""shortcut"": ""WindowsAltAboveTab"" } ] }"
Stop-Host
& icacls.exe $hostExe /setintegritylevel medium | Out-Null
Start-Host
$drop = @((Find-DropWindow 'drop-app-1'), (Find-DropWindow 'drop-app-2')) | Where-Object { $_ -ne [IntPtr]::Zero } | Select-Object -First 1
[void] [Win32]::ShowWindow($drop, 9)
Select-Window $other
Send-DropShortcut
Test-Check 'a host without admin rights leaves the window of a program with them alone' ([Win32]::IsWindowVisible($drop) -and -not [DropChecks]::IsMarked($drop) -and -not (Test-DroppedDown $drop)) (Get-DropState $drop)
Test-Check 'the host logs why' (Test-Logged 'app 0: "?RunsAsAdministrator')
Start-Process 'ms-settings:display'
$script:settingsWindow = [IntPtr]::Zero
[void] (Wait-Until { $script:settingsWindow = [Win32]::TopLevelWindows() | Where-Object { [Win32]::WindowClass($_) -eq 'ApplicationFrameWindow' -and [Win32]::WindowTitle($_) -eq 'Settings' } | Select-Object -First 1; $null -ne $script:settingsWindow } 30)
Start-Sleep -Seconds 3
if ($script:settingsWindow) {
    Send-Keys @($LeftWindows, $LeftAlt, $KeyAboveTab)
    Test-Check 'Settings, a Store app in Windows'' shared frame, doesn''t drop down' ([Win32]::IsWindowVisible($script:settingsWindow) -and -not [DropChecks]::IsMarked($script:settingsWindow))
    Test-Check 'the host logs why' (Test-Logged 'app 1: "?(SharedAppFrame|RunsAsAdministrator)')
    [void] [Win32]::PostMessage($script:settingsWindow, 0x0010, [UIntPtr]::Zero, [IntPtr]::Zero)
} else {
    Test-Check 'Settings opens' $false
}
Stop-Host
& icacls.exe $hostExe /setintegritylevel high | Out-Null

[void] [DropChecks]::SetAnimations($false)
Get-Process 'drop-app', 'other-app' -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Seconds 1
Remove-Item $dropFolder -Recurse -Force -ErrorAction SilentlyContinue
Write-DropSettings '{ "enabled": false }'
