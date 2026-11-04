<#
.SYNOPSIS
Draws SVG files as images, for the scripts that make the app's icon images.

.DESCRIPTION
Dot-source it and call Get-SvgImages. Microsoft Edge, which comes with Windows, draws every image
side by side on one transparent page without opening a window, and the page is then cut up. Each
image is drawn on a canvas of its own, which the page then shows as it is: drawn straight on the
page, a drawing's pixels change slightly with where it is, so adding an image would change others.
#>

Add-Type -AssemblyName System.Drawing

function Get-Edge {
    $key = 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\msedge.exe'
    $path = if (Test-Path $key) { (Get-ItemProperty $key).'(default)' }
    if (-not $path -or -not (Test-Path $path)) { throw 'Microsoft Edge, which draws the images, was not found.' }
    return $path
}

# Edge keeps files in its profile for a moment after it exits.
function Remove-Folder([string] $Path) {
    for ($try = 1; Test-Path $Path; $try++) {
        try {
            Remove-Item $Path -Recurse -Force
        }
        catch {
            if ($try -ge 10) { throw }
            Start-Sleep -Milliseconds 500
        }
    }
}

# $Images is a list of hashtables, each with Path, an SVG file, and Size, in pixels. Each gets
# Bitmap, a 32-bit image of the drawing at that size, which the caller disposes.
function Get-SvgImages([object[]] $Images) {
    $work = Join-Path ([IO.Path]::GetTempPath()) "PCCompanion-svg-images-$PID"
    New-Item -ItemType Directory -Force $work | Out-Null
    try {
        $x = 0
        $height = 0
        $tags = foreach ($image in $Images) {
            if (-not (Test-Path $image.Path)) { throw "$($image.Path) is missing." }
            "<canvas width=`"$($image.Size)`" height=`"$($image.Size)`" data-src=`"$(([Uri]$image.Path).AbsoluteUri)`" style=`"left:$($x)px`"></canvas>"
            $image.X = $x
            $x += $image.Size + 8
            $height = [math]::Max($height, $image.Size)
        }

        $page = Join-Path $work 'images.html'
        $draw = '<script>for (const canvas of document.querySelectorAll("canvas")) { const image = new Image(); ' +
            'image.onload = () => canvas.getContext("2d").drawImage(image, 0, 0, canvas.width, canvas.height); image.src = canvas.dataset.src; }</script>'
        [IO.File]::WriteAllText($page, '<!doctype html><html><head><style>html,body{margin:0;background:transparent}canvas{position:absolute;top:0}</style></head><body>' + ($tags -join '') + $draw + '</body></html>')
        $shot = Join-Path $work 'images.png'
        $arguments = @(
            '--headless=new', '--disable-gpu', '--hide-scrollbars', '--force-device-scale-factor=1', '--force-color-profile=srgb',
            '--default-background-color=00000000', '--virtual-time-budget=2000', "--window-size=$x,$height",
            "`"--user-data-dir=$(Join-Path $work 'profile')`"", "`"--screenshot=$shot`"", ([Uri]$page).AbsoluteUri)
        $edge = Start-Process -FilePath (Get-Edge) -ArgumentList $arguments -Wait -PassThru
        if ($edge.ExitCode -ne 0 -or -not (Test-Path $shot)) { throw "Edge couldn't draw the images (exit code $($edge.ExitCode))." }

        $sheet = New-Object System.Drawing.Bitmap -ArgumentList $shot
        try {
            foreach ($image in $Images) {
                $area = New-Object System.Drawing.Rectangle -ArgumentList $image.X, 0, $image.Size, $image.Size
                $image.Bitmap = $sheet.Clone($area, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
            }
        }
        finally {
            $sheet.Dispose()
        }
    }
    finally {
        Remove-Folder $work
    }
}
