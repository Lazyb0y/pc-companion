<#
.SYNOPSIS
Takes the website's screenshots of the settings app inside Windows Sandbox. Started by
scripts\website-screenshots.ps1; don't run it on your own PC: it installs PC Companion, changes its
settings and shuts Windows down at the end.

.DESCRIPTION
Inputs:  C:\Test\msi\PCCompanion-*.msi (read-only).
Outputs: C:\Test\results: <page>-light.png and <page>-dark.png for each page in $pages, report.txt
         with a PASS or FAIL line per step, and done.txt when finished.

Installs PC Companion just for the current user, turns its features on, and saves each page of the
settings app in the light and dark themes, on a plain desktop: Home as the whole window, the other
pages cropped to the page itself. When a release adds a feature, add its page to $pages and turn it
on in $features.
#>

$ErrorActionPreference = 'Continue'
. (Join-Path $PSScriptRoot 'common.ps1')
New-Item -ItemType Directory -Force -Path $results | Out-Null
Set-Content -Path $report -Value "PC Companion website screenshots - $(Get-Date -Format o)" -Encoding utf8

# The pages, by their names in the navigation pane, and the features' settings: an action for each
# corner, and a key for one, two rules and two profiles, so the pages show what the features can do.
$pages = 'Home', 'Hot Corners', 'App Windows', 'System Meters', 'Clipboard Actions', 'Network Profiles', 'General'
$features = @'
"hotCorners": {
    "enabled": true,
    "topLeft": { "action": "TaskView", "key": "None" },
    "topRight": { "action": "KeepDisplaysOn", "key": "None" },
    "bottomLeft": { "action": "ShowDesktop", "key": "None" },
    "bottomRight": { "action": "Lock", "key": "Control" },
    "activationDelayMilliseconds": 150,
    "ignoreFullScreenApps": true
},
"appWindows": { "enabled": true, "shortcut": "AltAboveTab", "includeMinimized": false },
"systemMeters": { "enabled": true },
"clipboardActions": {
    "enabled": true,
    "openLinks": true,
    "rules": [
        { "name": "Track the parcel", "pattern": "1Z[0-9A-Z]{16}", "matchCase": false, "action": "OpenAddress", "address": "https://tracking.example.com/?number={text}" },
        { "name": "Open in a private window", "pattern": "https?://\\S+", "matchCase": false, "action": "RunProgram", "program": "C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe", "arguments": "--inprivate {text}" }
    ]
},
"networkProfiles": {
    "enabled": true,
    "profiles": [
        { "name": "Office", "proxy": { "autoDetect": false, "useScript": false, "scriptAddress": "", "useServer": true, "server": "proxy.example.com:8080", "exceptions": ["*.example.com", "10.*"], "bypassLocal": true } },
        { "name": "Home", "proxy": { "autoDetect": true, "useScript": false, "scriptAddress": "", "useServer": false, "server": "", "exceptions": [], "bypassLocal": false } }
    ]
}
'@

# The desktop's gray, from 0 to 255: the window's Mica backdrop takes its tint from the desktop
# behind it, so a plain gray one leaves it neutral in both themes, without the colors of the
# Sandbox's wallpaper.
$desktopGray = 128

# Below the last part of a page that's in view whole, a feature's screenshot keeps the page's own
# margin, so it ends between parts rather than cutting one off.
$pageMargin = 24

# The size of the window without its invisible resize borders, as on the website.
$width = 1086
$height = 736

Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class Frame {
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr window, int attribute, out RECT value, int size);

    // The window's visible bounds, without its invisible resize borders (DWMWA_EXTENDED_FRAME_BOUNDS).
    public static RECT Bounds(IntPtr window) {
        RECT rect;
        DwmGetWindowAttribute(window, 9, out rect, Marshal.SizeOf(typeof(RECT)));
        return rect;
    }
}
public static class Desktop {
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern bool SystemParametersInfo(uint action, uint param, string value, uint flags);
    [DllImport("user32.dll")] public static extern bool SetSysColors(int count, int[] elements, int[] colors);
}
'@

# Takes the Sandbox's wallpaper away and makes the desktop plain gray (COLOR_DESKTOP), saved for the
# user and announced to open windows (SPI_SETDESKWALLPAPER with SPIF_UPDATEINIFILE and
# SPIF_SENDCHANGE).
function Set-PlainDesktop {
    Set-ItemProperty 'HKCU:\Control Panel\Colors' -Name Background -Value "$desktopGray $desktopGray $desktopGray"
    $colored = [Desktop]::SetSysColors(1, @(1), @($desktopGray * 0x010101))
    $cleared = [Desktop]::SystemParametersInfo(0x0014, 0, '', 3)
    Test-Check "the desktop is plain gray ($desktopGray)" ($colored -and $cleared)
}

# Sizes the settings window so that it's $width x $height without its borders: the borders' size
# is known only once the window is shown.
function Set-WindowSize {
    $handle = [IntPtr] (Get-AppWindow).Current.NativeWindowHandle
    $outerWidth = $width
    $outerHeight = $height
    for ($attempt = 0; $attempt -lt 2; $attempt++) {
        [void] [Win32]::MoveWindow($handle, 40, 40, $outerWidth, $outerHeight, $true)
        Start-Sleep -Milliseconds 800
        $frame = [Frame]::Bounds($handle)
        $outerWidth += $width - ($frame.Right - $frame.Left)
        $outerHeight += $height - ($frame.Bottom - $frame.Top)
    }
    [void] [Win32]::SetForegroundWindow($handle)
}

# Saves the settings window, without its borders, to $Name.png.
function Save-Window([string] $Name) {
    $frame = [Frame]::Bounds([IntPtr] (Get-AppWindow).Current.NativeWindowHandle)
    $frameWidth = $frame.Right - $frame.Left
    $frameHeight = $frame.Bottom - $frame.Top
    $bitmap = New-Object System.Drawing.Bitmap $frameWidth, $frameHeight
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.CopyFromScreen($frame.Left, $frame.Top, 0, 0, (New-Object System.Drawing.Size $frameWidth, $frameHeight))
    $bitmap.Save((Join-Path $results "$Name.png"), [System.Drawing.Imaging.ImageFormat]::Png)
    $graphics.Dispose()
    $bitmap.Dispose()
    Test-Check "$Name is $width x $height" (($frameWidth -eq $width) -and ($frameHeight -eq $height)) "$frameWidth x $frameHeight"
}

# Saves the page itself, right of the navigation pane and below the title bar, to $Name.png: down to
# the last part of the page that's in view whole, such as a card, and $pageMargin of background
# below it. Where the next part starts sooner, the margin repeats the last row of background before
# it. The page is the window's largest scroll viewer, and its parts are its children.
function Save-Page([string] $Name) {
    $window = Get-AppWindow
    $condition = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ClassNameProperty, 'ScrollViewer')
    $viewer = @($window.FindAll([System.Windows.Automation.TreeScope]::Descendants, $condition)) |
        Where-Object { -not $_.Current.BoundingRectangle.IsEmpty } |
        Sort-Object { $_.Current.BoundingRectangle.Width * $_.Current.BoundingRectangle.Height } -Descending |
        Select-Object -First 1
    if (-not $viewer) {
        Test-Check "$Name`: the page is found" $false
        return
    }
    $area = $viewer.Current.BoundingRectangle
    $left = [int] $area.Left
    $top = [int] $area.Top
    $right = [int] $area.Right
    $bottom = [int] $area.Bottom

    $parts = @()
    $walker = [System.Windows.Automation.TreeWalker]::ControlViewWalker
    $child = $walker.GetFirstChild($viewer)
    while ($child) {
        $rect = $child.Current.BoundingRectangle
        if (-not $rect.IsEmpty -and $rect.Top -lt $bottom -and $child.Current.ControlType -ne [System.Windows.Automation.ControlType]::ScrollBar) {
            $parts += , @([int] $rect.Top, [int] $rect.Bottom)
        }
        $child = $walker.GetNextSibling($child)
    }
    $whole = @($parts | Where-Object { $_[1] -lt $bottom } | ForEach-Object { $_[1] })
    if ($whole.Count -eq 0) {
        Test-Check "$Name`: a part of the page is in view whole" $false "$($parts.Count) parts"
        return
    }
    $end = [int] ($whole | Measure-Object -Maximum).Maximum
    $cut = @($parts | Where-Object { ($_[0] -lt $end) -and ($_[1] -gt $end) })
    Test-Check "$Name`: the screenshot ends between parts" ($cut.Count -eq 0) "$($parts.Count) parts, the last whole one ends at $($end - $top)"
    if ($cut.Count -gt 0) { return }
    $next = [int] (@($parts | Where-Object { $_[0] -ge $end } | ForEach-Object { $_[0] }) + $bottom | Measure-Object -Minimum).Minimum
    $background = [math]::Min($pageMargin, $next - $end)

    $shotWidth = $right - $left
    $shotHeight = $end - $top + $pageMargin
    $copied = $end - $top + $background
    $bitmap = New-Object System.Drawing.Bitmap $shotWidth, $shotHeight
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.CopyFromScreen($left, $top, 0, 0, (New-Object System.Drawing.Size $shotWidth, $copied))
    for ($y = $copied; $y -lt $shotHeight; $y++) {
        $graphics.CopyFromScreen($left, $top + $copied - 1, 0, $y, (New-Object System.Drawing.Size $shotWidth, 1))
    }
    $bitmap.Save((Join-Path $results "$Name.png"), [System.Drawing.Imaging.ImageFormat]::Png)
    $graphics.Dispose()
    $bitmap.Dispose()
    Test-Check "$Name is the page, $shotWidth x $shotHeight" (($background -ge 1) -and ($shotWidth -ge 700) -and ($shotHeight -ge 300)) "$background rows of background below the last part"
}

# Keeps the pointer off the window, so nothing shows as hovered, and out of the corners.
function Move-PointerAside { [void] (Move-Pointer ($screen.Width - 20) ([int] ($screen.Height / 2))) }

Write-Report (& (Join-Path $PSScriptRoot 'smart-app-control.ps1') Off)
$graphics = [System.Drawing.Graphics]::FromHwnd([IntPtr]::Zero)
$scale = $graphics.DpiX / 96
$graphics.Dispose()
Test-Check 'the display is at 100% scaling' ($scale -eq 1) "$($screen.Width) x $($screen.Height) at $($scale * 100)%"
Test-Check 'the window fits on the display' (($screen.Width -ge $width + 80) -and ($screen.Height -ge $height + 80)) "$($screen.Width) x $($screen.Height)"

Set-PlainDesktop

$code = (Start-Process msiexec.exe -ArgumentList @('/i', "`"$msi`"", '/qn', 'ALLUSERS=2', 'MSIINSTALLPERUSER=1', '/l*v', "`"$results\install.log`"") -Wait -PassThru).ExitCode
Test-Check 'install for the current user' ($code -eq 0) "exit code $code"

foreach ($theme in 'Light', 'Dark') {
    Stop-All
    Write-Settings "{ ""schemaVersion"": 1, ""general"": { ""theme"": ""$theme"" }, ""features"": { $features } }"
    Start-Sleep -Seconds 2
    Start-Process $appExe
    Test-Check "$theme`: the settings window opens" ([bool] (Wait-Until { Get-AppWindow }))
    Start-Sleep -Seconds 4
    Set-WindowSize
    # Home shows the whole window, with the navigation pane; the other pages show only the page,
    # which stays the same when a release adds a feature to the pane.
    foreach ($page in $pages) {
        Test-Check "$theme`: show $page" (Show-Page $page)
        Move-PointerAside
        Start-Sleep -Seconds 3
        $name = "$($page.ToLowerInvariant() -replace ' ', '-')-$($theme.ToLowerInvariant())"
        if ($page -eq 'Home') { Save-Window $name } else { Save-Page $name }
    }
}

Stop-All
Write-Report "finished $(Get-Date -Format o)"
Set-Content -Path (Join-Path $results 'done.txt') -Value 'done'
shutdown.exe /s /t 5
