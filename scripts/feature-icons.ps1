<#
.SYNOPSIS
Makes the feature icons' images from their drawings.

.DESCRIPTION
Writes src\PCCompanion.App\Assets\Icons\<name>-<pixels>.png and the one-color
<name>-<pixels>-mono.png for each feature icon in docs\design\icons, which Microsoft Edge draws.
Each image is drawn from the drawing closest to it in size, at each size the settings app needs
for an icon of 20 or 32 pixels at 100%, 125%, 150% and 200% display scale. The settings app
shows the one-color images in high contrast, in the theme's text color.

drawings.txt records a fingerprint of each drawing the images were made from. A test compares it
with the drawings, so the tests fail when a drawing has changed without its images being made
again. Run it after icon-drawings.ps1 changes a feature icon, and commit the files.

.EXAMPLE
powershell -ExecutionPolicy Bypass -File scripts\feature-icons.ps1
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'svg-images.ps1')

$root = Split-Path -Parent $PSScriptRoot
$drawings = Join-Path $root 'docs\design\icons'
$output = Join-Path $root 'src\PCCompanion.App\Assets\Icons'

# 20 and 32 pixels at 100%, 125%, 150% and 200%. The settings app's FeatureIcon and the test of
# the images use the same sizes.
$pixelSizes = 20, 25, 30, 32, 40, 48, 64

# The feature icons and the sizes each is drawn at.
$icons = @{}
foreach ($file in Get-ChildItem $drawings -Filter '*.svg') {
    if ($file.Name -notmatch '^(?<name>.+)-(?<size>\d+)\.svg$' -or $Matches.name -eq 'app') { continue }
    if (-not $icons.ContainsKey($Matches.name)) { $icons[$Matches.name] = @() }
    $icons[$Matches.name] += [int] $Matches.size
}
if ($icons.Count -eq 0) { throw "$drawings has no feature icons." }

# Each image, from the drawing closest to it in size, or the larger of two as close.
$images = foreach ($name in $icons.Keys | Sort-Object) {
    foreach ($pixels in $pixelSizes) {
        $drawing = $icons[$name] | Sort-Object { [math]::Abs($_ - $pixels) }, { -$_ } | Select-Object -First 1
        foreach ($suffix in '', '-mono') {
            @{ Path = Join-Path $drawings "$name-$drawing$suffix.svg"; Size = $pixels; Name = "$name-$pixels$suffix.png" }
        }
    }
}

# The text of a drawing with its line endings as Git stores them, so its fingerprint is the same
# whatever line endings a checkout gives it.
function Get-Fingerprint([string] $Path) {
    $text = [IO.File]::ReadAllText($Path).Replace("`r`n", "`n")
    $sha = [Security.Cryptography.SHA256]::Create()
    try {
        return [BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($text))).Replace('-', '')
    }
    finally {
        $sha.Dispose()
    }
}

Get-SvgImages $images
try {
    New-Item -ItemType Directory -Force $output | Out-Null
    # Images of icons or sizes that are gone shouldn't stay behind.
    Get-ChildItem $output -Filter '*.png' | Remove-Item
    foreach ($image in $images) {
        $image.Bitmap.Save((Join-Path $output $image.Name), [System.Drawing.Imaging.ImageFormat]::Png)
    }

    $used = $images | ForEach-Object { $_.Path } | Sort-Object -Unique
    $lines = $used | ForEach-Object { "$(Split-Path -Leaf $_) $(Get-Fingerprint $_)" }
    [IO.File]::WriteAllText((Join-Path $output 'drawings.txt'), ($lines -join "`r`n") + "`r`n")
    Write-Host "Wrote $($images.Count) images of $($icons.Count) feature icons in $output"
}
finally {
    foreach ($image in $images) { if ($image.Bitmap) { $image.Bitmap.Dispose() } }
}
