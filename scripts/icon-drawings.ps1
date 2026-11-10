<#
.SYNOPSIS
Writes the icon drawings in docs\design\icons from their shapes.

.DESCRIPTION
Each icon is described below by its shapes at each size it's drawn at. The script writes
<name>-<size>.svg and the one-color <name>-<size>-mono.svg for each, using only what Direct2D's
SVG renderer supports, as docs\design\icons.md says: a gap around a shape is a clip path that cuts
the shape, grown by half its stroke and the gap, out of the shapes under it. A one-color version
is one path in currentColor, which the geometry of WPF works out by cutting white symbols, paper
and the cuts between parts out of the shapes.

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

# The app icon's fills in docs\design\icons.md. Each runs from the top left of its shape.
$gradients = @{
    frame     = @{ End = '1'; Stops = 'stop-color="#5A86FF"', 'stop-color="#3940D6"' }
    highlight = @{ End = '.6'; Stops = 'stop-color="#FFFFFF" stop-opacity=".28"', 'stop-color="#FFFFFF" stop-opacity="0"' }
    accent    = @{ End = '1'; Stops = 'stop-color="#FFD25E"', 'stop-color="#FF8A3C"' }
    overlap   = @{ End = '1'; Stops = 'stop-color="#FFF6E6"', 'stop-color="#FFD9B0"' }
}

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

# A crescent as path data: $Moon is the middle and radius of a circle, then the middle and radius of
# the bite taken out of it. The path runs the long way round the circle, then back along the bite.
function Get-CrescentPath([double[]] $Moon) {
    $cx, $cy, $r, $bx, $by, $br = $Moon
    $d = [math]::Sqrt(($bx - $cx) * ($bx - $cx) + ($by - $cy) * ($by - $cy))
    $along = ($r * $r - $br * $br + $d * $d) / (2 * $d)
    $across = [math]::Sqrt($r * $r - $along * $along)
    $ux = ($bx - $cx) / $d
    $uy = ($by - $cy) / $d
    $mx = $cx + $along * $ux
    $my = $cy + $along * $uy
    $x1 = $mx + $across * $uy
    $y1 = $my - $across * $ux
    $x2 = $mx - $across * $uy
    $y2 = $my + $across * $ux
    "M$(F $x1) $(F $y1)A$(F $r) $(F $r) 0 1 0 $(F $x2) $(F $y2)A$(F $br) $(F $br) 0 0 1 $(F $x1) $(F $y1)Z"
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

# A window's title bar, $Height high, with the window's top corners. $Window is the window's x, y,
# width, height and corner radius.
function Get-TitleBarPath([double[]] $Window, [double] $Height) {
    $x, $y, $w, $h, $r = $Window
    "M$(F ($x + $r)) $(F $y)H$(F ($x + $w - $r))A$(F $r) $(F $r) 0 0 1 $(F ($x + $w)) $(F ($y + $r))V$(F ($y + $Height))H$(F $x)V$(F ($y + $r))" +
    "A$(F $r) $(F $r) 0 0 1 $(F ($x + $r)) $(F $y)Z"
}

# The strip a cut $Width wide takes out across a window under its title bar, $Height high.
function Get-TitleBarCut([double[]] $Window, [double] $Height, [double] $Width) {
    $x, $y, $w, $h, $r = $Window
    Get-StrokeArea "M$(F ($x - 2)) $(F ($y + $Height))H$(F ($x + $w + 2))" $Width
}

# A stop sign's octagon round x, y, $Half from its middle to each side, as points.
function Get-OctagonPoints([double] $CenterX, [double] $CenterY, [double] $Half) {
    $cx = $CenterX
    $cy = $CenterY
    $corner = $Half * [Math]::Tan([Math]::PI / 8)
    @(@(($cx - $corner), ($cy - $Half)), @(($cx + $corner), ($cy - $Half)), @(($cx + $Half), ($cy - $corner)), @(($cx + $Half), ($cy + $corner)),
        @(($cx + $corner), ($cy + $Half)), @(($cx - $corner), ($cy + $Half)), @(($cx - $Half), ($cy + $corner)), @(($cx - $Half), ($cy - $corner)))
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
    @{ Size = 20; Screen = 2, 4, 16, 12, 1.5; Lit = 13; Spot = 3.5; Pointer = 8, 7.5, 0.53; Stroke = 0.8; Cut = 1 }
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

# The orange bolt of Clipboard Actions and Drive Actions, an action, at each size: its points, and
# the width of the stroke that rounds its corners.
$bolts = @{
    32 = @{ Points = @(@(26, 14.5), @(18.5, 24), @(23.5, 24), @(21.5, 31), @(29.5, 21), @(24.5, 21)); Stroke = 1 }
    20 = @{ Points = @(@(15.8, 8.9), @(11.6, 14.2), @(14.1, 14.2), @(13.2, 17.6), @(17.6, 12.2), @(15, 12.2)); Stroke = 0.8 }
}

function Get-BoltPath([int] $Size) {
    $bolt = $bolts[$Size]
    $data = Get-PolygonPath $bolt.Points
    "<path fill=`"url(#orange)`" stroke=`"url(#orange)`" stroke-width=`"$(F $bolt.Stroke)`" stroke-linejoin=`"round`" d=`"$data`"/>"
}

function Get-BoltShape([int] $Size) {
    Get-GrownShape (Get-PolygonPath $bolts[$Size].Points) $bolts[$Size].Stroke
}

# Clipboard Actions, in the app icon's blue: a clipboard, its board in the main tone and its clip
# in the deepest, with paper and two lines of text in the light tone, and the orange bolt at its
# bottom right. The one-color version cuts the paper out, and Cut wide round the clip and the bolt.
$clipboardActions = @(
    @{ Size = 32; Board = 2, 3, 24, 28, 2.5; Clip = 9, 1, 10, 5, 1.5; Paper = 6, 8, 16, 19, 1; Lines = @(@(9, 12, 10, 2, 1), @(9, 16, 6, 2, 1)); Cut = 1.2 }
    @{ Size = 20; Board = 2, 3, 12, 15, 1.5; Clip = 5, 2, 6, 3, 1; Paper = 4, 6, 8, 9, 0.5; Lines = @(@(5, 8, 5, 1, 0.5), @(5, 10, 3, 1, 0.5)); Cut = 1 }
)
foreach ($icon in $clipboardActions) {
    $body = @((Get-Rect $icon.Board ' fill="url(#blue-main)"'), (Get-Rect $icon.Clip ' fill="url(#blue-deepest)"'), (Get-Rect $icon.Paper ' fill="url(#paper)"')) +
        @($icon.Lines | ForEach-Object { Get-Rect $_ ' fill="url(#blue-light)"' }) + @(Get-BoltPath $icon.Size)
    $defs = @(@('main', 'deepest', 'light') | ForEach-Object { Get-Gradient "blue-$_" }) + @(Get-Gradient 'paper') + @(Get-Gradient 'orange')
    Write-Drawing "clipboard-actions-$($icon.Size)" $icon.Size $defs $body
    $bolt = Get-BoltShape $icon.Size
    $clip = Get-RectPath $icon.Clip
    $board = Remove-Shape (ConvertTo-Shape (Get-RectPath $icon.Board)) (ConvertTo-Shape (Get-RectPath $icon.Paper))
    foreach ($line in $icon.Lines) { $board = Join-Shape $board (ConvertTo-Shape (Get-RectPath $line)) }
    $board = Remove-Shape (Remove-Shape $board (Get-GrownShape $clip (2 * $icon.Cut))) (Get-GrownShape (Get-PolygonPath $bolts[$icon.Size].Points) ($bolts[$icon.Size].Stroke + 2 * $icon.Cut))
    $clipboard = Join-Shape (Join-Shape $board (ConvertTo-Shape $clip)) $bolt
    Write-Drawing "clipboard-actions-$($icon.Size)-mono" $icon.Size @() @(Get-MonoPath $clipboard)
}

# System Meters, in green: three bars, from light and short to deep and tall.
$systemMeters = @(
    @{ Size = 32; Bars = @(@(1, 19, 8, 12, 1.5), @(12, 11, 8, 20, 1.5), @(23, 1, 8, 30, 1.5)) }
    @{ Size = 20; Bars = @(@(2, 12, 4, 6, 1), @(8, 8, 4, 10, 1), @(14, 2, 4, 16, 1)) }
)
foreach ($icon in $systemMeters) {
    $short, $middle, $tall = $icon.Bars
    $body = @((Get-Rect $short ' fill="url(#green-light)"'), (Get-Rect $middle ' fill="url(#green-main)"'), (Get-Rect $tall ' fill="url(#green-deep)"'))
    Write-Drawing "system-meters-$($icon.Size)" $icon.Size (@('light', 'main', 'deep') | ForEach-Object { Get-Gradient "green-$_" }) $body
    Write-Drawing "system-meters-$($icon.Size)-mono" $icon.Size @() @("<path fill=`"currentColor`" d=`"$(($icon.Bars | ForEach-Object { Get-RectPath $_ }) -join '')`"/>")
}

# Lock Keys, in violet: a keycap, its sides in the deepest tone and its face in the main one, with
# the white Caps Lock arrow and bar. The one-color version cuts the arrow and bar out, and Cut wide
# round the face.
$lockKeys = @(
    @{ Size = 32; Cap = 1, 1, 30, 30, 3; Face = 4, 3, 24, 23, 2; Bar = 12, 20, 8, 3, 0.75; Stroke = 1; Cut = 1.2
        Arrow = @(@(16, 7.5), @(22.5, 14.5), @(18.5, 14.5), @(18.5, 18.5), @(13.5, 18.5), @(13.5, 14.5), @(9.5, 14.5)) }
    @{ Size = 20; Cap = 2, 2, 16, 16, 2; Face = 3, 3, 14, 12, 1.5; Bar = 8, 12, 4, 2, 0.5; Stroke = 0.8; Cut = 1
        Arrow = @(@(10, 5.2), @(13.6, 8.6), @(11.6, 8.6), @(11.6, 10.6), @(8.4, 10.6), @(8.4, 8.6), @(6.4, 8.6)) }
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
    @{ Size = 20; Window = 2, 3, 14, 12, 1.5; TitleBar = 4; Badge = 9, 9, 9, 9, 1.5; A = 5.6, 11, 5.4, 11.9; Wen = 13.5, 13.5, 6, 12.3
        Letter = 1.3, 1.2; Cut = 1 }
)
foreach ($icon in $inputLanguagePerApp) {
    $titleBar = Get-TitleBarPath $icon.Window $icon.TitleBar
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
    foreach ($cut in (Get-TitleBarCut $icon.Window $icon.TitleBar $icon.Cut), (Get-StrokeArea $letterA $aWidth), (Get-GrownShape $badge (2 * $icon.Cut))) {
        $window = Remove-Shape $window $cut
    }
    $sign = Remove-Shape (ConvertTo-Shape $badge) (Get-StrokeArea $wen $wenWidth)
    Write-Drawing "input-language-per-app-$($icon.Size)-mono" $icon.Size @() @(Get-MonoPath (Join-Shape $window $sign))
}

# App Windows, in sky: three of an app's windows stacked, Windows' boxes from the back to the
# front, in the deepest, deep and light tones, the front one with a title bar TitleBar high in the
# main tone. The one-color version keeps them apart with cuts Cut wide.
$appWindows = @(
    @{ Size = 32; Windows = @(@(11, 1, 20, 16, 2), @(6, 8, 20, 16, 2), @(1, 15, 20, 16, 2)); TitleBar = 4; Cut = 1.2 }
    @{ Size = 20; Windows = @(@(8, 2, 10, 8, 1.5), @(5, 6, 10, 8, 1.5), @(2, 10, 10, 8, 1.5)); TitleBar = 2; Cut = 1 }
)
foreach ($icon in $appWindows) {
    $back, $middle, $front = $icon.Windows
    $body = @((Get-Rect $back ' fill="url(#sky-deepest)"'), (Get-Rect $middle ' fill="url(#sky-deep)"'), (Get-Rect $front ' fill="url(#sky-light)"'),
        "<path fill=`"url(#sky-main)`" d=`"$(Get-TitleBarPath $front $icon.TitleBar)`"/>")
    Write-Drawing "app-windows-$($icon.Size)" $icon.Size (@('deepest', 'deep', 'light', 'main') | ForEach-Object { Get-Gradient "sky-$_" }) $body
    $shape = Remove-Shape (ConvertTo-Shape (Get-RectPath $back)) (Get-GrownShape (Get-RectPath $middle) (2 * $icon.Cut))
    $shape = Join-Shape $shape (ConvertTo-Shape (Get-RectPath $middle))
    $shape = Remove-Shape $shape (Get-GrownShape (Get-RectPath $front) (2 * $icon.Cut))
    $shape = Join-Shape $shape (Remove-Shape (ConvertTo-Shape (Get-RectPath $front)) (Get-TitleBarCut $front $icon.TitleBar $icon.Cut))
    Write-Drawing "app-windows-$($icon.Size)-mono" $icon.Size @() @(Get-MonoPath $shape)
}

# Minimize to Tray, in green: a window, its title bar TitleBar high in the deep tone, with a white
# arrow down to the taskbar in the deepest tone, and a tray icon in the light tone on the taskbar.
# The one-color version cuts the arrow and the tray icon out, and Cut wide under the title bar.
$minimizeToTray = @(
    @{ Size = 32; Window = 2, 1, 28, 20, 2; TitleBar = 4; Taskbar = 1, 24, 30, 7, 1.5; Tray = 24, 26, 4, 3, 0.5; Stroke = 1; Cut = 1.2
        Arrow = @(@(14.5, 7.5), @(17.5, 7.5), @(17.5, 12.5), @(21.5, 12.5), @(16, 18.5), @(10.5, 12.5), @(14.5, 12.5)) }
    @{ Size = 20; Window = 2, 2, 16, 10, 1.5; TitleBar = 2; Taskbar = 2, 14, 16, 4, 1; Tray = 13, 15, 3, 2, 0.5; Stroke = 0.8; Cut = 1
        Arrow = @(@(9.4, 5.4), @(10.6, 5.4), @(10.6, 8.2), @(13.2, 8.2), @(10, 11.2), @(6.8, 8.2), @(9.4, 8.2)) }
)
foreach ($icon in $minimizeToTray) {
    $arrow = Get-PolygonPath $icon.Arrow
    $body = @((Get-Rect $icon.Window ' fill="url(#green-main)"'), "<path fill=`"url(#green-deep)`" d=`"$(Get-TitleBarPath $icon.Window $icon.TitleBar)`"/>",
        "<path fill=`"$white`" stroke=`"$white`" stroke-width=`"$(F $icon.Stroke)`" stroke-linejoin=`"round`" d=`"$arrow`"/>",
        (Get-Rect $icon.Taskbar ' fill="url(#green-deepest)"'), (Get-Rect $icon.Tray ' fill="url(#green-light)"'))
    Write-Drawing "minimize-to-tray-$($icon.Size)" $icon.Size (@('main', 'deep', 'deepest', 'light') | ForEach-Object { Get-Gradient "green-$_" }) $body
    $window = Remove-Shape (Remove-Shape (ConvertTo-Shape (Get-RectPath $icon.Window)) (Get-TitleBarCut $icon.Window $icon.TitleBar $icon.Cut)) (Get-GrownShape $arrow $icon.Stroke)
    $taskbar = Remove-Shape (ConvertTo-Shape (Get-RectPath $icon.Taskbar)) (ConvertTo-Shape (Get-RectPath $icon.Tray))
    Write-Drawing "minimize-to-tray-$($icon.Size)-mono" $icon.Size @() @(Get-MonoPath (Join-Shape $window $taskbar))
}

# Hide Apps, in violet: a window, its title bar TitleBar high in the deep tone, with a white eye,
# its outline Line wide round a pupil, crossed out by a white slash Slash wide, which a cut Gap wide
# keeps apart from the eye. Eye is the eye's middle, half its width and half its height. The
# one-color version cuts the eye and the slash out, and Cut wide under the title bar.
$hideApps = @(
    @{ Size = 32; Window = 1, 3, 30, 26, 2.5; TitleBar = 5; Eye = 16, 18.5, 10, 6.1; Line = 2; Pupil = 3; SlashLine = 8.5, 26, 23.5, 11; Slash = 2.2; Gap = 1.4; Cut = 1.2 }
    @{ Size = 20; Window = 2, 3, 16, 14, 1.5; TitleBar = 3; Eye = 10, 11.5, 5.4, 3.3; Line = 1.2; Pupil = 1.6; SlashLine = 6, 15.5, 14, 7.5; Slash = 1.2; Gap = 0.9; Cut = 1 }
)
foreach ($icon in $hideApps) {
    $cx, $cy, $half, $tall = $icon.Eye
    # Two curves through the eye's corners, each drawn as a pair of cubic curves that meet at its top or bottom.
    $eye = "M$(F ($cx - $half)) $(F $cy)C$(F ($cx - $half * 0.75)) $(F ($cy - $tall * 0.69)) $(F ($cx - $half * 0.4)) $(F ($cy - $tall)) $(F $cx) $(F ($cy - $tall))" +
        "S$(F ($cx + $half * 0.75)) $(F ($cy - $tall * 0.69)) $(F ($cx + $half)) $(F $cy)" +
        "C$(F ($cx + $half * 0.75)) $(F ($cy + $tall * 0.69)) $(F ($cx + $half * 0.4)) $(F ($cy + $tall)) $(F $cx) $(F ($cy + $tall))" +
        "S$(F ($cx - $half * 0.75)) $(F ($cy + $tall * 0.69)) $(F ($cx - $half)) $(F $cy)Z"
    $pupil = Get-EllipsePath $cx $cy $icon.Pupil $icon.Pupil
    $x1, $y1, $x2, $y2 = $icon.SlashLine
    $slash = "M$(F $x1) $(F $y1)L$(F $x2) $(F $y2)"
    $clip = @(Get-GapClip $icon.Size (Get-CapsulePath $icon.SlashLine ($icon.Slash / 2 + $icon.Gap)))
    $body = @((Get-Rect $icon.Window ' fill="url(#violet-main)"'), "<path fill=`"url(#violet-deep)`" d=`"$(Get-TitleBarPath $icon.Window $icon.TitleBar)`"/>") +
        @(Get-Group 'clip-path="url(#gap)"' @((Get-SymbolPath $eye $icon.Line), "<path fill=`"$white`" d=`"$pupil`"/>")) + @(Get-SymbolPath $slash $icon.Slash)
    Write-Drawing "hide-apps-$($icon.Size)" $icon.Size (@(Get-Gradient 'violet-main') + @(Get-Gradient 'violet-deep') + $clip) $body
    $marks = Remove-Shape (Join-Shape (Get-StrokeArea $eye $icon.Line) (ConvertTo-Shape $pupil)) (Get-StrokeArea $slash ($icon.Slash + 2 * $icon.Gap))
    $shape = ConvertTo-Shape (Get-RectPath $icon.Window)
    foreach ($cut in (Get-TitleBarCut $icon.Window $icon.TitleBar $icon.Cut), $marks, (Get-StrokeArea $slash $icon.Slash)) { $shape = Remove-Shape $shape $cut }
    Write-Drawing "hide-apps-$($icon.Size)-mono" $icon.Size @() @(Get-MonoPath $shape)
}

# Drop-down Apps, in the app icon's blue: a screen in the light tone with a panel in the deep tone
# hanging from its top edge, Panel's x, y, width, height and the radius of its bottom corners, and
# a white chevron on the panel, pointing down, Line wide. The one-color version cuts the chevron
# out, and Cut wide round the panel.
$dropDownApps = @(
    @{ Size = 32; Screen = 1, 3, 30, 26, 2.5; Panel = 4, 3, 24, 16, 2; Chevron = @(@(12, 10), @(16, 14), @(20, 10)); Line = 2.4; Cut = 1.2 }
    @{ Size = 20; Screen = 2, 3, 16, 14, 1.5; Panel = 4, 3, 12, 9, 1.5; Chevron = @(@(7.8, 6.2), @(10, 8.4), @(12.2, 6.2)); Line = 1.5; Cut = 1 }
)
foreach ($icon in $dropDownApps) {
    $x, $y, $w, $h, $r = $icon.Panel
    $panel = "M$(F $x) $(F $y)H$(F ($x + $w))V$(F ($y + $h - $r))A$(F $r) $(F $r) 0 0 1 $(F ($x + $w - $r)) $(F ($y + $h))H$(F ($x + $r))" +
        "A$(F $r) $(F $r) 0 0 1 $(F $x) $(F ($y + $h - $r))Z"
    $chevron = 'M' + (($icon.Chevron | ForEach-Object { "$(F $_[0]) $(F $_[1])" }) -join 'L')
    $body = @((Get-Rect $icon.Screen ' fill="url(#blue-light)"'), "<path fill=`"url(#blue-deep)`" d=`"$panel`"/>", (Get-SymbolPath $chevron $icon.Line))
    Write-Drawing "drop-down-apps-$($icon.Size)" $icon.Size (@(Get-Gradient 'blue-light') + @(Get-Gradient 'blue-deep')) $body
    $screen = Remove-Shape (ConvertTo-Shape (Get-RectPath $icon.Screen)) (Get-GrownShape $panel (2 * $icon.Cut))
    $hanging = Remove-Shape (ConvertTo-Shape $panel) (Get-StrokeArea $chevron $icon.Line)
    Write-Drawing "drop-down-apps-$($icon.Size)-mono" $icon.Size @() @(Get-MonoPath (Join-Shape $screen $hanging))
}

# Title Bar Actions, in sky: a window in the main tone, its title bar above paper, and a mouse at
# its bottom right in the app icon's deep blue with an orange wheel, for the middle click and the
# wheel the feature uses on title bars. The one-color version cuts the paper out, and Cut wide
# round the mouse and round its wheel.
$titleBarActions = @(
    @{ Size = 32; Window = 1, 2, 26, 23, 2; Paper = 3, 9, 22, 14, 1; Mouse = 19, 14, 12, 17, 6; Wheel = 23.75, 16.5, 2.5, 5, 1.25; Cut = 1.2 }
    @{ Size = 20; Window = 2, 2, 13, 12, 1.5; Paper = 3, 6, 11, 7, 0.5; Mouse = 10, 9, 8, 9, 4; Wheel = 13, 11, 2, 3, 1; Cut = 1 }
)
foreach ($icon in $titleBarActions) {
    $body = @((Get-Rect $icon.Window ' fill="url(#sky-main)"'), (Get-Rect $icon.Paper ' fill="url(#paper)"'), (Get-Rect $icon.Mouse ' fill="url(#blue-deep)"'),
        (Get-Rect $icon.Wheel ' fill="url(#orange)"'))
    Write-Drawing "title-bar-actions-$($icon.Size)" $icon.Size (@(Get-Gradient 'sky-main') + @(Get-Gradient 'paper') + @(Get-Gradient 'blue-deep') + @(Get-Gradient 'orange')) $body
    $mouse = Get-RectPath $icon.Mouse
    $wheel = Get-RectPath $icon.Wheel
    $window = Remove-Shape (Remove-Shape (ConvertTo-Shape (Get-RectPath $icon.Window)) (ConvertTo-Shape (Get-RectPath $icon.Paper))) (Get-GrownShape $mouse (2 * $icon.Cut))
    $device = Join-Shape (Remove-Shape (ConvertTo-Shape $mouse) (Get-GrownShape $wheel (2 * $icon.Cut))) (ConvertTo-Shape $wheel)
    Write-Drawing "title-bar-actions-$($icon.Size)-mono" $icon.Size @() @(Get-MonoPath (Join-Shape $window $device))
}

# The orange bookmark of Network Profiles and Display Profiles, a saved profile, at an icon's
# bottom right: Box is its x, y, width and height, and the notch at its foot is Notch deep.
function Get-BookmarkPath([double[]] $Box, [double] $Notch) {
    $x, $y, $w, $h = $Box
    "M$(F $x) $(F $y)H$(F ($x + $w))V$(F ($y + $h))L$(F ($x + $w / 2)) $(F ($y + $h - $Notch))L$(F $x) $(F ($y + $h))Z"
}

# Network Profiles, in sky: a Wi-Fi symbol, two arcs Arc wide round Center, Radii in radius, each
# Turn degrees either side of straight up, the outer in the main tone and the inner in the deep one,
# and a dot in the deepest tone, and the orange bookmark. The one-color version cuts Cut wide round
# the bookmark.
$networkProfiles = @(
    @{ Size = 32; Center = 16, 24; Radii = 18.5, 11.5; Turn = 45; Arc = 3.6; Dot = 16, 23.5, 3.6; Bookmark = 22, 15, 8, 13; Notch = 3; Cut = 1.2 }
    @{ Size = 20; Center = 10, 13.5; Radii = 9.5, 6; Turn = 45; Arc = 1.7; Dot = 10, 13, 2.1; Bookmark = 13, 9, 5, 8; Notch = 1.8; Cut = 1 }
)
foreach ($icon in $networkProfiles) {
    $cx, $cy = $icon.Center
    $angle = $icon.Turn * [Math]::PI / 180
    $arcs = foreach ($radius in $icon.Radii) {
        $dx = $radius * [Math]::Sin($angle)
        $top = $cy - $radius * [Math]::Cos($angle)
        "M$(F ($cx - $dx)) $(F $top)A$(F $radius) $(F $radius) 0 0 1 $(F ($cx + $dx)) $(F $top)"
    }
    $dx, $dy, $dr = $icon.Dot
    $dot = Get-EllipsePath $dx $dy $dr $dr
    $bookmark = Get-BookmarkPath $icon.Bookmark $icon.Notch
    $body = @((Get-SymbolPath $arcs[0] $icon.Arc 'url(#sky-main)'), (Get-SymbolPath $arcs[1] $icon.Arc 'url(#sky-deep)'), "<path fill=`"url(#sky-deepest)`" d=`"$dot`"/>",
        "<path fill=`"url(#orange)`" d=`"$bookmark`"/>")
    Write-Drawing "network-profiles-$($icon.Size)" $icon.Size (@(@('main', 'deep', 'deepest') | ForEach-Object { Get-Gradient "sky-$_" }) + @(Get-Gradient 'orange')) $body
    $symbol = Join-Shape (Join-Shape (Get-StrokeArea $arcs[0] $icon.Arc) (Get-StrokeArea $arcs[1] $icon.Arc)) (ConvertTo-Shape $dot)
    $symbol = Remove-Shape $symbol (Get-GrownShape $bookmark (2 * $icon.Cut))
    Write-Drawing "network-profiles-$($icon.Size)-mono" $icon.Size @() @(Get-MonoPath (Join-Shape $symbol (ConvertTo-Shape $bookmark)))
}

# Display Profiles, in the app icon's blue: a monitor, its frame in the deepest tone round a screen
# in the main tone with a light plane below the curve Light, on a neck in the deep tone and a foot
# in the deepest, and the orange bookmark. The one-color version keeps the frame, the screen, its
# light plane and the stand apart with cuts Cut wide, and cuts Cut wide round the bookmark.
$displayProfiles = @(
    @{ Size = 32; Frame = 1, 2, 30, 20, 2; Screen = 3, 4, 26, 16, 1; Light = 'M3 19C10 14 18 10 29 9'; Neck = 13, 22, 6, 4, 0; Foot = 8, 26, 16, 3, 1
        Bookmark = 22, 17, 8, 13; Notch = 3; Cut = 1.2 }
    @{ Size = 20; Frame = 2, 3, 16, 10, 1.5; Screen = 3, 4, 14, 8, 0.5; Light = 'M3 11.5C6.5 9 10.5 7 17 6.5'; Neck = 9, 13, 2, 2, 0; Foot = 6, 15, 8, 2, 1
        Bookmark = 13, 10, 5, 8; Notch = 1.8; Cut = 1 }
)
foreach ($icon in $displayProfiles) {
    $sx, $sy, $sw, $sh, $sr = $icon.Screen
    # The light plane: under the curve, down the screen's right edge and along its rounded foot.
    $plane = "$($icon.Light)V$(F ($sy + $sh - $sr))A$(F $sr) $(F $sr) 0 0 1 $(F ($sx + $sw - $sr)) $(F ($sy + $sh))H$(F ($sx + $sr))" +
        "A$(F $sr) $(F $sr) 0 0 1 $(F $sx) $(F ($sy + $sh - $sr))Z"
    $bookmark = Get-BookmarkPath $icon.Bookmark $icon.Notch
    $body = @((Get-Rect $icon.Neck ' fill="url(#blue-deep)"'), (Get-Rect $icon.Foot ' fill="url(#blue-deepest)"'), (Get-Rect $icon.Frame ' fill="url(#blue-deepest)"'),
        (Get-Rect $icon.Screen ' fill="url(#blue-main)"'), "<path fill=`"url(#blue-light)`" d=`"$plane`"/>", "<path fill=`"url(#orange)`" d=`"$bookmark`"/>")
    Write-Drawing "display-profiles-$($icon.Size)" $icon.Size (@(@('deep', 'deepest', 'main', 'light') | ForEach-Object { Get-Gradient "blue-$_" }) + @(Get-Gradient 'orange')) $body
    $screen = Get-RectPath $icon.Screen
    $frame = Remove-Shape (ConvertTo-Shape (Get-RectPath $icon.Frame)) (Get-GrownShape $screen (2 * $icon.Cut))
    $monitor = Join-Shape $frame (Remove-Shape (ConvertTo-Shape $screen) (Get-StrokeArea $icon.Light $icon.Cut))
    $stand = Remove-Shape (Join-Shape (ConvertTo-Shape (Get-RectPath $icon.Neck)) (ConvertTo-Shape (Get-RectPath $icon.Foot))) (Get-GrownShape (Get-RectPath $icon.Frame) (2 * $icon.Cut))
    $shape = Remove-Shape (Join-Shape $monitor $stand) (Get-GrownShape $bookmark (2 * $icon.Cut))
    Write-Drawing "display-profiles-$($icon.Size)-mono" $icon.Size @() @(Get-MonoPath (Join-Shape $shape (ConvertTo-Shape $bookmark)))
}

# Folder Rules, in yellow, as folders are in File Explorer: a folder, its back with the tab in the
# darker yellow and its front in the lighter one, and a funnel at its bottom right in the app
# icon's blue, the rules that sort its files. Back is the back's outline. The one-color version
# keeps the back and the front apart with a cut Cut wide, and cuts Cut wide round the funnel.
$folderRules = @(
    @{ Size = 32; Back = 'M1 5A2 2 0 0 1 3 3H11.5L14.5 6H29A2 2 0 0 1 31 8V12H1Z'; Front = 1, 8, 30, 21, 2; Stroke = 1; Cut = 1.2
        Funnel = @(@(17.5, 17.5), @(30.5, 17.5), @(25.5, 23.5), @(25.5, 30), @(22.5, 28.5), @(22.5, 23.5)) }
    @{ Size = 20; Back = 'M2 4.5A1.5 1.5 0 0 1 3.5 3H7L9 5H16.5A1.5 1.5 0 0 1 18 6.5V8H2Z'; Front = 2, 6, 16, 12, 1.5; Stroke = 0.8; Cut = 1
        Funnel = @(@(10.4, 10.4), @(17.6, 10.4), @(14.6, 13.6), @(14.6, 17.6), @(13.4, 17), @(13.4, 13.6)) }
)
foreach ($icon in $folderRules) {
    $funnel = Get-PolygonPath $icon.Funnel
    $body = @("<path fill=`"url(#yellow-back)`" d=`"$($icon.Back)`"/>", (Get-Rect $icon.Front ' fill="url(#yellow)"'),
        "<path fill=`"url(#blue-main)`" stroke=`"url(#blue-main)`" stroke-width=`"$(F $icon.Stroke)`" stroke-linejoin=`"round`" d=`"$funnel`"/>")
    Write-Drawing "folder-rules-$($icon.Size)" $icon.Size (@(Get-Gradient 'yellow-back') + @(Get-Gradient 'yellow') + @(Get-Gradient 'blue-main')) $body
    $front = Get-RectPath $icon.Front
    $shape = Join-Shape (Remove-Shape (ConvertTo-Shape $icon.Back) (Get-GrownShape $front (2 * $icon.Cut))) (ConvertTo-Shape $front)
    $shape = Remove-Shape $shape (Get-GrownShape $funnel ($icon.Stroke + 2 * $icon.Cut))
    Write-Drawing "folder-rules-$($icon.Size)-mono" $icon.Size @() @(Get-MonoPath (Join-Shape $shape (Get-GrownShape $funnel $icon.Stroke)))
}

# Drive Actions, in green: a USB stick, drawn upright about the icon's middle, then turned 45° and
# lifted Lift, so that what shows of it on a light background, where its pale plug hardly does, is
# centered. Its plug is in paper with two holes in the deep tone, and its body in the main tone with
# a band in the deep tone at its top, and the orange bolt is at its bottom right. The one-color
# version keeps the plug's holes, the band and the body apart, and cuts Cut wide round the bolt.
$driveActions = @(
    @{ Size = 32; Plug = 12, 2.5, 8, 7.5, 1; Holes = @(@(13.4, 4.6, 2, 2, 0), @(16.6, 4.6, 2, 2, 0)); Body = 10, 9, 12, 20.5, 2; Band = 3.5; Lift = 3; Cut = 1.2 }
    @{ Size = 20; Plug = 8, 2.8, 4, 3.6, 0.5; Holes = @(@(8.7, 3.8, 1, 1, 0), @(10.3, 3.8, 1, 1, 0)); Body = 7, 6, 6, 10.4, 1.2; Band = 1.8; Lift = 1; Cut = 1 }
)
foreach ($icon in $driveActions) {
    $lift = $icon.Lift
    $up = { param([double[]] $Box) , @($Box[0], ($Box[1] - $lift), $Box[2], $Box[3], $Box[4]) }
    $plugBox = & $up $icon.Plug
    $bodyBox = & $up $icon.Body
    $holes = @($icon.Holes | ForEach-Object { , (& $up $_) })
    $middle = $icon.Size / 2
    $rotation = New-Object Windows.Media.RotateTransform -ArgumentList 45, $middle, ($middle - $lift)
    $items = @((Get-Rect $plugBox ' fill="url(#paper)"'), (Get-Rect $bodyBox ' fill="url(#green-main)"'), "<path fill=`"url(#green-deep)`" d=`"$(Get-TitleBarPath $bodyBox $icon.Band)`"/>") +
        @($holes | ForEach-Object { Get-Rect $_ ' fill="url(#green-deep)"' })
    $body = @(Get-Group "transform=`"rotate(45 $(F $middle) $(F ($middle - $lift)))`"" $items) + @(Get-BoltPath $icon.Size)
    Write-Drawing "drive-actions-$($icon.Size)" $icon.Size (@(Get-Gradient 'paper') + @(Get-Gradient 'green-main') + @(Get-Gradient 'green-deep') + @(Get-Gradient 'orange')) $body
    $plug = ConvertTo-Shape (Get-RectPath $plugBox)
    foreach ($hole in $holes) { $plug = Remove-Shape $plug (ConvertTo-Shape (Get-RectPath $hole)) }
    $stick = Join-Shape $plug (Remove-Shape (ConvertTo-Shape (Get-RectPath $bodyBox)) (Get-TitleBarCut $bodyBox $icon.Band $icon.Cut))
    $stick = [Windows.Media.PathGeometry]::CreateFromGeometry($stick)
    $stick.Transform = $rotation
    $bolt = $bolts[$icon.Size]
    $stick = Remove-Shape $stick (Get-GrownShape (Get-PolygonPath $bolt.Points) ($bolt.Stroke + 2 * $icon.Cut))
    Write-Drawing "drive-actions-$($icon.Size)-mono" $icon.Size @() @(Get-MonoPath (Join-Shape $stick (Get-BoltShape $icon.Size)))
}

# End App, in violet: an app window, its title bar TitleBar high in the deep tone, with a red stop
# sign at its bottom right, an octagon whose middle and half width are Sign, its corners rounded by
# a stroke Stroke wide, with a white bar. The one-color version cuts the bar out, and Cut wide
# under the title bar and round the sign.
$endApp = @(
    @{ Size = 32; Window = 1, 2, 25, 22, 2; TitleBar = 5; Sign = 23.5, 23.5, 7.1; Bar = 18, 22, 11, 3, 0.75; Stroke = 0.8; Cut = 1.2 }
    @{ Size = 20; Window = 2, 3, 13, 11, 1.5; TitleBar = 3; Sign = 14, 14, 3.6; Bar = 11, 13, 6, 2, 0.5; Stroke = 0.8; Cut = 1 }
)
foreach ($icon in $endApp) {
    $cx, $cy, $half = $icon.Sign
    $octagon = Get-PolygonPath (Get-OctagonPoints $cx $cy $half)
    $body = @((Get-Rect $icon.Window ' fill="url(#violet-main)"'), "<path fill=`"url(#violet-deep)`" d=`"$(Get-TitleBarPath $icon.Window $icon.TitleBar)`"/>",
        "<path fill=`"url(#red)`" stroke=`"url(#red)`" stroke-width=`"$(F $icon.Stroke)`" stroke-linejoin=`"round`" d=`"$octagon`"/>", (Get-Rect $icon.Bar " fill=`"$white`""))
    Write-Drawing "end-app-$($icon.Size)" $icon.Size (@(Get-Gradient 'violet-main') + @(Get-Gradient 'violet-deep') + @(Get-Gradient 'red')) $body
    $window = ConvertTo-Shape (Get-RectPath $icon.Window)
    foreach ($cut in (Get-TitleBarCut $icon.Window $icon.TitleBar $icon.Cut), (Get-GrownShape $octagon ($icon.Stroke + 2 * $icon.Cut))) { $window = Remove-Shape $window $cut }
    $sign = Remove-Shape (Get-GrownShape $octagon $icon.Stroke) (ConvertTo-Shape (Get-RectPath $icon.Bar))
    Write-Drawing "end-app-$($icon.Size)-mono" $icon.Size @() @(Get-MonoPath (Join-Shape $window $sign))
}

# Dynamic Wallpaper, in sky: a picture of mountains by day and by night, its sky split along the
# line Split, in the main tone with a white sun on the left and in the deepest tone with a white
# moon on the right, above mountains in the light tone below the line Mountains. Sun is the sun's
# middle and radius, and Moon the moon's middle and radius and the middle and radius of the bite
# that makes it a crescent. The one-color version cuts the sun and the moon out, and Cut wide along
# the split, down to the mountains, and along the mountains.
$dynamicWallpaper = @(
    @{ Size = 32; Picture = 1, 4, 30, 24, 2; Split = 19, 4, 13, 28; Mountains = 'M1 22L8 16.5L13 20L21 14L31 21.5'; Sun = 7, 10, 3.5; Moon = 25, 9.5, 3.2, 26.8, 8.1, 2.7; Cut = 1.2 }
    @{ Size = 20; Picture = 2, 4, 16, 12, 1.5; Split = 12, 4, 8, 16; Mountains = 'M2 13.2L5.8 10.6L8.4 12.2L12.6 9.4L18 13'; Sun = 5.5, 7.2, 1.9; Moon = 14.8, 7, 2, 15.9, 6.1, 1.7; Cut = 1 }
)
foreach ($icon in $dynamicWallpaper) {
    $px, $py, $pw, $ph, $pr = $icon.Picture
    $tx, $ty, $sx, $sy = $icon.Split
    $daySky = Get-PolygonPath @(@($px, $py), @($tx, $ty), @($sx, $sy), @($px, ($py + $ph)))
    $nightSky = Get-PolygonPath @(@($tx, $ty), @(($px + $pw), $py), @(($px + $pw), ($py + $ph)), @($sx, $sy))
    $mountains = "$($icon.Mountains)V$(F ($py + $ph))H$(F $px)Z"
    $cx, $cy, $radius = $icon.Sun
    $sun = Get-EllipsePath $cx $cy $radius $radius
    $moon = Get-CrescentPath $icon.Moon
    $defs = @(Get-Gradient 'sky-main') + @(Get-Gradient 'sky-deepest') + @(Get-Gradient 'sky-light') +
        @('<clipPath id="picture">', "$indent$(Get-Rect $icon.Picture)", '</clipPath>')
    $landscape = "<path fill=`"url(#sky-main)`" d=`"$daySky`"/>", "<path fill=`"url(#sky-deepest)`" d=`"$nightSky`"/>", "<path fill=`"url(#sky-light)`" d=`"$mountains`"/>"
    $body = @(Get-Group 'clip-path="url(#picture)"' $landscape) + @("<path fill=`"$white`" d=`"$sun`"/>", "<path fill=`"$white`" d=`"$moon`"/>")
    Write-Drawing "dynamic-wallpaper-$($icon.Size)" $icon.Size $defs $body
    $split = Remove-Shape (Get-StrokeArea "M$(F $tx) $(F $ty)L$(F $sx) $(F $sy)" $icon.Cut) (ConvertTo-Shape $mountains)
    $shape = ConvertTo-Shape (Get-RectPath $icon.Picture)
    foreach ($cut in $split, (Get-StrokeArea $icon.Mountains $icon.Cut), (ConvertTo-Shape $sun), (ConvertTo-Shape $moon)) { $shape = Remove-Shape $shape $cut }
    Write-Drawing "dynamic-wallpaper-$($icon.Size)-mono" $icon.Size @() @(Get-MonoPath $shape)
}

Write-Host "Wrote the drawings in $folder"
