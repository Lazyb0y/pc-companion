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

# The feature icons' color families in docs\design\icons.md, each from its light tone to its
# deepest, and their other colors. A family's tones are named <family>-<tone>, such as green-main.
$families = [ordered]@{
    sky    = @('#9DEAFF', '#58AAFE'), @('#29C3FF', '#2764E7'), @('#0094F0', '#2052CB'), @('#0067BF', '#003580')
    blue   = @('#97B3FF', '#7799FF'), @('#5A86FF', '#4A6BF0'), @('#4A5FE8', '#3940D6'), @('#3940D6', '#2B2FA8')
    violet = @('#BD96FF', '#9C6CFE'), @('#9C6CFE', '#7A41DC'), @('#885EDB', '#5B2AB5'), @('#6D37CD', '#3B148A')
    green  = @('#B0F098', '#64DE89'), @('#52D17C', '#22918B'), @('#20AC9D', '#1A7F7C'), @('#1A7F7C', '#0E5C5A')
}
$tones = 'light', 'main', 'deep', 'deepest'
foreach ($family in $families.Keys) {
    for ($i = 0; $i -lt $tones.Count; $i++) {
        $from, $to = $families[$family][$i]
        $gradients["$family-$($tones[$i])"] = @{ End = '1'; Stops = "stop-color=`"$from`"", "stop-color=`"$to`"" }
    }
}
$gradients['paper'] = @{ End = '1'; Stops = 'stop-color="#F8FAFF"', 'stop-color="#DCE6FF"' }
$gradients['orange'] = @{ End = '1'; Stops = 'stop-color="#FFD25E"', 'stop-color="#FF8A3C"' }
$gradients['yellow'] = @{ End = '1'; Stops = 'stop-color="#FFE06B"', 'stop-color="#FAB500"' }
$gradients['yellow-back'] = @{ End = '1'; Stops = 'stop-color="#FFA43D"', 'stop-color="#E67505"' }
$gradients['red'] = @{ End = '1'; Stops = 'stop-color="#F83F54"', 'stop-color="#CA2134"' }
$white = '#FFFFFF'

# Shape arithmetic for the one-color versions, which cut white symbols, paper, the cuts between
# planes and the cut around a second object out of the shapes: the geometry of WPF, which comes
# with Windows.
Add-Type -AssemblyName PresentationCore
$tolerance = 0.001

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

# An ellipse as path data.
function Get-EllipsePath([double] $CenterX, [double] $CenterY, [double] $RadiusX, [double] $RadiusY) {
    "M$(F ($CenterX - $RadiusX)) $(F $CenterY)A$(F $RadiusX) $(F $RadiusY) 0 1 0 $(F ($CenterX + $RadiusX)) $(F $CenterY)" +
    "A$(F $RadiusX) $(F $RadiusY) 0 1 0 $(F ($CenterX - $RadiusX)) $(F $CenterY)Z"
}

# An eye's almond, $Width wide: two curves through its corners whose control points are $Height
# above and below its middle.
function Get-LensPath([double] $CenterX, [double] $CenterY, [double] $Width, [double] $Height) {
    "M$(F ($CenterX - $Width / 2)) $(F $CenterY)Q$(F $CenterX) $(F ($CenterY - $Height)) $(F ($CenterX + $Width / 2)) $(F $CenterY)" +
    "Q$(F $CenterX) $(F ($CenterY + $Height)) $(F ($CenterX - $Width / 2)) $(F $CenterY)Z"
}

# The line from x1, y1 to x2, y2 grown by $Radius with round ends, as path data.
function Get-CapsulePath([double[]] $Line, [double] $Radius) {
    $x1, $y1, $x2, $y2 = $Line
    $dx = $x2 - $x1
    $dy = $y2 - $y1
    $length = [math]::Sqrt($dx * $dx + $dy * $dy)
    $nx = -$dy / $length * $Radius
    $ny = $dx / $length * $Radius
    $arc = "A$(F $Radius) $(F $Radius) 0 0 0 "
    "M$(F ($x1 + $nx)) $(F ($y1 + $ny))L$(F ($x2 + $nx)) $(F ($y2 + $ny))$arc$(F ($x2 - $nx)) $(F ($y2 - $ny))" +
    "L$(F ($x1 - $nx)) $(F ($y1 - $ny))$arc$(F ($x1 + $nx)) $(F ($y1 + $ny))Z"
}

# A globe's lines, which are glass: its meridian ring, $RingSize's width, height and thickness, as
# path data, and its equator, $Line thick, as boxes in three pieces that meet the ring instead of
# crossing it, both $Rim short of the globe's edge.
function Get-GlobeLines([double] $CenterX, [double] $CenterY, [double] $Radius, [double[]] $RingSize, [double] $Line, [double] $Rim) {
    $cx = $CenterX
    $cy = $CenterY
    $r = $Radius
    $rx, $ry, $t = $RingSize
    $meridian = (Get-EllipsePath $cx $cy $rx $ry) + (Get-EllipsePath $cx $cy ($rx - $t) ($ry - $t))
    $pieces = @(@(
            @(($cx - $r + $Rim), ($cy - $Line / 2), (($cx - $rx) - ($cx - $r + $Rim)), $Line, 0),
            @(($cx - $rx + $t), ($cy - $Line / 2), (2 * ($rx - $t)), $Line, 0),
            @(($cx + $rx), ($cy - $Line / 2), (($cx + $r - $Rim) - ($cx + $rx)), $Line, 0)) | Where-Object { $_[2] -gt 0 })
    return @{ Ring = $meridian; Equator = $pieces }
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

function ConvertTo-Shape([string] $Data) {
    [Windows.Media.Geometry]::Parse($Data)
}

# The area a line $Width wide covers along $Data, with round ends and corners: the area of a white
# symbol drawn as a stroke, or the strip a cut takes out.
function Get-StrokeArea([string] $Data, [double] $Width) {
    $pen = New-Object Windows.Media.Pen -ArgumentList ([Windows.Media.Brushes]::Black), $Width
    $pen.StartLineCap = 'Round'
    $pen.EndLineCap = 'Round'
    $pen.LineJoin = 'Round'
    (ConvertTo-Shape $Data).GetWidenedPathGeometry($pen, $tolerance, 'Absolute')
}

function Join-Shape($Shape, $Other) {
    [Windows.Media.Geometry]::Combine($Shape, $Other, 'Union', $null, $tolerance, 'Absolute')
}

function Remove-Shape($Shape, $Other) {
    [Windows.Media.Geometry]::Combine($Shape, $Other, 'Exclude', $null, $tolerance, 'Absolute')
}

# A filled shape with the stroke drawn round it, or grown by half of $Width: what a polygon drawn
# with round-joined stroke covers, or what a cut round a second object takes out.
function Get-GrownShape([string] $Data, [double] $Width) {
    Join-Shape (ConvertTo-Shape $Data) (Get-StrokeArea $Data $Width)
}

# A shape as a path in the text color, for a one-color version: its path data in the drawings'
# number format, and its fill rule.
function Get-MonoPath($Shape) {
    $geometry = [Windows.Media.PathGeometry]::CreateFromGeometry($Shape)
    $point = { param($p) "$(F $p.X) $(F $p.Y)" }
    $data = foreach ($figure in $geometry.Figures) {
        "M$(& $point $figure.StartPoint)"
        foreach ($segment in $figure.Segments) {
            switch ($segment.GetType().Name) {
                'LineSegment' { "L$(& $point $segment.Point)" }
                'PolyLineSegment' { 'L' + (($segment.Points | ForEach-Object { & $point $_ }) -join ' ') }
                'BezierSegment' { "C$(& $point $segment.Point1) $(& $point $segment.Point2) $(& $point $segment.Point3)" }
                'PolyBezierSegment' { 'C' + (($segment.Points | ForEach-Object { & $point $_ }) -join ' ') }
                'QuadraticBezierSegment' { "Q$(& $point $segment.Point1) $(& $point $segment.Point2)" }
                'PolyQuadraticBezierSegment' { 'Q' + (($segment.Points | ForEach-Object { & $point $_ }) -join ' ') }
                default { throw "A shape has a $_, which the drawings don't write." }
            }
        }
        if ($figure.IsClosed) { 'Z' }
    }
    $rule = if ($geometry.FillRule -eq 'EvenOdd') { ' fill-rule="evenodd"' } else { '' }
    return "<path fill=`"currentColor`"$rule d=`"$($data -join '')`"/>"
}

# A white symbol drawn as a stroke along $Data, $Width wide.
function Get-SymbolPath([string] $Data, [double] $Width, [string] $Paint = $white) {
    "<path fill=`"none`" stroke=`"$Paint`" stroke-width=`"$(F $Width)`" stroke-linecap=`"round`" stroke-linejoin=`"round`" d=`"$Data`"/>"
}

# The letter A, $Height high, centered on x, y, as strokes: its two legs and its bar.
function Get-LetterA([double] $CenterX, [double] $CenterY, [double] $Height, [double] $BarY) {
    $half = $Height * 0.46
    $top = $CenterY - $Height / 2
    $bottom = $CenterY + $Height / 2
    $bar = $half * 0.52
    "M$(F ($CenterX - $half)) $(F $bottom)L$(F $CenterX) $(F $top)L$(F ($CenterX + $half)) $(F $bottom)" +
    "M$(F ($CenterX - $bar)) $(F $BarY)H$(F ($CenterX + $bar))"
}

# The character 文, $Height high, centered on x, y, as strokes: a dot, a bar at $BarY, and two
# strokes that cross below it.
function Get-LetterWen([double] $CenterX, [double] $CenterY, [double] $Height, [double] $BarY) {
    $s = $Height / 2
    $top = $CenterY - $s
    $start = $BarY + $Height * 0.08
    "M$(F $CenterX) $(F $top)V$(F ($top + $Height * 0.12))" +
    "M$(F ($CenterX - $s)) $(F $BarY)H$(F ($CenterX + $s))" +
    "M$(F ($CenterX + $s * 0.55)) $(F $start)Q$(F ($CenterX + $s * 0.2)) $(F ($CenterY + $s * 0.55)) $(F ($CenterX - $s)) $(F ($CenterY + $s))" +
    "M$(F ($CenterX - $s * 0.55)) $(F $start)Q$(F ($CenterX - $s * 0.2)) $(F ($CenterY + $s * 0.55)) $(F ($CenterX + $s)) $(F ($CenterY + $s))"
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

# Hot Corners, in green: a screen with its top-left corner lit, a light plane up to the line
# x + y = Lit, with a white spot, Spot in radius, in the corner itself, and a white pointer whose
# tip is at Pointer's x, y, drawn at Pointer's scale. The one-color version cuts the spot and the
# pointer out, and Cut wide along the edge of the lit plane.
$pointer = @(@(0, 0), @(0, 13), @(3.2, 10), @(5.6, 15.2), @(8, 14.1), @(5.6, 9), @(10, 9))
$hotCorners = @(
    @{ Size = 32; Screen = 1, 4, 30, 24, 2.5; Lit = 19; Spot = 6.5; Pointer = 12, 11.5, 1.05; Stroke = 1; Cut = 1.2 }
    @{ Size = 20; Screen = 1, 2, 18, 16, 1.5; Lit = 12; Spot = 4; Pointer = 7.5, 7, 0.66; Stroke = 0.8; Cut = 1 }
)
foreach ($icon in $hotCorners) {
    $x, $y, $w, $h, $r = $icon.Screen
    $tipX, $tipY, $scale = $icon.Pointer
    $corner = "M$(F $x) $(F ($y + $r))A$(F $r) $(F $r) 0 0 1 $(F ($x + $r)) $(F $y)"
    $plane = "$($corner)H$(F ($icon.Lit - $y))L$(F $x) $(F ($icon.Lit - $x))Z"
    $spot = "$($corner)H$(F ($x + $icon.Spot))A$(F $icon.Spot) $(F $icon.Spot) 0 0 1 $(F $x) $(F ($y + $icon.Spot))Z"
    $arrow = Get-PolygonPath @($pointer | ForEach-Object { , @(($tipX + $_[0] * $scale), ($tipY + $_[1] * $scale)) })
    $body = @((Get-Rect $icon.Screen ' fill="url(#green-main)"'), "<path fill=`"url(#green-light)`" d=`"$plane`"/>", "<path fill=`"$white`" d=`"$spot`"/>",
        "<path fill=`"$white`" stroke=`"$white`" stroke-width=`"$(F $icon.Stroke)`" stroke-linejoin=`"round`" d=`"$arrow`"/>")
    Write-Drawing "hot-corners-$($icon.Size)" $icon.Size (@(Get-Gradient 'green-main') + @(Get-Gradient 'green-light')) $body
    $line = "M$(F ($x - 2)) $(F ($icon.Lit - $x + 2))L$(F ($icon.Lit - $y + 2)) $(F ($y - 2))"
    $shape = ConvertTo-Shape (Get-RectPath $icon.Screen)
    foreach ($cut in (Get-StrokeArea $line $icon.Cut), (ConvertTo-Shape $spot), (Get-GrownShape $arrow $icon.Stroke)) { $shape = Remove-Shape $shape $cut }
    Write-Drawing "hot-corners-$($icon.Size)-mono" $icon.Size @() @(Get-MonoPath $shape)
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

# Lock Keys, in violet: a keycap, its sides in the deepest tone and its face in the main one, with
# the white Caps Lock arrow and bar. The one-color version cuts the arrow and bar out, and Cut wide
# round the face.
$lockKeys = @(
    @{ Size = 32; Cap = 1, 1, 30, 30, 3; Face = 4, 3, 24, 23, 2; Bar = 12, 20, 8, 3, 0.75; Stroke = 1; Cut = 1.2
        Arrow = @(@(16, 7.5), @(22.5, 14.5), @(18.5, 14.5), @(18.5, 18.5), @(13.5, 18.5), @(13.5, 14.5), @(9.5, 14.5)) }
    @{ Size = 20; Cap = 1, 1, 18, 18, 2; Face = 3, 2, 14, 13, 1.5; Bar = 8, 12, 4, 2, 0.5; Stroke = 0.8; Cut = 1
        Arrow = @(@(10, 4.6), @(14.2, 8.6), @(11.6, 8.6), @(11.6, 10.6), @(8.4, 10.6), @(8.4, 8.6), @(5.8, 8.6)) }
)
foreach ($icon in $lockKeys) {
    $arrow = Get-PolygonPath $icon.Arrow
    $body = @((Get-Rect $icon.Cap ' fill="url(#violet-deepest)"'), (Get-Rect $icon.Face ' fill="url(#violet-main)"'),
        "<path fill=`"$white`" stroke=`"$white`" stroke-width=`"$(F $icon.Stroke)`" stroke-linejoin=`"round`" d=`"$arrow`"/>", (Get-Rect $icon.Bar " fill=`"$white`""))
    Write-Drawing "lock-keys-$($icon.Size)" $icon.Size (@(Get-Gradient 'violet-deepest') + @(Get-Gradient 'violet-main')) $body
    $face = Get-RectPath $icon.Face
    $sides = Remove-Shape (ConvertTo-Shape (Get-RectPath $icon.Cap)) (Get-GrownShape $face (2 * $icon.Cut))
    $top = Remove-Shape (Remove-Shape (ConvertTo-Shape $face) (Get-GrownShape $arrow $icon.Stroke)) (ConvertTo-Shape (Get-RectPath $icon.Bar))
    Write-Drawing "lock-keys-$($icon.Size)-mono" $icon.Size @() @(Get-MonoPath (Join-Shape $sides $top))
}

# Input Language per App, in the app icon's blue: an app window, its title bar TitleBar high in the
# deep tone, with a white A, and an orange badge with a white 文 at its bottom right, the language
# the app keeps. A and Wen are each letter's middle, height and the y of its bar, and Letter their
# strokes' width. The one-color version cuts the letters out, and Cut wide under the title bar and
# round the badge.
$inputLanguagePerApp = @(
    @{ Size = 32; Window = 1, 2, 25, 22, 2; TitleBar = 5; Badge = 16, 16, 15, 15, 2.5; A = 10.2, 15.8, 9.6, 17.3; Wen = 23.5, 23.5, 8.4, 21.82
        Letter = 2.4, 2.1; Cut = 1.2 }
    @{ Size = 20; Window = 1, 2, 15, 13, 1.5; TitleBar = 3; Badge = 10, 10, 9, 9, 1.5; A = 6.3, 10, 6, 11.2; Wen = 14.5, 14.5, 6, 13.3
        Letter = 1.6, 1.4; Cut = 1 }
)
foreach ($icon in $inputLanguagePerApp) {
    $x, $y, $w, $h, $r = $icon.Window
    $bar = $y + $icon.TitleBar
    $titleBar = "M$(F ($x + $r)) $(F $y)H$(F ($x + $w - $r))A$(F $r) $(F $r) 0 0 1 $(F ($x + $w)) $(F ($y + $r))V$(F $bar)H$(F $x)V$(F ($y + $r))" +
        "A$(F $r) $(F $r) 0 0 1 $(F ($x + $r)) $(F $y)Z"
    $ax, $ay, $aHeight, $aBar = $icon.A
    $letterA = Get-LetterA $ax $ay $aHeight $aBar
    $wx, $wy, $wenHeight, $wenBar = $icon.Wen
    $wen = Get-LetterWen $wx $wy $wenHeight $wenBar
    $aWidth, $wenWidth = $icon.Letter
    $body = @((Get-Rect $icon.Window ' fill="url(#blue-main)"'), "<path fill=`"url(#blue-deep)`" d=`"$titleBar`"/>", (Get-SymbolPath $letterA $aWidth),
        (Get-Rect $icon.Badge ' fill="url(#orange)"'), (Get-SymbolPath $wen $wenWidth))
    Write-Drawing "input-language-per-app-$($icon.Size)" $icon.Size (@(Get-Gradient 'blue-main') + @(Get-Gradient 'blue-deep') + @(Get-Gradient 'orange')) $body
    $badge = Get-RectPath $icon.Badge
    $window = ConvertTo-Shape (Get-RectPath $icon.Window)
    foreach ($cut in (Get-StrokeArea "M$(F ($x - 2)) $(F $bar)H$(F ($x + $w + 2))" $icon.Cut), (Get-StrokeArea $letterA $aWidth), (Get-GrownShape $badge (2 * $icon.Cut))) {
        $window = Remove-Shape $window $cut
    }
    $sign = Remove-Shape (ConvertTo-Shape $badge) (Get-StrokeArea $wen $wenWidth)
    Write-Drawing "input-language-per-app-$($icon.Size)-mono" $icon.Size @() @(Get-MonoPath (Join-Shape $window $sign))
}

# The gradients most icons use.
$base = @(Get-Gradient 'frame') + @(Get-Gradient 'highlight') + @(Get-Gradient 'glass') + @(Get-Gradient 'accent')

# App Windows: a window switcher (frame) with three window tiles (glass). The selected tile, in
# the middle, is larger and holds amber.
$appWindows = @(
    @{ Size = 32; Panel = 2, 6, 28, 20, 4; Tiles = @(@(5, 9.5, 6, 13, 1.5), @(12.5, 8.5, 7, 15, 1.5), @(21, 9.5, 6, 13, 1.5)); Selected = 14, 10, 4, 12, 1 }
    @{ Size = 20; Panel = 1, 4, 18, 12, 2.5; Tiles = @(@(3, 6, 4, 8, 1), @(8, 5, 4, 10, 1), @(13, 6, 4, 8, 1)); Selected = 9, 6, 2, 8, 0.5 }
    @{ Size = 16; Panel = 1, 3, 14, 10, 2; Tiles = @(@(2, 5, 3, 6, 0.5), @(6, 4, 4, 8, 0.5), @(11, 5, 3, 6, 0.5)); Selected = 7, 5, 2, 6, 0 }
)
foreach ($icon in $appWindows) {
    $body = @((Get-Rect $icon.Panel ' fill="url(#frame)"'), (Get-Rect $icon.Panel ' fill="url(#highlight)"')) +
        @($icon.Tiles | ForEach-Object { Get-Rect $_ ' fill="url(#glass)"' }) + @(Get-Rect $icon.Selected ' fill="url(#accent)"')
    Write-Drawing "app-windows-$($icon.Size)" $icon.Size $base $body
    $outlines = (Get-RectPath $icon.Panel) + (($icon.Tiles | ForEach-Object { Get-RectPath $_ }) -join '') + (Get-RectPath $icon.Selected)
    Write-Drawing "app-windows-$($icon.Size)-mono" $icon.Size @() @("<path fill=`"currentColor`" fill-rule=`"evenodd`" d=`"$outlines`"/>")
}

# Minimize to Tray: a window with an amber arrow down to a bar, the notification area, at its
# bottom right.
$minimizeToTray = @(
    @{ Size = 32; Frame = 2, 2, 23, 19, 3.5; Glass = 4.5, 4.5, 18, 14, 1.5; Bar = 18.5, 28, 11, 2.5, 1.25; Stroke = 1; Gap = 1.5
        Arrow = @(@(22, 12), @(26, 12), @(26, 18), @(29.5, 18), @(24, 23.5), @(18.5, 18), @(22, 18)) }
    @{ Size = 20; Frame = 1, 2, 15, 12, 2.5; Glass = 3, 4, 11, 8, 1; Bar = 11, 18.5, 8, 1.5, 0.75; Stroke = 0.8; Gap = 1
        Arrow = @(@(13.5, 7.5), @(16.5, 7.5), @(16.5, 11.5), @(19, 11.5), @(15, 15.5), @(11, 11.5), @(13.5, 11.5)) }
    @{ Size = 16; Frame = 1, 1, 12, 10, 2; Glass = 2, 2, 10, 8, 1; Bar = 9, 14.5, 6, 1, 0.5; Stroke = 0.6; Gap = 1
        Arrow = @(@(11, 6), @(13, 6), @(13, 9), @(15, 9), @(12, 12), @(9, 9), @(11, 9)) }
)
foreach ($icon in $minimizeToTray) {
    $bx, $by, $bw, $bh, $br = $icon.Bar
    $g = $icon.Gap
    $outline = (Get-GrownPath $icon.Arrow ($icon.Stroke / 2 + $g)) + (Get-RectPath @(($bx - $g), ($by - $g), ($bw + 2 * $g), ($bh + 2 * $g), ($br + $g)))
    $clip = @(Get-GapClip $icon.Size $outline)
    $stroke = " stroke-width=`"$(F $icon.Stroke)`" stroke-linejoin=`"round`""
    $arrow = Get-PolygonPath $icon.Arrow
    $body = @(Get-Group 'clip-path="url(#gap)"' @((Get-Rect $icon.Frame ' fill="url(#frame)"'), (Get-Rect $icon.Frame ' fill="url(#highlight)"'), (Get-Rect $icon.Glass ' fill="url(#glass)"'))) +
        @("<path fill=`"url(#accent)`" stroke=`"url(#accent)`"$stroke d=`"$arrow`"/>", (Get-Rect $icon.Bar ' fill="url(#accent)"'))
    Write-Drawing "minimize-to-tray-$($icon.Size)" $icon.Size ($base + $clip) $body
    $mono = @(Get-Group 'clip-path="url(#gap)"' @("<path fill=`"currentColor`" fill-rule=`"evenodd`" d=`"$(Get-RectPath $icon.Frame)$(Get-RectPath $icon.Glass)`"/>")) +
        @("<path fill=`"currentColor`" stroke=`"currentColor`"$stroke d=`"$arrow`"/>", (Get-Rect $icon.Bar ' fill="currentColor"'))
    Write-Drawing "minimize-to-tray-$($icon.Size)-mono" $icon.Size $clip $mono
}

# Hide Apps: a blue eye (frame) with a glass iris and a deep pupil, and an amber slash through it,
# which a gap separates from the eye. Eye is its middle, width and the height of its curves.
$hideApps = @(
    @{ Size = 32; Eye = 16, 16, 29, 18; Iris = 5.5; Pupil = 2.4; Slash = 6.5, 26.5, 25.5, 5.5; Width = 2.6; Gap = 1.5 }
    @{ Size = 20; Eye = 10, 10, 18.5, 11.5; Iris = 3.5; Pupil = 1.5; Slash = 4, 16.5, 16, 3.5; Width = 1.8; Gap = 1 }
    @{ Size = 16; Eye = 8, 8, 15, 9.5; Iris = 2.8; Pupil = 1.1; Slash = 3.2, 13.3, 12.8, 2.7; Width = 1.5; Gap = 1 }
)
foreach ($icon in $hideApps) {
    $cx, $cy, $w, $h = $icon.Eye
    $x1, $y1, $x2, $y2 = $icon.Slash
    $clip = @(Get-GapClip $icon.Size (Get-CapsulePath $icon.Slash ($icon.Width / 2 + $icon.Gap)))
    $eye = Get-LensPath $cx $cy $w $h
    $iris = Get-EllipsePath $cx $cy $icon.Iris $icon.Iris
    $pupil = Get-EllipsePath $cx $cy $icon.Pupil $icon.Pupil
    $slash = "<path fill=`"none`" stroke=`"{0}`" stroke-width=`"$(F $icon.Width)`" stroke-linecap=`"round`" d=`"M$(F $x1) $(F $y1)L$(F $x2) $(F $y2)`"/>"
    $body = @(Get-Group 'clip-path="url(#gap)"' @("<path fill=`"url(#frame)`" d=`"$eye`"/>", "<path fill=`"url(#highlight)`" d=`"$eye`"/>",
            "<path fill=`"url(#glass)`" d=`"$iris`"/>", "<path fill=`"url(#deep)`" d=`"$pupil`"/>")) + @($slash -f 'url(#accent)')
    Write-Drawing "hide-apps-$($icon.Size)" $icon.Size ($base + @(Get-Gradient 'deep') + $clip) $body
    $mono = @(Get-Group 'clip-path="url(#gap)"' @("<path fill=`"currentColor`" fill-rule=`"evenodd`" d=`"$eye$iris$pupil`"/>")) + @($slash -f 'currentColor')
    Write-Drawing "hide-apps-$($icon.Size)-mono" $icon.Size $clip $mono
}

# Drop-down Apps: a screen with an amber panel hanging from the top of its glass, and a chevron
# (detail) below it, pointing down.
$dropDownApps = @(
    @{ Size = 32; Frame = 2, 5, 28, 22, 4; Glass = 4.5, 7.5, 23, 17, 2; Panel = 'M8 7.5H24V15A2 2 0 0 1 22 17H10A2 2 0 0 1 8 15Z'
        Chevron = @(@(12.5, 19.5), @(16, 22.5), @(19.5, 19.5)); Width = 1.8 }
    @{ Size = 20; Frame = 1, 3, 18, 14, 2.5; Glass = 3, 5, 14, 10, 1; Panel = 'M5 5H15V9A1 1 0 0 1 14 10H6A1 1 0 0 1 5 9Z'
        Chevron = @(@(7.5, 11.5), @(10, 13.5), @(12.5, 11.5)); Width = 1.2 }
    @{ Size = 16; Frame = 1, 3, 14, 11, 2; Glass = 2, 4, 12, 9, 1; Panel = 'M4 4H12V7.5A1 1 0 0 1 11 8.5H5A1 1 0 0 1 4 7.5Z'
        Chevron = @(@(6, 10), @(8, 11.5), @(10, 10)); Width = 1 }
)
foreach ($icon in $dropDownApps) {
    $chevron = 'M' + (($icon.Chevron | ForEach-Object { "$(F $_[0]) $(F $_[1])" }) -join 'L')
    $line = " stroke-width=`"$(F $icon.Width)`" stroke-linecap=`"round`" stroke-linejoin=`"round`""
    $body = @((Get-Rect $icon.Frame ' fill="url(#frame)"'), (Get-Rect $icon.Frame ' fill="url(#highlight)"'), (Get-Rect $icon.Glass ' fill="url(#glass)"'),
        "<path fill=`"url(#accent)`" d=`"$($icon.Panel)`"/>", "<path fill=`"none`" stroke=`"$detail`"$line d=`"$chevron`"/>")
    Write-Drawing "drop-down-apps-$($icon.Size)" $icon.Size $base $body
    $mono = @("<path fill=`"currentColor`" fill-rule=`"evenodd`" d=`"$(Get-RectPath $icon.Frame)$(Get-RectPath $icon.Glass)`"/>",
        "<path fill=`"currentColor`" d=`"$($icon.Panel)`"/>", "<path fill=`"none`" stroke=`"currentColor`"$line d=`"$chevron`"/>")
    Write-Drawing "drop-down-apps-$($icon.Size)-mono" $icon.Size @() $mono
}

# Title Bar Actions: a mouse (frame and glass) with a line under its buttons (frame) and an amber
# wheel, for the middle click and the wheel the feature uses.
$titleBarActions = @(
    @{ Size = 32; Body = 8, 2, 16, 28, 7.5; Glass = 10.5, 4.5, 11, 23, 5.5; Divider = 10.5, 15, 11, 1.5, 0; Wheel = 14.5, 7, 3, 6, 1.5 }
    @{ Size = 20; Body = 5, 1, 10, 18, 5; Glass = 6.5, 2.5, 7, 15, 3.5; Divider = 6.5, 9.5, 7, 1, 0; Wheel = 9, 4, 2, 4, 1 }
    @{ Size = 16; Body = 4, 1, 8, 14, 4; Glass = 5, 2, 6, 12, 3; Divider = 5, 7.5, 6, 1, 0; Wheel = 7, 3, 2, 3.5, 1 }
)
foreach ($icon in $titleBarActions) {
    $body = @((Get-Rect $icon.Body ' fill="url(#frame)"'), (Get-Rect $icon.Body ' fill="url(#highlight)"'), (Get-Rect $icon.Glass ' fill="url(#glass)"'),
        (Get-Rect $icon.Divider ' fill="url(#frame)"'), (Get-Rect $icon.Wheel ' fill="url(#accent)"'))
    Write-Drawing "title-bar-actions-$($icon.Size)" $icon.Size $base $body
    $mono = @("<path fill=`"currentColor`" fill-rule=`"evenodd`" d=`"$(Get-RectPath $icon.Body)$(Get-RectPath $icon.Glass)$(Get-RectPath $icon.Wheel)`"/>",
        (Get-Rect $icon.Divider ' fill="currentColor"'))
    Write-Drawing "title-bar-actions-$($icon.Size)-mono" $icon.Size @() $mono
}

# An amber polygon at an icon's bottom right: its path, its stroke and the clip that cuts its gap
# out of the shapes under it.
function Get-Badge([object[]] $Points, [double] $Stroke, [double] $Gap, [int] $Size) {
    @{
        Path = Get-PolygonPath $Points
        Clip = @(Get-GapClip $Size (Get-GrownPath $Points ($Stroke / 2 + $Gap)))
        Stroke = " stroke-width=`"$(F $Stroke)`" stroke-linejoin=`"round`""
    }
}

function Get-BadgePath([hashtable] $Badge, [string] $Paint) {
    "<path fill=`"$Paint`" stroke=`"$Paint`"$($Badge.Stroke) d=`"$($Badge.Path)`"/>"
}

# Network Profiles: a blue globe whose lines are glass, and an amber bookmark, a saved profile.
$networkProfiles = @(
    @{ Size = 32; Globe = 15, 15, 12.5; Ring = 5.5, 10.5, 1.5; Line = 1.5; Rim = 1.8; Stroke = 1; Gap = 1.5
        Ribbon = @(@(20, 17), @(29, 17), @(29, 30), @(24.5, 26.5), @(20, 30)) }
    @{ Size = 20; Globe = 9.5, 9.5, 8; Ring = 3.5, 6.8, 1; Line = 1; Rim = 1.2; Stroke = 0.8; Gap = 1
        Ribbon = @(@(13, 11), @(19, 11), @(19, 19.5), @(16, 17.5), @(13, 19.5)) }
    @{ Size = 16; Globe = 7.5, 7.5, 6.5; Ring = 2.8, 5.5, 1; Line = 1; Rim = 1; Stroke = 0.6; Gap = 1
        Ribbon = @(@(10.5, 9), @(15, 9), @(15, 15.5), @(12.75, 14), @(10.5, 15.5)) }
)
foreach ($icon in $networkProfiles) {
    $cx, $cy, $r = $icon.Globe
    $lines = Get-GlobeLines $cx $cy $r $icon.Ring $icon.Line $icon.Rim
    $ribbon = Get-Badge $icon.Ribbon $icon.Stroke $icon.Gap $icon.Size
    $disk = Get-EllipsePath $cx $cy $r $r
    $items = @("<path fill=`"url(#frame)`" d=`"$disk`"/>", "<path fill=`"url(#highlight)`" d=`"$disk`"/>", "<path fill=`"url(#glass)`" fill-rule=`"evenodd`" d=`"$($lines.Ring)`"/>") +
        @($lines.Equator | ForEach-Object { Get-Rect $_ ' fill="url(#glass)"' })
    $body = @(Get-Group 'clip-path="url(#gap)"' $items) + @(Get-BadgePath $ribbon 'url(#accent)')
    Write-Drawing "network-profiles-$($icon.Size)" $icon.Size ($base + $ribbon.Clip) $body
    $holes = $lines.Ring + (($lines.Equator | ForEach-Object { Get-RectPath $_ }) -join '')
    $mono = @(Get-Group 'clip-path="url(#gap)"' @("<path fill=`"currentColor`" fill-rule=`"evenodd`" d=`"$disk$holes`"/>")) + @(Get-BadgePath $ribbon 'currentColor')
    Write-Drawing "network-profiles-$($icon.Size)-mono" $icon.Size $ribbon.Clip $mono
}

# Display Profiles: a monitor (frame and glass, a deep neck and a foot) with the amber bookmark.
$displayProfiles = @(
    @{ Size = 32; Frame = 2, 3, 28, 19, 3.5; Glass = 4.5, 5.5, 23, 14, 1.5; Neck = 13, 22, 6, 3.5, 0; Foot = 9, 25, 14, 2.5, 1.25; Stroke = 1; Gap = 1.5
        Ribbon = @(@(21, 15), @(29, 15), @(29, 28), @(25, 24.5), @(21, 28)) }
    @{ Size = 20; Frame = 1, 2, 18, 12, 2.5; Glass = 3, 4, 14, 8, 1; Neck = 8, 14, 4, 2, 0; Foot = 5.5, 16, 9, 1.5, 0.75; Stroke = 0.8; Gap = 1
        Ribbon = @(@(13.5, 9), @(19, 9), @(19, 18), @(16.25, 16), @(13.5, 18)) }
    @{ Size = 16; Frame = 1, 1, 14, 10, 2; Glass = 2, 2, 12, 8, 1; Neck = 6.5, 11, 3, 2, 0; Foot = 4, 13, 8, 1.5, 0.75; Stroke = 0.6; Gap = 1
        Ribbon = @(@(11, 7.5), @(15, 7.5), @(15, 14.5), @(13, 13), @(11, 14.5)) }
)
foreach ($icon in $displayProfiles) {
    $ribbon = Get-Badge $icon.Ribbon $icon.Stroke $icon.Gap $icon.Size
    $items = @((Get-Rect $icon.Neck ' fill="url(#deep)"'), (Get-Rect $icon.Foot ' fill="url(#frame)"'), (Get-Rect $icon.Frame ' fill="url(#frame)"'),
        (Get-Rect $icon.Frame ' fill="url(#highlight)"'), (Get-Rect $icon.Glass ' fill="url(#glass)"'))
    $body = @(Get-Group 'clip-path="url(#gap)"' $items) + @(Get-BadgePath $ribbon 'url(#accent)')
    Write-Drawing "display-profiles-$($icon.Size)" $icon.Size ($base + @(Get-Gradient 'deep') + $ribbon.Clip) $body
    $mono = @(Get-Group 'clip-path="url(#gap)"' @((Get-Rect $icon.Neck ' fill="currentColor"'), (Get-Rect $icon.Foot ' fill="currentColor"'),
            "<path fill=`"currentColor`" fill-rule=`"evenodd`" d=`"$(Get-RectPath $icon.Frame)$(Get-RectPath $icon.Glass)`"/>")) + @(Get-BadgePath $ribbon 'currentColor')
    Write-Drawing "display-profiles-$($icon.Size)-mono" $icon.Size $ribbon.Clip $mono
}

# Folder Rules: a folder (its tab and body in frame, its front in glass) with an amber funnel, the
# rules that sort its files.
$folderRules = @(
    @{ Size = 32; Tab = 2, 3.5, 12, 5, 2; Body = 2, 6, 28, 20, 3.5; Front = 4.5, 10.5, 23, 13, 1.5; Stroke = 1; Gap = 1.5
        Funnel = @(@(19, 16), @(30, 16), @(26, 21), @(26, 27), @(23, 29), @(23, 21)) }
    @{ Size = 20; Tab = 1, 3, 8, 4, 1.5; Body = 1, 5, 18, 12, 2.5; Front = 3, 8, 14, 7, 1; Stroke = 0.8; Gap = 1
        Funnel = @(@(12, 11), @(19, 11), @(16.5, 14.5), @(16.5, 18.5), @(14.5, 19.5), @(14.5, 14.5)) }
    @{ Size = 16; Tab = 1, 2, 6, 3, 1; Body = 1, 4, 14, 10, 2; Front = 2, 6.5, 12, 6.5, 1; Stroke = 0.6; Gap = 1
        Funnel = @(@(9.5, 9), @(15, 9), @(13, 11.5), @(13, 14.5), @(11.5, 15.5), @(11.5, 11.5)) }
)
foreach ($icon in $folderRules) {
    $funnel = Get-Badge $icon.Funnel $icon.Stroke $icon.Gap $icon.Size
    $items = @((Get-Rect $icon.Tab ' fill="url(#frame)"'), (Get-Rect $icon.Body ' fill="url(#frame)"'), (Get-Rect $icon.Body ' fill="url(#highlight)"'),
        (Get-Rect $icon.Front ' fill="url(#glass)"'))
    $body = @(Get-Group 'clip-path="url(#gap)"' $items) + @(Get-BadgePath $funnel 'url(#accent)')
    Write-Drawing "folder-rules-$($icon.Size)" $icon.Size ($base + $funnel.Clip) $body
    $mono = @(Get-Group 'clip-path="url(#gap)"' @((Get-Rect $icon.Tab ' fill="currentColor"'),
            "<path fill=`"currentColor`" fill-rule=`"evenodd`" d=`"$(Get-RectPath $icon.Body)$(Get-RectPath $icon.Front)`"/>")) + @(Get-BadgePath $funnel 'currentColor')
    Write-Drawing "folder-rules-$($icon.Size)-mono" $icon.Size $funnel.Clip $mono
}

# Drive Actions: a USB drive (a deep plug with glass holes, and a body with a glass label) with
# the same amber bolt as Clipboard Actions.
$driveActions = @(
    @{ Size = 32; Plug = 11.5, 2, 9, 8, 1.5; Holes = @(@(13.5, 4, 2, 2.5, 0.5), @(16.5, 4, 2, 2.5, 0.5)); Body = 9, 9, 14, 21, 3.5; Label = 11.5, 12.5, 9, 12, 1.5
        Bolt = @(@(26, 15), @(18.5, 24.5), @(23.5, 24.5), @(21.5, 31.5), @(29.5, 21.5), @(24.5, 21.5)); Stroke = 1; Gap = 1.7 }
    @{ Size = 20; Plug = 7, 1, 6, 5, 1; Holes = @(@(8, 2.5, 1.5, 1.5, 0), @(10.5, 2.5, 1.5, 1.5, 0)); Body = 5.5, 5.5, 9, 13.5, 2.5; Label = 7.5, 7.5, 5, 7, 1
        Bolt = @(@(16.5, 9), @(11.5, 15.5), @(14.5, 15.5), @(13.5, 19.5), @(18.5, 13), @(15.5, 13)); Stroke = 0.8; Gap = 1.2 }
    @{ Size = 16; Plug = 5.5, 1, 5, 4, 1; Holes = @(@(6.5, 2, 1, 1.5, 0), @(8.5, 2, 1, 1.5, 0)); Body = 4, 4.5, 8, 10.5, 2; Label = 5.5, 6.5, 5, 5, 0.5
        Bolt = @(@(13.7, 6.8), @(9.6, 12), @(12.3, 12), @(11.2, 15.6), @(15.6, 10.3), @(12.9, 10.3)); Stroke = 0.6; Gap = 1 }
)
foreach ($icon in $driveActions) {
    $bolt = Get-Badge $icon.Bolt $icon.Stroke $icon.Gap $icon.Size
    $items = @(Get-Rect $icon.Plug ' fill="url(#deep)"') + @($icon.Holes | ForEach-Object { Get-Rect $_ ' fill="url(#glass)"' }) +
        @((Get-Rect $icon.Body ' fill="url(#frame)"'), (Get-Rect $icon.Body ' fill="url(#highlight)"'), (Get-Rect $icon.Label ' fill="url(#glass)"'))
    $body = @(Get-Group 'clip-path="url(#gap)"' $items) + @(Get-BadgePath $bolt 'url(#accent)')
    Write-Drawing "drive-actions-$($icon.Size)" $icon.Size ($base + @(Get-Gradient 'deep') + $bolt.Clip) $body
    $plug = (Get-RectPath $icon.Plug) + (($icon.Holes | ForEach-Object { Get-RectPath $_ }) -join '')
    $mono = @(Get-Group 'clip-path="url(#gap)"' @("<path fill=`"currentColor`" fill-rule=`"evenodd`" d=`"$plug`"/>",
            "<path fill=`"currentColor`" fill-rule=`"evenodd`" d=`"$(Get-RectPath $icon.Body)$(Get-RectPath $icon.Label)`"/>")) + @(Get-BadgePath $bolt 'currentColor')
    Write-Drawing "drive-actions-$($icon.Size)-mono" $icon.Size $bolt.Clip $mono
}

# End App: an app window (frame and glass) with an amber stop sign, an octagon with a white bar
# (neutral), at its bottom right. Sign is the octagon's middle and half its width.
$endApp = @(
    @{ Size = 32; Frame = 2, 2, 24, 20, 3.5; Glass = 4.5, 4.5, 19, 15, 1.5; Sign = 23.5, 23.5, 7.5; Bar = 19.5, 22.25, 8, 2.5, 0.5; Stroke = 1; Gap = 1.5 }
    @{ Size = 20; Frame = 1, 2, 15, 12, 2.5; Glass = 3, 4, 11, 8, 1; Sign = 14.5, 14.5, 5; Bar = 12, 14, 5, 1, 0; Stroke = 0.8; Gap = 1 }
    @{ Size = 16; Frame = 1, 1, 12, 10, 2; Glass = 2, 2, 10, 8, 1; Sign = 11.5, 11.5, 4.5; Bar = 9, 11, 5, 1, 0; Stroke = 0.6; Gap = 1 }
)
foreach ($icon in $endApp) {
    $cx, $cy, $half = $icon.Sign
    $corner = $half * [Math]::Tan([Math]::PI / 8)
    $octagon = @(@(($cx - $corner), ($cy - $half)), @(($cx + $corner), ($cy - $half)), @(($cx + $half), ($cy - $corner)), @(($cx + $half), ($cy + $corner)),
        @(($cx + $corner), ($cy + $half)), @(($cx - $corner), ($cy + $half)), @(($cx - $half), ($cy + $corner)), @(($cx - $half), ($cy - $corner)))
    $sign = Get-Badge $octagon $icon.Stroke $icon.Gap $icon.Size
    $items = @((Get-Rect $icon.Frame ' fill="url(#frame)"'), (Get-Rect $icon.Frame ' fill="url(#highlight)"'), (Get-Rect $icon.Glass ' fill="url(#glass)"'))
    $body = @(Get-Group 'clip-path="url(#gap)"' $items) + @((Get-BadgePath $sign 'url(#accent)'), (Get-Rect $icon.Bar ' fill="#FFFFFF"'))
    Write-Drawing "end-app-$($icon.Size)" $icon.Size ($base + $sign.Clip) $body
    $mono = @(Get-Group 'clip-path="url(#gap)"' @("<path fill=`"currentColor`" fill-rule=`"evenodd`" d=`"$(Get-RectPath $icon.Frame)$(Get-RectPath $icon.Glass)`"/>")) +
        @("<path fill=`"currentColor`" fill-rule=`"evenodd`" d=`"$(Get-GrownPath $octagon ($icon.Stroke / 2))$(Get-RectPath $icon.Bar)`"/>")
    Write-Drawing "end-app-$($icon.Size)-mono" $icon.Size $sign.Clip $mono
}

Write-Host "Wrote the drawings in $folder"
