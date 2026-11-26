# Window Shading: the Home switch turns it on and its page saves each setting and opens Title Bar
# Actions and Shortcuts & Mouse Buttons. With test programs, Title Bar Actions' middle-click rolls a
# window up: it's minimized, the front goes to the window behind, and a strip at its title bar's
# place shows its title bar. A click on the strip rolls it down, in front; the wheel turned up rolls
# it up and turned down rolls it down, and a wheel that bounces back up right after doesn't roll it
# up again; the taskbar and Alt + Tab roll it down, the picture's Close button closes the window,
# and a dragged strip moves where the window rolls down. Hover shows the window after its delay,
# rolls it up again once the pointer has left, and a click keeps it rolled down. Shortcuts roll up
# the window in front and roll down the last one, the PC Companion icon's menu rolls every window
# down, and Minimize to Tray leaves a listed app's rolled-up window alone. File Explorer, Settings
# and Edge roll up and down. Exit rolls the windows down, a host that was ended leaves them
# minimized and marked, and the next start rolls them down. With the host's file labeled Medium
# integrity, a window running as administrator isn't rolled up.
[void] (Close-SettingsApp)

# Detailed logging is on for the host's Debug entries.
$detailedUntil = [DateTimeOffset]::Now.AddHours(1).ToString('o')
function Write-ShadingSettings([string] $Section, [string] $Others = '') {
    Write-Settings "{ ""schemaVersion"": 1, ""general"": { ""theme"": ""Light"" }, ""diagnostics"": { ""detailedLoggingUntil"": ""$detailedUntil"" }, ""features"": { ""windowShading"": $Section$Others } }"
    Start-Sleep -Seconds 3
}
function Start-Host([switch] $Off) {
    if ((Get-Processes 'PCCompanion.Host').Count -eq 0) {
        Start-Process $hostExe
        [void] (Wait-Until { [Win32]::FindWindow('PCCompanion.Host', $null) -ne [IntPtr]::Zero } 30)
    }
    if (-not $Off) { [void] (Wait-Until { [Win32]::FindWindow('PCCompanion.WindowShading', $null) -ne [IntPtr]::Zero } 20) }
    Start-Sleep -Seconds 2
}
function Stop-Host {
    [void] [Win32]::PostMessage([Win32]::FindWindow('PCCompanion.Host', $null), 0x0010, [UIntPtr]::Zero, [IntPtr]::Zero)
    [void] (Wait-Until { (Get-Processes 'PCCompanion.Host').Count -eq 0 } 15)
}
function Get-ShadingSettings { (Get-Content (Join-Path $dataFolder 'settings.json') -Raw | ConvertFrom-Json).features.windowShading }
function Test-Logged([string] $Pattern, [int] $Seconds = 5) { Wait-Until { (Get-HostLog) -match $Pattern } $Seconds }
function Get-LastLogged([string] $Pattern) { (@([regex]::Matches((Get-HostLog), "[^\r\n]*(?:$Pattern)[^\r\n]*")) | Select-Object -Last 1).Value }

# The test program, built once and copied: shade-app is the window rolled up and behind-app the one
# behind it, whose orange body is under shade-app's title bar, so a strip that showed nothing would
# show orange. Each window has the standard title bar, titled after its file, at the place in its
# first argument.
$shadeFolder = Join-Path $env:TEMP 'PCCompanionShadeApp'
New-Item -ItemType Directory -Force -Path $shadeFolder | Out-Null
$shadeExe = Join-Path $shadeFolder 'shade-app.exe'
$behindExe = Join-Path $shadeFolder 'behind-app.exe'
$mediumExe = Join-Path $shadeFolder 'medium-app.exe'
Add-Type -OutputAssembly $shadeExe -OutputType WindowsApplication -ReferencedAssemblies System.Windows.Forms, System.Drawing -TypeDefinition @'
using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
public static class ShadeApp {
    [DllImport("user32.dll")] private static extern bool SetProcessDPIAware();

    [STAThread]
    public static void Main(string[] arguments) {
        SetProcessDPIAware();
        var name = Path.GetFileNameWithoutExtension(Application.ExecutablePath);
        var place = arguments[0].Split(',').Select(int.Parse).ToArray();
        Application.Run(new Form { Text = name, BackColor = Color.FromArgb(230, 120, 40), StartPosition = FormStartPosition.Manual, Bounds = new Rectangle(place[0], place[1], place[2], place[3]) });
    }
}
'@
Copy-Item $shadeExe $behindExe -Force
Copy-Item $shadeExe $mediumExe -Force
& icacls.exe $mediumExe /setintegritylevel medium | Out-Null
Add-Type -ReferencedAssemblies System.Drawing -TypeDefinition @'
using System;
using System.Drawing;
using System.Runtime.InteropServices;
public static class ShadeChecks {
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr window, int attribute, out RECT value, int size);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr window, out RECT rect);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetProp(IntPtr window, string name);
    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr window, int index);
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

    public static RECT Bounds(IntPtr window) {
        RECT bounds;
        GetWindowRect(window, out bounds);
        return bounds;
    }

    public static bool IsMarked(IntPtr window) { return GetProp(window, "PCCompanion.WindowShading") != IntPtr.Zero; }

    public static bool IsInTray(IntPtr window) { return GetProp(window, "PCCompanion.MinimizeToTray") != IntPtr.Zero; }

    public static bool IsTopmost(IntPtr window) { return (GetWindowLong(window, -20) & 0x8) != 0; } // GWL_EXSTYLE, WS_EX_TOPMOST

    [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct WINDOWPLACEMENT { public int length, flags, showCmd; public POINT min, max; public RECT normal; }
    [DllImport("user32.dll")] private static extern bool GetWindowPlacement(IntPtr window, ref WINDOWPLACEMENT placement);

    // Where the window goes when it's restored, in workspace coordinates.
    public static RECT NormalPlace(IntPtr window) {
        var placement = new WINDOWPLACEMENT { length = Marshal.SizeOf(typeof(WINDOWPLACEMENT)) };
        GetWindowPlacement(window, ref placement);
        return placement.normal;
    }

    // The screen's color at a point.
    public static Color ColorAt(int x, int y) {
        using (var picture = new Bitmap(1, 1)) {
            using (var graphics = Graphics.FromImage(picture)) { graphics.CopyFromScreen(x, y, 0, 0, new Size(1, 1)); }
            return picture.GetPixel(0, 0);
        }
    }
}
'@
function Find-AppWindow([string] $Name) { [Win32]::FindWindow([NullString]::Value, $Name) }
function Start-ShadeApp([string] $Exe, [string] $Place) {
    $name = [IO.Path]::GetFileNameWithoutExtension($Exe)
    $process = Start-Process $Exe -ArgumentList $Place -PassThru
    [void] (Wait-Until { (Find-AppWindow $name) -ne [IntPtr]::Zero } 30)
    Start-Sleep -Seconds 1
    return $process
}
function Stop-ShadeApps { foreach ($name in 'shade-app', 'behind-app', 'medium-app') { Get-Processes $name | ForEach-Object { Stop-Process -Id $_.Id -Force -ErrorAction SilentlyContinue } } }
# The strips are windows of PC Companion's own, each with a class of its own.
function Find-Strips { @([Win32]::TopLevelWindows() | Where-Object { ([Win32]::WindowClass($_) -like 'PCCompanion.WindowShading.Strip.*') -and [Win32]::IsWindowVisible($_) }) }
function Find-Strip { Find-Strips | Select-Object -First 1 }
function Test-RolledUp([IntPtr] $Window) { [Win32]::IsIconic($Window) -and [ShadeChecks]::IsMarked($Window) -and ((Find-Strips).Count -gt 0) }
function Test-RolledDown([IntPtr] $Window) { (-not [Win32]::IsIconic($Window)) -and (-not [ShadeChecks]::IsMarked($Window)) -and ((Find-Strips).Count -eq 0) }
function Get-ShadeState([IntPtr] $Window) {
    $strips = @(Find-Strips | ForEach-Object { $b = [ShadeChecks]::Bounds($_); "($($b.Left),$($b.Top),$($b.Right),$($b.Bottom)) topmost $([ShadeChecks]::IsTopmost($_))" }) -join ' '
    "minimized $([Win32]::IsIconic($Window)), visible $([Win32]::IsWindowVisible($Window)), marked $([ShadeChecks]::IsMarked($Window)), strips: $strips, front: $([Win32]::WindowTitle([Win32]::GetForegroundWindow())), $(Get-PointerText)"
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
# Rests the pointer on a window's title bar, $X pixels from the left of the part that shows, so
# Title Bar Actions asks the window and claims the point; the move waits a second there.
function Move-ToTitleBar([IntPtr] $Window, [int] $X = 300) {
    $visible = [ShadeChecks]::Visible($Window)
    return (Move-Pointer ($visible.Left + $X) ($visible.Top + 15))
}
function Send-MiddleClick {
    [void] [ShadeChecks]::Mouse(0x0020, 0)
    Start-Sleep -Milliseconds 50
    [void] [ShadeChecks]::Mouse(0x0040, 0)
    Start-Sleep -Milliseconds 1500
}
function Send-Wheel([int] $Notches, [int] $Wait = 400) {
    for ($notch = 1; $notch -le [math]::Abs($Notches); $notch++) {
        [void] [ShadeChecks]::Mouse(0x0800, [math]::Sign($Notches) * 120)
        Start-Sleep -Milliseconds $Wait
    }
}
function Send-Click {
    [void] [Win32]::LeftButton($true)
    Start-Sleep -Milliseconds 50
    [void] [Win32]::LeftButton($false)
    Start-Sleep -Milliseconds 1000
}
$LeftControl = 0x1D; $LeftAlt = 0x38; $KeyTab = 0x0F; $KeyU = 0x16; $KeyY = 0x15
function Send-Keys([int[]] $Keys, [int] $Wait = 1500) {
    foreach ($key in $Keys) { [void] [Win32]::ScanKey($key, $false, $false) }
    [array]::Reverse($Keys)
    foreach ($key in $Keys) { [void] [Win32]::ScanKey($key, $true, $false) }
    Start-Sleep -Milliseconds $Wait
}
# Rolls a window up with Title Bar Actions' middle-click, from the front.
function Invoke-RollUp([IntPtr] $Window) {
    Select-Window $Window
    [void] (Move-ToTitleBar $Window)
    Send-MiddleClick
    return (Wait-Until { Test-RolledUp $Window } 5)
}
function Get-StripPoint([int] $X = 200) {
    $strip = Find-Strip
    if (-not $strip) { return $null }
    $bounds = [ShadeChecks]::Bounds($strip)
    return [System.Drawing.Point]::new($bounds.Left + $X, [int](($bounds.Top + $bounds.Bottom) / 2))
}
function Test-Light([System.Drawing.Color] $Color) { ($Color.R -gt 170) -and ($Color.G -gt 170) -and ($Color.B -gt 170) }
$withTitleBarActions = ', "titleBarActions": { "enabled": true, "middleClick": "RollUp", "wheel": "RollUp" }'

# Turned on from Home, which saves only that it's on, the page shows its settings, saves each one,
# and opens Title Bar Actions and Shortcuts & Mouse Buttons.
Write-Settings '{ "schemaVersion": 1, "general": { "theme": "Light" } }'
Start-Sleep -Seconds 2
Start-Process $appExe
[void] (Wait-Until { Get-AppWindow })
Start-Sleep -Seconds 3
Test-Check 'turn on Window Shading on Home' (Set-Toggle 'Window Shading' $true)
Start-Sleep -Seconds 2
$saved = Get-ShadingSettings
Test-Check 'the Home switch saves Window Shading as on' ($saved.enabled -eq $true) "saved: $($saved | ConvertTo-Json -Compress)"
Test-Check 'select Window Shading' (Select-NavigationItem 'Window Shading')
Start-Sleep -Seconds 2
Save-Screenshot '23a-window-shading'
$text = Get-WindowText
$missing = @('How to roll windows up', 'Keep on top while rolled up', 'Hover to roll down', 'Roll down after', 'Roll up again after', 'Permissions & data') | Where-Object { $text -notcontains $_ }
Test-Check 'the page shows its settings' (@($missing).Count -eq 0) "missing: $($missing -join ', '); $($text -join ' | ')"
Test-Check 'it says which windows can''t be rolled up' (@($text | Where-Object { $_ -like 'Full-screen windows, windows with a dialog open*' }).Count -eq 1) ($text -join ' | ')
$selected = @('Roll down after', 'Roll up again after' | ForEach-Object { Start-Sleep -Milliseconds 700; Get-ListSelection $_ }) -join ', '
Test-Check 'hover rolls down after 0.5 seconds and up again after 1 second' ($selected -eq '0.5 seconds, 1 second') "chosen: $selected"
Test-Check 'turn off Keep on top while rolled up' (Set-Toggle 'Keep on top while rolled up' $false)
Test-Check 'turn on Hover to roll down' (Set-Toggle 'Hover to roll down' $true)
Test-Check 'choose 0.25 seconds to roll down after' (Select-ListItem 'Roll down after' '0.25 seconds')
Test-Check 'choose 2 seconds to roll up again after' (Select-ListItem 'Roll up again after' '2 seconds')
Test-Check 'the page saves each setting' (Wait-Until { $s = Get-ShadingSettings; ($s.keepOnTop -eq $false) -and ($s.hover.enabled -eq $true) -and ($s.hover.rollDownAfterMs -eq 250) -and ($s.hover.rollUpAfterMs -eq 2000) } 5) "saved: $(Get-ShadingSettings | ConvertTo-Json -Compress)"
# The buttons share their names with the navigation pane's items, so they're found as buttons.
function Invoke-PageButton([string] $Name) {
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
Test-Check 'the Title Bar Actions button' (Invoke-PageButton 'Title Bar Actions')
Test-Check 'opens Title Bar Actions' (Wait-Until { (Get-WindowText) -contains 'Ctrl + middle-click' } 5)
[void] (Select-NavigationItem 'Window Shading')
Start-Sleep -Seconds 2
Test-Check 'the Shortcuts & Mouse Buttons button' (Invoke-PageButton 'Shortcuts & Mouse Buttons')
Test-Check 'opens Shortcuts & Mouse Buttons' (Wait-Until { (Get-WindowText) -contains 'Shortcuts on other pages' } 5)
[void] (Close-SettingsApp)

# Title Bar Actions' middle-click rolls the window up: it's minimized and marked, the window behind
# gets the front, and the strip, at the title bar's place, shows the title bar rather than the
# orange window behind it. A click on the strip rolls it down, in front.
Write-ShadingSettings '{ "enabled": true }' $withTitleBarActions
Start-Host
$behindProcess = Start-ShadeApp $behindExe '40,60,760,560'
$process = Start-ShadeApp $shadeExe '120,140,600,400'
$behind = Find-AppWindow 'behind-app'
$window = Find-AppWindow 'shade-app'
$frame = [ShadeChecks]::Visible($window)
Test-Check 'a middle-click on the title bar rolls the window up' (Invoke-RollUp $window) "$(Get-ShadeState $window); $(Get-LastLogged 'Window Shading|Title Bar Actions:')"
Test-Check 'the host logs the title bar''s height, as the window answered' (Test-Logged 'rolled a window up to its title bar, \d+ pixels high at 96 DPI, found by "?Measured') (Get-LastLogged 'rolled a window up')
$strip = Find-Strip
$bounds = if ($strip) { [ShadeChecks]::Bounds($strip) } else { $null }
Test-Check 'the strip is at the title bar''s place, inside the frame''s edge' ($bounds -and ($bounds.Left -eq $frame.Left + 1) -and ($bounds.Top -eq $frame.Top + 1) -and ($bounds.Right -eq $frame.Right - 1) -and (($bounds.Bottom - $bounds.Top) -ge 24) -and (($bounds.Bottom - $bounds.Top) -le 40)) "frame ($($frame.Left),$($frame.Top),$($frame.Right),$($frame.Bottom)); $(Get-ShadeState $window)"
Test-Check 'it stays on top' ($strip -and [ShadeChecks]::IsTopmost($strip)) (Get-ShadeState $window)
$color = [ShadeChecks]::ColorAt($frame.Left + 300, $frame.Top + 15)
Test-Check 'it shows the title bar''s picture' (Test-Light $color) "color $color; $(Get-ShadeState $window)"
Test-Check 'the window behind gets the front' (Wait-Until { [Win32]::GetForegroundWindow() -eq $behind } 3) (Get-ShadeState $window)
Save-Screenshot '23b-window-shading-rolled-up'
$point = Get-StripPoint
[void] (Move-Pointer $point.X $point.Y)
Send-Click
Test-Check 'a click on the strip rolls the window down' (Wait-Until { Test-RolledDown $window } 5) (Get-ShadeState $window)
Test-Check 'in front' (Wait-Until { [Win32]::GetForegroundWindow() -eq $window } 3) (Get-ShadeState $window)
Test-Check 'the host logs it' (Test-Logged 'rolled a window down: "?Click')

# The wheel turned up rolls the window up, turned up on the strip does nothing, and turned down
# rolls it down; a wheel that bounces back up right after doesn't roll it up again, but a turn a
# second later does.
[void] (Move-ToTitleBar $window)
Send-Wheel 1
Test-Check 'the wheel turned up on the title bar rolls the window up' (Wait-Until { Test-RolledUp $window } 5) (Get-ShadeState $window)
Send-Wheel 2
Test-Check 'turned up on the strip, it does nothing' (Test-RolledUp $window) (Get-ShadeState $window)
Start-Sleep -Milliseconds 800
Send-Wheel -1 0
$rolledDown = Wait-Until { Test-RolledDown $window } 1
$downState = Get-ShadeState $window
# Title Bar Actions claims the title bar again a moment after the pointer moves on it.
[void] [Win32]::PushPointer(1, 0)
Start-Sleep -Milliseconds 400
Send-Wheel 1 0
Start-Sleep -Milliseconds 800
Test-Check 'turned down on the strip, it rolls the window down' $rolledDown $downState
# Title Bar Actions takes the bounce for the same turn as the notch down; Window Shading's own
# guard is for a bounce Title Bar Actions takes for a new turn.
Test-Check 'a wheel that bounces back up right after doesn''t roll it up again' (Test-RolledDown $window) (Get-ShadeState $window)
Start-Sleep -Milliseconds 500
Send-Wheel 1
Test-Check 'a turn a second later rolls it up' (Wait-Until { Test-RolledUp $window } 5) (Get-ShadeState $window)

# The taskbar and Alt + Tab roll the window down, as restoring it any way does.
function Get-TaskbarButton([string] $Like) {
    $condition = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ClassNameProperty, 'Shell_TrayWnd')
    $taskbar = [System.Windows.Automation.AutomationElement]::RootElement.FindFirst([System.Windows.Automation.TreeScope]::Children, $condition)
    if (-not $taskbar) { return $null }
    $buttons = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Button)
    @($taskbar.FindAll([System.Windows.Automation.TreeScope]::Descendants, $buttons) | Where-Object { $_.Current.Name -like $Like }) | Select-Object -First 1
}
# The taskbar's buttons don't act on UI Automation's Invoke, so the button is clicked.
$button = Get-TaskbarButton 'shade-app*'
$clicked = $false
if ($button) {
    $rectangle = $button.Current.BoundingRectangle
    $clicked = Move-Pointer ([int] ($rectangle.X + $rectangle.Width / 2)) ([int] ($rectangle.Y + $rectangle.Height / 2))
    Send-Click
}
Test-Check 'the window''s taskbar button rolls it down' ($clicked -and (Wait-Until { Test-RolledDown $window } 5)) "button found $($null -ne $button); $(Get-ShadeState $window)"
Test-Check 'the host logs that it was restored' (Test-Logged 'rolled a window down: "?Restored')
Test-Check 'roll it up again' (Invoke-RollUp $window) (Get-ShadeState $window)
Select-Window $behind
Send-Keys @($LeftAlt, $KeyTab)
Test-Check 'Alt + Tab rolls it down' (Wait-Until { Test-RolledDown $window } 5) (Get-ShadeState $window)

# A drag moves the strip, and the window rolls down there.
$boundsBefore = [ShadeChecks]::Bounds($window)
$placeBefore = [ShadeChecks]::NormalPlace($window)
Test-Check 'roll it up for a drag' (Invoke-RollUp $window) (Get-ShadeState $window)
$before = [ShadeChecks]::Bounds((Find-Strip))
$point = Get-StripPoint
[void] (Move-Pointer $point.X $point.Y)
[void] [Win32]::LeftButton($true)
[void] (Move-Pointer ($point.X + 150) ($point.Y + 100))
[void] [Win32]::LeftButton($false)
Start-Sleep -Seconds 1
$after = [ShadeChecks]::Bounds((Find-Strip))
Test-Check 'a drag moves the strip' ((($after.Left - $before.Left) -eq 150) -and (($after.Top - $before.Top) -eq 100) -and (Test-RolledUp $window)) "before ($($before.Left),$($before.Top)); $(Get-ShadeState $window)"
Test-Check 'and where the window is restored' (Wait-Until { $n = [ShadeChecks]::NormalPlace($window); (($n.Left - $placeBefore.Left) -eq 150) -and (($n.Top - $placeBefore.Top) -eq 100) } 3) "normal place ($(([ShadeChecks]::NormalPlace($window)).Left),$(([ShadeChecks]::NormalPlace($window)).Top)), was ($($placeBefore.Left),$($placeBefore.Top))"
$point = Get-StripPoint
[void] (Move-Pointer $point.X $point.Y)
Send-Click
Test-Check 'the window rolls down where the strip was dragged' (Wait-Until { $m = [ShadeChecks]::Bounds($window); (Test-RolledDown $window) -and (($m.Left - $boundsBefore.Left) -eq 150) -and (($m.Top - $boundsBefore.Top) -eq 100) } 3) "now ($(([ShadeChecks]::Bounds($window)).Left),$(([ShadeChecks]::Bounds($window)).Top)), was ($($boundsBefore.Left),$($boundsBefore.Top)); $(Get-ShadeState $window)"

# The picture's Close button, the last 46 pixels of the strip, closes the window.
Test-Check 'roll it up to close it' (Invoke-RollUp $window) (Get-ShadeState $window)
$strip = Find-Strip
$bounds = [ShadeChecks]::Bounds($strip)
[void] (Move-Pointer ($bounds.Right - 23) ([int](($bounds.Top + $bounds.Bottom) / 2)))
Send-Click
Test-Check 'a click on the picture''s Close button closes the window' (Wait-Until { $process.HasExited -and ((Find-Strips).Count -eq 0) } 5) (Get-ShadeState $window)
Test-Check 'the host logs it' (Test-Logged 'rolled a window down: "?Close')

# Hover shows the window, without the front, after its delay, and rolls it up again once the
# pointer has left it; a click keeps it rolled down. The window is rolled up before hover is
# turned on, since the pointer rests on the strip then.
$process = Start-ShadeApp $shadeExe '120,140,600,400'
$window = Find-AppWindow 'shade-app'
$away = [System.Drawing.Point]::new($screen.Width - 60, 300)
Test-Check 'roll it up for hover' (Invoke-RollUp $window) (Get-ShadeState $window)
$stripBounds = [ShadeChecks]::Bounds((Find-Strip))
[void] (Move-Pointer $away.X $away.Y)
Write-ShadingSettings '{ "enabled": true, "hover": { "enabled": true, "rollDownAfterMs": 250, "rollUpAfterMs": 500 } }' $withTitleBarActions
Test-Check 'with the pointer away, it stays rolled up' (Test-RolledUp $window) (Get-ShadeState $window)
[void] (Move-Pointer ($stripBounds.Left + 200) ($stripBounds.Top + 15))
Test-Check 'resting on the strip shows the window, in its place' (Wait-Until { (-not [Win32]::IsIconic($window)) -and [ShadeChecks]::IsMarked($window) -and ((Find-Strips).Count -eq 0) } 3) (Get-ShadeState $window)
Test-Check 'without the front' ([Win32]::GetForegroundWindow() -ne $window) (Get-ShadeState $window)
Test-Check 'in front of the window behind it' (Wait-Until { [Win32]::IsInFrontOf($window, $behind) } 2) (Get-ShadeState $window)
Save-Screenshot '23c-window-shading-hover'
[void] (Move-Pointer $away.X $away.Y)
Test-Check 'once the pointer has left, it rolls up again' (Wait-Until { Test-RolledUp $window } 3) (Get-ShadeState $window)
$point = Get-StripPoint
[void] (Move-Pointer $point.X $point.Y)
[void] (Wait-Until { -not [Win32]::IsIconic($window) } 3)
# A window that was just restored still reports its minimized place for a moment, so its body is
# found from where its strip was.
[void] (Move-Pointer ($stripBounds.Left + 200) ($stripBounds.Bottom + 150))
Send-Click
[void] (Move-Pointer $away.X $away.Y)
Start-Sleep -Seconds 1
Test-Check 'a click on the shown window keeps it rolled down' ((Test-RolledDown $window) -and ([Win32]::GetForegroundWindow() -eq $window)) (Get-ShadeState $window)
Test-Check 'the host logs it' (Test-Logged 'rolled a window down: "?Hover')

# Shortcuts roll up the window in front and roll down the last one rolled up, in front, and the PC
# Companion icon's menu rolls every window down.
$triggers = @(
    '{ "action": { "feature": "windowShading", "id": "rollUp" }, "shortcut": { "modifiers": [ "Control", "Alt" ], "key": "KeyU" } }',
    '{ "action": { "feature": "windowShading", "id": "rollDownLast" }, "shortcut": { "modifiers": [ "Control", "Alt" ], "key": "KeyY" } }'
) -join ', '
Write-ShadingSettings '{ "enabled": true }' ", ""shortcutsAndMouseButtons"": { ""enabled"": true, ""triggers"": [ $triggers ] }"
Select-Window $window
Send-Keys @($LeftControl, $LeftAlt, $KeyU)
Test-Check 'Roll up the window in front rolls it up' (Wait-Until { Test-RolledUp $window } 5) (Get-ShadeState $window)
Select-Window $behind
Send-Keys @($LeftControl, $LeftAlt, $KeyU)
Test-Check 'and another window too' (Wait-Until { [Win32]::IsIconic($behind) -and ((Find-Strips).Count -eq 2) } 5) (Get-ShadeState $behind)
Test-Check 'the PC Companion icon''s menu requested' (Send-TrayEvent $WM_CONTEXTMENU $menuX $menuY)
Test-Check 'choose Roll down all windows' (Select-TrayMenuItem 'Roll down all windows')
Test-Check 'it rolls every window down' (Wait-Until { (-not [Win32]::IsIconic($window)) -and (-not [Win32]::IsIconic($behind)) -and ((Find-Strips).Count -eq 0) } 5) "$(Get-ShadeState $window); $(Get-ShadeState $behind)"
Select-Window $window
Send-Keys @($LeftControl, $LeftAlt, $KeyU)
[void] (Wait-Until { Test-RolledUp $window } 5)
Send-Keys @($LeftControl, $LeftAlt, $KeyY)
Test-Check 'Roll down the last rolled-up window rolls it down, in front' (Wait-Until { (Test-RolledDown $window) -and ([Win32]::GetForegroundWindow() -eq $window) } 5) (Get-ShadeState $window)

# Minimize to Tray leaves a listed app's rolled-up window on the taskbar.
Write-ShadingSettings '{ "enabled": true }' "$withTitleBarActions, ""minimizeToTray"": { ""enabled"": true, ""shortcut"": ""None"", ""shiftMinimize"": false, ""minimizedApps"": [ { ""name"": ""shade-app"", ""program"": ""$($shadeExe -replace '\\', '\\')"" } ] }"
Test-Check 'a listed app''s window rolls up' (Invoke-RollUp $window) (Get-ShadeState $window)
Start-Sleep -Seconds 1
Test-Check 'Minimize to Tray leaves it rolled up' ((Test-RolledUp $window) -and -not [ShadeChecks]::IsInTray($window)) (Get-ShadeState $window)
Test-Check 'Minimize to Tray logs why' (Test-Logged 'didn''t send a window to the tray: [^\r\n]*RolledUp') (Get-LastLogged 'Minimize to Tray')
$point = Get-StripPoint
[void] (Move-Pointer $point.X $point.Y)
Send-Click
[void] (Wait-Until { Test-RolledDown $window } 5)

# File Explorer, Settings and Edge roll up and down; their title bars answer through child windows.
Write-ShadingSettings '{ "enabled": true }' $withTitleBarActions
$edgeExe = 'C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe'
Start-Process $edgeExe -ArgumentList '--no-first-run', '--no-default-browser-check', "--user-data-dir=$shadeFolder\edge", 'about:blank'
$script:edge = [IntPtr]::Zero
[void] (Wait-Until { $script:edge = [Win32]::TopLevelWindows() | Where-Object { [Win32]::WindowClass($_) -eq 'Chrome_WidgetWin_1' -and [Win32]::WindowTitle($_) -like '*Edge*' } | Select-Object -First 1; $null -ne $script:edge } 40)
Start-Process explorer.exe 'C:\'
$script:explorer = [IntPtr]::Zero
[void] (Wait-Until { $script:explorer = [Win32]::TopLevelWindows() | Where-Object { [Win32]::WindowClass($_) -eq 'CabinetWClass' } | Select-Object -First 1; $null -ne $script:explorer } 30)
Start-Process 'ms-settings:about'
$script:settingsWindow = [IntPtr]::Zero
[void] (Wait-Until { $script:settingsWindow = [Win32]::TopLevelWindows() | Where-Object { [Win32]::WindowClass($_) -eq 'ApplicationFrameWindow' -and [Win32]::WindowTitle($_) -eq 'Settings' } | Select-Object -First 1; $null -ne $script:settingsWindow } 30)
Start-Sleep -Seconds 3
foreach ($app in @(@{ Name = 'Edge'; Window = $script:edge; X = 520; Shot = '23d-window-shading-edge' }, @{ Name = 'File Explorer'; Window = $script:explorer; X = 520; Shot = '23e-window-shading-explorer' }, @{ Name = 'Settings'; Window = $script:settingsWindow; X = 720; Shot = '23f-window-shading-settings' })) {
    $appWindow = $app.Window
    if (-not $appWindow) {
        Test-Check "$($app.Name) opens" $false
        continue
    }
    [void] [Win32]::ShowWindow($appWindow, 9) # SW_RESTORE
    Start-Sleep -Milliseconds 500
    [void] [Win32]::MoveWindow($appWindow, 300, 120, 900, 280, $true)
    Select-Window $appWindow
    [void] (Move-ToTitleBar $appWindow $app.X)
    Send-MiddleClick
    Test-Check "$($app.Name) rolls up" (Wait-Until { Test-RolledUp $appWindow } 5) "$(Get-ShadeState $appWindow); $(Get-LastLogged 'Window Shading|Title Bar Actions:')"
    Write-Report "[NOTE] $($app.Name): $(Get-LastLogged 'rolled a window up to its title bar')"
    Save-Screenshot $app.Shot
    $point = Get-StripPoint
    if ($point) {
        [void] (Move-Pointer $point.X $point.Y)
        Send-Click
    }
    Test-Check "a click rolls $($app.Name) down" (Wait-Until { Test-RolledDown $appWindow } 5) (Get-ShadeState $appWindow)
    [void] [Win32]::PostMessage($appWindow, 0x0010, [UIntPtr]::Zero, [IntPtr]::Zero)
}
Get-Processes 'msedge' | ForEach-Object { Stop-Process -Id $_.Id -Force -ErrorAction SilentlyContinue }

# Exit rolls the windows down. A host that was ended leaves them minimized and marked, and the
# next start rolls them down.
Test-Check 'roll it up before exit' (Invoke-RollUp $window) (Get-ShadeState $window)
Stop-Host
Test-Check 'when PC Companion exits, the window rolls down' (Wait-Until { Test-RolledDown $window } 5) (Get-ShadeState $window)
Test-Check 'the host logs it' (Test-Logged 'rolled down 1 windows as it stopped')
Start-Host
Test-Check 'roll it up before ending the host' (Invoke-RollUp $window) (Get-ShadeState $window)
Get-Processes 'PCCompanion.Host' | Stop-Process -Force
Start-Sleep -Seconds 2
Test-Check 'a host that''s ended leaves the window minimized and marked' ([Win32]::IsIconic($window) -and [ShadeChecks]::IsMarked($window)) (Get-ShadeState $window)
Start-Host
Test-Check 'the next start rolls it down' (Wait-Until { (-not [Win32]::IsIconic($window)) -and -not [ShadeChecks]::IsMarked($window) } 10) (Get-ShadeState $window)
Test-Check 'the host logs it' (Test-Logged 'rolled down 1 windows left rolled up')
Stop-Host
Stop-ShadeApps

# Everything in Windows Sandbox runs as administrator. A host whose file is labeled Medium integrity
# runs without admin rights: it leaves the test window, which has them, alone, and rolls up one
# that runs without them.
Write-ShadingSettings '{ "enabled": true }' ", ""shortcutsAndMouseButtons"": { ""enabled"": true, ""triggers"": [ $triggers ] }"
& icacls.exe $hostExe /setintegritylevel medium | Out-Null
Start-Host
[void] (Start-ShadeApp $shadeExe '120,140,600,400')
[void] (Start-ShadeApp $mediumExe '200,220,600,400')
$window = Find-AppWindow 'shade-app'
$medium = Find-AppWindow 'medium-app'
Select-Window $window
Send-Keys @($LeftControl, $LeftAlt, $KeyU)
Start-Sleep -Seconds 1
Test-Check 'a host without admin rights doesn''t roll up a window of an app running as administrator' (Test-RolledDown $window) (Get-ShadeState $window)
Test-Check 'the host logs why' (Test-Logged 'didn''t roll a window up: "?RunsAsAdministrator') (Get-LastLogged 'Window Shading')
Select-Window $medium
Send-Keys @($LeftControl, $LeftAlt, $KeyU)
Test-Check 'it rolls up a window of an app running without admin rights' (Wait-Until { Test-RolledUp $medium } 5) (Get-ShadeState $medium)
Stop-Host
Test-Check 'and rolls it down as it exits' (Wait-Until { Test-RolledDown $medium } 5) (Get-ShadeState $medium)
& icacls.exe $hostExe /setintegritylevel high | Out-Null

Stop-ShadeApps
Remove-Item $shadeFolder -Recurse -Force -ErrorAction SilentlyContinue
Write-ShadingSettings '{ "enabled": false }'
