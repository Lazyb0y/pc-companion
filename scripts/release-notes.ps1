<#
.SYNOPSIS
Writes a release's draft notes from .github\release-notes.md; the release workflow runs it.

.DESCRIPTION
Fills in the template's {{version}}, and its {{installers}} with a row for each installer in the
checksums file: a link to its download and its SHA-256 hash.

.PARAMETER Version
The version being released, such as 0.1.0.

.PARAMETER Checksums
SHA256SUMS.txt, as sha256sum writes it.

.PARAMETER Output
Where to write the notes.

.EXAMPLE
pwsh -File scripts/release-notes.ps1 -Version 0.1.0 -Checksums release/SHA256SUMS.txt -Output notes.md
#>
param(
    [Parameter(Mandatory)]
    [string] $Version,
    [Parameter(Mandatory)]
    [string] $Checksums,
    [Parameter(Mandatory)]
    [string] $Output
)

$ErrorActionPreference = 'Stop'
$template = Join-Path $PSScriptRoot '..\.github\release-notes.md'
$download = "https://github.com/Lazyb0y/pc-companion/releases/download/v$Version"

$rows = foreach ($line in Get-Content -Path $Checksums -Encoding utf8) {
    if ($line -notmatch '^([0-9a-f]{64}) [ *](.+)$') {
        throw "Not a line of sha256sum's output: $line"
    }

    $hash = $Matches[1]
    $name = $Matches[2]
    if ($name -like '*.msi') {
        "| [$name]($download/$name) | ``$hash`` |"
    }
}

if (-not $rows) {
    throw "$Checksums lists no installers."
}

$text = (Get-Content -Path $template -Raw -Encoding utf8).Replace('{{version}}', $Version).Replace('{{installers}}', ($rows -join "`n"))

# GitHub adds a blank line before its list of pull requests, so the notes end with no blank lines.
[IO.File]::WriteAllText($Output, $text.TrimEnd() + "`n", (New-Object Text.UTF8Encoding $false))
