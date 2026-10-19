<#
.SYNOPSIS
Runtime checks of PC Companion inside Windows Sandbox. Started by scripts\sandbox-test.ps1; don't run
it on your own PC: it installs and uninstalls PC Companion and shuts Windows down at the end.

.DESCRIPTION
Inputs:  C:\Test\msi\PCCompanion-*.msi (read-only).
Outputs: C:\Test\results: report.txt with one PASS or FAIL line per check, screenshots, the app and
         host logs, the install logs, and done.txt when finished.

The checks look for English, Arabic and Bangla text from src\PCCompanion.Localization; update them
when those strings change.
#>

$ErrorActionPreference = 'Continue'
$results = 'C:\Test\results'
$report = Join-Path $results 'report.txt'
New-Item -ItemType Directory -Force -Path $results | Out-Null
Set-Content -Path $report -Value "PC Companion runtime checks - $(Get-Date -Format o)" -Encoding utf8

function Write-Report([string] $Text) { Add-Content -Path $report -Value $Text -Encoding utf8 }

function Test-Check([string] $Name, [bool] $Passed, [string] $Detail = '') {
    $status = if ($Passed) { 'PASS' } else { 'FAIL' }
    $suffix = if ($Detail) { " - $Detail" } else { '' }
    Write-Report "[$status] $Name$suffix"
}

Add-Type -AssemblyName System.Windows.Forms, System.Drawing, UIAutomationClient, UIAutomationTypes
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class Win32 {
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindow(string className, string title);
    [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr window, uint message, UIntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
    [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr window);
    [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr window);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr window);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr window, int attribute, out int value, int size);

    // Whether Windows keeps the window out of sight (DWMWA_CLOAKED).
    public static bool IsCloaked(IntPtr window) {
        int value;
        return DwmGetWindowAttribute(window, 14, out value, 4) == 0 && value != 0;
    }
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr window, int command);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll")] public static extern bool AllowSetForegroundWindow(uint processId);
    [DllImport("user32.dll")] public static extern bool MoveWindow(IntPtr window, int x, int y, int width, int height, bool repaint);
    [DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint count, INPUT[] inputs, int size);

    [StructLayout(LayoutKind.Sequential)] private struct MOUSEINPUT { public int dx, dy; public uint mouseData, dwFlags, time; public IntPtr extra; }
    [StructLayout(LayoutKind.Sequential)] private struct KEYBDINPUT { public ushort wVk, wScan; public uint dwFlags, time; public IntPtr extra; }
    [StructLayout(LayoutKind.Explicit)] private struct INPUT { [FieldOffset(0)] public uint type; [FieldOffset(8)] public MOUSEINPUT mi; [FieldOffset(8)] public KEYBDINPUT ki; }

    // Moves the pointer as a mouse does, so raw input reports it; (x, y) is on the main display.
    public static bool MovePointer(int x, int y, int width, int height) {
        var input = new INPUT { type = 0 };
        input.mi.dx = (int)((long)x * 65535 / (width - 1));
        input.mi.dy = (int)((long)y * 65535 / (height - 1));
        input.mi.dwFlags = 0x8001; // MOUSEEVENTF_MOVE | MOUSEEVENTF_ABSOLUTE
        return SendInput(1, new[] { input }, Marshal.SizeOf(typeof(INPUT))) == 1;
    }

    // Moves the pointer by (dx, dy), as a mouse does.
    public static bool PushPointer(int dx, int dy) {
        var input = new INPUT { type = 0 };
        input.mi.dx = dx;
        input.mi.dy = dy;
        input.mi.dwFlags = 0x0001; // MOUSEEVENTF_MOVE
        return SendInput(1, new[] { input }, Marshal.SizeOf(typeof(INPUT))) == 1;
    }

    public static bool LeftButton(bool down) {
        var input = new INPUT { type = 0 };
        input.mi.dwFlags = down ? 0x0002u : 0x0004u; // MOUSEEVENTF_LEFTDOWN, MOUSEEVENTF_LEFTUP
        return SendInput(1, new[] { input }, Marshal.SizeOf(typeof(INPUT))) == 1;
    }

    public static bool Key(ushort key, bool up) {
        var input = new INPUT { type = 1 };
        input.ki.wVk = key;
        input.ki.dwFlags = up ? 0x0002u : 0u; // KEYEVENTF_KEYUP
        return SendInput(1, new[] { input }, Marshal.SizeOf(typeof(INPUT))) == 1;
    }

    // Presses or releases the key at a position on the keyboard, whatever the layout makes of it.
    public static bool ScanKey(ushort scanCode, bool up, bool extended) {
        var input = new INPUT { type = 1 };
        input.ki.wScan = scanCode;
        input.ki.dwFlags = 0x0008u | (up ? 0x0002u : 0u) | (extended ? 0x0001u : 0u); // KEYEVENTF_SCANCODE, KEYUP, EXTENDEDKEY
        return SendInput(1, new[] { input }, Marshal.SizeOf(typeof(INPUT))) == 1;
    }

    private delegate bool EnumWindowsProc(IntPtr window, IntPtr parameter);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr parameter);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr window, System.Text.StringBuilder text, int length);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr window, out RECT rect);
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }

    public static string WindowTitle(IntPtr window) {
        var text = new System.Text.StringBuilder(256);
        GetWindowText(window, text, text.Capacity);
        return text.ToString();
    }

    // The visible top-level windows, front to back.
    public static IntPtr[] TopLevelWindows() {
        var windows = new System.Collections.Generic.List<IntPtr>();
        EnumWindows(delegate(IntPtr window, IntPtr parameter) { if (IsWindowVisible(window)) windows.Add(window); return true; }, IntPtr.Zero);
        return windows.ToArray();
    }
}
'@
[void] [Win32]::SetProcessDPIAware()

function Save-Screenshot([string] $Name) {
    $bounds = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
    $bitmap = New-Object System.Drawing.Bitmap $bounds.Width, $bounds.Height
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.CopyFromScreen($bounds.Location, [System.Drawing.Point]::Empty, $bounds.Size)
    $bitmap.Save((Join-Path $results "$Name.png"), [System.Drawing.Imaging.ImageFormat]::Png)

    # The Sandbox draws nothing while its window is minimized, so screenshots come out blank.
    $first = $bitmap.GetPixel(0, 0)
    $blank = $true
    for ($x = 0; $x -lt $bounds.Width -and $blank; $x += 50) {
        for ($y = 0; $y -lt $bounds.Height -and $blank; $y += 50) {
            if ($bitmap.GetPixel($x, $y) -ne $first) { $blank = $false }
        }
    }
    if ($blank) {
        Write-Report "[NOTE] Screenshot $Name is blank; keep the Sandbox window open and visible while the checks run."
    }

    $graphics.Dispose()
    $bitmap.Dispose()
}

function Wait-Until([scriptblock] $Condition, [int] $Seconds = 30) {
    for ($i = 0; $i -lt ($Seconds * 4); $i++) {
        if (& $Condition) { return $true }
        Start-Sleep -Milliseconds 250
    }
    return $false
}

function Get-Processes([string] $Name) { @(Get-Process -Name $Name -ErrorAction SilentlyContinue) }

function Close-SettingsApp {
    Get-Processes 'PCCompanion' | ForEach-Object { [void] $_.CloseMainWindow() }
    return (Wait-Until { (Get-Processes 'PCCompanion').Count -eq 0 } 15)
}

function Stop-All {
    [void] (Close-SettingsApp)
    foreach ($name in 'PCCompanion', 'PCCompanion.Host') {
        Get-Processes $name | ForEach-Object { Stop-Process -Id $_.Id -Force -ErrorAction SilentlyContinue }
    }
    Start-Sleep -Seconds 1
}

function Get-AppWindow {
    $process = Get-Processes 'PCCompanion' | Select-Object -First 1
    if (-not $process) { return $null }
    $condition = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ProcessIdProperty, $process.Id)
    return [System.Windows.Automation.AutomationElement]::RootElement.FindFirst([System.Windows.Automation.TreeScope]::Children, $condition)
}

function Select-NavigationItem([string] $Name) {
    $window = Get-AppWindow
    if (-not $window) { return $false }
    $condition = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, $Name)
    $item = $window.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
    if (-not $item) { return $false }
    $pattern = $null
    if ($item.TryGetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern, [ref] $pattern)) { $pattern.Select(); return $true }
    if ($item.TryGetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern, [ref] $pattern)) { $pattern.Invoke(); return $true }
    return $false
}

function Find-Toggle([string] $Name) {
    $window = Get-AppWindow
    if (-not $window) { return $null }
    $condition = New-Object System.Windows.Automation.AndCondition(
        (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, $Name)),
        (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::IsTogglePatternAvailableProperty, $true)))
    return $window.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
}

# The paths of the Always run as administrator tasks, from Task Scheduler.
function Get-StartupTasks {
    @(schtasks.exe /Query /TN '\PCCompanion\' /FO CSV /NH 2>$null | ForEach-Object { ($_ -split '","')[0].Trim('"') } | Where-Object { $_ -like '\PCCompanion\*' })
}

function Set-Toggle([string] $Name, [bool] $On) {
    $window = Get-AppWindow
    if (-not $window) { return $false }
    $condition = New-Object System.Windows.Automation.AndCondition(
        (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, $Name)),
        (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::IsTogglePatternAvailableProperty, $true)))
    $toggle = $window.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
    if (-not $toggle) { return $false }
    $pattern = $toggle.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern)
    if (($pattern.Current.ToggleState -eq [System.Windows.Automation.ToggleState]::On) -ne $On) { $pattern.Toggle() }
    return $true
}

# Where the page titled $Title and its content are: the page's scrolling area, and the leftmost
# and rightmost edges of everything in it except its scroll bars. The title is the tallest text
# with that name, since a navigation item can have the same one.
function Get-PageLayout([string] $Title) {
    $window = Get-AppWindow
    if (-not $window) { return $null }
    $condition = New-Object System.Windows.Automation.AndCondition(
        (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, $Title)),
        (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Text)))
    $titleElement = @($window.FindAll([System.Windows.Automation.TreeScope]::Descendants, $condition)) |
        Sort-Object { $_.Current.BoundingRectangle.Height } -Descending | Select-Object -First 1
    if (-not $titleElement -or $titleElement.Current.BoundingRectangle.Height -lt 30) { return $null }
    $walker = [System.Windows.Automation.TreeWalker]::RawViewWalker
    $area = $walker.GetParent($titleElement)
    while ($area -and -not [bool] $area.GetCurrentPropertyValue([System.Windows.Automation.AutomationElement]::IsScrollPatternAvailableProperty)) {
        $area = $walker.GetParent($area)
    }
    if (-not $area) { return $null }
    $edges = @($area.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition) |
        Where-Object {
            $_.Current.ControlType -ne [System.Windows.Automation.ControlType]::ScrollBar -and
            $_.Current.AutomationId -notmatch '^(Vertical|Horizontal)' -and
            -not $_.Current.BoundingRectangle.IsEmpty -and $_.Current.BoundingRectangle.Width -gt 0
        } | ForEach-Object { $_.Current.BoundingRectangle })
    if ($edges.Count -eq 0) { return $null }
    $paneButton = $window.FindFirst([System.Windows.Automation.TreeScope]::Descendants,
        (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::AutomationIdProperty, 'TogglePaneButton')))
    return [pscustomobject] @{
        Window = $window.Current.BoundingRectangle
        Area = $area.Current.BoundingRectangle
        Title = $titleElement.Current.BoundingRectangle
        PaneButton = if ($paneButton) { $paneButton.Current.BoundingRectangle } else { [System.Windows.Rect]::Empty }
        Left = ($edges | ForEach-Object { $_.Left } | Measure-Object -Minimum).Minimum
        Right = ($edges | ForEach-Object { $_.Right } | Measure-Object -Maximum).Maximum
    }
}

# Checks the page as Windows Settings lays out its pages: the content is a column in the middle of
# the page's area, at most 1,000 pixels wide, with at least 36 pixels on each side; it never runs
# past the area or the window, and the page's title stays clear of the pane's button.
function Test-PageLayout([string] $Name, [string] $Title, [int] $Width) {
    $check = "$Name at $Width pixels: the page's content is centered and fits in the window"
    $layout = Get-PageLayout $Title
    if (-not $layout) {
        Test-Check $check $false 'the page or its title isn''t shown'
        return
    }
    $window = $layout.Window
    $area = $layout.Area
    $inWindow = ($area.Left -ge $window.Left - 1) -and ($area.Right -le $window.Right + 1)
    $inArea = ($layout.Left -ge $area.Left - 1) -and ($layout.Right -le $area.Right + 1)
    $gap = [math]::Max(36, ($area.Width - 1000) / 2)
    $leftGap = $layout.Left - $area.Left
    $rightGap = $area.Right - $layout.Right
    $contentWidth = $layout.Right - $layout.Left
    $centered = ([math]::Abs($leftGap - $gap) -le 3) -and ([math]::Abs($rightGap - $gap) -le 3)
    $clearOfButton = $layout.PaneButton.IsEmpty -or -not $layout.Title.IntersectsWith($layout.PaneButton)
    $passed = $inWindow -and $inArea -and $centered -and ($contentWidth -le 1002) -and $clearOfButton
    $detail = "window $([int] $window.Left)..$([int] $window.Right), page area $([int] $area.Left)..$([int] $area.Right), content $([int] $layout.Left)..$([int] $layout.Right), $([int] $contentWidth) wide, $([int] $leftGap) and $([int] $rightGap) from the area's edges (expected $([int] $gap)), title clear of the pane button: $clearOfButton"
    Test-Check $check $passed $detail
}

# Shows the page of navigation item $Name. In a narrow window the pane folds away, so if the item
# can't be selected, this opens the pane with its button first.
function Show-Page([string] $Name) {
    if (Select-PaneItem $Name) { return $true }
    $window = Get-AppWindow
    if (-not $window) { return $false }
    $button = $window.FindFirst([System.Windows.Automation.TreeScope]::Descendants,
        (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::AutomationIdProperty, 'TogglePaneButton')))
    $pattern = $null
    if (-not $button -or -not $button.TryGetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern, [ref] $pattern)) { return $false }
    $pattern.Invoke()
    Start-Sleep -Milliseconds 800
    return (Select-PaneItem $Name)
}

# Selects the navigation pane's item $Name, and only that: a page can have text or a card with the
# same name, such as Home's Hot Corners card.
function Select-PaneItem([string] $Name) {
    $window = Get-AppWindow
    if (-not $window) { return $false }
    $condition = New-Object System.Windows.Automation.AndCondition(
        (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, $Name)),
        (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::ListItem)))
    $item = $window.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
    $pattern = $null
    if (-not $item -or -not $item.TryGetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern, [ref] $pattern)) { return $false }
    $pattern.Select()
    return $true
}

# Resizes the settings app's window to $Width pixels, as tall as it fits.
function Set-AppWindowWidth([int] $Width) {
    $window = Get-AppWindow
    if (-not $window) { return $false }
    $handle = [IntPtr] $window.Current.NativeWindowHandle
    $result = [Win32]::MoveWindow($handle, 0, 0, [math]::Min($Width, $screen.Width), [math]::Min(780, $screen.Height - 40), $true)
    Start-Sleep -Milliseconds 1500
    return $result
}

# Puts the settings app's window back at $Bounds, from its BoundingRectangle.
function Restore-AppWindow($Bounds) {
    $window = Get-AppWindow
    if (-not $window) { return }
    [void] [Win32]::MoveWindow([IntPtr] $window.Current.NativeWindowHandle, [int] $Bounds.X, [int] $Bounds.Y, [int] $Bounds.Width, [int] $Bounds.Height, $true)
    Start-Sleep -Milliseconds 1500
}

# Opens a list to read the name a screen reader gives its selected item, then closes it.
function Get-SelectedItemName([string] $ListName) {
    $window = Get-AppWindow
    if (-not $window) { return $null }
    $condition = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, $ListName)
    $list = $window.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
    if (-not $list) { return $null }
    $expand = $list.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern)
    $expand.Expand()
    Start-Sleep -Milliseconds 500
    $selected = @($list.GetCurrentPattern([System.Windows.Automation.SelectionPattern]::Pattern).Current.GetSelection() | ForEach-Object { $_.Current.Name })
    $expand.Collapse()
    return ($selected -join ', ')
}

# A list of the settings app, such as a ComboBox, rather than a card or text of the same name.
function Find-AppList([string] $Name) {
    $window = Get-AppWindow
    if (-not $window) { return $null }
    $condition = New-Object System.Windows.Automation.AndCondition(
        (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, $Name)),
        (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::ComboBox)))
    return $window.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
}

# The name of the item chosen in the list named $Name.
function Get-ListSelection([string] $Name) {
    $list = Find-AppList $Name
    if (-not $list) { return $null }
    $expand = $list.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern)
    $expand.Expand()
    Start-Sleep -Milliseconds 500
    $selected = @($list.GetCurrentPattern([System.Windows.Automation.SelectionPattern]::Pattern).Current.GetSelection() | ForEach-Object { $_.Current.Name })
    $expand.Collapse()
    return ($selected -join ', ')
}

# Chooses the item named $ItemName in the list named $ListName, as a click on it does.
function Select-ListItem([string] $ListName, [string] $ItemName) {
    $list = Find-AppList $ListName
    if (-not $list) { return $false }
    $expand = $list.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern)
    $expand.Expand()
    Start-Sleep -Milliseconds 500
    $condition = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, $ItemName)
    $item = $list.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
    if ($item) { $item.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select() }
    Start-Sleep -Milliseconds 300
    if ($expand.Current.ExpandCollapseState -eq [System.Windows.Automation.ExpandCollapseState]::Expanded) { $expand.Collapse() }
    return [bool] $item
}

function Find-AppElement([string] $Name) {
    $window = Get-AppWindow
    if (-not $window) { return $null }
    $condition = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, $Name)
    return $window.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
}

function Get-BoxText([string] $Name) {
    $box = Find-AppElement $Name
    if (-not $box) { return $null }
    return $box.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value
}

# A dialog of the settings app, which UI Automation lists on the desktop or under the app's window.
function Find-AppDialog([string] $Name) {
    $condition = New-Object System.Windows.Automation.AndCondition(
        (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, $Name)),
        (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Window)))
    $dialog = [System.Windows.Automation.AutomationElement]::RootElement.FindFirst([System.Windows.Automation.TreeScope]::Children, $condition)
    if ($dialog) { return $dialog }
    $window = Get-AppWindow
    if (-not $window) { return $null }
    return $window.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
}

# A tooltip or text of the settings app whose name is exactly $Name.
function Find-AppText([string] $Name) {
    $window = Get-AppWindow
    if (-not $window) { return $null }
    $condition = New-Object System.Windows.Automation.AndCondition(
        (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, $Name)),
        (New-Object System.Windows.Automation.OrCondition(
            (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::ToolTip)),
            (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Text)))))
    $found = $window.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
    if ($found) { return $found }
    return [System.Windows.Automation.AutomationElement]::RootElement.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
}

function Get-PointerText { "pointer at $([System.Windows.Forms.Cursor]::Position)" }

# Presses and releases a key as a keyboard does. Unlike UI Automation's Invoke, it doesn't wait
# for what the key opens, such as a dialog.
function Send-Key([uint16] $Key) {
    [void] [Win32]::Key($Key, $false)
    [void] [Win32]::Key($Key, $true)
}

function Get-WindowText {
    $window = Get-AppWindow
    if (-not $window) { return @() }
    $all = $window.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
    return @($all | ForEach-Object { $_.Current.Name } | Where-Object { $_ })
}

# Sends what Explorer sends when the tray icon is used: the host's window gets WM_APP + 1 with the
# event in the low word of lParam and the icon ID (1) in the high word. The icon itself is usually
# hidden in the overflow area of a new Windows 11 profile.
function Send-TrayEvent([int] $TrayEvent, [int] $X = 0, [int] $Y = 0) {
    $window = [Win32]::FindWindow('PCCompanion.Host', $null)
    if ($window -eq [IntPtr]::Zero) { return $false }
    return [Win32]::PostMessage($window, 0x8001, [UIntPtr] [uint32] (($X -band 0xFFFF) -bor ($Y -shl 16)), [IntPtr] ($TrayEvent -bor (1 -shl 16)))
}

$WM_CONTEXTMENU = 0x007B
$NIN_SELECT = 0x0400

# Moves the pointer in steps from where it is to (X, Y), then waits. Returns whether it got there:
# it doesn't while the Sandbox window is minimized or the mouse is moving over it.
function Move-Pointer([int] $X, [int] $Y) {
    $start = [System.Windows.Forms.Cursor]::Position
    for ($step = 1; $step -le 10; $step++) {
        $stepX = $start.X + [int](($X - $start.X) * $step / 10)
        $stepY = $start.Y + [int](($Y - $start.Y) * $step / 10)
        [void] [Win32]::MovePointer($stepX, $stepY, $screen.Width, $screen.Height)
        Start-Sleep -Milliseconds 15
    }
    Start-Sleep -Milliseconds 1000
    $position = [System.Windows.Forms.Cursor]::Position
    return ($position.X -eq $X) -and ($position.Y -eq $Y)
}

# Chooses an item of the host's open tray menu with the mouse, as a user does. Keys would go to
# the window in front, which needn't be the menu when the menu was opened by a posted message.
function Select-TrayMenuItem([string] $Name) {
    $menuCondition = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ClassNameProperty, '#32768')
    $itemCondition = New-Object System.Windows.Automation.AndCondition(
        (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, $Name)),
        (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::MenuItem)))
    $script:menuItem = $null
    $found = Wait-Until {
        $menu = [System.Windows.Automation.AutomationElement]::RootElement.FindFirst([System.Windows.Automation.TreeScope]::Children, $menuCondition)
        $script:menuItem = if ($menu) { $menu.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $itemCondition) }
        $null -ne $script:menuItem
    } 10
    if (-not $found) { return $false }
    $rectangle = $script:menuItem.Current.BoundingRectangle
    if (-not (Move-Pointer ([int] ($rectangle.X + $rectangle.Width / 2)) ([int] ($rectangle.Y + $rectangle.Height / 2)))) { return $false }
    [void] [Win32]::LeftButton($true)
    [void] [Win32]::LeftButton($false)
    return $true
}

# Moves the pointer into the top-left corner and pushes it against the edges, as a mouse does.
# Windows reports no mouse input for an absolute move to exactly (0, 0), so the push makes the
# host see the pointer arrive.
function Move-IntoTopLeftCorner {
    [void] (Move-Pointer 0 0)
    [void] [Win32]::PushPointer(-10, -10)
    Start-Sleep -Milliseconds 1000
    $position = [System.Windows.Forms.Cursor]::Position
    return ($position.X -eq 0) -and ($position.Y -eq 0)
}

function Get-HostLog {
    (Get-ChildItem $logs -Filter 'host-*.log' -ErrorAction SilentlyContinue | ForEach-Object { Get-Content $_.FullName -Raw }) -join "`n"
}

function Get-MatchCount([string] $Text, [string] $Pattern) { ([regex]::Matches($Text, $Pattern)).Count }

function Get-VirtualDesktopCount {
    $ids = (Get-ItemProperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\VirtualDesktops' -Name VirtualDesktopIDs -ErrorAction SilentlyContinue).VirtualDesktopIDs
    if ($ids) { $ids.Length / 16 } else { 1 }
}

function Write-Settings([string] $Json) {
    New-Item -ItemType Directory -Force -Path $dataFolder | Out-Null
    Set-Content -Path (Join-Path $dataFolder 'settings.json') -Value $Json -Encoding utf8
}

$msi = (Get-ChildItem 'C:\Test\msi' -Filter 'PCCompanion-*.msi' | Select-Object -First 1).FullName
$upgradeMsi = 'C:\Test\upgrade\PCCompanion-upgrade.msi'
$sameVersionMsi = 'C:\Test\upgrade\PCCompanion-same-version.msi'
$installFolder = Join-Path $env:LOCALAPPDATA 'Programs\PCCompanion'
$appExe = Join-Path $installFolder 'PCCompanion.exe'
$hostExe = Join-Path $installFolder 'PCCompanion.Host.exe'
$dataFolder = Join-Path $env:LOCALAPPDATA 'PCCompanion'
$logs = Join-Path $dataFolder 'Logs'
$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$screen = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
$menuX = $screen.Width - 300
$menuY = $screen.Height - 100
Write-Report "msi: $msi; user: $env:USERNAME; screen: $($screen.Width)x$($screen.Height); Windows $([Environment]::OSVersion.Version)"

# 1. Install for the current user.
$code = (Start-Process msiexec.exe -ArgumentList @('/i', "`"$msi`"", '/qn', 'ALLUSERS=2', 'MSIINSTALLPERUSER=1', '/l*v', "`"$results\install.log`"") -Wait -PassThru).ExitCode
Test-Check 'install for the current user' ($code -eq 0) "exit code $code"
Test-Check 'settings app and host installed' ((Test-Path $appExe) -and (Test-Path $hostExe))
Test-Check 'third-party notices installed' (Test-Path (Join-Path $installFolder 'ThirdPartyNotices.txt'))
$run = (Get-ItemProperty $runKey -Name PCCompanion -ErrorAction SilentlyContinue).PCCompanion
Test-Check 'startup entry in HKCU Run' ($run -eq "`"$hostExe`" --startup") "value: $run"

# 2. Started at sign-in while Start with Windows is off: the host exits and creates nothing.
$process = Start-Process $hostExe -ArgumentList '--startup' -PassThru
Test-Check 'host --startup exits when Start with Windows is off' ($process.WaitForExit(10000))
Test-Check 'host --startup creates no data folder' (-not (Test-Path $dataFolder))

# 3. The settings app starts the host and shows Home.
Start-Process $appExe
Test-Check 'settings app window opens' (Wait-Until { Get-AppWindow })
Test-Check 'settings app starts the host' (Wait-Until { (Get-Processes 'PCCompanion.Host').Count -eq 1 } 15)
Start-Sleep -Seconds 3
Save-Screenshot '01-home-english'
$text = Get-WindowText
Test-Check 'Home shows the feature cards' (($text -contains 'Home') -and ($text -contains 'Input & Triggers') -and ($text -contains 'Hot Corners')) ($text -join ' | ')

# 4. The General page.
Test-Check 'select General' (Select-NavigationItem 'General')
Start-Sleep -Seconds 2
Save-Screenshot '02-general-english'
$text = Get-WindowText
Test-Check 'General shows its settings' (($text -contains 'Language') -and ($text -contains 'App theme') -and ($text -contains 'Start with Windows')) ($text -join ' | ')
Test-Check 'General shows how long logs are kept' ($text -contains 'Logs are kept for 14 days.')
Test-Check 'General shows the version' (@($text | Where-Object { $_ -match '^Version \d+\.\d+\.\d+' }).Count -eq 1)
$adminToggle = Find-Toggle 'Always run as administrator'
Test-Check 'Always run as administrator is unavailable for a per-user install' (($null -ne $adminToggle) -and -not $adminToggle.Current.IsEnabled)
Test-Check 'General says why' ($text -contains 'Available when PC Companion is installed for all users.') ($text -join ' | ')

# 4b. Page layout at window widths from narrow (the pane folds away) to the full screen, on Home,
#     Hot Corners, App Windows and General: a centered column, as Windows Settings lays out its pages.
$pages = @(@{ Name = 'Home'; Title = 'Home' }, @{ Name = 'Hot Corners'; Title = 'Hot Corners' }, @{ Name = 'App Windows'; Title = 'App Windows' }, @{ Name = 'General'; Title = 'General' })
$windowBounds = (Get-AppWindow).Current.BoundingRectangle
foreach ($width in 600, 1000, 1300, $screen.Width) {
    [void] (Set-AppWindowWidth $width)
    foreach ($page in $pages) {
        [void] (Show-Page $page.Name)
        Start-Sleep -Milliseconds 1500
        Test-PageLayout $page.Name $page.Title $width
        if ($width -eq $screen.Width -or $width -eq 600) {
            Save-Screenshot "02b-layout-$($page.Name.ToLowerInvariant() -replace ' ', '-')-$width"
        }
    }
}
Restore-AppWindow $windowBounds
[void] (Select-NavigationItem 'General')

# 4c. The settings window isn't seen empty: it's out of sight (cloaked) when it first shows, until
#     its page is there, and it's cloaked before it's taken down when it closes.
$windowClass = 'WinUIDesktopWin32WindowClass'
$closing = [Win32]::FindWindow($windowClass, 'PC Companion')
$process = Get-Processes 'PCCompanion' | Select-Object -First 1
$cloakedBeforeGone = $false
[void] [Win32]::PostMessage($closing, 0x0010, [UIntPtr]::Zero, [IntPtr]::Zero) # WM_CLOSE
$watch = [System.Diagnostics.Stopwatch]::StartNew()
while ([Win32]::IsWindow($closing) -and $watch.Elapsed.TotalSeconds -lt 10) {
    if ([Win32]::IsCloaked($closing)) { $cloakedBeforeGone = $true }
    [System.Threading.Thread]::Sleep(1)
}
Test-Check 'closing hides the settings window before it''s taken down' $cloakedBeforeGone
[void] (Wait-Until { (Get-Processes 'PCCompanion').Count -eq 0 } 10)

Start-Process $appExe
$firstShownCloaked = $null
$pageWhenShown = $false
$shownAfter = $null
$watch = [System.Diagnostics.Stopwatch]::StartNew()
while ($watch.Elapsed.TotalSeconds -lt 20) {
    $opening = [Win32]::FindWindow($windowClass, 'PC Companion')
    if ($opening -ne [IntPtr]::Zero -and [Win32]::IsWindowVisible($opening)) {
        $cloaked = [Win32]::IsCloaked($opening)
        if ($null -eq $firstShownCloaked) { $firstShownCloaked = $cloaked; $firstShown = $watch.ElapsedMilliseconds }
        if (-not $cloaked) {
            $shownAfter = $watch.ElapsedMilliseconds - $firstShown
            $condition = New-Object System.Windows.Automation.AndCondition(
                (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, 'Home')),
                (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Text)))
            $titles = @([System.Windows.Automation.AutomationElement]::FromHandle($opening).FindAll([System.Windows.Automation.TreeScope]::Descendants, $condition))
            $pageWhenShown = @($titles | Where-Object { $_.Current.BoundingRectangle.Height -ge 30 }).Count -gt 0
            break
        }
    }
    [System.Threading.Thread]::Sleep(1)
}
Test-Check 'the settings window is out of sight when it first shows' ($firstShownCloaked -eq $true) "cloaked when first shown: $firstShownCloaked"
Test-Check 'the settings window comes into sight within a second' (($null -ne $shownAfter) -and ($shownAfter -le 1000)) "after $shownAfter ms"
Test-Check 'the settings window comes into sight with its page' $pageWhenShown
[void] (Wait-Until { Get-AppWindow })
Start-Sleep -Seconds 2

# 5. One settings app per user session.
Start-Process $appExe
Start-Sleep -Seconds 5
Test-Check 'a second launch keeps one settings app' ((Get-Processes 'PCCompanion').Count -eq 1) "processes: $((Get-Processes 'PCCompanion').Count)"

# 6. The tray icon's menu, and selecting the icon.
Test-Check 'tray menu requested' (Send-TrayEvent $WM_CONTEXTMENU $menuX $menuY)
Start-Sleep -Seconds 2
Save-Screenshot '03-tray-menu-english'
[System.Windows.Forms.SendKeys]::SendWait('{ESC}')
Start-Sleep -Seconds 1
Test-Check 'settings app closes' (Close-SettingsApp)
Test-Check 'host keeps running after the settings app closes' ((Get-Processes 'PCCompanion.Host').Count -eq 1)
Test-Check 'tray icon selected' (Send-TrayEvent $NIN_SELECT)
Test-Check 'selecting the tray icon opens the settings app' (Wait-Until { Get-AppWindow } 20)
Start-Process $hostExe -Wait
Test-Check 'a second host launch keeps one host' ((Get-Processes 'PCCompanion.Host').Count -eq 1)

# 6b. Selecting the tray icon brings an open settings window to the front, from minimized and from
#     behind another window. When the user clicks the icon, Explorer lets the host bring a window to
#     the front; a posted message doesn't, so Select-TrayIcon passes the right on as Explorer does.
#     Pressing Alt lets this script do that.
function Select-TrayIcon {
    $hostProcess = Get-Processes 'PCCompanion.Host' | Select-Object -First 1
    if (-not $hostProcess) { return $false }
    [void] [Win32]::Key(0x12, $false)
    [void] [Win32]::Key(0x12, $true)
    $granted = [Win32]::AllowSetForegroundWindow([uint32] $hostProcess.Id)
    $sent = Send-TrayEvent $NIN_SELECT
    return $granted -and $sent
}

$appWindow = [IntPtr] (Get-AppWindow).Current.NativeWindowHandle
[void] [Win32]::ShowWindow($appWindow, 6) # SW_MINIMIZE
Test-Check 'the settings window is minimized' (Wait-Until { [Win32]::IsIconic($appWindow) } 5)
Test-Check 'select the tray icon while the settings window is minimized' (Select-TrayIcon)
Test-Check 'the tray icon brings the minimized settings window to the front' (Wait-Until { -not [Win32]::IsIconic($appWindow) -and [Win32]::GetForegroundWindow() -eq $appWindow } 10) "minimized: $([Win32]::IsIconic($appWindow)); in front: $([Win32]::GetForegroundWindow() -eq $appWindow)"
$other = Start-Process (Join-Path $env:WINDIR 'System32\charmap.exe') -PassThru
[void] (Wait-Until { $other.Refresh(); $other.MainWindowHandle -ne [IntPtr]::Zero } 10)
[void] [Win32]::Key(0x12, $false)
[void] [Win32]::SetForegroundWindow($other.MainWindowHandle)
[void] [Win32]::Key(0x12, $true)
Test-Check 'another window is in front of the settings window' (Wait-Until { [Win32]::GetForegroundWindow() -eq $other.MainWindowHandle } 5)
Test-Check 'select the tray icon while another window is in front' (Select-TrayIcon)
Test-Check 'the tray icon brings the settings window in front of the other window' (Wait-Until { [Win32]::GetForegroundWindow() -eq $appWindow } 10) "in front: $([Win32]::GetForegroundWindow())"
[void] (Close-SettingsApp)
[void] [Win32]::Key(0x12, $false)
[void] [Win32]::SetForegroundWindow($other.MainWindowHandle)
[void] [Win32]::Key(0x12, $true)
Test-Check 'select the tray icon while the settings app is closed and another window is in front' (Select-TrayIcon)
Test-Check 'the tray icon opens the settings window in front of the other window' (Wait-Until { (Get-AppWindow) -and [Win32]::GetForegroundWindow() -eq [IntPtr] (Get-AppWindow).Current.NativeWindowHandle } 15) "in front: $([Win32]::GetForegroundWindow())"

# 6c. Starting the host again, as a user does from the Run dialog, brings the open settings window
#     in front of another window: the new launch passes its right to do that on to the running host.
[void] [Win32]::Key(0x12, $false)
[void] [Win32]::SetForegroundWindow($other.MainWindowHandle)
[void] [Win32]::Key(0x12, $true)
Test-Check 'another window is in front of the settings window again' (Wait-Until { [Win32]::GetForegroundWindow() -eq $other.MainWindowHandle } 5)
[void] [Win32]::Key(0x5B, $false) # Windows key
Send-Key 0x52                     # R
[void] [Win32]::Key(0x5B, $true)
$runCondition = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, 'Run')
Test-Check 'the Run dialog opens' (Wait-Until { $null -ne [System.Windows.Automation.AutomationElement]::RootElement.FindFirst([System.Windows.Automation.TreeScope]::Children, $runCondition) } 10)
Start-Sleep -Milliseconds 500
[System.Windows.Forms.SendKeys]::SendWait($hostExe)
[System.Windows.Forms.SendKeys]::SendWait('{ENTER}')
Test-Check 'starting the host again brings the settings window in front of the other window' (Wait-Until { (Get-AppWindow) -and [Win32]::GetForegroundWindow() -eq [IntPtr] (Get-AppWindow).Current.NativeWindowHandle } 15) "in front: $([Win32]::GetForegroundWindow())"
Test-Check 'starting the host again keeps one host' (Wait-Until { (Get-Processes 'PCCompanion.Host').Count -eq 1 } 10)
Stop-Process -Id $other.Id -Force -ErrorAction SilentlyContinue
Test-Check 'the settings app is open before Exit' ((Get-Processes 'PCCompanion').Count -eq 1)
Test-Check 'tray menu requested for Exit' (Send-TrayEvent $WM_CONTEXTMENU $menuX $menuY)
Test-Check 'choose Exit in the tray menu' (Select-TrayMenuItem 'Exit')
Test-Check 'Exit in the tray menu stops the host' (Wait-Until { (Get-Processes 'PCCompanion.Host').Count -eq 0 } 15)
Test-Check 'Exit in the tray menu closes the settings app' (Wait-Until { (Get-Processes 'PCCompanion').Count -eq 0 } 15)
Start-Process $hostExe
[void] (Wait-Until { (Get-Processes 'PCCompanion.Host').Count -eq 1 } 10)

# 7. Arabic with the dark theme, as the General page saves them; the host follows the change.
[void] (Close-SettingsApp)
Write-Settings '{ "schemaVersion": 1, "general": { "language": "ar", "theme": "Dark", "startWithWindows": false } }'
Start-Sleep -Seconds 2
Start-Process $appExe
[void] (Wait-Until { Get-AppWindow })
Start-Sleep -Seconds 3
Save-Screenshot '04-home-arabic-dark'
$text = Get-WindowText
Test-Check 'Home in Arabic' ($text -contains 'الصفحة الرئيسية') ($text -join ' | ')
Test-Check 'select General in Arabic' (Select-NavigationItem 'عام')
Start-Sleep -Seconds 2
Save-Screenshot '05-general-arabic-dark'
Test-Check 'select Hot Corners in Arabic' (Select-NavigationItem 'الزوايا النشطة')
Start-Sleep -Seconds 2
Save-Screenshot '05b-hot-corners-arabic-dark'
$windowBounds = (Get-AppWindow).Current.BoundingRectangle
foreach ($width in 1000, $screen.Width) {
    [void] (Set-AppWindowWidth $width)
    foreach ($page in 'الصفحة الرئيسية', 'الزوايا النشطة', 'نوافذ التطبيق', 'عام') {
        [void] (Show-Page $page)
        Start-Sleep -Milliseconds 1500
        Test-PageLayout "$page (right to left)" $page $width
    }
}
Restore-AppWindow $windowBounds
Test-Check 'tray menu requested in Arabic' (Send-TrayEvent $WM_CONTEXTMENU $menuX $menuY)
Start-Sleep -Seconds 2
Save-Screenshot '06-tray-menu-arabic'
[System.Windows.Forms.SendKeys]::SendWait('{ESC}')
Start-Sleep -Seconds 1

# 8. Bangla with the light theme.
[void] (Close-SettingsApp)
Write-Settings '{ "schemaVersion": 1, "general": { "language": "bn", "theme": "Light", "startWithWindows": false } }'
Start-Sleep -Seconds 2
Start-Process $appExe
[void] (Wait-Until { Get-AppWindow })
Start-Sleep -Seconds 3
Test-Check 'select General in Bangla' (Select-NavigationItem 'সাধারণ')
Start-Sleep -Seconds 2
Save-Screenshot '07-general-bangla-light'

# 9. Hot Corners: the Home switch turns it on; a corner shows the desktop when the pointer reaches
#    it, not while a mouse button is held, and only with the key it requires.
[void] (Close-SettingsApp)
Write-Settings '{ "schemaVersion": 1, "general": { "theme": "Light" } }'
Start-Sleep -Seconds 2
Start-Process $appExe
[void] (Wait-Until { Get-AppWindow })
Start-Sleep -Seconds 3
Test-Check 'turn on Hot Corners on Home' (Set-Toggle 'Hot Corners' $true)
Start-Sleep -Seconds 2
$saved = (Get-Content (Join-Path $dataFolder 'settings.json') -Raw | ConvertFrom-Json).features.hotCorners
Test-Check 'the Home switch saves Hot Corners as on' ($saved.enabled -eq $true) "saved: $($saved | ConvertTo-Json -Compress)"
Test-Check 'select Hot Corners' (Select-NavigationItem 'Hot Corners')
Start-Sleep -Seconds 2
Save-Screenshot '09-hot-corners-english'
$text = Get-WindowText
Test-Check 'Hot Corners shows its settings' (($text -contains 'Top left') -and ($text -contains 'Activation delay') -and ($text -contains 'Permissions & data')) ($text -join ' | ')
$selected = Get-SelectedItemName 'Top left action'
Test-Check 'the top-left corner shows Task View when first turned on' ($selected -eq 'Task View') "selected: $selected"
[void] (Close-SettingsApp)

# The top-left corner shows the desktop; detailed logging is on for the host's Debug entries.
$detailedUntil = [DateTimeOffset]::Now.AddHours(1).ToString('o')
function Write-HotCornersSettings([bool] $Enabled, [string] $Key) {
    Write-Settings "{ ""schemaVersion"": 1, ""general"": { ""theme"": ""Light"" }, ""diagnostics"": { ""detailedLoggingUntil"": ""$detailedUntil"" }, ""features"": { ""hotCorners"": { ""enabled"": $($Enabled.ToString().ToLowerInvariant()), ""topLeft"": { ""action"": ""ShowDesktop"", ""key"": ""$Key"" } } } }"
    Start-Sleep -Seconds 3
}
$middleX = [int]($screen.Width / 2)
$middleY = [int]($screen.Height / 2)
$ran = 'The "?TopLeft"? corner ran "?ShowDesktop"?\.'
Write-HotCornersSettings $true 'None'
Start-Process $appExe
[void] (Wait-Until { Get-AppWindow })
Start-Sleep -Seconds 3
$appWindow = (Get-Processes 'PCCompanion' | Select-Object -First 1).MainWindowHandle
Test-Check 'the pointer can be moved in the Sandbox' (Move-Pointer 100 100) "at $([System.Windows.Forms.Cursor]::Position); keep the Sandbox window open and the mouse outside it"
[void] (Move-Pointer $middleX $middleY)
[void] (Move-IntoTopLeftCorner)
Test-Check 'the top-left corner shows the desktop' (Wait-Until { [Win32]::IsIconic($appWindow) } 5) (Get-PointerText)
Test-Check 'the host logs which corner ran which action' (Wait-Until { (Get-MatchCount (Get-HostLog) $ran) -eq 1 } 5) (Get-PointerText)
Save-Screenshot '10-hot-corners-show-desktop'

# A mouse button held as the pointer reaches the corner, as when dragging a window there.
[void] (Move-Pointer $middleX $middleY)
[void] [Win32]::LeftButton($true)
[void] (Move-IntoTopLeftCorner)
Start-Sleep -Seconds 1
[void] [Win32]::LeftButton($false)
Test-Check 'a held mouse button skips the corner' (Wait-Until { (Get-HostLog) -match "The `"?TopLeft`"? corner didn't run `"?ShowDesktop`"?: `"?MouseButtonHeld`"?\." } 5) (Get-PointerText)

# The corner requires Ctrl. Holding it must not change the shortcut: Ctrl + Windows + D adds a virtual desktop.
Write-HotCornersSettings $true 'Control'
[void] (Move-Pointer $middleX $middleY)
[void] (Move-IntoTopLeftCorner)
Test-Check 'without the required key the corner is skipped' (Wait-Until { (Get-HostLog) -match "The `"?TopLeft`"? corner didn't run `"?ShowDesktop`"?: `"?KeyNotHeld`"?\." } 5) (Get-PointerText)
$desktops = Get-VirtualDesktopCount
[void] (Move-Pointer $middleX $middleY)
[void] [Win32]::Key(0xA2, $false)
[void] (Move-IntoTopLeftCorner)
Start-Sleep -Seconds 1
[void] [Win32]::Key(0xA2, $true)
Test-Check 'with the required key the corner runs' (Wait-Until { (Get-MatchCount (Get-HostLog) $ran) -eq 2 } 5) (Get-PointerText)
Test-Check 'the held key adds no virtual desktop' ((Get-VirtualDesktopCount) -eq $desktops) "before: $desktops, after: $(Get-VirtualDesktopCount)"

# Keep displays on: while the pointer stays in the corner, powercfg lists the host's display and
# system requests with their reason; once it leaves, neither. Windows Sandbox runs no screen saver,
# so whether it starts is checked by hand.
function Get-HostPowerRequests {
    $requests = @{}
    $section = ''
    foreach ($line in @(& powercfg.exe /requests)) {
        if ($line -match '^([A-Z]+):$') { $section = $Matches[1]; $requests[$section] = '' }
        elseif ($section) { $requests[$section] += "$line`n" }
    }
    $reason = [regex]::Escape('Hot Corners: the pointer is in a corner that keeps the displays on.')
    return @{
        Display = $requests['DISPLAY'] -match "PCCompanion\.Host\.exe\s*\n$reason"
        System = $requests['SYSTEM'] -match "PCCompanion\.Host\.exe\s*\n$reason"
        Any = ($requests['DISPLAY'] + $requests['SYSTEM']) -match 'PCCompanion\.Host\.exe'
        Text = (($requests.Keys | ForEach-Object { "$_ $($requests[$_].Trim() -replace '\s*\n\s*', ' ')" }) -join ' | ')
    }
}
Write-Settings "{ ""schemaVersion"": 1, ""general"": { ""theme"": ""Light"" }, ""diagnostics"": { ""detailedLoggingUntil"": ""$detailedUntil"" }, ""features"": { ""hotCorners"": { ""enabled"": true, ""topLeft"": { ""action"": ""KeepDisplaysOn"", ""key"": ""None"" } } } }"
Start-Sleep -Seconds 3
[void] (Move-Pointer $middleX $middleY)
[void] (Move-IntoTopLeftCorner)
Test-Check 'the top-left corner keeps the displays on' (Wait-Until { (Get-HostLog) -match 'The "?TopLeft"? corner ran "?KeepDisplaysOn"?\.' } 5) (Get-PointerText)
$requests = Get-HostPowerRequests
Test-Check 'powercfg lists the host''s display request with its reason' $requests.Display $requests.Text
Test-Check 'powercfg lists the host''s system request with its reason' $requests.System $requests.Text
[void] (Move-Pointer $middleX $middleY)
Test-Check 'leaving the corner ends Keep displays on' (Wait-Until { (Get-HostLog) -match 'Keep displays on ended: "?PointerLeft"?\.' } 5) (Get-PointerText)
$requests = Get-HostPowerRequests
Test-Check 'powercfg lists no request of the host once the pointer leaves' (-not $requests.Any) $requests.Text

# Turned off, Hot Corners stops reading the pointer.
Write-HotCornersSettings $false 'None'
Test-Check 'turning Hot Corners off stops it' ((Get-HostLog) -match 'Hot Corners stopped\.')
[void] (Move-Pointer $middleX $middleY)
[void] (Move-IntoTopLeftCorner)
Start-Sleep -Seconds 1
Test-Check 'a corner does nothing while Hot Corners is off' ((Get-MatchCount (Get-HostLog) $ran) -eq 2)
[void] (Move-Pointer $middleX $middleY)
Save-Screenshot '11-hot-corners-off'

# Run a program: the page shows the program, and Browse opens the Open dialog. Everything in the
# Sandbox runs as administrator, so the program must not start; the host logs why.
[void] (Close-SettingsApp)
$program = Join-Path $env:WINDIR 'System32\charmap.exe'
Test-Check 'the test program exists' (Test-Path $program) $program
Write-Settings "{ ""schemaVersion"": 1, ""general"": { ""theme"": ""Light"" }, ""diagnostics"": { ""detailedLoggingUntil"": ""$detailedUntil"" }, ""features"": { ""hotCorners"": { ""enabled"": true, ""topLeft"": { ""action"": ""RunProgram"", ""key"": ""None"", ""program"": ""$($program.Replace('\', '\\'))"", ""arguments"": ""/A"" } } } }"
Start-Sleep -Seconds 3
Start-Process $appExe
[void] (Wait-Until { Get-AppWindow })
Start-Sleep -Seconds 3
Test-Check 'select Hot Corners for Run a program' (Select-NavigationItem 'Hot Corners')
Start-Sleep -Seconds 2
$selected = Get-SelectedItemName 'Top left action'
Test-Check 'the top-left corner shows Run a program' ($selected -eq 'Run a program') "selected: $selected"
$shown = Get-BoxText 'Top left program'
Test-Check 'the program box shows the program' ($shown -eq $program) "shown: $shown"
Test-Check 'the arguments box shows the arguments' ((Get-BoxText 'Top left arguments') -eq '/A')
Save-Screenshot '12-hot-corners-run-program'
# A path that doesn't fit shows its end after an ellipsis; the tooltip and the box while editing show all of it.
$programBox = Find-AppElement 'Top left program'
if ($programBox) {
    $bounds = $programBox.Current.BoundingRectangle
    [void] (Move-Pointer ([int]($bounds.X + $bounds.Width / 2)) ([int]($bounds.Y + $bounds.Height / 2)))
}
Test-Check 'the program box shows the whole path in its tooltip' (Wait-Until { Find-AppText $program } 5)
Save-Screenshot '12b-hot-corners-program-tooltip'
if ($programBox) { $programBox.SetFocus() }
Start-Sleep -Seconds 1
Save-Screenshot '12c-hot-corners-program-editing'
[void] (Move-Pointer $middleX $middleY)
$browse = Find-AppElement 'Top left program: Browse'
if ($browse) { $browse.SetFocus(); Start-Sleep -Milliseconds 300; Send-Key 0x20 }
Test-Check 'Browse opens the Open dialog' (Wait-Until { Find-AppDialog 'Choose a program' } 10)
Save-Screenshot '13-hot-corners-open-dialog'
Send-Key 0x1B
Test-Check 'the Open dialog closes' (Wait-Until { -not (Find-AppDialog 'Choose a program') } 10)
Test-Check 'cancelling the Open dialog keeps the program' ((Get-BoxText 'Top left program') -eq $program)
$running = (Get-Processes 'charmap').Count
[void] (Move-Pointer $middleX $middleY)
[void] (Move-IntoTopLeftCorner)
Test-Check 'Run a program logs that it doesn''t run as administrator' (Wait-Until { (Get-HostLog) -match "Run a program doesn't run while PC Companion runs as administrator\." } 5) (Get-PointerText)
Test-Check 'no program starts while PC Companion runs as administrator' ((Get-Processes 'charmap').Count -eq $running) "charmap processes before: $running, after: $((Get-Processes 'charmap').Count)"
[void] (Move-Pointer $middleX $middleY)

# 9b. App Windows: the Home switch turns it on and its page saves its settings; the key above Tab
#     switches between File Explorer windows by its position, also on the German layout after a
#     switch in the same window, and does nothing once App Windows is off.
[void] (Close-SettingsApp)
Write-Settings '{ "schemaVersion": 1, "general": { "theme": "Light" } }'
Start-Sleep -Seconds 2
Start-Process $appExe
[void] (Wait-Until { Get-AppWindow })
Start-Sleep -Seconds 3
function Get-AppWindowsSettings { (Get-Content (Join-Path $dataFolder 'settings.json') -Raw | ConvertFrom-Json).features.appWindows }
Test-Check 'turn on App Windows on Home' (Set-Toggle 'App Windows' $true)
Start-Sleep -Seconds 2
$saved = Get-AppWindowsSettings
Test-Check 'the Home switch saves App Windows as on' ($saved.enabled -eq $true) "saved: $($saved | ConvertTo-Json -Compress)"
Test-Check 'select App Windows' (Select-NavigationItem 'App Windows')
Start-Sleep -Seconds 2
Save-Screenshot '13b-app-windows-english'
$text = Get-WindowText
Test-Check 'App Windows shows its settings' (($text -contains 'Shortcut') -and ($text -contains 'Include minimized windows') -and ($text -contains 'Permissions & data')) ($text -join ' | ')
$selected = Get-ListSelection 'Shortcut'
Test-Check 'the shortcut is Alt with the key above Tab, shown as it types' ($selected -eq 'Alt + `') "selected: $selected"
Test-Check 'choose the Windows logo key' (Select-ListItem 'Shortcut' 'Windows logo key + `')
Start-Sleep -Seconds 1
$saved = Get-AppWindowsSettings
Test-Check 'the page saves the shortcut' ($saved.shortcut -eq 'WindowsAboveTab') "saved: $($saved | ConvertTo-Json -Compress)"
Test-Check 'turn on Include minimized windows' (Set-Toggle 'Include minimized windows' $true)
Start-Sleep -Seconds 1
$saved = Get-AppWindowsSettings
Test-Check 'the page saves Include minimized windows' ($saved.includeMinimized -eq $true) "saved: $($saved | ConvertTo-Json -Compress)"
[void] (Close-SettingsApp)

function Write-AppWindowsSettings([string] $Section) {
    Write-Settings "{ ""schemaVersion"": 1, ""general"": { ""theme"": ""Light"" }, ""diagnostics"": { ""detailedLoggingUntil"": ""$detailedUntil"" }, ""features"": { ""appWindows"": $Section } }"
    Start-Sleep -Seconds 3
}
function Get-ExplorerWindows { @([Win32]::TopLevelWindows() | Where-Object { [Win32]::WindowTitle($_) -like '* - File Explorer' }) }
function Get-ExplorerOrder { (Get-ExplorerWindows | ForEach-Object { [Win32]::WindowTitle($_) -replace ' - File Explorer$', '' }) -join ' | ' }
function Get-FrontTitle { [Win32]::WindowTitle([Win32]::GetForegroundWindow()) -replace ' - File Explorer$', '' }
$LeftAlt = 0x38; $LeftShift = 0x2A; $KeyAboveTab = 0x29
function Send-ScanKeys([int[]] $Modifiers, [int] $Key) {
    foreach ($modifier in $Modifiers) { [void] [Win32]::ScanKey($modifier, $false, $false) }
    [void] [Win32]::ScanKey($Key, $false, $false)
    [void] [Win32]::ScanKey($Key, $true, $false)
    for ($i = $Modifiers.Count - 1; $i -ge 0; $i--) { [void] [Win32]::ScanKey($Modifiers[$i], $true, $false) }
    Start-Sleep -Milliseconds 700
}

Write-AppWindowsSettings '{ "enabled": true }'
foreach ($folder in 'C:\Windows', 'C:\Users', 'C:\Program Files') {
    Start-Process explorer.exe $folder
    Start-Sleep -Seconds 3
}
$explorers = Get-ExplorerWindows
Test-Check 'three File Explorer windows open' ($explorers.Count -eq 3) "$($explorers.Count) open"
$area = New-Object Win32+RECT
[void] [Win32]::GetWindowRect($explorers[0], [ref] $area)
[void] (Move-Pointer ([int](($area.Left + $area.Right) / 2)) ([int](($area.Top + $area.Bottom) / 2)))
[void] [Win32]::LeftButton($true)
[void] [Win32]::LeftButton($false)
Start-Sleep -Seconds 1
$start = Get-ExplorerOrder
$names = $start -split ' \| '
$fronts = @()
for ($i = 0; $i -lt 3; $i++) { Send-ScanKeys @($LeftAlt) $KeyAboveTab; $fronts += Get-FrontTitle }
Test-Check 'Alt + the key above Tab brings each File Explorer window to the front, the one furthest back first' (($fronts -join ' | ') -eq (@($names[2], $names[1], $names[0]) -join ' | ')) "start: $start; fronts: $($fronts -join ', ')"
Test-Check 'after a press for each window, they are in their first order' ((Get-ExplorerOrder) -eq $start) (Get-ExplorerOrder)
Send-ScanKeys @($LeftAlt, $LeftShift) $KeyAboveTab
Test-Check 'Alt + Shift + the key above Tab brings the second window to the front' ((Get-FrontTitle) -eq $names[1]) "front: $(Get-FrontTitle); order: $(Get-ExplorerOrder)"
Send-ScanKeys @($LeftAlt) $KeyAboveTab
Test-Check 'a press forward undoes it' ((Get-ExplorerOrder) -eq $start) (Get-ExplorerOrder)
Test-Check 'the host logs the switches' (Wait-Until { (Get-HostLog) -match 'App Windows went "?Forward"?; the app has 3 windows' } 5)

# The key above Tab is ^ on the German layout. Windows reports no switch within a window, so the
# host finds it with its check once a second; a press forward first lets it see the new layout.
$languages = Get-WinUserLanguageList
$withGerman = New-WinUserLanguageList en-US
$withGerman.Add('de-DE')
Set-WinUserLanguageList $withGerman -Force
Start-Sleep -Seconds 2
Send-ScanKeys @($LeftAlt) $KeyAboveTab
Send-ScanKeys @($LeftAlt) $LeftShift
Start-Sleep -Milliseconds 1500
$before = Get-FrontTitle
Send-ScanKeys @($LeftAlt) $KeyAboveTab
Test-Check 'after switching to German in the same window, the key above Tab still switches' ((Get-FrontTitle) -ne $before) "front: $before -> $(Get-FrontTitle)"
Send-ScanKeys @($LeftAlt) $LeftShift
Set-WinUserLanguageList $languages -Force

Write-AppWindowsSettings '{ "enabled": false }'
$before = Get-FrontTitle
Send-ScanKeys @($LeftAlt) $KeyAboveTab
Test-Check 'turned off, the key above Tab switches no window' ((Get-FrontTitle) -eq $before) "front: $before -> $(Get-FrontTitle)"
foreach ($window in Get-ExplorerWindows) { [void] [Win32]::PostMessage($window, 0x0010, [UIntPtr]::Zero, [IntPtr]::Zero) } # WM_CLOSE
Start-Sleep -Seconds 2

# 10. Detailed logging: the General page switch turns it on for 24 hours; it turns itself off when the time is up.
[void] (Close-SettingsApp)
Write-Settings '{ "schemaVersion": 1, "general": { "theme": "Light" } }'
Start-Sleep -Seconds 2
Start-Process $appExe
[void] (Wait-Until { Get-AppWindow })
Start-Sleep -Seconds 3
[void] (Select-NavigationItem 'General')
Start-Sleep -Seconds 2
Test-Check 'turn on detailed logging' (Set-Toggle 'Detailed logging' $true)
Start-Sleep -Seconds 3
$saved = (Get-Content (Join-Path $dataFolder 'settings.json') -Raw | ConvertFrom-Json).diagnostics.detailedLoggingUntil
$hoursLeft = if ($saved) { ([DateTimeOffset]::Parse([string] $saved) - [DateTimeOffset]::Now).TotalHours } else { 0 }
Test-Check 'the switch saves an end time 24 hours ahead' (($hoursLeft -gt 23.9) -and ($hoursLeft -le 24)) "saved: $saved"
$text = Get-WindowText
Test-Check 'General shows when detailed logging turns off' (@($text | Where-Object { $_ -like 'On until *' }).Count -ge 1) ($text -join ' | ')
Save-Screenshot '08-general-detailed-logging'
Test-Check 'tray menu requested with detailed logging on' (Send-TrayEvent $WM_CONTEXTMENU $menuX $menuY)
Start-Sleep -Seconds 2
[System.Windows.Forms.SendKeys]::SendWait('{ESC}')
Start-Sleep -Seconds 1
[void] (Close-SettingsApp)
# As if the 24 hours were nearly over.
$soon = [DateTimeOffset]::Now.AddSeconds(20).ToString('o')
Write-Settings "{ ""schemaVersion"": 1, ""general"": { ""theme"": ""Light"" }, ""diagnostics"": { ""detailedLoggingUntil"": ""$soon"" } }"
Start-Sleep -Seconds 25
Test-Check 'tray menu requested after detailed logging ended' (Send-TrayEvent $WM_CONTEXTMENU $menuX $menuY)
Start-Sleep -Seconds 2
[System.Windows.Forms.SendKeys]::SendWait('{ESC}')
Start-Sleep -Seconds 1

# 11. Start with Windows on.
Write-Settings '{ "schemaVersion": 1, "general": { "language": "bn", "theme": "Light", "startWithWindows": true } }'
Start-Sleep -Seconds 2
[void] (Close-SettingsApp)
$process = Start-Process $hostExe -ArgumentList '--startup' -PassThru
Test-Check 'host --startup leaves a running host alone' ($process.WaitForExit(10000))
Start-Sleep -Seconds 3
Test-Check 'host --startup opens no settings window' ((Get-Processes 'PCCompanion').Count -eq 0)
Stop-All
$process = Start-Process $hostExe -ArgumentList '--startup' -PassThru
Start-Sleep -Seconds 5
Test-Check 'host --startup keeps running when Start with Windows is on' (-not $process.HasExited)

# 12. Logs.
Stop-All
Copy-Item -Path (Join-Path $logs '*') -Destination $results -ErrorAction SilentlyContinue
$logText = (Get-ChildItem $logs -File -ErrorAction SilentlyContinue | ForEach-Object { Get-Content $_.FullName -Raw }) -join "`n"
Test-Check 'settings app and host logs written' ((Test-Path (Join-Path $logs 'app-*.log')) -and (Test-Path (Join-Path $logs 'host-*.log')))
Test-Check 'logs have no errors' (-not ($logText -match '\[(ERR|FTL)\]'))
Test-Check 'logs don''t show the user name' (-not ($logText -match [regex]::Escape($env:USERNAME)))
Test-Check 'host loaded the changed settings' ($logText -match 'The settings changed and were loaded again')
Test-Check 'the settings app logged that Exit closed it' ($logText -match 'Asked to close, as Exit was chosen in the tray menu')

$appLog = (Get-ChildItem $logs -Filter 'app-*.log' -ErrorAction SilentlyContinue | ForEach-Object { Get-Content $_.FullName -Raw }) -join "`n"
$hostLines = @(Get-ChildItem $logs -Filter 'host-*.log' -ErrorAction SilentlyContinue | ForEach-Object { Get-Content $_.FullName })
$turnedOff = @(0..($hostLines.Count - 1) | Where-Object { $hostLines[$_] -match 'Detailed logging is off\.' })
$debugLines = @(0..($hostLines.Count - 1) | Where-Object { $hostLines[$_] -match '\[DBG\]' })
Test-Check 'settings app turned detailed logging on' ($appLog -match 'Detailed logging is on until')
Test-Check 'host turned detailed logging on' (@($hostLines | Where-Object { $_ -match 'Detailed logging is on until' }).Count -ge 1)
Test-Check 'host wrote Debug entries while detailed logging was on' (@($hostLines | Where-Object { $_ -match '\[DBG\] PCCompanion\.Host\.TrayController: Opened the tray menu' }).Count -ge 1)
Test-Check 'detailed logging turned itself off in the host' ($turnedOff.Count -ge 1)
Test-Check 'host wrote no Debug entries after detailed logging turned off' (($turnedOff.Count -ge 1) -and (@($debugLines | Where-Object { $_ -gt $turnedOff[-1] }).Count -eq 0))

# 12a. The host's exit argument, which setup runs before Windows Installer looks for files in use,
#      closes the settings app and the host and returns once they've ended.
function Get-InstalledProducts {
    @(Get-ItemProperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\*', 'HKLM:\Software\Microsoft\Windows\CurrentVersion\Uninstall\*' -ErrorAction SilentlyContinue |
        Where-Object { $_.DisplayName -eq 'PC Companion' })
}
function Get-InstalledVersions { @(Get-InstalledProducts | ForEach-Object { $_.DisplayVersion }) }
function Open-PCCompanion {
    Start-Process $appExe
    return (Wait-Until { ((Get-Processes 'PCCompanion').Count -eq 1) -and ((Get-Processes 'PCCompanion.Host').Count -eq 1) } 30)
}
# Files setup couldn't remove or replace while they were in use, left for the next restart.
function Get-PendingRenames {
    @((Get-ItemProperty 'HKLM:\SYSTEM\CurrentControlSet\Control\Session Manager' -Name PendingFileRenameOperations -ErrorAction SilentlyContinue).PendingFileRenameOperations | Where-Object { $_ -like '*PCCompanion*' })
}
function Get-RunningApp { @(Get-Processes 'PCCompanion') + @(Get-Processes 'PCCompanion.Host') }
# Selects OK in a window of Windows Installer, such as its prompt to close programs.
function Confirm-InstallerPrompt {
    $msiexec = @(Get-Processes 'msiexec' | ForEach-Object { $_.Id })
    $ok = [System.Windows.Automation.AndCondition]::new(
        [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Button),
        [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::NameProperty, 'OK'))
    foreach ($window in [System.Windows.Automation.AutomationElement]::RootElement.FindAll([System.Windows.Automation.TreeScope]::Children, [System.Windows.Automation.Condition]::TrueCondition)) {
        if ($msiexec -notcontains $window.Current.ProcessId) { continue }
        $button = $window.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $ok)
        $pattern = $null
        if ($button -and $button.TryGetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern, [ref] $pattern)) {
            $pattern.Invoke()
            return $true
        }
    }
    return $false
}
# Had Windows Installer found them in use, its log would show Restart Manager closing them; with
# setup pages, it would ask first.
function Test-ClosedBySetup([string] $Name, [int] $PendingBefore, [string] $Log) {
    $running = Get-RunningApp
    Test-Check "$Name closes the settings app and the host" ($running.Count -eq 0) "running: $(($running | ForEach-Object { $_.ProcessName }) -join ', ')"
    $restartManager = @(Select-String -Path $Log -Pattern 'RESTART MANAGER: (Will attempt to shut down|Successfully shut down)' -ErrorAction SilentlyContinue | ForEach-Object { $_.Line -replace '^.*RESTART MANAGER: ', '' })
    Test-Check "$Name closes them before Windows Installer looks for files in use" ($restartManager.Count -eq 0) "Restart Manager: $($restartManager -join ' | ')"
    $pending = @(Get-PendingRenames)
    Test-Check "$Name leaves no files for a restart" ($pending.Count -le $PendingBefore) "pending: $($pending -join ', ')"
}
Test-Check 'the settings app and the host run before the exit argument' (Open-PCCompanion)
$exitTime = [Diagnostics.Stopwatch]::StartNew()
$code = (Start-Process $hostExe -ArgumentList '--exit' -Wait -PassThru).ExitCode
$running = Get-RunningApp
Test-Check 'the exit argument closes the settings app and the host before it returns' ($running.Count -eq 0) "running: $(($running | ForEach-Object { $_.ProcessName }) -join ', '); returned after $($exitTime.ElapsedMilliseconds) ms"
Test-Check 'the exit argument returns 0' ($code -eq 0) "exit code $code"
$exitTime.Restart()
[void] (Start-Process $hostExe -ArgumentList '--exit' -Wait -PassThru)
$running = Get-RunningApp
Test-Check 'with nothing running, the exit argument starts nothing and returns without waiting' (($running.Count -eq 0) -and ($exitTime.ElapsedMilliseconds -lt 4000)) "running: $(($running | ForEach-Object { $_.ProcessName }) -join ', '); returned after $($exitTime.ElapsedMilliseconds) ms"

# 12b. Another build of the same version replaces the installed one, as an upgrade does, and keeps
#      the settings and logs, rather than installing next to it. Before this, the upgrade and the
#      uninstall, the settings app and the host run: setup closes them first, so it needs no restart.
Test-Check 'the settings app and the host run before installing another build' (Open-PCCompanion)
$pendingBefore = @(Get-PendingRenames).Count
$settingsBefore = Get-Content (Join-Path $dataFolder 'settings.json') -Raw
$logsBefore = @(Get-ChildItem $logs -File).Count
$productsBefore = @(Get-InstalledProducts | ForEach-Object { $_.PSChildName })
$code = (Start-Process msiexec.exe -ArgumentList @('/i', "`"$sameVersionMsi`"", '/qn', 'ALLUSERS=2', 'MSIINSTALLPERUSER=1', '/l*v', "`"$results\same-version.log`"") -Wait -PassThru).ExitCode
Test-Check 'install another build of the same version' ($code -eq 0) "exit code $code"
Test-ClosedBySetup 'installing another build' $pendingBefore "$results\same-version.log"
$productsAfter = @(Get-InstalledProducts | ForEach-Object { $_.PSChildName })
Test-Check 'it replaces the installed build, leaving one entry in Settings > Apps' (($productsAfter.Count -eq 1) -and ($productsAfter[0] -ne $productsBefore[0])) "before: $($productsBefore -join ', '); after: $($productsAfter -join ', ')"
Test-Check 'it keeps the settings' ((Get-Content (Join-Path $dataFolder 'settings.json') -Raw -ErrorAction SilentlyContinue) -eq $settingsBefore)
Test-Check 'it keeps the logs' (@(Get-ChildItem $logs -File -ErrorAction SilentlyContinue).Count -ge $logsBefore) "before: $logsBefore; after: $(@(Get-ChildItem $logs -File -ErrorAction SilentlyContinue).Count)"

# 12c. An upgrade to the next version keeps the settings and logs.
Test-Check 'the settings app and the host run before the upgrade' (Open-PCCompanion)
$pendingBefore = @(Get-PendingRenames).Count
$settingsBefore = Get-Content (Join-Path $dataFolder 'settings.json') -Raw
$logsBefore = @(Get-ChildItem $logs -File).Count
$versionBefore = @(Get-InstalledVersions)
$code = (Start-Process msiexec.exe -ArgumentList @('/i', "`"$upgradeMsi`"", '/qn', 'ALLUSERS=2', 'MSIINSTALLPERUSER=1', '/l*v', "`"$results\upgrade.log`"") -Wait -PassThru).ExitCode
Test-Check 'upgrade to the next version' ($code -eq 0) "exit code $code"
Test-ClosedBySetup 'the upgrade' $pendingBefore "$results\upgrade.log"
$versionAfter = @(Get-InstalledVersions)
Test-Check 'the upgrade replaces the installed version' (($versionAfter.Count -eq 1) -and ($versionAfter[0] -ne $versionBefore[0])) "before: $($versionBefore -join ', '); after: $($versionAfter -join ', ')"
Test-Check 'the upgrade keeps the settings' ((Get-Content (Join-Path $dataFolder 'settings.json') -Raw -ErrorAction SilentlyContinue) -eq $settingsBefore)
Test-Check 'the upgrade keeps the logs' (@(Get-ChildItem $logs -File -ErrorAction SilentlyContinue).Count -ge $logsBefore) "before: $logsBefore; after: $(@(Get-ChildItem $logs -File -ErrorAction SilentlyContinue).Count)"

# 13. Uninstall, with the reduced setup pages that Settings > Apps shows, where Windows Installer
#     would ask to close PC Companion if it found it running. It removes the data folder.
Test-Check 'the settings app and the host run before uninstalling' (Open-PCCompanion)
$pendingBefore = @(Get-PendingRenames).Count
$savedSettings = Get-Content (Join-Path $dataFolder 'settings.json') -Raw
$uninstall = Start-Process msiexec.exe -ArgumentList @('/x', "`"$upgradeMsi`"", '/qb', '/l*v', "`"$results\uninstall.log`"") -PassThru
[void] $uninstall.Handle
$finished = $uninstall.WaitForExit(120000)
Test-Check 'uninstalling with reduced pages asks nothing' $finished
if (-not $finished) {
    # Most likely Windows Installer asks to close PC Companion: OK, its default, closes it.
    Save-Screenshot 'uninstall-asks'
    Test-Check 'OK on the prompt that keeps the uninstall waiting' (Confirm-InstallerPrompt)
    [void] $uninstall.WaitForExit(120000)
}
$code = $uninstall.ExitCode
Test-Check 'uninstall' ($code -eq 0) "exit code $code"
Test-ClosedBySetup 'uninstalling' $pendingBefore "$results\uninstall.log"
Test-Check 'startup entry removed' (-not (Get-ItemProperty $runKey -Name PCCompanion -ErrorAction SilentlyContinue))
Test-Check 'install folder removed' (-not (Test-Path $appExe))
Test-Check 'uninstalling removes the settings and logs' (-not (Test-Path $dataFolder)) "left: $((Get-ChildItem $dataFolder -Force -Recurse -Name -ErrorAction SilentlyContinue) -join ', ')"

# 13b. Uninstalling with KEEPSETTINGS=1, as Keep my settings does, keeps settings.json and removes
#      the rest, the logs included.
$code = (Start-Process msiexec.exe -ArgumentList @('/i', "`"$msi`"", '/qn', 'ALLUSERS=2', 'MSIINSTALLPERUSER=1', '/l*v', "`"$results\install-again.log`"") -Wait -PassThru).ExitCode
Test-Check 'install for the current user again' ($code -eq 0) "exit code $code"
Write-Settings $savedSettings
Test-Check 'the settings app and the host run before uninstalling with KEEPSETTINGS=1' (Open-PCCompanion)
Test-Check 'the settings app and the host write their logs' (Wait-Until { @(Get-ChildItem $logs -File -ErrorAction SilentlyContinue).Count -ge 2 } 15)
$settingsBefore = Get-Content (Join-Path $dataFolder 'settings.json') -Raw
$code = (Start-Process msiexec.exe -ArgumentList @('/x', "`"$msi`"", '/qn', 'KEEPSETTINGS=1', '/l*v', "`"$results\uninstall-keep-settings.log`"") -Wait -PassThru).ExitCode
Test-Check 'uninstall with KEEPSETTINGS=1' ($code -eq 0) "exit code $code"
$left = @(Get-ChildItem $dataFolder -Force -Recurse -Name -ErrorAction SilentlyContinue)
Test-Check 'KEEPSETTINGS=1 keeps the settings and removes the logs' (($left.Count -eq 1) -and ($left[0] -eq 'settings.json') -and ((Get-Content (Join-Path $dataFolder 'settings.json') -Raw -ErrorAction SilentlyContinue) -eq $settingsBefore)) "left: $($left -join ', ')"
Remove-Item $dataFolder -Recurse -Force -ErrorAction SilentlyContinue

# 14. Always run as administrator, in an all-users install. UAC is off in the Sandbox, so every
#     program already runs as administrator: the hand-off to the task and the protected log
#     folder in use are checked by hand on a PC with UAC on.
Stop-All
$code = (Start-Process msiexec.exe -ArgumentList @('/i', "`"$msi`"", '/qn', 'ALLUSERS=1', '/l*v', "`"$results\install-all-users.log`"") -Wait -PassThru).ExitCode
Test-Check 'install for all users' ($code -eq 0) "exit code $code"
$allUsersFolder = Join-Path $env:ProgramW6432 'PCCompanion'
$protectedLogs = Join-Path $env:ProgramData 'PCCompanion\Logs'
$acl = Get-Acl $protectedLogs -ErrorAction SilentlyContinue
$writers = @($acl.Access | Where-Object { $_.AccessControlType -eq 'Allow' -and ($_.FileSystemRights -band [System.Security.AccessControl.FileSystemRights]::WriteData) } | ForEach-Object { $_.IdentityReference.Translate([System.Security.Principal.SecurityIdentifier]).Value })
Test-Check 'the protected logs folder is created' ($null -ne $acl)
Test-Check 'only SYSTEM and Administrators can change it' (($writers.Count -gt 0) -and -not ($writers | Where-Object { $_ -notin 'S-1-5-18', 'S-1-5-32-544' })) "writers: $($writers -join ', ')"
$sid = [System.Security.Principal.WindowsIdentity]::GetCurrent().User.Value
Write-Settings '{ "schemaVersion": 1, "general": { "theme": "Light" } }'
Start-Process (Join-Path $allUsersFolder 'PCCompanion.exe')
[void] (Wait-Until { Get-AppWindow })
Start-Sleep -Seconds 3
[void] (Select-NavigationItem 'General')
Start-Sleep -Seconds 2
$adminToggle = Find-Toggle 'Always run as administrator'
Test-Check 'Always run as administrator is available for an all-users install' (($null -ne $adminToggle) -and $adminToggle.Current.IsEnabled)
Test-Check 'turn on Always run as administrator' (Set-Toggle 'Always run as administrator' $true)
Test-Check 'turning it on creates the user''s two tasks' (Wait-Until { (Get-StartupTasks).Count -eq 2 } 30) "tasks: $((Get-StartupTasks) -join ', ')"
Save-Screenshot '14-general-run-as-administrator'
$saved = (Get-Content (Join-Path $dataFolder 'settings.json') -Raw | ConvertFrom-Json).general.alwaysRunAsAdministrator
Test-Check 'the setting is saved' ($saved -eq $true)
# The helper logs to the user's protected logs folder, which the settings app can't get from
# it directly; a failure says why, with Task Scheduler's own message.
$helperLogs = Join-Path $env:ProgramData "PCCompanion\Logs\$sid"
$helperLog = (Get-ChildItem $helperLogs -Filter 'elevated-*.log' -ErrorAction SilentlyContinue | ForEach-Object { Get-Content $_.FullName -Raw }) -join "`n"
Test-Check 'the elevated helper logs that it created the tasks' ($helperLog -match 'Created the Always run as administrator tasks') "folder: $((Get-ChildItem $helperLogs -Name -ErrorAction SilentlyContinue) -join ', ')"
$missingSid = 'S-1-5-21-1111111111-2222222222-3333333333-1001'
$helper = Join-Path $allUsersFolder 'PCCompanion.Elevated.exe'
$code = (Start-Process $helper -ArgumentList @('create-startup-tasks', $missingSid) -Wait -PassThru).ExitCode
$failureLog = (Get-ChildItem (Join-Path $env:ProgramData "PCCompanion\Logs\$missingSid") -Filter 'elevated-*.log' -ErrorAction SilentlyContinue | ForEach-Object { Get-Content $_.FullName -Raw }) -join "`n"
Test-Check 'the elevated helper fails for a user that doesn''t exist' ($code -eq 1) "exit code $code"
Test-Check 'the elevated helper logs why it failed' (($failureLog -match '\[ERR\].*create-startup-tasks failed') -and ($failureLog -match 'schtasks')) ($failureLog -replace '\s+', ' ')
[void] (Start-Process $helper -ArgumentList @('delete-startup-tasks', $missingSid) -Wait -PassThru)
$signInTask = [xml] ((schtasks.exe /Query /TN "\PCCompanion\Host at sign-in $sid" /XML) -join "`n")
$exec = $signInTask.Task.Actions.Exec
$triggerUser = [string] $signInTask.Task.Triggers.LogonTrigger.UserId
$runLevel = [string] $signInTask.Task.Principals.Principal.RunLevel
# Task Scheduler may show the user as an account name rather than a SID.
$triggerSid = try { ([System.Security.Principal.NTAccount] $triggerUser).Translate([System.Security.Principal.SecurityIdentifier]).Value } catch { $triggerUser }
Test-Check 'the sign-in task starts the host from Program Files at sign-in with highest rights' (
    ($exec.Command -eq "`"$allUsersFolder\PCCompanion.Host.exe`"") -and ($exec.Arguments -eq '--startup') -and ($triggerSid -eq $sid) -and ($runLevel -eq 'HighestAvailable')) "$($exec.Command) $($exec.Arguments); user: $triggerUser; run level: $runLevel"
Test-Check 'the host restarts after the change' (Wait-Until { (Get-Processes 'PCCompanion.Host').Count -eq 1 } 20)
# With UAC off, the host keeps its logs in the user's logs folder, so there are no host logs to open.
[void] (Wait-Until { (Find-Toggle 'Always run as administrator').Current.IsEnabled } 20)
Test-Check 'with UAC off, the General page doesn''t offer to open host logs' (-not (Find-AppElement 'Open host logs')) ((Get-WindowText) -join ' | ')
[void] (Close-SettingsApp)
Get-Processes 'PCCompanion.Host' | ForEach-Object { Stop-Process -Id $_.Id -Force }
[void] (Wait-Until { (Get-Processes 'PCCompanion.Host').Count -eq 0 } 10)
schtasks.exe /Run /TN "\PCCompanion\Host $sid" | Out-Null
Test-Check 'the on-request task starts the host' (Wait-Until { @(Get-Process -Name 'PCCompanion.Host' -ErrorAction SilentlyContinue | Where-Object { $_.Path -like "$allUsersFolder\*" }).Count -eq 1 } 20)
Start-Process (Join-Path $allUsersFolder 'PCCompanion.exe')
[void] (Wait-Until { Get-AppWindow })
Start-Sleep -Seconds 3
[void] (Select-NavigationItem 'General')
Start-Sleep -Seconds 2
# The switch stays disabled until a change, the host's restart included, has finished.
[void] (Wait-Until { (Find-Toggle 'Always run as administrator').Current.IsEnabled } 20)
Test-Check 'turn off Always run as administrator' (Set-Toggle 'Always run as administrator' $false)
Test-Check 'turning it off deletes the tasks' (Wait-Until { (Get-StartupTasks).Count -eq 0 } 30) "tasks: $((Get-StartupTasks) -join ', ')"
[void] (Wait-Until { (Find-Toggle 'Always run as administrator').Current.IsEnabled } 20)
Test-Check 'turn it on again' ((Set-Toggle 'Always run as administrator' $true) -and (Wait-Until { (Get-StartupTasks).Count -eq 2 } 30))
Stop-All
$allUsersLogs = New-Item -ItemType Directory -Force -Path (Join-Path $results 'logs-all-users')
Copy-Item -Path (Join-Path $logs '*') -Destination $allUsersLogs -ErrorAction SilentlyContinue
$logText = (Get-ChildItem $logs -File -ErrorAction SilentlyContinue | ForEach-Object { Get-Content $_.FullName -Raw }) -join "`n"
Test-Check 'Always run as administrator logs no errors' (-not ($logText -match '\[(ERR|FTL)\]'))
# An uninstall for all users runs as administrator, while any program running as the user can
# change the user's data folder: the host removes that folder as the user, and removes links planted
# in it without what they lead to.
$machineLog = Join-Path $env:ProgramData 'PCCompanion\Logs\host-test.log'
Set-Content -Path $machineLog -Value 'test'
Test-Check 'the user''s data folder is there before uninstalling for all users' (Test-Path (Join-Path $dataFolder 'settings.json'))
$outside = (New-Item -ItemType Directory -Force -Path (Join-Path $env:TEMP 'PCCompanion.Outside')).FullName
Set-Content -Path (Join-Path $outside 'behind-folder-link.txt') -Value 'keep'
Set-Content -Path (Join-Path $outside 'behind-file-link.txt') -Value 'keep'
New-Item -ItemType Junction -Path (Join-Path $dataFolder 'FolderLink') -Target $outside | Out-Null
New-Item -ItemType SymbolicLink -Path (Join-Path $dataFolder 'FileLink.txt') -Target (Join-Path $outside 'behind-file-link.txt') | Out-Null
Test-Check 'links to other files are planted in the data folder' ((Test-Path (Join-Path $dataFolder 'FolderLink\behind-folder-link.txt')) -and (Test-Path (Join-Path $dataFolder 'FileLink.txt')))
$code = (Start-Process msiexec.exe -ArgumentList @('/x', "`"$msi`"", '/qn', '/l*v', "`"$results\uninstall-all-users.log`"") -Wait -PassThru).ExitCode
Test-Check 'uninstall for all users' ($code -eq 0) "exit code $code"
$left = @(Get-ChildItem $dataFolder -Force -Name -ErrorAction SilentlyContinue)
Test-Check 'uninstalling for all users removes the user''s settings and logs' ($left.Count -eq 0) "left: $($left -join ', ')"
$kept = @(Get-ChildItem $outside -Force -Name -ErrorAction SilentlyContinue)
Test-Check 'uninstalling doesn''t remove files that links in the data folder lead to' (($kept -contains 'behind-folder-link.txt') -and ($kept -contains 'behind-file-link.txt')) "left outside: $($kept -join ', ')"
Test-Check 'uninstalling deletes the tasks' ((Get-StartupTasks).Count -eq 0) "tasks: $((Get-StartupTasks) -join ', ')"
Test-Check 'the all-users folder is removed' (-not (Test-Path (Join-Path $allUsersFolder 'PCCompanion.Host.exe')))
Test-Check 'uninstalling for all users removes the protected logs folder, with its logs' (-not (Test-Path (Join-Path $env:ProgramData 'PCCompanion')))

Write-Report "finished $(Get-Date -Format o)"
Set-Content -Path (Join-Path $results 'done.txt') -Value 'done'
shutdown.exe /s /t 5
