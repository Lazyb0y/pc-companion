# Title Bar Actions: the Home switch turns it on and its page saves each choice and its list of
# apps. With a test program that draws its title bar, with a tab, as a browser does: a middle-click
# on the title bar minimizes a window without activating it, and the window gets no click, while one
# on the tab reaches it; the pointer hook is set only while the pointer rests on a title bar. Edge's
# empty tab strip, File Explorer's tab row and Settings' title bar beside its buttons minimize their
# windows the same way. Ctrl + middle-click sends the window in front to the back, and a middle-click
# minimizes the window in front too. The wheel changes a window's opacity in 10%
# steps, without the window getting the wheel; turning the feature off and Exit make it opaque, and a
# host that was ended leaves it translucent until the next start. A window that stopped responding,
# a listed app and, for a host without admin rights, a program with them get no action, and the host
# logs why. With Minimize to Tray and Hide Apps on, their actions send the window to the tray and hide
# its app; with them off, the host logs why. The page in Arabic with the dark theme, with a
# screenshot.
[void] (Close-SettingsApp)

# Detailed logging is on for the host's Debug entries.
$detailedUntil = [DateTimeOffset]::Now.AddHours(1).ToString('o')
function Write-TitleBarSettings([string] $Section, [string] $Others = '') {
    Write-Settings "{ ""schemaVersion"": 1, ""general"": { ""theme"": ""Light"" }, ""diagnostics"": { ""detailedLoggingUntil"": ""$detailedUntil"" }, ""features"": { ""titleBarActions"": $Section$Others } }"
    Start-Sleep -Seconds 3
}
function Start-Host {
    if ((Get-Processes 'PCCompanion.Host').Count -eq 0) {
        Start-Process $hostExe
        [void] (Wait-Until { [Win32]::FindWindow('PCCompanion.Host', $null) -ne [IntPtr]::Zero } 30)
    }
    [void] (Wait-Until { [Win32]::FindWindow('PCCompanion.TitleBarActions', $null) -ne [IntPtr]::Zero } 20)
    Start-Sleep -Seconds 1
}
function Stop-Host {
    [void] [Win32]::PostMessage([Win32]::FindWindow('PCCompanion.Host', $null), 0x0010, [UIntPtr]::Zero, [IntPtr]::Zero)
    [void] (Wait-Until { (Get-Processes 'PCCompanion.Host').Count -eq 0 } 15)
}

# The test program, built once and copied: tba-app is the one clicked, other-app another app, and
# medium-app a copy that runs without admin rights. Each has two windows titled after its file, at
# the top of the screen or at the height its argument gives. A window draws its own title bar, as a
# browser does: its top 32 pixels answer HTCAPTION, but for a tab between 40 and 190 pixels from its
# left edge, which answers HTCLIENT and gets its clicks. It logs, to the program's .log file, when
# it's activated and when it gets a middle-click or the wheel. A posted message makes it stop
# responding for the milliseconds in wParam (WM_APP + 3).
$appFolder = Join-Path $env:TEMP 'PCCompanionTitleBarApp'
New-Item -ItemType Directory -Force -Path $appFolder | Out-Null
$tbaExe = Join-Path $appFolder 'tba-app.exe'
$otherExe = Join-Path $appFolder 'other-app.exe'
$mediumExe = Join-Path $appFolder 'medium-app.exe'
$tbaLog = Join-Path $appFolder 'tba-app.log'
Add-Type -OutputAssembly $tbaExe -OutputType WindowsApplication -ReferencedAssemblies System.Windows.Forms, System.Drawing -TypeDefinition @'
using System;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
public static class TitleBarApp {
    [DllImport("user32.dll")] private static extern void DisableProcessWindowsGhosting();
    [DllImport("user32.dll")] private static extern bool SetProcessDPIAware();
    private static string s_log;

    private sealed class Window : Form {
        protected override void WndProc(ref Message message) {
            switch (message.Msg) {
                case 0x8003: Thread.Sleep((int)message.WParam); return;
                case 0x0006: if (((long)message.WParam & 0xFFFF) != 0) Log("activated"); break; // WM_ACTIVATE
                case 0x00A7: Log("WM_NCMBUTTONDOWN"); break;
                case 0x0207: Log("WM_MBUTTONDOWN"); break;
                case 0x020A: Log("WM_MOUSEWHEEL"); break;
                case 0x0083: // WM_NCCALCSIZE: the whole window is client area.
                    if (message.WParam != IntPtr.Zero) { message.Result = IntPtr.Zero; return; }
                    break;
                case 0x0084: // WM_NCHITTEST: HTCAPTION in the title bar, HTCLIENT on the tab and below.
                    var point = PointToClient(new Point((short)((long)message.LParam & 0xFFFF), (short)(((long)message.LParam >> 16) & 0xFFFF)));
                    message.Result = (IntPtr)(point.Y < 32 && (point.X < 40 || point.X >= 190) ? 2 : 1);
                    return;
            }
            base.WndProc(ref message);
        }

        protected override void OnPaint(PaintEventArgs e) {
            base.OnPaint(e);
            e.Graphics.FillRectangle(Brushes.Gainsboro, 0, 0, ClientSize.Width, 32);
            e.Graphics.FillRectangle(Brushes.White, 40, 4, 150, 28);
            e.Graphics.DrawString("Tab", Font, Brushes.Black, 48, 10);
            e.Graphics.DrawString(Text, Font, Brushes.Black, 200, 10);
        }

        private void Log(string what) { File.AppendAllText(s_log, Text + " " + what + Environment.NewLine); }
    }

    [STAThread]
    public static void Main(string[] arguments) {
        // Otherwise Windows puts a window of its own in place of one that doesn't respond for 5 seconds.
        DisableProcessWindowsGhosting();
        SetProcessDPIAware();
        var name = Path.GetFileNameWithoutExtension(Application.ExecutablePath);
        s_log = Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), name + ".log");
        var top = arguments.Length > 0 ? int.Parse(arguments[0]) : 40;
        var first = new Window { Text = name + "-1", StartPosition = FormStartPosition.Manual, Bounds = new Rectangle(40, top, 600, 300) };
        var second = new Window { Text = name + "-2", StartPosition = FormStartPosition.Manual, Bounds = new Rectangle(680, top, 560, 300) };
        second.Show();
        Application.Run(first);
    }
}
'@
Copy-Item $tbaExe $otherExe -Force
Copy-Item $tbaExe $mediumExe -Force
& icacls.exe $mediumExe /setintegritylevel medium | Out-Null
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class TitleBarChecks {
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr window, int attribute, out RECT value, int size);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetProp(IntPtr window, string name);
    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr window, int index);
    [DllImport("user32.dll")] private static extern bool GetLayeredWindowAttributes(IntPtr window, out uint key, out byte alpha, out uint flags);
    [DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint count, INPUT[] inputs, int size);
    [StructLayout(LayoutKind.Sequential)] private struct MOUSEINPUT { public int dx, dy; public uint mouseData, dwFlags, time; public IntPtr extra; }
    [StructLayout(LayoutKind.Explicit)] private struct INPUT { [FieldOffset(0)] public uint type; [FieldOffset(8)] public MOUSEINPUT mi; }

    // A mouse button or the wheel, as a mouse sends it: MOUSEEVENTF_MIDDLEDOWN, MIDDLEUP or WHEEL.
    public static bool Mouse(uint flags, int data) {
        var input = new INPUT { type = 0 };
        input.mi.dwFlags = flags;
        input.mi.mouseData = unchecked((uint)data);
        return SendInput(1, new[] { input }, Marshal.SizeOf(typeof(INPUT))) == 1;
    }

    // The part of the window that shows, without its invisible borders (DWMWA_EXTENDED_FRAME_BOUNDS).
    public static RECT Visible(IntPtr window) {
        RECT bounds;
        DwmGetWindowAttribute(window, 9, out bounds, Marshal.SizeOf(typeof(RECT)));
        return bounds;
    }

    public static bool IsLayered(IntPtr window) { return (GetWindowLong(window, -20) & 0x80000) != 0; } // GWL_EXSTYLE, WS_EX_LAYERED

    // The window's opacity in percent, as Title Bar Actions rounds it; 100 without the layered style.
    public static int Opacity(IntPtr window) {
        uint key, flags;
        byte alpha;
        if (!IsLayered(window) || !GetLayeredWindowAttributes(window, out key, out alpha, out flags) || (flags & 2) == 0) return 100;
        return (int)Math.Round(alpha / 2.55);
    }

    public static bool IsMarked(IntPtr window) { return GetProp(window, "PCCompanion.TitleBarActions.Opacity") != IntPtr.Zero; }

    public static bool IsInTray(IntPtr window) { return GetProp(window, "PCCompanion.MinimizeToTray") != IntPtr.Zero; }

    [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X, Y; }
    [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(POINT point);
    [DllImport("user32.dll")] private static extern IntPtr SendMessageTimeout(IntPtr window, uint message, UIntPtr wParam, IntPtr lParam, uint flags, uint timeout, out UIntPtr result);

    public static IntPtr WindowAt(int x, int y) { return WindowFromPoint(new POINT { X = x, Y = y }); }

    // What the window answers WM_NCHITTEST at the point, or -1 when it doesn't within half a second.
    public static long HitTest(IntPtr window, int x, int y) {
        UIntPtr result;
        var lParam = (IntPtr)(((y & 0xFFFF) << 16) | (x & 0xFFFF));
        return SendMessageTimeout(window, 0x0084, UIntPtr.Zero, lParam, 2, 500, out result) == IntPtr.Zero ? -1 : (long)result.ToUInt64();
    }
}
'@
function Find-AppWindow([string] $Name) { [Win32]::FindWindow([NullString]::Value, $Name) }
function Start-TestApp([string] $Exe, [int] $Top = 40) {
    $name = [IO.Path]::GetFileNameWithoutExtension($Exe)
    $process = Start-Process $Exe -ArgumentList "$Top" -PassThru
    [void] (Wait-Until { ((Find-AppWindow "$name-1") -ne [IntPtr]::Zero) -and ((Find-AppWindow "$name-2") -ne [IntPtr]::Zero) } 30)
    Start-Sleep -Seconds 1
    return $process
}
# What's at the pointer, for the report: the window there and what it answers WM_NCHITTEST.
function Get-PointDiagnosis {
    $point = [System.Windows.Forms.Cursor]::Position
    $window = [TitleBarChecks]::WindowAt($point.X, $point.Y)
    "$(Get-PointerText): $([Win32]::WindowClass($window)) ""$([Win32]::WindowTitle($window))"", hit test $([TitleBarChecks]::HitTest($window, $point.X, $point.Y))"
}
function Get-WindowState([IntPtr] $Window) {
    "minimized $([Win32]::IsIconic($Window)), visible $([Win32]::IsWindowVisible($Window)), opacity $([TitleBarChecks]::Opacity($Window)), layered $([TitleBarChecks]::IsLayered($Window)), marked $([TitleBarChecks]::IsMarked($Window)), front: $([Win32]::WindowTitle([Win32]::GetForegroundWindow()))"
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
function Restore-Window([IntPtr] $Window) {
    if ([Win32]::IsIconic($Window) -or -not [Win32]::IsWindowVisible($Window)) { [void] [Win32]::ShowWindow($Window, 4) } # SW_SHOWNOACTIVATE
    [void] (Wait-Until { -not [Win32]::IsIconic($Window) } 5)
    Start-Sleep -Milliseconds 300
}

# Rests the pointer on a window's title bar, $X pixels from the left of the part that shows, so the
# feature asks the window and claims the point; the move waits a second there.
function Move-ToTitleBar([IntPtr] $Window, [int] $X = 400) {
    $visible = [TitleBarChecks]::Visible($Window)
    return (Move-Pointer ($visible.Left + $X) ($visible.Top + 15))
}
$VK_SHIFT = 0x10; $VK_CONTROL = 0x11
function Send-MiddleClick([uint16[]] $Keys = @(), [int] $Wait = 1500) {
    foreach ($key in $Keys) { [void] [Win32]::Key($key, $false) }
    Start-Sleep -Milliseconds 100
    [void] [TitleBarChecks]::Mouse(0x0020, 0)
    Start-Sleep -Milliseconds 50
    [void] [TitleBarChecks]::Mouse(0x0040, 0)
    Start-Sleep -Milliseconds 300
    for ($at = $Keys.Count - 1; $at -ge 0; $at--) { [void] [Win32]::Key($Keys[$at], $true) }
    Start-Sleep -Milliseconds $Wait
}
function Send-Wheel([int] $Notches, [int] $Wait = 400) {
    for ($notch = 1; $notch -le [math]::Abs($Notches); $notch++) {
        [void] [TitleBarChecks]::Mouse(0x0800, [math]::Sign($Notches) * 120)
        Start-Sleep -Milliseconds $Wait
    }
}
function Get-AppLog { if (Test-Path $tbaLog) { @(Get-Content $tbaLog) } else { @() } }
function Get-AppLogSince([int] $Lines) { @(Get-AppLog | Select-Object -Skip $Lines) }
function Test-Logged([string] $Pattern, [int] $Seconds = 5) { Wait-Until { (Get-HostLog) -match $Pattern } $Seconds }
function Get-TitleBarSettings { (Get-Content (Join-Path $dataFolder 'settings.json') -Raw | ConvertFrom-Json).features.titleBarActions }

# Turned on from Home, which saves only that it's on, the page shows its settings and saves each
# choice, notes that Minimize to tray needs Minimize to Tray turned on, and saves the list: Running
# app adds the test program and Remove takes it out. Program file is the same list as Hot Corners',
# whose checks cover it.
Write-Settings '{ "schemaVersion": 1, "general": { "theme": "Light" } }'
Start-Sleep -Seconds 2
$tbaProcess = Start-TestApp $tbaExe
Start-Process $appExe
[void] (Wait-Until { Get-AppWindow })
Start-Sleep -Seconds 3
Test-Check 'turn on Title Bar Actions on Home' (Set-Toggle 'Title Bar Actions' $true)
Start-Sleep -Seconds 2
$saved = Get-TitleBarSettings
Test-Check 'the Home switch saves Title Bar Actions as on' ($saved.enabled -eq $true) "saved: $($saved | ConvertTo-Json -Compress)"
Test-Check 'select Title Bar Actions' (Select-NavigationItem 'Title Bar Actions')
Start-Sleep -Seconds 2
Save-Screenshot '14a-title-bar-actions-english'
$text = Get-WindowText
Test-Check 'Title Bar Actions shows its settings' (($text -contains 'Middle-click') -and ($text -contains 'Shift + middle-click') -and ($text -contains 'Ctrl + middle-click') -and ($text -contains 'Wheel') -and ($text -contains 'Ctrl + wheel') -and ($text -contains 'Apps that keep their title-bar clicks') -and ($text -contains 'Permissions & data')) ($text -join ' | ')
# A list read right after another one closed can find nothing, so each read waits a moment first.
$selected = @('Middle-click', 'Shift + middle-click', 'Ctrl + middle-click', 'Wheel', 'Ctrl + wheel' | ForEach-Object { Start-Sleep -Milliseconds 700; Get-ListSelection $_ }) -join ', '
Test-Check 'middle-click minimizes and the rest do nothing' ($selected -eq 'Minimize, None, None, None, None') "chosen: $selected"
Test-Check 'no app is listed yet' ($text -contains 'No apps are listed.') ($text -join ' | ')
Test-Check 'it says which windows get no actions' (@($text | Where-Object { $_ -like 'Tabs, buttons and search boxes in a title bar*' }).Count -eq 1) ($text -join ' | ')
Test-Check 'it names the keyboard ways' (@($text | Where-Object { $_ -like 'With the keyboard*' }).Count -eq 1) ($text -join ' | ')
Test-Check 'no note about the other features yet' (@($text | Where-Object { $_ -like 'Minimize to tray works only*' }).Count -eq 0) ($text -join ' | ')
Test-Check 'choose Send to back for middle-click' (Select-ListItem 'Middle-click' 'Send to back')
Test-Check 'choose Minimize to tray for Shift + middle-click' (Select-ListItem 'Shift + middle-click' 'Minimize to tray')
Test-Check 'choose Hide app for Ctrl + middle-click' (Select-ListItem 'Ctrl + middle-click' 'Hide app')
Test-Check 'choose Change opacity for the wheel' (Select-ListItem 'Wheel' 'Change opacity')
Test-Check 'choose Change opacity for Ctrl + wheel' (Select-ListItem 'Ctrl + wheel' 'Change opacity')
Test-Check 'the page saves each choice' (Wait-Until { $s = Get-TitleBarSettings; ($s.middleClick -eq 'SendToBack') -and ($s.shiftMiddleClick -eq 'MinimizeToTray') -and ($s.controlMiddleClick -eq 'HideApp') -and ($s.wheel -eq 'Opacity') -and ($s.controlWheel -eq 'Opacity') } 5) "saved: $(Get-TitleBarSettings | ConvertTo-Json -Compress)"
$text = Get-WindowText
Test-Check 'it notes that Minimize to tray and Hide app need their features' (@($text | Where-Object { $_ -like 'Minimize to tray works only*' }).Count -eq 1) ($text -join ' | ')
Test-Check 'Add an app offers Running app' ((Invoke-AppElement 'Add an app') -and (Wait-Until { Find-AppElement 'Running app…' } 5))
[void] (Invoke-AppElement 'Running app…')
$appItem = $null
Test-Check 'Running app lists the test program' (Wait-Until { ($script:appItem = Find-AppListItem 'tba-app') -ne $null } 15)
if ($appItem) { $appItem.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select(); Start-Sleep -Milliseconds 300 }
[void] (Invoke-AppElement 'Add')
Test-Check 'Running app adds it' (Wait-Until { $apps = @((Get-TitleBarSettings).excludedApps); ($apps.Count -eq 1) -and ($apps[0].program -eq $tbaExe) } 5) "saved: $(Get-TitleBarSettings | ConvertTo-Json -Compress)"
Save-Screenshot '14b-title-bar-actions-list'
Test-Check 'Remove takes it out' ((Invoke-AppElement 'Remove tba-app') -and (Wait-Until { @((Get-TitleBarSettings).excludedApps).Count -eq 0 } 5)) "saved: $(Get-TitleBarSettings | ConvertTo-Json -Compress)"
[void] (Close-SettingsApp)

# A middle-click on the title bar minimizes the window without activating it, and the window gets
# no click; the pointer hook is set only while the pointer rests there. One on the tab reaches the
# window.
Write-TitleBarSettings '{ "enabled": true }'
Start-Host
$otherProcess = Start-TestApp $otherExe 420
$tba = Find-AppWindow 'tba-app-1'
$tba2 = Find-AppWindow 'tba-app-2'
$other = Find-AppWindow 'other-app-1'
Select-Window $tba2
Select-Window $other
$claims = Get-MatchCount (Get-HostLog) 'claimed the title bar under the pointer'
$releases = Get-MatchCount (Get-HostLog) 'removed its claim'
Test-Check 'the pointer rests on the test window''s title bar' (Move-ToTitleBar $tba) (Get-PointerText)
$claimed = Wait-Until { (Get-MatchCount (Get-HostLog) 'claimed the title bar under the pointer') -gt $claims } 5
Test-Check 'the host claims the title bar, so the pointer hook is set' $claimed (Get-PointDiagnosis)
if (-not $claimed) {
    [void] [Win32]::PushPointer(1, 0)
    Start-Sleep -Milliseconds 300
    [void] [Win32]::PushPointer(-1, 0)
    Write-Report "[NOTE] after a nudge of the pointer, the host claimed the title bar: $(Wait-Until { (Get-MatchCount (Get-HostLog) 'claimed the title bar under the pointer') -gt $claims } 3); $(Get-PointDiagnosis)"
}
$lines = (Get-AppLog).Count
Send-MiddleClick
Test-Check 'a middle-click on the title bar minimizes the window' (Wait-Until { [Win32]::IsIconic($tba) } 5) (Get-WindowState $tba)
Test-Check 'without activating it' ([Win32]::GetForegroundWindow() -ne $tba) (Get-WindowState $tba)
$since = Get-AppLogSince $lines
Test-Check 'the window gets no click and isn''t activated' (@($since | Where-Object { $_ -like 'tba-app-1 *' }).Count -eq 0) ($since -join ' | ')
Test-Check 'the host logs that the hook kept it' (Test-Logged 'keys "?None"?, "?Minimize"? \("?Kept')
[void] (Move-Pointer 700 600)
Test-Check 'leaving the title bar removes the claim, so the pointer hook is removed' (Wait-Until { (Get-MatchCount (Get-HostLog) 'removed its claim') -gt $releases } 5)
Restore-Window $tba
Select-Window $other
[void] (Move-ToTitleBar $tba 100)
$lines = (Get-AppLog).Count
Send-MiddleClick
Test-Check 'a middle-click on its tab doesn''t minimize it' (-not [Win32]::IsIconic($tba)) (Get-WindowState $tba)
Test-Check 'and the window gets the click' (Wait-Until { @(Get-AppLogSince $lines | Where-Object { $_ -eq 'tba-app-1 WM_MBUTTONDOWN' }).Count -eq 1 } 5) "$((Get-AppLogSince $lines) -join ' | '); $(Get-PointDiagnosis)"
Save-Screenshot '14c-title-bar-actions-tab'

# Edge's empty tab strip, File Explorer's tab row and Settings' title bar answer through child
# windows; a middle-click there minimizes the window, which isn't activated.
$edgeExe = 'C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe'
Start-Process $edgeExe -ArgumentList '--no-first-run', '--no-default-browser-check', "--user-data-dir=$appFolder\edge", 'about:blank'
$script:edge = [IntPtr]::Zero
[void] (Wait-Until { $script:edge = [Win32]::TopLevelWindows() | Where-Object { [Win32]::WindowClass($_) -eq 'Chrome_WidgetWin_1' -and [Win32]::WindowTitle($_) -like '*Edge*' } | Select-Object -First 1; $null -ne $script:edge } 40)
Start-Process explorer.exe 'C:\'
$script:explorer = [IntPtr]::Zero
[void] (Wait-Until { $script:explorer = [Win32]::TopLevelWindows() | Where-Object { [Win32]::WindowClass($_) -eq 'CabinetWClass' } | Select-Object -First 1; $null -ne $script:explorer } 30)
Start-Process 'ms-settings:about'
$script:settingsWindow = [IntPtr]::Zero
[void] (Wait-Until { $script:settingsWindow = [Win32]::TopLevelWindows() | Where-Object { [Win32]::WindowClass($_) -eq 'ApplicationFrameWindow' -and [Win32]::WindowTitle($_) -eq 'Settings' } | Select-Object -First 1; $null -ne $script:settingsWindow } 30)
Start-Sleep -Seconds 3
# The points are on the empty tab strip and tab row, and in Settings beside its buttons: its own
# content, with its name and a search box, fills the rest of its title bar and answers HTCLIENT.
foreach ($app in @(@{ Name = 'Edge'; Window = $script:edge; X = 520; Shot = '14d-title-bar-actions-edge' }, @{ Name = 'File Explorer'; Window = $script:explorer; X = 520; Shot = '14e-title-bar-actions-explorer' }, @{ Name = 'Settings'; Window = $script:settingsWindow; X = 720; Shot = '14f-title-bar-actions-settings' })) {
    $window = $app.Window
    if (-not $window) {
        Test-Check "$($app.Name) opens" $false
        continue
    }
    [void] [Win32]::ShowWindow($window, 9) # SW_RESTORE
    Start-Sleep -Milliseconds 500
    [void] [Win32]::MoveWindow($window, 300, 120, 900, 280, $true)
    Select-Window $window
    Select-Window $other
    Start-Sleep -Milliseconds 500
    Save-Screenshot $app.Shot
    [void] (Move-ToTitleBar $window $app.X)
    $diagnosis = Get-PointDiagnosis
    Send-MiddleClick
    Test-Check "a middle-click on $($app.Name)'s title bar minimizes it" (Wait-Until { [Win32]::IsIconic($window) } 5) "$(Get-WindowState $window); $diagnosis; $((@([regex]::Matches((Get-HostLog), 'Title Bar Actions: [^\r\n]*')) | Select-Object -Last 1).Value)"
    Test-Check "$($app.Name) isn't activated" ([Win32]::GetForegroundWindow() -eq $other) (Get-WindowState $window)
    [void] [Win32]::PostMessage($window, 0x0010, [UIntPtr]::Zero, [IntPtr]::Zero)
}
Get-Processes 'msedge' | ForEach-Object { Stop-Process -Id $_.Id -Force -ErrorAction SilentlyContinue }

# Ctrl + middle-click sends the window in front to the back, and the next window gets the front.
Write-TitleBarSettings '{ "enabled": true, "controlMiddleClick": "SendToBack" }'
Select-Window $tba2
Select-Window $tba
[void] (Move-ToTitleBar $tba)
Send-MiddleClick @($VK_CONTROL)
Test-Check 'the hook keeps the Ctrl + middle-click' (Test-Logged 'keys "?Control"?, "?SendToBack"? \("?Kept') (@([regex]::Matches((Get-HostLog), 'Title Bar Actions: [^\r\n]*')) | Select-Object -Last 1).Value
Test-Check 'Ctrl + middle-click sends the window in front to the back' ([Win32]::IsInFrontOf($tba2, $tba) -and [Win32]::IsInFrontOf($other, $tba) -and -not [Win32]::IsIconic($tba)) (Get-WindowState $tba)
Test-Check 'the next window gets the front' (Wait-Until { [Win32]::GetForegroundWindow() -eq $tba2 } 3) "$(Get-WindowState $tba); $([regex]::Match((Get-HostLog), '[^\r\n]*didn''t give the front[^\r\n]*').Value)"


# A middle-click on the window in front minimizes it too, and the hook keeps it.
Select-Window $tba
[void] (Move-ToTitleBar $tba 430)
$kept = Get-MatchCount (Get-HostLog) 'keys "?None"?, "?Minimize"? \("?Kept'
Send-MiddleClick
Test-Check 'a middle-click on the title bar of the window in front minimizes it' (Wait-Until { [Win32]::IsIconic($tba) } 5) (Get-WindowState $tba)
Test-Check 'the hook keeps that click too' (Wait-Until { (Get-MatchCount (Get-HostLog) 'keys "?None"?, "?Minimize"? \("?Kept') -gt $kept } 5)
Restore-Window $tba

# The wheel changes the window's opacity in 10% steps, and the window gets no wheel; back at 100%,
# the layered style and the mark go.
Write-TitleBarSettings '{ "enabled": true, "wheel": "Opacity" }'
Select-Window $other
[void] (Move-ToTitleBar $tba)
$lines = (Get-AppLog).Count
Send-Wheel -1
Test-Check 'a notch of the wheel down makes the window 90% opaque' (Wait-Until { [TitleBarChecks]::Opacity($tba) -eq 90 } 3) (Get-WindowState $tba)
Test-Check 'it marks the window' ([TitleBarChecks]::IsMarked($tba)) (Get-WindowState $tba)
Send-Wheel -2
Test-Check 'two more make it 70% opaque' (Wait-Until { [TitleBarChecks]::Opacity($tba) -eq 70 } 3) (Get-WindowState $tba)
Save-Screenshot '14g-title-bar-actions-opacity'
$since = Get-AppLogSince $lines
Test-Check 'the window gets no wheel' (@($since | Where-Object { $_ -like 'tba-app-1 *' }).Count -eq 0) ($since -join ' | ')
Send-Wheel 3
Test-Check 'back at 100%, the layered style and the mark go' (Wait-Until { -not [TitleBarChecks]::IsLayered($tba) -and -not [TitleBarChecks]::IsMarked($tba) } 3) (Get-WindowState $tba)

# Turning the feature off and Exit make the window opaque again; a host that was ended leaves it
# translucent, and the next start puts it back.
Send-Wheel -5
[void] (Wait-Until { [TitleBarChecks]::Opacity($tba) -eq 50 } 3)
Write-TitleBarSettings '{ "enabled": false, "wheel": "Opacity" }'
Test-Check 'turning the feature off makes the window opaque' (Wait-Until { -not [TitleBarChecks]::IsLayered($tba) -and -not [TitleBarChecks]::IsMarked($tba) } 5) (Get-WindowState $tba)
Write-TitleBarSettings '{ "enabled": true, "wheel": "Opacity" }'
Start-Host
[void] (Move-ToTitleBar $tba 420)
Send-Wheel -5
Test-Check 'turned on again, the wheel makes it 50% opaque' (Wait-Until { [TitleBarChecks]::Opacity($tba) -eq 50 } 3) (Get-WindowState $tba)
Stop-Host
Test-Check 'Exit makes the window opaque' ((-not [TitleBarChecks]::IsLayered($tba)) -and -not [TitleBarChecks]::IsMarked($tba)) (Get-WindowState $tba)
Start-Host
[void] (Move-ToTitleBar $tba 400)
Send-Wheel -5
[void] (Wait-Until { [TitleBarChecks]::Opacity($tba) -eq 50 } 3)
Get-Processes 'PCCompanion.Host' | Stop-Process -Force
Start-Sleep -Seconds 2
Test-Check 'a host that''s ended leaves the window translucent and marked' (([TitleBarChecks]::Opacity($tba) -eq 50) -and [TitleBarChecks]::IsMarked($tba)) (Get-WindowState $tba)
Start-Host
Test-Check 'the next start makes it opaque' (Wait-Until { -not [TitleBarChecks]::IsLayered($tba) -and -not [TitleBarChecks]::IsMarked($tba) } 10) (Get-WindowState $tba)
Test-Check 'the host logs it' (Test-Logged 'gave 1 windows left translucent their opacity back')

# A window whose program stopped responding gets no action and doesn't hold the host; a listed
# app's window gets none either, and its click reaches it. The host logs why.
Write-TitleBarSettings '{ "enabled": true }'
Select-Window $other
[void] [Win32]::PostMessage($tba, 0x8003, [UIntPtr]::new([uint32] 9000), [IntPtr]::Zero)
Start-Sleep -Milliseconds 5500
Test-Check 'the test program stopped responding' (-not [Win32]::Responds($tba, 200))
[void] (Move-ToTitleBar $tba)
Send-MiddleClick @() 300
Test-Check 'the host still responds' ([Win32]::Responds([Win32]::FindWindow('PCCompanion.Host', $null), 1000))
Test-Check 'the host logs why it did nothing' (Test-Logged 'got no action: "?WindowLeftOut')
[void] (Wait-Until { [Win32]::Responds($tba, 500) } 20)
Start-Sleep -Seconds 1
Test-Check 'once it responds, the window isn''t minimized' (-not [Win32]::IsIconic($tba)) (Get-WindowState $tba)
Write-TitleBarSettings "{ ""enabled"": true, ""excludedApps"": [ { ""name"": ""tba-app"", ""program"": ""$($tbaExe -replace '\\', '\\')"" } ] }"
Select-Window $other
[void] (Move-ToTitleBar $tba 410)
$lines = (Get-AppLog).Count
Send-MiddleClick
Test-Check 'a listed app''s window isn''t minimized' (-not [Win32]::IsIconic($tba)) (Get-WindowState $tba)
Test-Check 'its click reaches it' (Wait-Until { @(Get-AppLogSince $lines | Where-Object { $_ -eq 'tba-app-1 WM_NCMBUTTONDOWN' }).Count -eq 1 } 5) ((Get-AppLogSince $lines) -join ' | ')
Test-Check 'the host logs why' (Test-Logged 'got no action: "?AppListed')

# With Minimize to Tray and Hide Apps on, Shift + middle-click sends the window to the tray and Ctrl
# + middle-click hides its app; with them off, the host logs why.
$features = ', "minimizeToTray": { "enabled": true }, "hideApps": { "enabled": true }'
Write-TitleBarSettings '{ "enabled": true, "shiftMiddleClick": "MinimizeToTray", "controlMiddleClick": "HideApp" }' $features
[void] (Wait-Until { ([Win32]::FindWindow('PCCompanion.MinimizeToTray', $null) -ne [IntPtr]::Zero) -and ([Win32]::FindWindow('PCCompanion.HideApps', $null) -ne [IntPtr]::Zero) } 10)
Start-Sleep -Seconds 1
Select-Window $other
[void] (Move-ToTitleBar $tba)
Send-MiddleClick @($VK_CONTROL)
Test-Check 'Ctrl + middle-click hides both windows of the app' (Wait-Until { [Win32]::IsIconic($tba) -and [Win32]::IsIconic($tba2) } 5) "first: $(Get-WindowState $tba); second: $(Get-WindowState $tba2)"
Test-Check 'the other app stays' (-not [Win32]::IsIconic($other)) (Get-WindowState $other)
Restore-Window $tba
Restore-Window $tba2
Select-Window $other
[void] (Move-ToTitleBar $tba 410)
Send-MiddleClick @($VK_SHIFT)
Test-Check 'Shift + middle-click sends the window to the tray' (Wait-Until { (-not [Win32]::IsWindowVisible($tba)) -and [TitleBarChecks]::IsInTray($tba) } 5) (Get-WindowState $tba)
Test-Check 'Minimize to Tray logs it' (Test-Logged 'sent a window to the tray: "?TitleBar')
Write-TitleBarSettings '{ "enabled": true, "shiftMiddleClick": "MinimizeToTray", "controlMiddleClick": "HideApp" }'
[void] (Wait-Until { [Win32]::IsWindowVisible($tba) } 10)
Restore-Window $tba
Select-Window $other
[void] (Move-ToTitleBar $tba 400)
Send-MiddleClick @($VK_SHIFT)
Send-MiddleClick @($VK_CONTROL)
Test-Check 'with the features off, the window stays' ([Win32]::IsWindowVisible($tba) -and -not [Win32]::IsIconic($tba) -and -not [TitleBarChecks]::IsInTray($tba)) (Get-WindowState $tba)
Test-Check 'the host logs why' ((Get-MatchCount (Get-HostLog) 'didn''t run: "?FeatureOff') -ge 2)

# Everything in Windows Sandbox runs as administrator; a host whose file is labeled Medium integrity
# runs without admin rights, and Windows refuses it the question to the test program's window, which
# has them. Windows gives such a host pointer input only while a window without admin rights is in
# front, so a medium-labeled copy of the test program is.
Stop-Host
& icacls.exe $hostExe /setintegritylevel medium | Out-Null
Write-TitleBarSettings '{ "enabled": true }'
Start-Host
$mediumProcess = Start-TestApp $mediumExe 420
$medium = Find-AppWindow 'medium-app-1'
Select-Window $medium
[void] (Move-ToTitleBar $tba)
$lines = (Get-AppLog).Count
Send-MiddleClick
Test-Check 'a host without admin rights leaves the window of a program with them alone' (-not [Win32]::IsIconic($tba)) (Get-WindowState $tba)
Test-Check 'its click reaches it' (Wait-Until { @(Get-AppLogSince $lines | Where-Object { $_ -eq 'tba-app-1 WM_NCMBUTTONDOWN' }).Count -eq 1 } 5) ((Get-AppLogSince $lines) -join ' | ')
Test-Check 'the host logs why' (Test-Logged 'got no action: "?Refused')
Stop-Host
& icacls.exe $hostExe /setintegritylevel high | Out-Null
Stop-Process -Id $mediumProcess.Id -Force -ErrorAction SilentlyContinue

# The page in Arabic with the dark theme.
Write-Settings '{ "schemaVersion": 1, "general": { "language": "ar", "theme": "Dark" }, "features": { "titleBarActions": { "enabled": true } } }'
Start-Sleep -Seconds 2
Start-Process $appExe
[void] (Wait-Until { Get-AppWindow })
Start-Sleep -Seconds 3
Test-Check 'select Title Bar Actions in Arabic' (Select-NavigationItem 'إجراءات شريط العنوان')
Start-Sleep -Seconds 2
Save-Screenshot '14h-title-bar-actions-arabic-dark'
$text = Get-WindowText
Test-Check 'the page is in Arabic' ($text -contains 'النقر الأوسط') ($text -join ' | ')
[void] (Close-SettingsApp)

Stop-Host
Get-Process 'tba-app', 'other-app', 'medium-app' -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Seconds 1
Remove-Item $appFolder -Recurse -Force -ErrorAction SilentlyContinue
Write-TitleBarSettings '{ "enabled": false }'
