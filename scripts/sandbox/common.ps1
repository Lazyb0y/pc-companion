<#
.SYNOPSIS
Helpers that the Windows Sandbox checks share. run-checks.ps1 dot-sources it before the groups.
#>

$results = 'C:\Test\results'
$report = Join-Path $results 'report.txt'

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

    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr window, System.Text.StringBuilder name, int length);
    [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr window, uint command);

    // The window that owns another, such as a tooltip's.
    public static IntPtr Owner(IntPtr window) { return GetWindow(window, 4); }

    public static string WindowClass(IntPtr window) {
        var name = new System.Text.StringBuilder(256);
        GetClassName(window, name, name.Capacity);
        return name.ToString();
    }

    // Whether a window is in front of another (GW_HWNDPREV leads toward the front).
    public static bool IsInFrontOf(IntPtr window, IntPtr other) {
        for (var current = GetWindow(other, 3); current != IntPtr.Zero; current = GetWindow(current, 3)) {
            if (current == window) return true;
        }
        return false;
    }

    // The visible top-level windows, front to back.
    public static IntPtr[] TopLevelWindows() {
        var windows = new System.Collections.Generic.List<IntPtr>();
        EnumWindows(delegate(IntPtr window, IntPtr parameter) { if (IsWindowVisible(window)) windows.Add(window); return true; }, IntPtr.Zero);
        return windows.ToArray();
    }
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern void mouse_event(uint flags, uint dx, uint dy, uint data, UIntPtr extraInfo);
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

# Checks a feature page's header as it's drawn: the icon, the title's capital letters and the
# switch share one middle line. UI Automation doesn't see the icon and gives the title's box rather
# than its letters, so the icon's middle and that of the title's first letter, a capital, come from
# the screen's pixels; the switch's middle is that of its track. For a title on one line, in a page
# laid out left to right. $When says what's different, such as the text size. Returns the first
# letter's height in pixels, or 0 when it isn't found.
function Test-HeaderAlignment([string] $Name, [string] $Title, [string] $When = '') {
    $check = "${Name}${When}: the header's icon and switch are centered on the title"
    $layout = Get-PageLayout $Title
    $toggle = Find-Toggle $Title
    if (-not $layout -or -not $toggle) {
        Test-Check $check $false 'the page''s title or switch isn''t shown'
        return 0
    }

    # The icon is in the 16 pixels left of the title and at most 80 wide; the header's rows reach
    # past the title's box when the icon or the switch is taller.
    $box = $layout.Title
    $switch = $toggle.Current.BoundingRectangle
    $left = [int] $box.Left - 100
    $top = [int] [math]::Min($box.Top, $switch.Top) - 24
    $bitmap = New-Object System.Drawing.Bitmap 160, ([int] [math]::Max($box.Bottom, $switch.Bottom) + 24 - $top)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.CopyFromScreen($left, $top, 0, 0, $bitmap.Size)
    # Ink is what differs clearly from the background, taken from the gap between the icon and the title.
    $background = $bitmap.GetPixel(92, [int] ($box.Top + $box.Height / 2) - $top).GetBrightness()
    $isInk = { param($x, $y) [math]::Abs($bitmap.GetPixel($x, $y).GetBrightness() - $background) -gt 0.3 }
    $rowsWithInk = {
        param($fromX, $toX, $fromY, $toY)
        @(for ($y = $fromY; $y -le $toY; $y++) {
            for ($x = $fromX; $x -le $toX; $x++) { if (& $isInk $x $y) { $y; break } }
        })
    }
    $iconRows = & $rowsWithInk 0 90 0 ($bitmap.Height - 1)
    # The title's first letter: the ink joined to the leftmost ink in the title's box, which keeps
    # out the description below it. Joined pixels rather than columns, since the next letter can
    # reach under a capital, as y does under S.
    $titleLeft = [int] $box.Left - $left - 2
    $titleTop = [int] $box.Top - $top
    $titleBottom = [int] $box.Bottom - $top - 1
    $start = $null
    for ($x = $titleLeft; $x -lt $bitmap.Width -and -not $start; $x++) {
        for ($y = $titleTop; $y -le $titleBottom; $y++) { if (& $isInk $x $y) { $start = @($x, $y); break } }
    }
    $letterRows = @()
    if ($start) {
        $joined = @{}
        $pending = New-Object System.Collections.Stack
        $pending.Push($start)
        while ($pending.Count -gt 0) {
            $x, $y = $pending.Pop()
            if ($joined.ContainsKey("$x,$y") -or $x -lt $titleLeft -or $x -ge $bitmap.Width -or $y -lt $titleTop -or $y -gt $titleBottom -or -not (& $isInk $x $y)) { continue }
            $joined["$x,$y"] = $y
            foreach ($dx in -1, 0, 1) { foreach ($dy in -1, 0, 1) { $pending.Push(@(($x + $dx), ($y + $dy))) } }
        }
        $letterRows = @($joined.Values | Sort-Object)
    }
    $graphics.Dispose()
    $bitmap.Dispose()
    if ($iconRows.Count -eq 0 -or $letterRows.Count -eq 0) {
        Test-Check $check $false "icon rows: $($iconRows.Count), first letter rows: $($letterRows.Count)"
        return 0
    }

    $iconMiddle = $top + ($iconRows[0] + $iconRows[-1] + 1) / 2
    $letterMiddle = $top + ($letterRows[0] + $letterRows[-1] + 1) / 2
    $letterHeight = $letterRows[-1] - $letterRows[0] + 1
    $switchMiddle = $switch.Top + $switch.Height / 2
    $passed = ([math]::Abs($iconMiddle - $letterMiddle) -le 1.5) -and ([math]::Abs($switchMiddle - $letterMiddle) -le 1.5)
    Test-Check $check $passed "middles: icon $iconMiddle, title's first letter $letterMiddle, switch $switchMiddle; first letter $letterHeight pixels high"
    return $letterHeight
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

# The names of the settings window's elements. While a page changes, UI Automation can miss the
# window, find no element or fail to read one, so a failed or empty read is tried again, up to three
# times; the window always has some text, such as its navigation pane. The report notes each failed
# read.
function Get-WindowText {
    for ($attempt = 1; $attempt -le 3; $attempt++) {
        if ($attempt -gt 1) { Start-Sleep -Milliseconds 500 }
        try {
            $window = Get-AppWindow
            if ($window) {
                $all = $window.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
                $text = @($all | ForEach-Object { $_.Current.Name } | Where-Object { $_ })
                if ($text.Count -gt 0) { return $text }
                $problem = 'the window had no text'
            }
            else {
                $problem = 'no settings window was found'
            }
        }
        catch {
            $problem = "$($_.Exception.GetType().Name): $($_.Exception.Message)"
        }
        Write-Report "[NOTE] Reading the settings window's text failed, attempt $attempt of 3: $problem"
    }
    return @()
}

# Sends what Explorer sends when the tray icon is used: the host's window gets WM_APP + 1 with the
# event in the low word of lParam and the icon ID (1) in the high word. The icon itself is usually
# hidden in the overflow area of a new Windows 11 profile. A meter's icon has its own ID and the
# window of the meters' thread, PCCompanion.SystemMeters.
function Send-TrayEvent([int] $TrayEvent, [int] $X = 0, [int] $Y = 0, [int] $IconId = 1, [string] $WindowClass = 'PCCompanion.Host') {
    $window = [Win32]::FindWindow($WindowClass, $null)
    if ($window -eq [IntPtr]::Zero) { return $false }
    return [Win32]::PostMessage($window, 0x8001, [UIntPtr] [uint32] (($X -band 0xFFFF) -bor ($Y -shl 16)), [IntPtr] ($TrayEvent -bor ($IconId -shl 16)))
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

# The average brightness of the open tray menu, from 0 (black) to 255 (white), or -1 when no menu
# opened within 10 seconds.
function Get-TrayMenuBrightness {
    $menuCondition = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ClassNameProperty, '#32768')
    $script:menu = $null
    $found = Wait-Until {
        $script:menu = [System.Windows.Automation.AutomationElement]::RootElement.FindFirst([System.Windows.Automation.TreeScope]::Children, $menuCondition)
        $null -ne $script:menu
    } 10
    if (-not $found) { return -1 }
    # Menus fade in.
    Start-Sleep -Milliseconds 500
    $rectangle = $script:menu.Current.BoundingRectangle
    if ($rectangle.Width -lt 1 -or $rectangle.Height -lt 1) { return -1 }

    $bitmap = New-Object System.Drawing.Bitmap ([int] $rectangle.Width), ([int] $rectangle.Height)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.CopyFromScreen([int] $rectangle.X, [int] $rectangle.Y, 0, 0, $bitmap.Size)
    $total = 0.0
    $count = 0
    for ($x = 0; $x -lt $bitmap.Width; $x += 2) {
        for ($y = 0; $y -lt $bitmap.Height; $y += 2) {
            $total += $bitmap.GetPixel($x, $y).GetBrightness()
            $count++
        }
    }
    $graphics.Dispose()
    $bitmap.Dispose()
    return [int] (255 * $total / $count)
}

# The average brightness of the settings window below its title bar, from 0 (black) to 255
# (white), or -1 when it isn't open.
function Get-AppWindowBrightness {
    $window = Get-AppWindow
    if (-not $window) { return -1 }
    $bounds = $window.Current.BoundingRectangle
    $width = [int] $bounds.Width - 16
    $height = [int] $bounds.Height - 56
    if ($width -lt 1 -or $height -lt 1) { return -1 }

    $bitmap = New-Object System.Drawing.Bitmap $width, $height
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.CopyFromScreen([int] $bounds.Left + 8, [int] $bounds.Top + 48, 0, 0, $bitmap.Size)
    $total = 0.0
    $count = 0
    for ($x = 0; $x -lt $width; $x += 4) {
        for ($y = 0; $y -lt $height; $y += 4) {
            $total += $bitmap.GetPixel($x, $y).GetBrightness()
            $count++
        }
    }
    $graphics.Dispose()
    $bitmap.Dispose()
    return [int] (255 * $total / $count)
}

# Closes the host's open tray menu and returns whether it closed. Escape would go to the window in
# front, which needn't be the menu; WM_CANCELMODE makes the window that owns the menu end it.
function Close-TrayMenu([string] $WindowClass = 'PCCompanion.Host') {
    $window = [Win32]::FindWindow($WindowClass, $null)
    if ($window -eq [IntPtr]::Zero) { return $false }
    [void] [Win32]::PostMessage($window, 0x001F, [UIntPtr]::Zero, [IntPtr]::Zero)
    $menuCondition = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ClassNameProperty, '#32768')
    return Wait-Until { $null -eq [System.Windows.Automation.AutomationElement]::RootElement.FindFirst([System.Windows.Automation.TreeScope]::Children, $menuCondition) } 5
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

function Get-InstalledProducts {
    @(Get-ItemProperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\*', 'HKLM:\Software\Microsoft\Windows\CurrentVersion\Uninstall\*' -ErrorAction SilentlyContinue |
        Where-Object { $_.DisplayName -eq 'PC Companion' })
}

# Tries to install another build in the other scope, $Scope, and checks that setup stops and leaves
# the installed one alone.
function Test-OtherScopeStops([string] $Name, [string[]] $Scope, [string] $Log, [string] $Message) {
    $before = @(Get-InstalledProducts | ForEach-Object { $_.PSChildName })
    $code = (Start-Process msiexec.exe -ArgumentList (@('/i', "`"$sameVersionMsi`"", '/qn') + $Scope + @('/l*v', "`"$results\$Log`"")) -Wait -PassThru).ExitCode
    Test-Check "$Name stops" ($code -eq 1603) "exit code $code"
    Test-Check "$Name says why" ([bool] (Select-String -Path (Join-Path $results $Log) -Pattern $Message -SimpleMatch -Quiet))
    $after = @(Get-InstalledProducts | ForEach-Object { $_.PSChildName })
    Test-Check "$Name leaves the installed one alone" (($after.Count -eq 1) -and ($after[0] -eq $before[0])) "before: $($before -join ', '); after: $($after -join ', ')"
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
