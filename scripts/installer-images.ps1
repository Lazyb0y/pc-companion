<#
.SYNOPSIS
Draws the installer's dialog images from the app icon.

.DESCRIPTION
Writes two bitmaps to src\PCCompanion.Installer, in the sizes the installer's dialogs use:
Banner.bmp (493x58), the top of most pages, and Dialog.bmp (493x312), whose left 164 pixels
show on the welcome, maintenance and finish pages. Run it when the icon changes and commit the
images. The images hold no text, so they need no translation.

.EXAMPLE
powershell -ExecutionPolicy Bypass -File scripts\installer-images.ps1
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$root = Split-Path -Parent $PSScriptRoot
$iconPath = Join-Path $root 'src\PCCompanion.App\Assets\PCCompanion.ico'
$installer = Join-Path $root 'src\PCCompanion.Installer'

# The icon's 256-pixel image is stored as PNG, which System.Drawing.Icon skips, so it's read from
# the icon file directly.
function Get-LargeIcon {
    $bytes = [IO.File]::ReadAllBytes($iconPath)
    for ($index = 0; $index -lt [BitConverter]::ToUInt16($bytes, 4); $index++) {
        $entry = 6 + 16 * $index
        # A width of 0 means 256 pixels.
        if ($bytes[$entry] -eq 0) {
            $length = [BitConverter]::ToInt32($bytes, $entry + 8)
            $offset = [BitConverter]::ToInt32($bytes, $entry + 12)
            $stream = New-Object IO.MemoryStream -ArgumentList $bytes, $offset, $length
            return [System.Drawing.Image]::FromStream($stream)
        }
    }

    throw "$iconPath has no 256-pixel image."
}

function Get-SmallIcon([int] $Size) {
    $icon = New-Object System.Drawing.Icon -ArgumentList $iconPath, $Size, $Size
    try {
        if ($icon.Width -ne $Size) { throw "$iconPath has no $Size-pixel image." }
        return $icon.ToBitmap()
    }
    finally {
        $icon.Dispose()
    }
}

# Windows Installer dialogs are always light, so both images have a white background.
function Save-Image([string] $Name, [int] $Width, [int] $Height, [scriptblock] $Draw) {
    $image = New-Object System.Drawing.Bitmap -ArgumentList $Width, $Height, ([System.Drawing.Imaging.PixelFormat]::Format24bppRgb)
    $graphics = [System.Drawing.Graphics]::FromImage($image)
    try {
        $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $graphics.Clear([System.Drawing.Color]::White)
        & $Draw $graphics
        $path = Join-Path $installer $Name
        $image.Save($path, [System.Drawing.Imaging.ImageFormat]::Bmp)
        Write-Host "Wrote $path"
    }
    finally {
        $graphics.Dispose()
        $image.Dispose()
    }
}

# The page title is drawn over the left part of the banner, so the icon goes on the right.
$smallIcon = Get-SmallIcon 40
Save-Image 'Banner.bmp' 493 58 {
    param($graphics)
    $graphics.DrawImage($smallIcon, 441, 9, 40, 40)
}

# The page text is drawn over the right part, so only the left panel is colored: a light tint of
# the icon's blue, with the icon in it.
$largeIcon = Get-LargeIcon
Save-Image 'Dialog.bmp' 493 312 {
    param($graphics)
    $panel = New-Object System.Drawing.SolidBrush -ArgumentList ([System.Drawing.Color]::FromArgb(0xEE, 0xF4, 0xFE))
    $graphics.FillRectangle($panel, 0, 0, 164, 312)
    $panel.Dispose()
    $graphics.DrawImage($largeIcon, 34, 52, 96, 96)
}

$smallIcon.Dispose()
$largeIcon.Dispose()
