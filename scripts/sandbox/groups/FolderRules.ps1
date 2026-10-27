# Folder Rules: with rules in the settings file, the host watches a folder made for the test and
# acts on the files that arrive there: a PDF written there moves into a subfolder named after its
# extension, a picture copied there is copied, and a note renamed into it gets the date in its name.
# A file written slowly and held open, and a download named .crdownload until it's renamed, are
# handled only once done; a name that's taken gets a number, or the file stays with a rule that
# skips; a hidden file and an empty one stay. A log goes to the Recycle Bin, and with Don't move
# files to the Recycle Bin on, it stays. With the Recycle Bin for C: set to 1 MB, a 50 MB log is too
# big for it, so Windows asks, in its own window, while the history shows the file waiting: No, Esc
# and closing the window keep the file, and Yes deletes it. The files already there when the folder
# was added stay, and a file that arrives while the host isn't running is handled when it starts.
# The history file says what happened, and the logs hold no file names or folders. Turned off, the
# folder isn't watched. Detailed logging is on for the Debug entries.

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
        @{ name = 'Notes'; names = @('note*'); action = 'Rename'; newName = '{date} {name}' },
        @{ name = 'Logs'; extensions = @('log'); action = 'RecycleBin' }
    )
    $section = @{ enabled = $Enabled; folders = @(@{ path = $inbox; rules = $rules }) }
    $settings = @{ schemaVersion = 1; general = @{ theme = 'Light' }; diagnostics = @{ detailedLoggingUntil = $detailedUntil }; features = @{ folderRules = $section } }
    Write-Settings ($settings | ConvertTo-Json -Depth 8)
}

function Get-History { if (Test-Path $historyFile) { @((Get-Content $historyFile -Raw | ConvertFrom-Json).entries) } else { @() } }

function New-TestFile([string] $Path, [int] $Size = 1000) { [IO.File]::WriteAllBytes($Path, [byte[]]::new($Size)) }

Add-Type @'
using System;
using System.Runtime.InteropServices;
using System.Text;

// The windows of a process, read from another one.
public static class FolderRulesCheck
{
    public delegate bool EnumProc(IntPtr window, IntPtr data);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc proc, IntPtr data);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr window, out uint pid);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowText(IntPtr window, StringBuilder text, int length);
    [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr window);

    // The visible window of the process with that title, or zero.
    public static IntPtr Find(uint pid, string title)
    {
        IntPtr found = IntPtr.Zero;
        EnumWindows((window, data) =>
        {
            uint owner;
            GetWindowThreadProcessId(window, out owner);
            var text = new StringBuilder(256);
            GetWindowText(window, text, 256);
            if (owner == pid && IsWindowVisible(window) && text.ToString() == title) { found = window; return false; }
            return true;
        }, IntPtr.Zero);
        return found;
    }
}
'@

function Get-RecycleBinNames { @((New-Object -ComObject Shell.Application).NameSpace(10).Items() | ForEach-Object { $_.Name }) }

# Windows' question about a file too big for the Recycle Bin, a window of the host's.
function Find-Question {
    $hostProcess = Get-Processes 'PCCompanion.Host' | Select-Object -First 1
    if ($null -eq $hostProcess) { return [IntPtr]::Zero }
    [FolderRulesCheck]::Find([uint32] $hostProcess.Id, 'Delete File')
}

# Writes a 50 MB log, which is too big for the Recycle Bin, and answers Windows' question about it
# with $Answer: Close, Esc, No or Yes.
function Test-Question([string] $Answer, [bool] $Kept) {
    $name = "big-$($Answer.ToLowerInvariant()).log"
    $path = Join-Path $inbox $name
    $stream = [IO.File]::Create($path)
    $stream.SetLength(50MB)
    $stream.Dispose()
    $script:question = [IntPtr]::Zero
    $asked = Wait-Until { ($script:question = Find-Question) -ne [IntPtr]::Zero } 20
    Test-Check "Windows asks about a log too big for the Recycle Bin, to answer $Answer" $asked
    if (-not $asked) { return }
    Save-Screenshot "folder-rules-question-$($Answer.ToLowerInvariant())"
    Test-Check "the history shows the log waiting for an answer ($Answer)" (Wait-Until { @(Get-History | Where-Object { $_.name -eq $name -and $_.outcome -eq 'WaitingForAnswer' }).Count -eq 1 } 10)
    if ($Answer -eq 'Close') {
        [void] [FolderRulesCheck]::PostMessage($script:question, 0x0010, [IntPtr]::Zero, [IntPtr]::Zero)
    } else {
        [void] [FolderRulesCheck]::SetForegroundWindow($script:question)
        Start-Sleep -Milliseconds 300
        [System.Windows.Forms.SendKeys]::SendWait(@{ Esc = '{ESC}'; No = 'n'; Yes = 'y' }[$Answer])
    }

    $outcome = if ($Kept) { 'KeptByAnswer' } else { 'DeletedForGood' }
    $recorded = Wait-Until { @(Get-History | Where-Object { $_.name -eq $name -and $_.outcome -eq $outcome }).Count -eq 1 } 10
    Test-Check "after $Answer, the history says $outcome in place of the wait" $recorded ((Get-History | Where-Object { $_.name -eq $name } | ForEach-Object { $_.outcome }) -join ', ')
    Test-Check "after $Answer, the log is $(if ($Kept) { 'kept' } else { 'gone' })" ((Test-Path $path) -eq $Kept)
}

Remove-Item $root -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $inbox, $pictures | Out-Null
New-TestFile (Join-Path $inbox 'there.pdf')

# The Recycle Bin for C: holds 1 MB, so that a 50 MB log is too big for it. A process reads the
# Recycle Bin's settings once, so they're set before the host starts.
$volume = Get-CimInstance Win32_Volume | Where-Object { $_.DriveLetter -eq 'C:' }
$bitBucket = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\BitBucket\Volume\$(([regex] '\{[0-9a-fA-F-]+\}').Match($volume.DeviceID).Value)"
$recycleSettings = Get-ItemProperty $bitBucket -ErrorAction SilentlyContinue
New-Item -Force -Path $bitBucket -ErrorAction SilentlyContinue | Out-Null
Set-ItemProperty $bitBucket -Name MaxCapacity -Value 1 -Type DWord

Write-FolderRules $true
Start-Process $hostExe
Test-Check 'the host uses the folder and its rules' (Wait-Until { (Get-HostLog) -match 'Folder Rules applied: 1 folders and 4 rules can be used\.' } 15)
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

# A log goes to the Recycle Bin.
New-TestFile (Join-Path $inbox 'small.log')
Test-Check 'a log written there goes to the Recycle Bin' (Wait-Until { (Get-RecycleBinNames) -contains 'small.log' } 15) ((Get-RecycleBinNames) -join ', ')
Test-Check 'and the history says so' (Wait-Until { @(Get-History | Where-Object { $_.name -eq 'small.log' -and $_.outcome -eq 'MovedToRecycleBin' }).Count -eq 1 } 5)

# A 50 MB log is too big for the Recycle Bin, so Windows asks.
Test-Question 'Close' $true
Test-Question 'Esc' $true
Test-Question 'No' $true
Test-Question 'Yes' $false

# A file that arrives while the host isn't running is handled when it starts. With Don't move files
# to the Recycle Bin on, Windows would delete a log for good, so it stays.
Stop-All
Set-ItemProperty $bitBucket -Name NukeOnDelete -Value 1 -Type DWord
New-TestFile (Join-Path $inbox 'offline.pdf')
New-TestFile (Join-Path $inbox 'nuke.log')
Start-Process $hostExe
Test-Check 'a file that arrived while the host wasn''t running moves when it starts' (Wait-Until { Test-Path "$sorted\pdf\offline.pdf" } 20)
Test-Check 'and the files that were there before stay' ((Test-Path "$inbox\there.pdf") -and (Test-Path "$inbox\photo.png") -and (Test-Path "$inbox\empty.pdf"))
Test-Check 'without the Recycle Bin, a log stays' (Wait-Until { @(Get-History | Where-Object { $_.name -eq 'nuke.log' -and $_.reason -eq 'NoRecycleBin' }).Count -eq 1 } 15)
Test-Check 'and the log is still there' (Test-Path "$inbox\nuke.log")
Test-Check 'the logs kept by the answers aren''t asked about again' (@(Get-History | Where-Object { $_.name -like 'big-*.log' }).Count -eq 4)
foreach ($name in 'NukeOnDelete', 'MaxCapacity') {
    if ($null -ne $recycleSettings.$name) { Set-ItemProperty $bitBucket -Name $name -Value $recycleSettings.$name -Type DWord } else { Remove-ItemProperty $bitBucket -Name $name -ErrorAction SilentlyContinue }
}

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
