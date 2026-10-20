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
settings app in the light and dark themes, cropped to the window. When a release adds a feature, add
its page to $pages and turn it on in $features.
#>

$ErrorActionPreference = 'Continue'
. (Join-Path $PSScriptRoot 'common.ps1')
New-Item -ItemType Directory -Force -Path $results | Out-Null
Set-Content -Path $report -Value "PC Companion website screenshots - $(Get-Date -Format o)" -Encoding utf8

# The pages, by their names in the navigation pane, and the features' settings: an action for each
# corner, and a key for one, so the pages show what the features can do.
$pages = 'Home', 'Hot Corners', 'App Windows', 'System Meters', 'General'
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
"systemMeters": { "enabled": true }
'@

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
'@

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

# Keeps the pointer off the window, so nothing shows as hovered, and out of the corners.
function Move-PointerAside { [void] (Move-Pointer ($screen.Width - 20) ([int] ($screen.Height / 2))) }

Write-Report (& (Join-Path $PSScriptRoot 'smart-app-control.ps1') Off)
$graphics = [System.Drawing.Graphics]::FromHwnd([IntPtr]::Zero)
$scale = $graphics.DpiX / 96
$graphics.Dispose()
Test-Check 'the display is at 100% scaling' ($scale -eq 1) "$($screen.Width) x $($screen.Height) at $($scale * 100)%"
Test-Check 'the window fits on the display' (($screen.Width -ge $width + 80) -and ($screen.Height -ge $height + 80)) "$($screen.Width) x $($screen.Height)"

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
    foreach ($page in $pages) {
        Test-Check "$theme`: show $page" (Show-Page $page)
        Move-PointerAside
        Start-Sleep -Seconds 3
        Save-Window "$($page.ToLowerInvariant() -replace ' ', '-')-$($theme.ToLowerInvariant())"
    }
}

Stop-All
Write-Report "finished $(Get-Date -Format o)"
Set-Content -Path (Join-Path $results 'done.txt') -Value 'done'
shutdown.exe /s /t 5
