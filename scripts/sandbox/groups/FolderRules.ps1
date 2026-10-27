# Folder Rules: with rules in the settings file, the host watches a folder made for the test and
# acts on the files that arrive there: a PDF written there moves into a subfolder named after its
# extension, a picture copied there is copied, and a note renamed into it gets the date in its name.
# A file written slowly and held open, and a download named .crdownload until it's renamed, are
# handled only once done; a name that's taken gets a number, or the file stays with a rule that
# skips; a hidden file and an empty one stay. The files already there when the folder was added
# stay, and a file that arrives while the host isn't running is handled when it starts. The history
# file says what happened, and the logs hold no file names or folders. Turned off, the folder isn't
# watched. Detailed logging is on for the Debug entries.

$root = Join-Path $env:USERPROFILE 'FolderRulesTest'
$inbox = Join-Path $root 'Inbox'
$sorted = Join-Path $root 'Sorted'
$pictures = Join-Path $sorted 'Pictures'
$historyFile = Join-Path $dataFolder 'folder-rules.json'
$detailedUntil = [DateTimeOffset]::Now.AddHours(1).ToString('o')

function Write-FolderRules([bool] $Enabled) {
    $rules = @(
        @{ name = 'PDFs'; extensions = @('pdf'); action = 'Move'; destination = "$sorted\{extension}"; ifExists = 'AddNumber' },
        @{ name = 'Pictures'; extensions = @('png'); action = 'Copy'; destination = $pictures; ifExists = 'Skip' },
        @{ name = 'Notes'; names = @('note*'); action = 'Rename'; newName = '{date} {name}' }
    )
    $section = @{ enabled = $Enabled; folders = @(@{ path = $inbox; rules = $rules }) }
    $settings = @{ schemaVersion = 1; general = @{ theme = 'Light' }; diagnostics = @{ detailedLoggingUntil = $detailedUntil }; features = @{ folderRules = $section } }
    Write-Settings ($settings | ConvertTo-Json -Depth 8)
}

function Get-History { if (Test-Path $historyFile) { @((Get-Content $historyFile -Raw | ConvertFrom-Json).entries) } else { @() } }

function New-TestFile([string] $Path, [int] $Size = 1000) { [IO.File]::WriteAllBytes($Path, [byte[]]::new($Size)) }

Remove-Item $root -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $inbox, $pictures | Out-Null
New-TestFile (Join-Path $inbox 'there.pdf')

Write-FolderRules $true
Start-Process $hostExe
Test-Check 'the host uses the folder and its rules' (Wait-Until { (Get-HostLog) -match 'Folder Rules applied: 1 folders and 3 rules can be used\.' } 15)
Test-Check 'the host looks at the folder it never looked at before' (Wait-Until { (Get-HostLog) -match 'Folder Rules looked at folder 1: 0 of 1 files arrived since it last looked\.' } 10)

# Written there, a PDF moves into a subfolder made from its extension.
New-TestFile (Join-Path $inbox 'report.pdf')
Test-Check 'a PDF written there moves into the pdf subfolder' (Wait-Until { (Test-Path "$sorted\pdf\report.pdf") -and -not (Test-Path "$inbox\report.pdf") } 15)
Test-Check 'the file that was there when the folder was added stays' (Test-Path "$inbox\there.pdf")

# Copied there, a picture is copied, and the original stays.
New-TestFile (Join-Path $root 'photo.png')
Copy-Item (Join-Path $root 'photo.png') $inbox
Test-Check 'a picture copied there is copied to Pictures' (Wait-Until { Test-Path "$pictures\photo.png" } 15)
Test-Check 'and the picture stays in the folder' (Test-Path "$inbox\photo.png")

# Renamed there, a note gets the date it was last modified in its name.
New-TestFile (Join-Path $inbox 'draft.txt') 100
$modified = (Get-Item "$inbox\draft.txt").LastWriteTime.ToString('yyyy-MM-dd', [Globalization.CultureInfo]::InvariantCulture)
Rename-Item "$inbox\draft.txt" 'note.txt'
Test-Check 'a note renamed there gets the date in its name' (Wait-Until { Test-Path "$inbox\$modified note.txt" } 15) "$((Get-ChildItem $inbox -Name) -join ', ')"

# Moved there, a PDF whose name is taken gets a number.
New-TestFile (Join-Path $root 'report.pdf')
Move-Item (Join-Path $root 'report.pdf') $inbox
Test-Check 'a PDF moved there whose name is taken gets a number' (Wait-Until { Test-Path "$sorted\pdf\report (2).pdf" } 15)

# A picture whose name is taken in Pictures stays, with a rule that skips.
New-TestFile (Join-Path $pictures 'taken.png') 10
New-TestFile (Join-Path $inbox 'taken.png') 2000
Test-Check 'a picture whose name is taken is skipped' (Wait-Until { @(Get-History | Where-Object { $_.name -eq 'taken.png' -and $_.reason -eq 'NameTaken' }).Count -eq 1 } 15)
Test-Check 'and the picture in Pictures stays as it was' ((Get-Item "$pictures\taken.png").Length -eq 10)

# A file written slowly and held open is handled only once it's closed.
$stream = [IO.File]::Open("$inbox\slow.pdf", 'CreateNew', 'Write', 'ReadWrite')
$stream.Write([byte[]]::new(1000), 0, 1000)
$stream.Flush()
Start-Sleep -Seconds 6
Test-Check 'a file held open for writing stays' ((Test-Path "$inbox\slow.pdf") -and -not (Test-Path "$sorted\pdf\slow.pdf"))
$stream.Write([byte[]]::new(1000), 0, 1000)
$stream.Dispose()
Test-Check 'once closed, it moves' (Wait-Until { Test-Path "$sorted\pdf\slow.pdf" } 15)
Test-Check 'whole' ((Get-Item "$sorted\pdf\slow.pdf").Length -eq 2000)

# A download that isn't done yet stays until the browser gives it its name.
New-TestFile (Join-Path $inbox 'big.pdf.crdownload')
Start-Sleep -Seconds 5
Test-Check 'a download still named .crdownload stays' (Test-Path "$inbox\big.pdf.crdownload")
Rename-Item "$inbox\big.pdf.crdownload" 'big.pdf'
Test-Check 'renamed to its name, it moves' (Wait-Until { Test-Path "$sorted\pdf\big.pdf" } 15)

# A hidden file and an empty one stay.
New-TestFile (Join-Path $inbox 'hidden.pdf')
(Get-Item "$inbox\hidden.pdf").Attributes = 'Hidden'
New-TestFile (Join-Path $inbox 'empty.pdf') 0
Start-Sleep -Seconds 6
Test-Check 'a hidden file stays' (Test-Path "$inbox\hidden.pdf" -PathType Leaf)
Test-Check 'an empty file stays' (Test-Path "$inbox\empty.pdf")

# A file that arrives while the host isn't running is handled when it starts.
Stop-All
New-TestFile (Join-Path $inbox 'offline.pdf')
Start-Process $hostExe
Test-Check 'a file that arrived while the host wasn''t running moves when it starts' (Wait-Until { Test-Path "$sorted\pdf\offline.pdf" } 20)
Test-Check 'and the files that were there before stay' ((Test-Path "$inbox\there.pdf") -and (Test-Path "$inbox\photo.png") -and (Test-Path "$inbox\empty.pdf"))

# The history says what happened to each file.
$history = Get-History
$outcomes = ($history | ForEach-Object { "$($_.name) $($_.outcome)" }) -join ', '
Test-Check 'the history has the move, the copy, the rename and the skip' (
    (@($history | Where-Object { $_.name -eq 'report.pdf' -and $_.outcome -eq 'Moved' }).Count -eq 2) -and
    (@($history | Where-Object { $_.name -eq 'photo.png' -and $_.outcome -eq 'Copied' }).Count -eq 1) -and
    (@($history | Where-Object { $_.name -eq 'note.txt' -and $_.outcome -eq 'Renamed' }).Count -eq 1) -and
    (@($history | Where-Object { $_.name -eq 'offline.pdf' -and $_.outcome -eq 'Moved' }).Count -eq 1)) $outcomes
Test-Check 'the picture that was copied isn''t copied again' (@($history | Where-Object { $_.name -eq 'photo.png' }).Count -eq 1) $outcomes

# Turned off, the folder isn't watched.
Write-FolderRules $false
Test-Check 'turned off, Folder Rules'' thread ends' (Wait-Until { (Get-HostLog) -match "Folder Rules' thread ended\." } 10)
New-TestFile (Join-Path $inbox 'after.pdf')
Start-Sleep -Seconds 6
Test-Check 'turned off, a file that arrives stays' (Test-Path "$inbox\after.pdf")

$hostLog = Get-HostLog
Test-Check 'the logs hold no file names or folders' (($hostLog -notmatch 'report') -and ($hostLog -notmatch 'photo') -and ($hostLog -notmatch 'FolderRulesTest') -and ($hostLog -notmatch 'note\.txt'))
Test-Check 'the host logged the move by number' ($hostLog -match 'Folder Rules folder 1, rule 1: "?Moved"?\.')

Stop-All
Remove-Item $root -Recurse -Force -ErrorAction SilentlyContinue
