<#
.SYNOPSIS
Makes Dynamic Wallpaper's table of time zones and their main cities from the time zone database.

.DESCRIPTION
Reads zone.tab, zone1970.tab and tzdata.zi from a copy of the time zone database, which is in the
public domain, and writes src\PCCompanion.Modules.Desktop\DynamicWallpaper\TimeZonePlaces.txt:
each zone's name and the coordinates of its main city, as zone.tab gives them for each country, or
zone1970.tab for zones zone.tab doesn't list, then each older name, from tzdata.zi's links, with
the zone it now stands for. Windows uses older names, such as Asia/Calcutta, for some zones.

Git for Windows has a copy in its share\zoneinfo folder. Run it when the time zone database gains
or moves a zone, and commit the file. See "Location" in docs\features\desktop\dynamic-wallpaper.md.

.PARAMETER Folder
The folder with zone.tab, zone1970.tab and tzdata.zi.

.EXAMPLE
powershell -ExecutionPolicy Bypass -File scripts\time-zone-places.ps1 -Folder 'C:\Program Files\Git\ucrt64\share\zoneinfo'
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $Folder
)

$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$output = Join-Path $root 'src\PCCompanion.Modules.Desktop\DynamicWallpaper\TimeZonePlaces.txt'

function Read-Lines([string] $Name) {
    $path = Join-Path $Folder $Name
    if (-not (Test-Path $path)) { throw "$path doesn't exist." }
    return Get-Content -Path $path -Encoding utf8 | Where-Object { $_ -and -not $_.StartsWith('#') }
}

$version = (Get-Content -Path (Join-Path $Folder 'tzdata.zi') -TotalCount 1) -replace '^# version ', ''

# Each zone's coordinates: zone.tab first, for its city in each country, then zone1970.tab.
$places = [ordered]@{}
foreach ($name in 'zone.tab', 'zone1970.tab') {
    foreach ($line in Read-Lines $name) {
        $columns = $line.Split("`t")
        if (-not $places.Contains($columns[2])) { $places[$columns[2]] = $columns[1] }
    }
}

# Older names, such as Asia/Calcutta for Asia/Kolkata, from the links: L <zone> <older name>.
$links = [ordered]@{}
foreach ($line in Read-Lines 'tzdata.zi') {
    $columns = $line.Split(' ')
    if ($columns[0] -eq 'L' -and $places.Contains($columns[1]) -and -not $places.Contains($columns[2])) {
        $links[$columns[2]] = $columns[1]
    }
}

$lines = @("# Made by scripts\time-zone-places.ps1 from the time zone database $version, which is in the public domain.")
$lines += foreach ($zone in $places.Keys | Sort-Object) { "$zone`t$($places[$zone])" }
$lines += foreach ($name in $links.Keys | Sort-Object) { "$name`t=$($links[$name])" }

[IO.File]::WriteAllText($output, ($lines -join "`r`n") + "`r`n", (New-Object Text.UTF8Encoding $false))
Write-Host "$output`: $($places.Count) zones and $($links.Count) older names."
