<#
.SYNOPSIS
Makes the app icon file from the app icon's drawings.

.DESCRIPTION
Writes src\PCCompanion.App\Assets\PCCompanion.ico from docs\design\icons\app-<size>.svg, which
Microsoft Edge draws without opening a window. The 256-pixel image comes from the 48-pixel
drawing, and the others from their own drawings. Images up to 64 pixels are stored as 32-bit
bitmaps, which System.Drawing reads, and the 256-pixel image as PNG.
Run it when the drawings change, then installer-images.ps1, and commit the files.

.EXAMPLE
powershell -ExecutionPolicy Bypass -File scripts\app-icon.ps1
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'svg-images.ps1')

$root = Split-Path -Parent $PSScriptRoot
$drawings = Join-Path $root 'docs\design\icons'
$iconPath = Join-Path $root 'src\PCCompanion.App\Assets\PCCompanion.ico'

# Each image in the icon file, and the size of the drawing it comes from.
$images = @(
    @{ Size = 16; Drawing = 16 }, @{ Size = 20; Drawing = 20 }, @{ Size = 24; Drawing = 24 },
    @{ Size = 32; Drawing = 32 }, @{ Size = 40; Drawing = 40 }, @{ Size = 48; Drawing = 48 },
    @{ Size = 64; Drawing = 64 }, @{ Size = 256; Drawing = 48 })
foreach ($image in $images) { $image.Path = Join-Path $drawings "app-$($image.Drawing).svg" }

# A 32-bit bitmap as an icon file holds it: a BITMAPINFOHEADER twice as high as the image, the
# rows from the bottom up in BGRA order, then a mask with a bit set for each transparent pixel.
function Get-BitmapEntry([System.Drawing.Bitmap] $Bitmap) {
    $size = $Bitmap.Width
    $area = New-Object System.Drawing.Rectangle -ArgumentList 0, 0, $size, $size
    $data = $Bitmap.LockBits($area, [System.Drawing.Imaging.ImageLockMode]::ReadOnly, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    try {
        $stride = $data.Stride
        $pixels = New-Object byte[] ($stride * $size)
        [Runtime.InteropServices.Marshal]::Copy($data.Scan0, $pixels, 0, $pixels.Length)
    }
    finally {
        $Bitmap.UnlockBits($data)
    }

    $maskStride = [int][Math]::Ceiling($size / 32) * 4
    $stream = New-Object IO.MemoryStream
    $writer = New-Object IO.BinaryWriter -ArgumentList $stream
    $writer.Write([int]40)
    $writer.Write([int]$size)
    $writer.Write([int]($size * 2))
    $writer.Write([int16]1)
    $writer.Write([int16]32)
    $writer.Write([int]0)
    $writer.Write([int]($size * $size * 4 + $maskStride * $size))
    $writer.Write([int]0)
    $writer.Write([int]0)
    $writer.Write([int]0)
    $writer.Write([int]0)
    for ($y = $size - 1; $y -ge 0; $y--) {
        $writer.Write($pixels, $y * $stride, $size * 4)
    }
    for ($y = $size - 1; $y -ge 0; $y--) {
        $row = New-Object byte[] $maskStride
        for ($x = 0; $x -lt $size; $x++) {
            if ($pixels[$y * $stride + $x * 4 + 3] -eq 0) { $row[$x -shr 3] = $row[$x -shr 3] -bor (0x80 -shr ($x -band 7)) }
        }
        $writer.Write($row)
    }
    $writer.Flush()
    return , $stream.ToArray()
}

function Get-PngEntry([System.Drawing.Bitmap] $Bitmap) {
    $stream = New-Object IO.MemoryStream
    $Bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
    return , $stream.ToArray()
}

Get-SvgImages $images
try {
    $entries = foreach ($image in $images) {
        if ($image.Size -eq 256) { , (Get-PngEntry $image.Bitmap) } else { , (Get-BitmapEntry $image.Bitmap) }
    }

    # The icon file's header and directory, then the images. A size of 0 in the directory means 256.
    $stream = New-Object IO.MemoryStream
    $writer = New-Object IO.BinaryWriter -ArgumentList $stream
    $writer.Write([int16]0)
    $writer.Write([int16]1)
    $writer.Write([int16]$images.Count)
    $offset = 6 + 16 * $images.Count
    for ($index = 0; $index -lt $images.Count; $index++) {
        $writer.Write([byte]($images[$index].Size % 256))
        $writer.Write([byte]($images[$index].Size % 256))
        $writer.Write([byte]0)
        $writer.Write([byte]0)
        $writer.Write([int16]1)
        $writer.Write([int16]32)
        $writer.Write([int]$entries[$index].Length)
        $writer.Write([int]$offset)
        $offset += $entries[$index].Length
    }
    foreach ($entry in $entries) { $writer.Write($entry) }
    $writer.Flush()
    [IO.File]::WriteAllBytes($iconPath, $stream.ToArray())
    Write-Host "Wrote $iconPath"
}
finally {
    foreach ($image in $images) { if ($image.Bitmap) { $image.Bitmap.Dispose() } }
}
