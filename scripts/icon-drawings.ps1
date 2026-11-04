<#
.SYNOPSIS
Writes the icon drawings in docs\design\icons from their shapes.

.DESCRIPTION
Each icon is described below by its shapes at each size it's drawn at. The script writes
<name>-<size>.svg and the one-color <name>-<size>-mono.svg for each, using only what Direct2D's
SVG renderer supports, as docs\design\icons.md says: a gap around a shape is a clip path that cuts
the shape, grown by half its stroke and the gap, out of the shapes under it, and the one-color
versions are even-odd paths in currentColor.

To change an icon, change its numbers here and run the script, then app-icon.ps1 for the app icon
or feature-icons.ps1 for a feature icon, and commit the files.

.EXAMPLE
powershell -ExecutionPolicy Bypass -File scripts\icon-drawings.ps1
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$folder = Join-Path $root 'docs\design\icons'
$culture = [Globalization.CultureInfo]::InvariantCulture
$utf8 = New-Object Text.UTF8Encoding -ArgumentList $false
$indent = '    '

# The fills of the color roles in docs\design\icons.md. Each runs from the top left of its shape.
$gradients = @{
    frame     = @{ End = '1'; Stops = 'stop-color="#5A86FF"', 'stop-color="#3940D6"' }
    highlight = @{ End = '.6'; Stops = 'stop-color="#FFFFFF" stop-opacity=".28"', 'stop-color="#FFFFFF" stop-opacity="0"' }
    deep      = @{ End = '1'; Stops = 'stop-color="#3F55D9"', 'stop-color="#2B2FA8"' }
    glass     = @{ End = '1'; Stops = 'stop-color="#F3F7FF"', 'stop-color="#CBD9FF"' }
    accent    = @{ End = '1'; Stops = 'stop-color="#FFD25E"', 'stop-color="#FF8A3C"' }
    overlap   = @{ End = '1'; Stops = 'stop-color="#FFF6E6"', 'stop-color="#FFD9B0"' }
}
$detail = '#9DB0F5'
$outline = '#2B2FA8'

# A number as the drawings write it: at most three decimals, without trailing zeros.
function F([double] $Value) {
    $text = [math]::Round($Value, 3).ToString('0.###', $culture)
    if ($text -eq '-0') { return '0' }
    return $text
}

function Get-Gradient([string] $Name) {
    $gradient = $gradients[$Name]
    "<linearGradient id=`"$Name`" x1=`"0`" y1=`"0`" x2=`"$($gradient.End)`" y2=`"$($gradient.End)`">"
    "$indent<stop offset=`"0`" $($gradient.Stops[0])/>"
    "$indent<stop offset=`"1`" $($gradient.Stops[1])/>"
    '</linearGradient>'
}

# $Box is x, y, width, height and corner radius.
function Get-Rect([double[]] $Box, [string] $Extra = '') {
    "<rect x=`"$(F $Box[0])`" y=`"$(F $Box[1])`" width=`"$(F $Box[2])`" height=`"$(F $Box[3])`" rx=`"$(F $Box[4])`"$Extra/>"
}

# A rounded rectangle as path data, clockwise.
function Get-RectPath([double[]] $Box) {
    $x, $y, $w, $h, $r = $Box
    if ($r -eq 0) { return "M$(F $x) $(F $y)H$(F ($x + $w))V$(F ($y + $h))H$(F $x)Z" }
    $arc = "A$(F $r) $(F $r) 0 0 1 "
    $right = if ($w -gt 2 * $r) { "H$(F ($x + $w - $r))" } else { '' }
    $left = if ($w -gt 2 * $r) { "H$(F ($x + $r))" } else { '' }
    $down = if ($h -gt 2 * $r) { "V$(F ($y + $h - $r))" } else { '' }
    $up = if ($h -gt 2 * $r) { "V$(F ($y + $r))" } else { '' }
    "M$(F ($x + $r)) $(F $y)$right$arc$(F ($x + $w)) $(F ($y + $r))$down$arc$(F ($x + $w - $r)) $(F ($y + $h))" +
    "$left$arc$(F $x) $(F ($y + $h - $r))$up$arc$(F ($x + $r)) $(F $y)Z"
}

# A square of side $Side with corner radius $Radius, turned $Degrees about its center, as path data.
function Get-TurnedSquarePath([double] $CenterX, [double] $CenterY, [double] $Side, [double] $Radius, [double] $Degrees) {
    $angle = $Degrees * ([math]::PI / 180)
    $cos = [math]::Cos($angle)
    $sin = [math]::Sin($angle)
    $point = {
        param([double] $px, [double] $py)
        $dx = $px - $CenterX
        $dy = $py - $CenterY
        "$(F ($CenterX + $dx * $cos - $dy * $sin)) $(F ($CenterY + $dx * $sin + $dy * $cos))"
    }
    $x = $CenterX - $Side / 2
    $y = $CenterY - $Side / 2
    $s = $Side
    $r = $Radius
    $arc = "A$(F $r) $(F $r) 0 0 1 "
    "M$(& $point ($x + $r) $y)L$(& $point ($x + $s - $r) $y)$arc$(& $point ($x + $s) ($y + $r))" +
    "L$(& $point ($x + $s) ($y + $s - $r))$arc$(& $point ($x + $s - $r) ($y + $s))" +
    "L$(& $point ($x + $r) ($y + $s))$arc$(& $point $x ($y + $s - $r))" +
    "L$(& $point $x ($y + $r))$arc$(& $point ($x + $r) $y)Z"
}

# $Points is a list of x, y pairs.
function Get-PolygonPath([object[]] $Points) {
    'M' + (($Points | ForEach-Object { "$(F $_[0]) $(F $_[1])" }) -join 'L') + 'Z'
}

# The outline of a polygon grown by $Distance with round corners, which is what its fill and a
# round-joined stroke twice $Distance wide cover: convex corners become arcs, and inward corners
# the point where the two moved edges meet.
function Get-GrownPath([object[]] $Points, [double] $Distance) {
    # A copy, since the points are turned round below.
    $Points = $Points.Clone()
    $count = $Points.Count
    $area = 0.0
    for ($i = 0; $i -lt $count; $i++) {
        $next = $Points[($i + 1) % $count]
        $area += $Points[$i][0] * $next[1] - $next[0] * $Points[$i][1]
    }
    # Clockwise on the screen, so (dy, -dx) points outward.
    if ($area -lt 0) { [array]::Reverse($Points) }
    $normals = for ($i = 0; $i -lt $count; $i++) {
        $from = $Points[$i]
        $to = $Points[($i + 1) % $count]
        $dx = $to[0] - $from[0]
        $dy = $to[1] - $from[1]
        $length = [math]::Sqrt($dx * $dx + $dy * $dy)
        , @(($dy / $length), (-$dx / $length))
    }
    $d = $Distance
    $steps = for ($i = 0; $i -lt $count; $i++) {
        $vx, $vy = $Points[$i]
        $n0 = $normals[$i - 1]
        $n1 = $normals[$i]
        $px, $py = $Points[$i - 1]
        $nx, $ny = $Points[($i + 1) % $count]
        $cross = ($vx - $px) * ($ny - $vy) - ($vy - $py) * ($nx - $vx)
        if ($cross -gt 0) {
            , @('L', ($vx + $n0[0] * $d), ($vy + $n0[1] * $d))
            , @('A', ($vx + $n1[0] * $d), ($vy + $n1[1] * $d))
        }
        else {
            $a1 = @(($px + $n0[0] * $d), ($py + $n0[1] * $d))
            $a2 = @(($vx + $n0[0] * $d), ($vy + $n0[1] * $d))
            $b1 = @(($vx + $n1[0] * $d), ($vy + $n1[1] * $d))
            $b2 = @(($nx + $n1[0] * $d), ($ny + $n1[1] * $d))
            $r = @(($a2[0] - $a1[0]), ($a2[1] - $a1[1]))
            $s = @(($b2[0] - $b1[0]), ($b2[1] - $b1[1]))
            $den = $r[0] * $s[1] - $r[1] * $s[0]
            $t = (($b1[0] - $a1[0]) * $s[1] - ($b1[1] - $a1[1]) * $s[0]) / $den
            $u = (($b1[0] - $a1[0]) * $r[1] - ($b1[1] - $a1[1]) * $r[0]) / $den
            if ($t -lt 0 -or $t -gt 1 -or $u -lt 0 -or $u -gt 1) { throw "The gap is too wide for the corner at $vx, $vy." }
            , @('L', ($a1[0] + $r[0] * $t), ($a1[1] + $r[1] * $t))
        }
    }
    $path = "M$(F $steps[-1][1]) $(F $steps[-1][2])"
    foreach ($step in $steps) {
        if ($step[0] -eq 'L') { $path += "L$(F $step[1]) $(F $step[2])" }
        else { $path += "A$(F $d) $(F $d) 0 0 1 $(F $step[1]) $(F $step[2])" }
    }
    return $path + 'Z'
}

# A clip path that keeps everything but the gap's outline.
function Get-GapClip([int] $Size, [string] $Outline) {
    '<clipPath id="gap">'
    "$indent<path clip-rule=`"evenodd`" d=`"M0 0H$($Size)V$($Size)H0Z$Outline`"/>"
    '</clipPath>'
}

function Get-Group([string] $Attributes, [string[]] $Items) {
    "<g $Attributes>"
    $Items | ForEach-Object { "$indent$_" }
    '</g>'
}

function Write-Drawing([string] $Name, [int] $Size, [string[]] $Defs, [string[]] $Body) {
    $lines = New-Object Collections.Generic.List[string]
    $lines.Add("<svg xmlns=`"http://www.w3.org/2000/svg`" width=`"$Size`" height=`"$Size`" viewBox=`"0 0 $Size $Size`">")
    if ($Defs) {
        $lines.Add("$indent<defs>")
        $Defs | ForEach-Object { $lines.Add("$indent$indent$_") }
        $lines.Add("$indent</defs>")
    }
    $Body | ForEach-Object { $lines.Add("$indent$_") }
    $lines.Add('</svg>')
    [IO.File]::WriteAllText((Join-Path $folder "$Name.svg"), ($lines -join "`r`n") + "`r`n", $utf8)
}

# App icon: the blue square's box (x, y, side, corner radius) and the amber square's center,
# side, corner radius and turn, at each size.
$app = @(
    @{ Size = 64; Blue = 4, 17, 43, 12; Amber = 45, 19, 29.5, 8.75, 10 }
    @{ Size = 48; Blue = 3, 13, 32, 9; Amber = 33.5, 14.5, 22, 6.5, 10 }
    @{ Size = 40; Blue = 2, 11, 27, 7.5; Amber = 27.75, 12.25, 18.5, 5.5, 10 }
    @{ Size = 32; Blue = 2, 9, 21, 6; Amber = 22, 10, 14.5, 4.25, 10 }
    @{ Size = 24; Blue = 1, 7, 16, 4.5; Amber = 16.5, 7.5, 11, 3.25, 10 }
    @{ Size = 20; Blue = 1, 5, 14, 4; Amber = 14, 6, 9, 2.7, 10 }
    @{ Size = 16; Blue = 1, 4, 11, 3; Amber = 11.5, 4.5, 7, 2, 10 }
)
foreach ($icon in $app) {
    $bx, $by, $side, $radius = $icon.Blue
    $blue = @($bx, $by, $side, $side, $radius)
    $cx, $cy, $amberSide, $amberRadius, $turn = $icon.Amber
    $amber = @(($cx - $amberSide / 2), ($cy - $amberSide / 2), $amberSide, $amberSide, $amberRadius)
    $rotate = " transform=`"rotate($(F $turn) $(F $cx) $(F $cy))`""
    $defs = @(Get-Gradient 'frame') + @(Get-Gradient 'highlight') + @(Get-Gradient 'accent') + @(Get-Gradient 'overlap') +
        @('<clipPath id="blue">', "$indent$(Get-Rect $blue)", '</clipPath>')
    $body = @((Get-Rect $blue ' fill="url(#frame)"'), (Get-Rect $blue ' fill="url(#highlight)"'), (Get-Rect $amber " fill=`"url(#accent)`"$rotate")) +
        @(Get-Group 'clip-path="url(#blue)"' @(Get-Rect $amber " fill=`"url(#overlap)`"$rotate"))
    Write-Drawing "app-$($icon.Size)" $icon.Size $defs $body
    $mono = "<path fill=`"currentColor`" fill-rule=`"evenodd`" d=`"$(Get-RectPath $blue)$(Get-TurnedSquarePath $cx $cy $amberSide $amberRadius $turn)`"/>"
    Write-Drawing "app-$($icon.Size)-mono" $icon.Size @() @($mono)
}

# Hot Corners: a screen (frame and glass) with its top-left corner in amber, and a pointer whose
# tip is at Pointer's x, y, drawn at Pointer's scale. Hole is the glass without the corner, which
# the one-color version cuts out.
$pointer = @(@(0, 0), @(0, 12.2), @(3.1, 9.4), @(5.4, 14.3), @(7.6, 13.3), @(5.3, 8.5), @(9.6, 8.5))
$hotCorners = @(
    @{ Size = 32; Frame = 2, 5, 28, 22, 4; Glass = 4.5, 7.5, 23, 17, 2; Pointer = 15.5, 14.5, 0.9; Stroke = 1.4; Gap = 1.5
        Corner = 'M4.5 9.5A2 2 0 0 1 6.5 7.5H14A9.5 9.5 0 0 1 4.5 17Z'
        Hole = 'M14 7.5H25.5A2 2 0 0 1 27.5 9.5V22.5A2 2 0 0 1 25.5 24.5H6.5A2 2 0 0 1 4.5 22.5V17A9.5 9.5 0 0 0 14 7.5Z' }
    @{ Size = 20; Frame = 1, 3, 18, 14, 2.5; Glass = 3, 5, 14, 10, 1; Pointer = 9.5, 8.5, 0.56; Stroke = 1; Gap = 1
        Corner = 'M3 6A1 1 0 0 1 4 5H9A6 6 0 0 1 3 11Z'
        Hole = 'M9 5H16A1 1 0 0 1 17 6V14A1 1 0 0 1 16 15H4A1 1 0 0 1 3 14V11A6 6 0 0 0 9 5Z' }
    @{ Size = 16; Frame = 1, 3, 14, 11, 2; Glass = 2, 4, 12, 9, 1; Pointer = 8, 6, 0.6; Stroke = 1; Gap = 1
        Corner = 'M2 5A1 1 0 0 1 3 4H7A5 5 0 0 1 2 9Z'
        Hole = 'M7 4H13A1 1 0 0 1 14 5V12A1 1 0 0 1 13 13H3A1 1 0 0 1 2 12V9A5 5 0 0 0 7 4Z' }
)
foreach ($icon in $hotCorners) {
    $tipX, $tipY, $scale = $icon.Pointer
    $points = @($pointer | ForEach-Object { , @(($tipX + $_[0] * $scale), ($tipY + $_[1] * $scale)) })
    $shape = Get-PolygonPath $points
    $clip = @(Get-GapClip $icon.Size (Get-GrownPath $points ($icon.Stroke / 2 + $icon.Gap)))
    $stroke = " stroke-width=`"$(F $icon.Stroke)`" stroke-linejoin=`"round`""
    $defs = @(Get-Gradient 'frame') + @(Get-Gradient 'highlight') + @(Get-Gradient 'glass') + @(Get-Gradient 'accent') + $clip
    $body = @(Get-Group 'clip-path="url(#gap)"' @((Get-Rect $icon.Frame ' fill="url(#frame)"'), (Get-Rect $icon.Frame ' fill="url(#highlight)"'),
            (Get-Rect $icon.Glass ' fill="url(#glass)"'), "<path fill=`"url(#accent)`" d=`"$($icon.Corner)`"/>")) +
        @("<path fill=`"#FFFFFF`" stroke=`"$outline`"$stroke d=`"$shape`"/>")
    Write-Drawing "hot-corners-$($icon.Size)" $icon.Size $defs $body
    $mono = @(Get-Group 'clip-path="url(#gap)"' @("<path fill=`"currentColor`" fill-rule=`"evenodd`" d=`"$(Get-RectPath $icon.Frame)$($icon.Hole)`"/>")) +
        @("<path fill=`"currentColor`" stroke=`"currentColor`"$stroke d=`"$shape`"/>")
    Write-Drawing "hot-corners-$($icon.Size)-mono" $icon.Size $clip $mono
}

# Clipboard Actions: a clipboard (board, paper, lines of text and its clip) with an amber bolt.
$clipboardActions = @(
    @{ Size = 32; Board = 4, 4, 22, 26, 4; Paper = 7, 8, 16, 19, 1.5; Lines = @(@(10, 12, 10, 2, 1), @(10, 16.5, 6, 2, 1)); Clip = 10, 2, 10, 5.5, 1.75
        Bolt = @(@(26, 15), @(18.5, 24.5), @(23.5, 24.5), @(21.5, 31.5), @(29.5, 21.5), @(24.5, 21.5)); Stroke = 1; Gap = 1.7 }
    @{ Size = 20; Board = 2, 2, 14, 17, 2.5; Paper = 4, 5, 10, 12, 1; Lines = @(@(6, 7, 6, 1, 0), @(6, 10, 4, 1, 0)); Clip = 6, 1, 6, 3.5, 1
        Bolt = @(@(16.5, 9), @(11.5, 15.5), @(14.5, 15.5), @(13.5, 19.5), @(18.5, 13), @(15.5, 13)); Stroke = 0.8; Gap = 1.2 }
    @{ Size = 16; Board = 2, 2, 10, 13, 2; Paper = 3, 4, 8, 10, 0.5; Lines = @(@(4, 6, 5, 1, 0), @(4, 8, 3, 1, 0)); Clip = 4, 1, 6, 2.5, 1
        Bolt = @(@(13.7, 6.8), @(9.6, 12), @(12.3, 12), @(11.2, 15.6), @(15.6, 10.3), @(12.9, 10.3)); Stroke = 0.6; Gap = 1 }
)
foreach ($icon in $clipboardActions) {
    $clip = @(Get-GapClip $icon.Size (Get-GrownPath $icon.Bolt ($icon.Stroke / 2 + $icon.Gap)))
    $stroke = " stroke-width=`"$(F $icon.Stroke)`" stroke-linejoin=`"round`""
    $bolt = Get-PolygonPath $icon.Bolt
    $defs = @(Get-Gradient 'frame') + @(Get-Gradient 'highlight') + @(Get-Gradient 'glass') + @(Get-Gradient 'deep') + @(Get-Gradient 'accent') + $clip
    $items = @((Get-Rect $icon.Board ' fill="url(#frame)"'), (Get-Rect $icon.Board ' fill="url(#highlight)"'), (Get-Rect $icon.Paper ' fill="url(#glass)"')) +
        @($icon.Lines | ForEach-Object { Get-Rect $_ " fill=`"$detail`"" }) + @(Get-Rect $icon.Clip ' fill="url(#deep)"')
    $body = @(Get-Group 'clip-path="url(#gap)"' $items) + @("<path fill=`"url(#accent)`" stroke=`"url(#accent)`"$stroke d=`"$bolt`"/>")
    Write-Drawing "clipboard-actions-$($icon.Size)" $icon.Size $defs $body
    $outlines = (Get-RectPath $icon.Board) + (Get-RectPath $icon.Paper) + (($icon.Lines | ForEach-Object { Get-RectPath $_ }) -join '')
    $mono = @(Get-Group 'clip-path="url(#gap)"' @("<path fill=`"currentColor`" fill-rule=`"evenodd`" d=`"$outlines`"/>", (Get-Rect $icon.Clip ' fill="currentColor"'))) +
        @("<path fill=`"currentColor`" stroke=`"currentColor`"$stroke d=`"$bolt`"/>")
    Write-Drawing "clipboard-actions-$($icon.Size)-mono" $icon.Size $clip $mono
}

# System Meters: three meters, each a track with a channel of glass and an amber level in it.
# Tracks are the tracks' x positions; Track is their y, width, height and corner radius; Inset is
# the space between a track's edge and its channel.
$systemMeters = @(
    @{ Size = 32; Tracks = 3.5, 12.5, 21.5; Track = 3, 7, 26, 3.5; Channel = 3; Inset = 2; Levels = 12, 19, 7.5 }
    @{ Size = 20; Tracks = 1, 8, 15; Track = 2, 4, 16, 2; Channel = 2; Inset = 1; Levels = 7, 11, 4 }
    @{ Size = 16; Tracks = 1, 6, 11; Track = 1, 4, 14, 2; Channel = 2; Inset = 1; Levels = 6, 9, 3 }
)
foreach ($icon in $systemMeters) {
    $y, $w, $h, $rx = $icon.Track
    $ch = $icon.Channel
    $r = $ch / 2
    $top = $y + $icon.Inset
    $bottom = $y + $h - $icon.Inset
    $tracks = @($icon.Tracks | ForEach-Object { , @($_, $y, $w, $h, $rx) })
    $channels = @($icon.Tracks | ForEach-Object { $_ + ($w - $ch) / 2 })
    $body = @($tracks | ForEach-Object { Get-Rect $_ ' fill="url(#frame)"' }) + @($tracks | ForEach-Object { Get-Rect $_ ' fill="url(#highlight)"' }) +
        @($channels | ForEach-Object { Get-Rect @($_, $top, $ch, ($bottom - $top), $r) ' fill="url(#glass)"' })
    for ($i = 0; $i -lt 3; $i++) { $body += Get-Rect @($channels[$i], ($bottom - $icon.Levels[$i]), $ch, $icon.Levels[$i], $r) ' fill="url(#accent)"' }
    Write-Drawing "system-meters-$($icon.Size)" $icon.Size (@(Get-Gradient 'frame') + @(Get-Gradient 'highlight') + @(Get-Gradient 'glass') + @(Get-Gradient 'accent')) $body
    # The one-color version cuts out the channel above each level.
    $outlines = ''
    for ($i = 0; $i -lt 3; $i++) {
        $c = $channels[$i]
        $level = $bottom - $icon.Levels[$i]
        $outlines += (Get-RectPath $tracks[$i]) +
            "M$(F $c) $(F ($level + $r))V$(F ($top + $r))A$(F $r) $(F $r) 0 0 1 $(F ($c + $ch)) $(F ($top + $r))" +
            "V$(F ($level + $r))A$(F $r) $(F $r) 0 0 0 $(F $c) $(F ($level + $r))Z"
    }
    Write-Drawing "system-meters-$($icon.Size)-mono" $icon.Size @() @("<path fill=`"currentColor`" fill-rule=`"evenodd`" d=`"$outlines`"/>")
}

Write-Host "Wrote the drawings in $folder"
