# Networking: on
# Folder Rules: with rules in the settings file, the host watches a folder made for the test and
# acts on the files that arrive there: a PDF written there moves into a subfolder named after its
# extension, a picture copied there is copied, and a note renamed into it gets the date in its name.
# A file written slowly and held open, and a download named .crdownload until it's renamed, are
# handled only once done; a name that's taken gets a number, or the file stays with a rule that
# skips; a hidden file and an empty one stay, and a hidden system folder isn't watched. A log goes
# to the Recycle Bin, and with Don't move files to the Recycle Bin on, it stays. With the Recycle
# Bin for C: set to 1 MB, a 50 MB log is too big for it, so Windows asks, in its own window, while
# the history shows the file waiting: No, Esc and closing the window keep the file, and Yes deletes
# it. A PDF with a Mark of the Web keeps it when it moves; on an exFAT virtual disk, a file with a
# mark stays rather than lose it, and one without moves there. A download in Edge, from a server on
# the loopback address under a test name, arrives in Downloads, matches Downloaded from, and moves
# with its mark. A folder on a share on the Sandbox itself is watched; once the share is removed and
# back, it's watched again within a minute, and a file written while it was away moves. Networking
# is on for the share, with the update check off. The files already there when the folder was added
# stay, and a file that arrives while the host isn't running is handled when it starts. The history
# file says what happened, and the pipe gives the settings app the same history; Run now lists the
# file that was there before and moves it when chosen, and Undo puts it back, where it stays. The
# pipe refuses a file outside the folder and a request it doesn't know, and clears the history. The
# logs hold no file names or folders. Turned off, the folder isn't watched and the host serves no
# pipe. The page lists the folders and their rules, says why the hidden system folder can't be used,
# and saves Add folder, the switches, moving and deleting a rule and removing a folder, with focus
# staying near what changed. The rule editor lists the files there that the rule would act on as
# it's edited, refuses an extension a file name can't have, and saves the rule; Edit opens it again,
# and a template opens with its destination, whose folder Create folder makes. With Folder Rules on,
# Run now lists the files the rules would act on and acts on those left checked; the history shows
# the move, Undo puts the file back, Open Recycle Bin opens it, and Clear history asks first.
# Detailed logging is on for the Debug entries.

$root = Join-Path $env:USERPROFILE 'FolderRulesTest'
$inbox = Join-Path $root 'Inbox'
$sorted = Join-Path $root 'Sorted'
$pictures = Join-Path $sorted 'Pictures'
$downloaded = Join-Path $sorted 'Downloaded'
$downloads = Join-Path $env:USERPROFILE 'Downloads'
$card = 'X:\Card'
$shareFolder = Join-Path $root 'Shared'
$share = '\\localhost\FolderRulesShare'
$fromShare = Join-Path $sorted 'From the share'
$systemFolder = Join-Path $root 'Windows only'
$historyFile = Join-Path $dataFolder 'folder-rules.json'
$detailedUntil = [DateTimeOffset]::Now.AddHours(1).ToString('o')

function Write-FolderRules([bool] $Enabled) {
    $rules = @(
        @{ name = 'PDFs'; extensions = @('pdf'); action = 'Move'; destination = "$sorted\{extension}"; ifExists = 'AddNumber' },
        @{ name = 'Pictures'; extensions = @('png'); action = 'Copy'; destination = $pictures; ifExists = 'Skip' },
        @{ name = 'Notes'; names = @('note*'); action = 'Rename'; newName = '{date} {name}' },
        @{ name = 'Logs'; extensions = @('log'); action = 'RecycleBin' },
        @{ name = 'To the card'; names = @('card-*'); action = 'Move'; destination = $card }
    )
    $fromSite = @(@{ name = 'From the test site'; downloadedFrom = @('*.folder-rules.test'); action = 'Move'; destination = $downloaded })
    $scans = @(@{ name = 'Scans'; extensions = @('pdf'); action = 'Move'; destination = $fromShare })
    $section = @{ enabled = $Enabled; folders = @(@{ path = $inbox; rules = $rules }, @{ path = $downloads; rules = $fromSite }, @{ path = $share; rules = $scans }, @{ path = $systemFolder; rules = $scans }) }
    $settings = @{ schemaVersion = 1; general = @{ theme = 'Light' }; updates = @{ checkAutomatically = $false }; diagnostics = @{ detailedLoggingUntil = $detailedUntil }; features = @{ folderRules = $section } }
    Write-Settings ($settings | ConvertTo-Json -Depth 8)
}

function Get-History { if (Test-Path $historyFile) { @((Get-Content $historyFile -Raw | ConvertFrom-Json).entries) } else { @() } }

# Asks the host through Folder Rules' pipe, as the settings app does, and returns its answer, or
# $null when the host doesn't serve the pipe.
function Invoke-FolderRulesPipe([hashtable] $Request) {
    $pipe = New-Object IO.Pipes.NamedPipeClientStream('.', "PCCompanion.FolderRules.$((Get-Process -Id $PID).SessionId)", [IO.Pipes.PipeDirection]::InOut)
    try {
        $pipe.Connect(3000)
        $pipe.ReadMode = [IO.Pipes.PipeTransmissionMode]::Message
        $bytes = [Text.Encoding]::UTF8.GetBytes(($Request | ConvertTo-Json -Depth 4 -Compress))
        $pipe.Write($bytes, 0, $bytes.Length)
        $answer = New-Object IO.MemoryStream
        $buffer = [byte[]]::new(65536)
        do {
            $read = $pipe.Read($buffer, 0, $buffer.Length)
            if ($read -eq 0) { return $null }
            $answer.Write($buffer, 0, $read)
        } while (-not $pipe.IsMessageComplete)
        [Text.Encoding]::UTF8.GetString($answer.ToArray()) | ConvertFrom-Json
    } catch {
        $null
    } finally {
        $pipe.Dispose()
    }
}

function New-TestFile([string] $Path, [int] $Size = 1000) { [IO.File]::WriteAllBytes($Path, [byte[]]::new($Size)) }

function Set-Mark([string] $Path) { Set-Content -Path $Path -Stream Zone.Identifier -Value "[ZoneTransfer]`r`nZoneId=3`r`nHostUrl=https://downloads.example.com/file`r`n" }

function Get-Mark([string] $Path) { Get-Content -Path $Path -Stream Zone.Identifier -Raw -ErrorAction SilentlyContinue }

# Runs diskpart with these commands, for the virtual disk.
function Invoke-DiskPart([string] $Commands) {
    $script = Join-Path $env:TEMP 'folder-rules-diskpart.txt'
    Set-Content -Path $script -Value $Commands -Encoding ascii
    $output = diskpart /s $script 2>&1 | Out-String
    Remove-Item $script -ErrorAction SilentlyContinue
    $output
}

Add-Type -TypeDefinition @'
using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

// A web server on the loopback address that answers every request with a small zip file to save,
// as a site's download link does. Each connection gets a thread of its own.
public sealed class DownloadServer : IDisposable
{
    private readonly TcpListener _listener;

    public DownloadServer(int port)
    {
        _listener = new TcpListener(IPAddress.Loopback, port);
        _listener.Start();
        Thread accept = new Thread(Accept);
        accept.IsBackground = true;
        accept.Start();
    }

    public void Dispose()
    {
        _listener.Stop();
    }

    // An exception on these threads would end PowerShell, so each ends quietly instead.
    private void Accept()
    {
        while (true)
        {
            TcpClient client;
            try { client = _listener.AcceptTcpClient(); }
            catch (Exception) { return; }

            Thread serve = new Thread(() => Serve(client));
            serve.IsBackground = true;
            serve.Start();
        }
    }

    private static void Serve(TcpClient client)
    {
        try
        {
            using (client)
            {
                NetworkStream stream = client.GetStream();
                stream.ReadTimeout = 30000;
                byte[] buffer = new byte[8192];
                int length = 0;
                while (Encoding.ASCII.GetString(buffer, 0, length).IndexOf("\r\n\r\n", StringComparison.Ordinal) < 0 && length < buffer.Length)
                {
                    int read = stream.Read(buffer, length, buffer.Length - length);
                    if (read <= 0) { return; }
                    length += read;
                }

                byte[] body = new byte[20000];
                body[0] = 0x50; body[1] = 0x4B; body[2] = 5; body[3] = 6;
                string header = "HTTP/1.1 200 OK\r\nContent-Type: application/zip\r\nContent-Disposition: attachment; filename=\"invoice.zip\"\r\nContent-Length: " + body.Length + "\r\nConnection: close\r\n\r\n";
                byte[] head = Encoding.ASCII.GetBytes(header);
                stream.Write(head, 0, head.Length);
                stream.Write(body, 0, body.Length);
            }
        }
        catch (Exception)
        {
        }
    }
}
'@

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
New-Item -ItemType Directory -Force -Path $inbox, $pictures, $downloaded, $downloads | Out-Null
New-TestFile (Join-Path $inbox 'there.pdf')

# A hidden system folder, as the Recycle Bin's own folders are, which only Windows uses.
New-Item -ItemType Directory -Force -Path $systemFolder | Out-Null
(Get-Item $systemFolder -Force).Attributes = 'Directory, Hidden, System'

# An exFAT virtual disk, as X:, which can't keep a file's Mark of the Web.
$diskOutput = Invoke-DiskPart @"
create vdisk file="$root\card.vhdx" maximum=64 type=expandable
select vdisk file="$root\card.vhdx"
attach vdisk
create partition primary
format fs=exfat quick label=CARD
assign letter=X
"@
Test-Check 'an exFAT virtual disk is there' (Wait-Until { Test-Path 'X:\' } 10) ($diskOutput -replace "`r?`n", ' | ')
New-Item -ItemType Directory -Force -Path $card | Out-Null

# A share on the Sandbox itself, for a folder on a network location.
New-Item -ItemType Directory -Force -Path $shareFolder, $fromShare | Out-Null
New-SmbShare -Name FolderRulesShare -Path $shareFolder -FullAccess Everyone | Out-Null
Test-Check 'a share on the Sandbox is there' (Wait-Until { Test-Path $share } 10)

# The Recycle Bin for C: holds 1 MB, so that a 50 MB log is too big for it. A process reads the
# Recycle Bin's settings once, so they're set before the host starts.
$volume = Get-CimInstance Win32_Volume | Where-Object { $_.DriveLetter -eq 'C:' }
$bitBucket = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\BitBucket\Volume\$(([regex] '\{[0-9a-fA-F-]+\}').Match($volume.DeviceID).Value)"
$recycleSettings = Get-ItemProperty $bitBucket -ErrorAction SilentlyContinue
New-Item -Force -Path $bitBucket -ErrorAction SilentlyContinue | Out-Null
Set-ItemProperty $bitBucket -Name MaxCapacity -Value 1 -Type DWord

Write-FolderRules $true
Start-Process $hostExe
Test-Check 'the host uses the folder and its rules' (Wait-Until { (Get-HostLog) -match 'Folder Rules applied: 4 folders and 8 rules can be used\.' } 15)
Test-Check 'the host looks at the folder it never looked at before' (Wait-Until { (Get-HostLog) -match 'Folder Rules looked at folder 1: 0 of 1 files arrived since it last looked\.' } 10)
Test-Check 'the host doesn''t watch the hidden system folder' (Wait-Until { (Get-HostLog) -match 'Folder Rules folder 4 can''t be used: "?SystemFolder"?\.' } 10)

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

# A hidden file, an empty one and one in the hidden system folder stay.
New-TestFile (Join-Path $inbox 'hidden.pdf')
New-TestFile (Join-Path $systemFolder 'system.pdf')
(Get-Item "$inbox\hidden.pdf").Attributes = 'Hidden'
New-TestFile (Join-Path $inbox 'empty.pdf') 0
Start-Sleep -Seconds 6
Test-Check 'a hidden file stays' (Test-Path "$inbox\hidden.pdf" -PathType Leaf)
Test-Check 'an empty file stays' (Test-Path "$inbox\empty.pdf")
Test-Check 'a PDF in the hidden system folder stays' (Test-Path "$systemFolder\system.pdf")

# A PDF with a Mark of the Web keeps it when it moves.
New-TestFile (Join-Path $inbox 'marked.pdf')
Set-Mark "$inbox\marked.pdf"
Test-Check 'a PDF with a Mark of the Web moves' (Wait-Until { Test-Path "$sorted\pdf\marked.pdf" } 15)
Test-Check 'and keeps its mark' ((Get-Mark "$sorted\pdf\marked.pdf") -match 'HostUrl=https://downloads\.example\.com/file')

# On the exFAT disk, which can't keep a mark, a file with one stays and one without moves there.
New-TestFile (Join-Path $inbox 'card-marked.txt')
Set-Mark "$inbox\card-marked.txt"
New-TestFile (Join-Path $inbox 'card-plain.txt')
Test-Check 'a file without a mark moves to the exFAT disk' (Wait-Until { Test-Path "$card\card-plain.txt" } 15)
Test-Check 'a file with a mark stays rather than lose it there' (Wait-Until { @(Get-History | Where-Object { $_.name -eq 'card-marked.txt' -and $_.reason -eq 'MarkWouldBeLost' }).Count -eq 1 } 10)
Test-Check 'and the file and its mark are still there' ((Test-Path "$inbox\card-marked.txt") -and ((Get-Mark "$inbox\card-marked.txt") -match 'ZoneId=3'))

# A download in Edge from the test site arrives in Downloads, matches Downloaded from, and moves
# with its mark.
$hosts = 'C:\Windows\System32\drivers\etc\hosts'
$hostsBefore = Get-Content $hosts -Raw
Add-Content -Path $hosts -Value "`r`n127.0.0.1 downloads.folder-rules.test" -Encoding ascii
$server = New-Object DownloadServer 8080
$edge = 'C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe'
Start-Process $edge -ArgumentList @('--no-first-run', '--no-default-browser-check', "--user-data-dir=$root\edge", 'http://downloads.folder-rules.test:8080/invoice.zip')
Test-Check 'a download from the test site moves by Downloaded from' (Wait-Until { Test-Path "$downloaded\invoice.zip" } 60) "$((Get-ChildItem $downloads -Name -Force) -join ', ')"
Save-Screenshot 'folder-rules-download'
Test-Check 'with its mark naming the site' ((Get-Mark "$downloaded\invoice.zip") -match 'HostUrl=http://downloads\.folder-rules\.test:8080/invoice\.zip') (Get-Mark "$downloaded\invoice.zip")
Get-Processes 'msedge' | ForEach-Object { Stop-Process -Id $_.Id -Force -ErrorAction SilentlyContinue }
$server.Dispose()
Set-Content -Path $hosts -Value $hostsBefore -NoNewline -Encoding ascii

# A PDF written to the share moves. Removed, the share's watch stops; once it's back, the folder is
# watched again within a minute, and a PDF written while it was away moves.
New-TestFile (Join-Path $share 'scan.pdf')
Test-Check 'a PDF written to a shared folder moves' (Wait-Until { Test-Path "$fromShare\scan.pdf" } 15)
Remove-SmbShare -Name FolderRulesShare -Force
Test-Check 'with the share removed, its watch stops' (Wait-Until { (Get-HostLog) -match "Folder Rules' watch of folder 3 stopped" } 20)
New-TestFile (Join-Path $shareFolder 'while-away.pdf')
New-SmbShare -Name FolderRulesShare -Path $shareFolder -FullAccess Everyone | Out-Null
Test-Check 'the share is watched again within a minute' (Wait-Until { (Get-HostLog) -match 'Folder Rules watches folder 3 again\.' } 90)
Test-Check 'and the PDF written while it was away moves' (Wait-Until { Test-Path "$fromShare\while-away.pdf" } 15)

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

# The settings app gets the history through the pipe.
$answer = Invoke-FolderRulesPipe @{ kind = 'History' }
Test-Check 'the pipe gives the history' (($answer.status -eq 'Done') -and (@($answer.entries).Count -eq $history.Count) -and ($answer.entries[0].id -eq $history[0].id)) "$($answer.status), $(@($answer.entries).Count) of $($history.Count) entries"

# Run now lists the files there that the rules would act on: the PDF that was there before, and
# not the picture they copied, the logs Windows' answers kept, or the empty and hidden files.
$answer = Invoke-FolderRulesPipe @{ kind = 'RunNowList'; folder = $inbox }
$listed = @($answer.files)
$names = ($listed | ForEach-Object { $_.name }) -join ', '
$there = $listed | Where-Object { $_.name -eq 'there.pdf' }
Test-Check 'Run now lists the PDF that was there, to move into the pdf subfolder' (($answer.status -eq 'Done') -and ($there.action -eq 'Move') -and ($there.destination -eq "$sorted\pdf")) $names
Test-Check 'and not the files the rules acted on, or the empty and hidden ones' (-not ($listed | Where-Object { $_.name -in 'photo.png', 'big-no.log', 'empty.pdf', 'hidden.pdf' })) $names
$answer = Invoke-FolderRulesPipe @{ kind = 'RunNow'; folder = $inbox; files = @(@{ name = 'there.pdf'; size = $there.size; modified = $there.modified }) }
Test-Check 'Run now moves the chosen PDF' (($answer.status -eq 'Done') -and (Wait-Until { (Test-Path "$sorted\pdf\there.pdf") -and -not (Test-Path "$inbox\there.pdf") } 15)) "$($answer.status)"
Test-Check 'and only that file' ((Test-Path "$inbox\nuke.log") -and (Test-Path "$inbox\taken.png"))
$answer = Invoke-FolderRulesPipe @{ kind = 'RunNow'; folder = $inbox; files = @(@{ name = '..\Sorted\pdf\report.pdf'; size = 1000; modified = $there.modified }) }
Test-Check 'Run now refuses a file outside the folder' (($answer.status -eq 'Refused') -and (Test-Path "$sorted\pdf\report.pdf")) "$($answer.status)"

# Undo puts the PDF back, where the rules leave it.
$entry = Get-History | Where-Object { $_.name -eq 'there.pdf' } | Select-Object -First 1
$answer = Invoke-FolderRulesPipe @{ kind = 'Undo'; entry = $entry.id; time = $entry.time }
Test-Check 'Undo puts the PDF back' (($answer.status -eq 'Done') -and (Test-Path "$inbox\there.pdf") -and -not (Test-Path "$sorted\pdf\there.pdf")) "$($answer.status)"
Start-Sleep -Seconds 5
Test-Check 'and it stays' ((Test-Path "$inbox\there.pdf") -and ((Get-HostLog) -match 'Folder Rules folder 1 already handled a file that came back'))
Test-Check 'the history says it was undone' ($null -ne (Get-History | Where-Object { $_.id -eq $entry.id }).undone)
$answer = Invoke-FolderRulesPipe @{ kind = 'Undo'; entry = $entry.id; time = $entry.time }
Test-Check 'it isn''t undone twice' ($answer.status -eq 'Refused') "$($answer.status)"

# The pipe refuses a request it doesn't know, and Clear history empties the history.
$answer = Invoke-FolderRulesPipe @{ kind = 'Format' }
Test-Check 'the pipe refuses a request it doesn''t know' ($answer.status -eq 'Refused') "$($answer.status)"
$answer = Invoke-FolderRulesPipe @{ kind = 'ClearHistory' }
Test-Check 'Clear history through the pipe empties the history file' (($answer.status -eq 'Done') -and (Wait-Until { (Get-History).Count -eq 0 } 5)) "$($answer.status)"

# Turned off, the folder isn't watched.
Write-FolderRules $false
Test-Check 'turned off, Folder Rules'' thread ends' (Wait-Until { (Get-HostLog) -match "Folder Rules' thread ended\." } 10)
New-TestFile (Join-Path $inbox 'after.pdf')
Start-Sleep -Seconds 6
Test-Check 'turned off, a file that arrives stays' (Test-Path "$inbox\after.pdf")
Test-Check 'turned off, the host serves no pipe' ($null -eq (Invoke-FolderRulesPipe @{ kind = 'History' }))

$hostLog = Get-HostLog
Test-Check 'the logs hold no file names or folders' (($hostLog -notmatch 'report') -and ($hostLog -notmatch 'photo') -and ($hostLog -notmatch 'FolderRulesTest') -and ($hostLog -notmatch 'note\.txt'))
Test-Check 'the host logged the move by number' ($hostLog -match 'Folder Rules folder 1, rule 1: "?Moved"?\.')

# The page lists the folders and their rules, with what each rule does and why a folder can't be
# used. Add folder, the switches, moving and deleting a rule and removing a folder save at once,
# and keyboard focus stays near what changed, though the list is built again.
function Get-FolderRulesSettings { (Get-Content (Join-Path $dataFolder 'settings.json') -Raw | ConvertFrom-Json).features.folderRules }
function Get-FocusedName { try { [System.Windows.Automation.AutomationElement]::FocusedElement.Current.Name } catch { '' } }

# A menu item of the settings app, by its name: a folder in the list can have the same name. The
# condition's name isn't $condition, which in Wait-Until's script block would be Wait-Until's own.
function Invoke-AppMenuItem([string] $Name) {
    $menuItem = New-Object System.Windows.Automation.AndCondition(
        (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, $Name)),
        (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::MenuItem)))
    $script:item = $null
    if (-not (Wait-Until { ($script:item = (Get-AppWindow).FindFirst([System.Windows.Automation.TreeScope]::Descendants, $menuItem)) -ne $null } 5)) { return $false }
    $script:item.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    return $true
}

Start-Process $appExe
[void] (Wait-Until { Get-AppWindow })
Start-Sleep -Seconds 3
Test-Check 'select Folder Rules' (Show-Page 'Folder Rules')
Start-Sleep -Seconds 2
$text = Get-WindowText
Test-Check 'the page lists the folders and their rules' (
    ($text -contains 'Inbox') -and ($text -contains 'Downloads') -and ($text -contains '\\localhost\FolderRulesShare') -and ($text -contains 'PDFs') -and
    ($text -contains "Moves to $sorted\{extension}") -and ($text -contains "Copies to $pictures") -and ($text -contains 'Renames to {date} {name}') -and
    ($text -contains 'Moves to the Recycle Bin') -and ($text -contains 'Permissions & data')) ($text -join ' | ')
Test-Check 'with UAC off, the page doesn''t say Folder Rules is paused' ($text -notcontains 'While PC Companion runs as administrator after a UAC prompt, Folder Rules is paused: it never changes files with administrator rights.')
Test-Check 'the hidden system folder says it can''t be used' (Wait-Until { Find-AppText "Rules can't change files in hidden system folders, such as the Recycle Bin's own folders, which only Windows uses." } 5)
$runNow = Find-AppElement 'Run now for Windows only'
Test-Check 'and its Run now is off' (($null -ne $runNow) -and -not $runNow.Current.IsEnabled)
Save-Screenshot '15a-folder-rules-page'

$desktop = [Environment]::GetFolderPath('DesktopDirectory')
Test-Check 'Add folder offers Downloads, which is in the list already' ((Invoke-AppElement 'Add folder') -and (Invoke-AppMenuItem 'Downloads') -and (Wait-Until { Find-AppText 'This folder is already in the list.' } 5))
Test-Check 'and adds Desktop' ((Invoke-AppElement 'Add folder') -and (Invoke-AppMenuItem 'Desktop') -and (Wait-Until { (@((Get-FolderRulesSettings).folders) | Select-Object -Last 1).path -eq $desktop } 5)) ((Get-FolderRulesSettings).folders.path -join ', ')
Test-Check 'Desktop shows, without rules' ((Wait-Until { Find-AppText 'Desktop' } 5) -and (Find-AppText 'No rules yet.'))
Test-Check 'a folder''s switch turns it off' ((Set-Toggle 'Watch Desktop' $false) -and (Wait-Until { (@((Get-FolderRulesSettings).folders) | Select-Object -Last 1).enabled -eq $false } 5))
Test-Check 'a rule''s switch turns it off' ((Set-Toggle 'Use PDFs' $false) -and (Wait-Until { @((Get-FolderRulesSettings).folders)[0].rules[0].enabled -eq $false } 5))
[void] (Set-Toggle 'Use PDFs' $true)

Test-Check 'More options opens the rule''s menu' (Invoke-AppElement 'More options for Pictures')
Start-Sleep -Seconds 1
Test-Check 'Move down moves the rule' (Invoke-AppMenuItem 'Move down')
$order = { (@((Get-FolderRulesSettings).folders)[0].rules | ForEach-Object { $_.name }) -join ', ' }
Test-Check 'the page saves the new order' (Wait-Until { (& $order) -eq 'PDFs, Notes, Pictures, Logs, To the card' } 5) (& $order)
Test-Check 'focus stays on the rule that moved' (Wait-Until { (Get-FocusedName) -eq 'More options for Pictures' } 3) (Get-FocusedName)

[void] (Invoke-AppElement 'More options for Notes')
Start-Sleep -Seconds 1
Test-Check 'Delete asks first' ((Invoke-AppMenuItem 'Delete') -and (Wait-Until { Find-AppElement 'Delete Notes?' } 5))
Start-Sleep -Seconds 1
[void] (Invoke-AppElement 'Delete')
Test-Check 'the page deletes the rule' (Wait-Until { (& $order) -eq 'PDFs, Pictures, Logs, To the card' } 5) (& $order)
Test-Check 'focus goes to the rule''s folder' (Wait-Until { (Get-FocusedName) -eq 'More options for Inbox' } 3) (Get-FocusedName)

[void] (Invoke-AppElement 'More options for Desktop')
Start-Sleep -Seconds 1
Test-Check 'Remove asks first' ((Invoke-AppMenuItem 'Remove') -and (Wait-Until { Find-AppElement 'Remove Desktop?' } 5))
Start-Sleep -Seconds 1
[void] (Invoke-AppElement 'Remove')
Test-Check 'the page removes the folder' (Wait-Until { @((Get-FolderRulesSettings).folders).Count -eq 4 } 5) ((Get-FolderRulesSettings).folders.path -join ', ')
Test-Check 'focus goes to the folder before it' (Wait-Until { (Get-FocusedName) -eq 'More options for Windows only' } 3) (Get-FocusedName)
Save-Screenshot '15b-folder-rules-page-changed'

# The rule editor lists the files there that the rule would act on as it's edited, shows problems
# below their boxes, and saves the rule; Edit opens it again, and a template opens with its
# destination, whose folder Create folder makes.
Test-Check 'Add rule opens the rule editor' ((Invoke-AppElement 'Add a rule to Inbox') -and (Invoke-AppMenuItem 'Empty rule') -and (Wait-Until { Find-AppElement 'Add a rule' } 5))
Start-Sleep -Seconds 1
Test-Check 'an empty rule can''t be saved' (-not (Test-AppEnabled 'Save'))
[void] (Set-AppBox 'Name' 'Test PDFs')
[void] (Set-Toggle 'Type' $true)
[void] (Set-AppBox 'Type' 'p/df')
Test-Check 'an extension a file name can''t have is refused' ((Wait-Until { Find-AppText 'An extension is empty, has a dot inside it, or has characters a file name can''t have.' } 3) -and -not (Test-AppEnabled 'Save'))
[void] (Set-AppBox 'Type' 'pdf')
[void] (Set-AppBox 'Destination' $sorted)
Test-Check 'the editor lists the PDF there and where it would go' (Wait-Until { Find-AppText "Moves to $sorted\there.pdf" } 5)
Test-Check 'and how many files the rule would act on' (@(Get-WindowText | Where-Object { $_ -like 'Files the rule would act on: * of *' }).Count -gt 0) ((Get-WindowText | Where-Object { $_ -like 'Files the rule*' }) -join ' | ')
Save-Screenshot '15c-folder-rules-editor'
Test-Check 'Save the rule' ((Test-AppEnabled 'Save') -and (Invoke-AppElement 'Save'))
$last = { @(@((Get-FolderRulesSettings).folders)[0].rules) | Select-Object -Last 1 }
Test-Check 'the page saves the new rule' (Wait-Until { $rule = & $last; ($rule.name -eq 'Test PDFs') -and ((@($rule.extensions) -join ',') -eq 'pdf') -and ($rule.destination -eq $sorted) -and ($rule.action -eq 'Move') } 5) ((& $last) | ConvertTo-Json -Compress -Depth 4)
Test-Check 'focus goes to the new rule' (Wait-Until { (Get-FocusedName) -eq 'Edit Test PDFs' } 3) (Get-FocusedName)
Test-Check 'Edit opens the rule' ((Invoke-AppElement 'Edit Test PDFs') -and (Wait-Until { Find-AppElement 'Edit the rule' } 5) -and ((Get-AppBox 'Name') -eq 'Test PDFs')) (Get-AppBox 'Name')
[void] (Invoke-AppElement 'Cancel')
Start-Sleep -Seconds 1
Test-Check 'a template opens with its destination' ((Invoke-AppElement 'Add a rule to Inbox') -and (Invoke-AppMenuItem 'Installers to an Installers folder') -and (Wait-Until { (Get-AppBox 'Destination') -eq "$inbox\Installers" } 5)) (Get-AppBox 'Destination')
Test-Check 'and says its folder doesn''t exist yet' (Wait-Until { Find-AppText 'This folder doesn''t exist yet. Until it does, the rule leaves the files where they are.' } 5)
Test-Check 'Create folder makes it' ((Invoke-AppElement 'Create folder') -and (Wait-Until { Test-Path "$inbox\Installers" -PathType Container } 5))
Save-Screenshot '15d-folder-rules-template'
[void] (Invoke-AppElement 'Cancel')
Start-Sleep -Seconds 1

# With Folder Rules on, Run now lists the files the folder's rules would act on, each with its
# box, and acts on those left checked; the history shows what the rules did, Undo puts a file
# back, Open Recycle Bin opens it, and Clear history asks first.
function Invoke-DialogButton([string] $Dialog, [string] $Name) {
    $found = Find-AppDialog $Dialog
    if (-not $found) { return $false }
    $button = $found.FindFirst([System.Windows.Automation.TreeScope]::Descendants, (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, $Name)))
    if (-not $button) { return $false }
    $button.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    return $true
}
# A window on the desktop whose title starts with $Name, as File Explorer's Recycle Bin - File Explorer.
function Find-TopWindow([string] $Name) {
    @([System.Windows.Automation.AutomationElement]::RootElement.FindAll([System.Windows.Automation.TreeScope]::Children, [System.Windows.Automation.Condition]::TrueCondition)) |
        Where-Object { $_.Current.Name -like "$Name*" } | Select-Object -First 1
}

Test-Check 'turn Folder Rules on from its page' ((Set-Toggle 'Folder Rules' $true) -and (Wait-Until { $null -ne (Invoke-FolderRulesPipe @{ kind = 'History' }) } 15))
Test-Check 'the history has no entries yet' ((Invoke-AppElement 'Refresh') -and (Wait-Until { Find-AppText "The rules haven't acted on any file yet." } 5))

Test-Check 'Run now lists the folder''s files' ((Invoke-AppElement 'Run now for Inbox') -and (Wait-Until { Find-AppElement 'there.pdf' } 15) -and (Find-AppElement 'after.pdf'))
$boxType = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::CheckBox)
$boxes = @((Get-AppWindow).FindAll([System.Windows.Automation.TreeScope]::Descendants, $boxType))
foreach ($box in $boxes | Where-Object { $_.Current.Name -ne 'after.pdf' }) {
    $toggle = $box.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern)
    if ($toggle.Current.ToggleState -eq [System.Windows.Automation.ToggleState]::On) { $toggle.Toggle() }
}
Test-Check 'each file has its box, checked' ($boxes.Count -ge 2) "$($boxes.Count) boxes: $(($boxes | ForEach-Object { $_.Current.Name }) -join ', ')"
Save-Screenshot '15e-folder-rules-run-now'
Test-Check 'Run acts on the file left checked' ((Invoke-DialogButton 'Run now: Inbox' 'Run') -and (Wait-Until { (Test-Path "$sorted\pdf\after.pdf") -and -not (Test-Path "$inbox\after.pdf") } 15))
Test-Check 'and leaves the others' ((Test-Path "$inbox\there.pdf") -and (Test-Path "$inbox\nuke.log"))
Test-Check 'the history shows the move' (Wait-Until { Find-AppText "Moved to $sorted\pdf\after.pdf" } 15)

Test-Check 'Undo puts the file back' ((Invoke-AppElement 'Undo after.pdf') -and (Wait-Until { (Test-Path "$inbox\after.pdf") -and -not (Test-Path "$sorted\pdf\after.pdf") } 10))
Test-Check 'and the history says so' (Wait-Until { Find-AppText "Moved to $sorted\pdf\after.pdf Put back with Undo." } 10)
Test-Check 'and focus goes to Refresh' (Wait-Until { (Get-FocusedName) -eq 'Refresh' } 3) (Get-FocusedName)
Save-Screenshot '15f-folder-rules-history'

Test-Check 'Open Recycle Bin opens it' ((Invoke-AppElement 'Open Recycle Bin') -and (Wait-Until { Find-TopWindow 'Recycle Bin' } 15))
$recycleBin = Find-TopWindow 'Recycle Bin'
if ($recycleBin) { $recycleBin.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).Close() }

Test-Check 'Clear history asks first' ((Invoke-AppElement 'Clear history') -and (Wait-Until { Find-AppDialog 'Clear the history?' } 5))
Start-Sleep -Seconds 1
Test-Check 'and empties it' ((Invoke-DialogButton 'Clear the history?' 'Clear history') -and (Wait-Until { Find-AppText "The rules haven't acted on any file yet." } 10))

Stop-All
Remove-SmbShare -Name FolderRulesShare -Force -ErrorAction SilentlyContinue
[void] (Invoke-DiskPart @"
select vdisk file="$root\card.vhdx"
detach vdisk
"@)
Remove-Item $root -Recurse -Force -ErrorAction SilentlyContinue
