# Drive Actions: with drives in the settings file, the host recognizes virtual disks the checks
# attach with diskpart. A disk with a DCIM folder is a camera card: Camera cards acts at once and
# imports its photos into month folders, then asks Windows to eject it, which Windows refuses for a
# virtual disk; attached again, it imports nothing again. A disk with the ID file and the hashes of
# its serial numbers in the settings is the backup drive: it asks first, and selecting the
# notification, sent to the PC Companion icon, backs up a folder to it; a changed file then replaces
# its copy. An unfinished copy, as a disk pulled out during a copy leaves, is deleted by the next
# backup, which then copies the file. A disk with a copy of the ID file and another serial number
# gets nothing. A camera card encrypted with BitLocker arrives locked and gets nothing until it's
# unlocked, and is imported then. While the session is locked, as the session's messages to the feature's window say,
# nothing runs, and the card is asked about once it's unlocked; a card attached before the host
# starts is asked about too. Through the pipe, as the settings app asks, the host lists the
# drives, Run now lists the card's photos and copies only the one chosen, Undo sends its copy to
# the Recycle Bin, and Add a drive writes a new disk's ID file and gives its entry. The history
# file has the runs, and the logs hold no drive names or folders. Detailed logging is on for the
# Debug entries.

$root = Join-Path $env:USERPROFILE 'DriveActionsTest'
$documents = Join-Path $root 'Documents'
$pictures = Join-Path $root 'Pictures'
$disks = Join-Path $root 'Disks'
$historyFile = Join-Path $dataFolder 'drive-actions.json'
$detailedUntil = [DateTimeOffset]::Now.AddHours(1).ToString('o')
$driveId = '5f0c9a0e3b1d4c7e9a2f6b8d1c3e5a7f'
$taken = [DateTime]::new(2026, 10, 30, 14, 5, 0)
$NIN_BALLOONUSERCLICK = 0x0405
$WM_WTSSESSION_CHANGE = 0x02B1

# Runs diskpart with these commands, for the virtual disks.
function Invoke-DriveDiskPart([string] $Commands) {
    $script = Join-Path $env:TEMP 'drive-actions-diskpart.txt'
    Set-Content -Path $script -Value $Commands -Encoding ascii
    $output = diskpart /s $script 2>&1 | Out-String
    Remove-Item $script -ErrorAction SilentlyContinue
    $output
}

function New-TestDisk([string] $Name, [string] $FileSystem, [string] $Label, [string] $Letter, [int] $Megabytes = 256) {
    $output = Invoke-DriveDiskPart @"
create vdisk file="$disks\$Name.vhdx" maximum=$Megabytes type=expandable
select vdisk file="$disks\$Name.vhdx"
attach vdisk
convert mbr
create partition primary
format fs=$FileSystem quick label=$Label
assign letter=$Letter
"@
    Test-Check "the virtual disk $Name is there" (Wait-Until { Test-Path "$($Letter):\" } 20) ($output -replace "`r?`n", ' | ')
}

function Mount-TestDisk([string] $Name, [string] $Letter) {
    [void] (Invoke-DriveDiskPart "select vdisk file=`"$disks\$Name.vhdx`"`r`nattach vdisk")
    [void] (Wait-Until { Test-Path "$($Letter):\" } 20)
}

function Dismount-TestDisk([string] $Name, [string] $Letter) {
    [void] (Invoke-DriveDiskPart "select vdisk file=`"$disks\$Name.vhdx`"`r`ndetach vdisk")
    [void] (Wait-Until { -not (Test-Path "$($Letter):\") } 20)
}

# The hash Drive Actions keeps of a serial number: the first 16 hexadecimal digits of the SHA-256
# of the text and the number.
function Get-SerialHash([string] $Value) {
    $sha = [Security.Cryptography.SHA256]::Create()
    try {
        $hash = $sha.ComputeHash([Text.Encoding]::UTF8.GetBytes("PCCompanion.DriveActions:$Value"))
        -join ($hash[0..7] | ForEach-Object { $_.ToString('x2') })
    } finally {
        $sha.Dispose()
    }
}

function Get-VolumeSerial([string] $Letter) {
    (Get-CimInstance Win32_LogicalDisk -Filter "DeviceID='$($Letter):'").VolumeSerialNumber.ToUpperInvariant()
}

function Write-DriveActions([string] $BackupSerial) {
    $cards = @{
        name = 'Camera cards'; kind = 'CameraCards'; whenConnected = 'Act'; ejectWhenDone = $true
        actions = @(@{ action = 'Import'; from = 'DCIM'; extensions = @('jpg', 'cr3'); destination = "$pictures\{year}\{month}"; ifExists = 'AddNumber' })
    }
    $backup = @{
        name = 'Backup drive'; kind = 'Drive'; id = $driveId; volumeSerial = (Get-SerialHash $BackupSerial); hardwareSerial = ''; whenConnected = 'Ask'
        actions = @(@{ action = 'BackUp'; folders = @($documents); destination = 'Backup' })
    }
    $section = @{ enabled = $true; drives = @($cards, $backup) }
    $settings = @{ schemaVersion = 1; general = @{ theme = 'Light' }; updates = @{ checkAutomatically = $false }; diagnostics = @{ detailedLoggingUntil = $detailedUntil }; features = @{ driveActions = $section } }
    Write-Settings ($settings | ConvertTo-Json -Depth 8)
}

function Write-IdFile([string] $Letter) {
    $path = "$($Letter):\.pccompanion-drive"
    Set-Content -Path $path -Value $driveId -NoNewline -Encoding ascii
    (Get-Item $path -Force).Attributes = 'Hidden, System'
}

# Asks the host through Drive Actions' pipe, as the settings app does, and returns its answer, or
# $null when the host doesn't serve the pipe.
function Invoke-DriveActionsPipe([hashtable] $Request) {
    $pipe = New-Object IO.Pipes.NamedPipeClientStream('.', "PCCompanion.DriveActions.$((Get-Process -Id $PID).SessionId)", [IO.Pipes.PipeDirection]::InOut)
    try {
        $pipe.Connect(3000)
        $pipe.ReadMode = [IO.Pipes.PipeTransmissionMode]::Message
        $bytes = [Text.Encoding]::UTF8.GetBytes(($Request | ConvertTo-Json -Depth 6 -Compress))
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

function Get-ConnectedDrive([string] $Letter) { @((Invoke-DriveActionsPipe @{ kind = 'Drives' }).drives) | Where-Object { $_.letter -eq $Letter } }

function Send-SessionChange([int] $Change) {
    $window = [Win32]::FindWindow('PCCompanion.DriveActions.Devices', $null)
    [void] [Win32]::PostMessage($window, $WM_WTSSESSION_CHANGE, [UIntPtr] [uint32] $Change, [IntPtr]::Zero)
}

New-Item -ItemType Directory -Force -Path $documents, $pictures, $disks | Out-Null
Set-Content -Path (Join-Path $documents 'notes.txt') -Value 'notes'
New-Item -ItemType Directory -Force -Path (Join-Path $documents 'Work') | Out-Null
Set-Content -Path (Join-Path $documents 'Work\plan.txt') -Value 'plan'

# The camera card, exFAT with photos in DCIM; the backup drive, NTFS with the ID file; a look-alike,
# with the same label and a copy of the ID file, and another serial number.
New-TestDisk 'card' 'exfat' 'EOS_DIGITAL' 'P'
New-Item -ItemType Directory -Force -Path 'P:\DCIM\100TEST', 'P:\DCIM\101TEST' | Out-Null
foreach ($photo in 'P:\DCIM\100TEST\IMG_0001.JPG', 'P:\DCIM\101TEST\IMG_0002.CR3', 'P:\DCIM\100TEST\clip.mov') {
    Set-Content -Path $photo -Value 'photo'
    (Get-Item $photo).LastWriteTime = $taken
}
Dismount-TestDisk 'card' 'P'

New-TestDisk 'backup' 'ntfs' 'BACKUP' 'Q' 6144
Write-IdFile 'Q'
$backupSerial = Get-VolumeSerial 'Q'
Dismount-TestDisk 'backup' 'Q'

New-TestDisk 'lookalike' 'ntfs' 'BACKUP' 'R'
Write-IdFile 'R'
Dismount-TestDisk 'lookalike' 'R'

# A camera card encrypted with BitLocker, with a recovery password only, so that it arrives locked.
New-TestDisk 'lockedcard' 'ntfs' 'LOCKED' 'S' 512
New-Item -ItemType Directory -Force -Path 'S:\DCIM\100TEST' | Out-Null
Set-Content -Path 'S:\DCIM\100TEST\IMG_0101.JPG' -Value 'photo'
(Get-Item 'S:\DCIM\100TEST\IMG_0101.JPG').LastWriteTime = $taken
$encrypting = cmd.exe /c 'manage-bde -on S: -RecoveryPassword -UsedSpaceOnly 2>&1' | Out-String
$recoveryPassword = ([regex] '\d{6}(-\d{6}){7}').Match($encrypting).Value
Test-Check 'BitLocker encrypts the card''s virtual disk' (($recoveryPassword.Length -gt 0) -and (Wait-Until { (cmd.exe /c 'manage-bde -status S: 2>&1' | Out-String) -match 'Percentage Encrypted:\s*100' } 120)) ($encrypting -replace "`r?`n", ' | ')
Dismount-TestDisk 'lockedcard' 'S'

Write-DriveActions $backupSerial
Start-Process $hostExe
Test-Check 'the host uses the drives and their actions' (Wait-Until { (Get-HostLog) -match 'Drive Actions applied its settings: 2 drives and 2 actions can be used\.' } 15)

# Camera cards acts at once: the photos go into month folders, the video stays.
Mount-TestDisk 'card' 'P'
$month = Join-Path $pictures '2026\10'
Test-Check 'a camera card''s photos are imported into a month folder' (Wait-Until { (Test-Path "$month\IMG_0001.JPG") -and (Test-Path "$month\IMG_0002.CR3") } 20)
Test-Check 'the run ends with both photos imported' (Wait-Until { (Get-HostLog) -match 'The run for the drive "?P"? ended: "?Done"?, 2 files copied, 0 skipped\.' } 10)
Test-Check 'a file of another type isn''t imported' (-not (Test-Path "$month\clip.mov"))
Test-Check 'the card is recognized as a camera card' ((Get-HostLog) -match 'A drive arrived as "?P"?: "?CameraCards"?, drive 1 in the list\.')
Test-Check 'with Eject when done, Windows is asked to eject it, and refuses for a virtual disk' (Wait-Until { (Get-HostLog) -match 'Asked Windows to eject the drive "?P"? after its actions: ejected [Ff]alse' } 10)
Test-Check 'and the disk stays' (Test-Path 'P:\DCIM')

# Attached again, nothing is imported again.
$runs = Get-MatchCount (Get-HostLog) 'The run for the drive "?P"? ended'
Dismount-TestDisk 'card' 'P'
Mount-TestDisk 'card' 'P'
Test-Check 'attached again, the card''s run imports nothing' (Wait-Until { (Get-HostLog) -match 'The run for the drive "?P"? ended: "?Done"?, 0 files copied, 0 skipped\.' } 20)
Test-Check 'and no photo gets a second copy' (-not (Test-Path "$month\IMG_0001 (2).JPG"))
Dismount-TestDisk 'card' 'P'

# The backup drive asks first; selecting the notification backs up the folder.
Mount-TestDisk 'backup' 'Q'
Test-Check 'the backup drive is recognized' (Wait-Until { (Get-HostLog) -match 'A drive arrived as "?Q"?: "?Recognized"?, drive 2 in the list\.' } 15)
Test-Check 'it asks first' (Wait-Until { (Get-HostLog) -match 'Asking whether to run the actions of the drive "?Q"?\.' } 10)
Test-Check 'nothing is backed up before the question is selected' (-not (Test-Path 'Q:\Backup'))
Test-Check 'select the notification' (Send-TrayEvent $NIN_BALLOONUSERCLICK)
Test-Check 'the folder is backed up to the drive' (Wait-Until { (Test-Path 'Q:\Backup\Documents\notes.txt') -and (Test-Path 'Q:\Backup\Documents\Work\plan.txt') } 20)
Test-Check 'no unfinished copy is left' (-not (Get-ChildItem 'Q:\Backup' -Recurse -Force -Filter '*.pccompanion-new' -ErrorAction SilentlyContinue))

# A changed file replaces its copy the next time.
Set-Content -Path (Join-Path $documents 'notes.txt') -Value 'notes, changed'
Dismount-TestDisk 'backup' 'Q'
Mount-TestDisk 'backup' 'Q'
$asked = Get-MatchCount (Get-HostLog) 'Asking whether to run the actions of the drive "?Q"?\.'
Test-Check 'attached again, it asks again' (Wait-Until { (Get-MatchCount (Get-HostLog) 'Asking whether to run the actions of the drive "?Q"?\.') -ge 2 } 15)
Test-Check 'select the notification again' (Send-TrayEvent $NIN_BALLOONUSERCLICK)
Test-Check 'the changed file replaces its copy' (Wait-Until { (Get-Content 'Q:\Backup\Documents\notes.txt' -Raw -ErrorAction SilentlyContinue) -match 'changed' } 20)
Test-Check 'and only it is copied' (Wait-Until { (Get-HostLog) -match 'The run for the drive "?Q"? ended: "?Done"?, 1 files copied, 0 skipped\.' } 10)
Dismount-TestDisk 'backup' 'Q'

# A drive pulled out during a copy leaves the unfinished copy, at its full size, under its temporary
# name, as the spike showed; the copy here is too fast to pull the disk out during it, so the
# leftover is made by hand, while the host isn't watching. The next backup deletes it and copies the file.
$large = Join-Path $documents 'large.bin'
& fsutil file createnew $large 104857600 | Out-Null
Stop-All
Mount-TestDisk 'backup' 'Q'
& fsutil file createnew 'Q:\Backup\Documents\large.bin.pccompanion-new' 52428800 | Out-Null
$asked = Get-MatchCount (Get-HostLog) 'Asking whether to run the actions of the drive "?Q"?\.'
Start-Process $hostExe
Test-Check 'connected before the host starts, the backup drive is asked about' (Wait-Until { (Get-MatchCount (Get-HostLog) 'Asking whether to run the actions of the drive "?Q"?\.') -gt $asked } 20)
Test-Check 'select the notification for the large file' (Send-TrayEvent $NIN_BALLOONUSERCLICK)
Test-Check 'the large file is backed up' (Wait-Until { (Get-Item 'Q:\Backup\Documents\large.bin' -ErrorAction SilentlyContinue).Length -eq 104857600 } 30)
Test-Check 'and the unfinished copy from before is gone' (-not (Test-Path 'Q:\Backup\Documents\large.bin.pccompanion-new'))
Test-Check 'the other copies stay' ((Get-Content 'Q:\Backup\Documents\notes.txt' -Raw) -match 'changed')
Dismount-TestDisk 'backup' 'Q'
Remove-Item $large -Force

# A look-alike with a copy of the ID file and another serial number gets nothing.
Mount-TestDisk 'lookalike' 'R'
Test-Check 'a drive with a copy of the ID file and another serial number is a look-alike' (Wait-Until { (Get-HostLog) -match 'The drive "?R"? has the ID file of drive 2 in the list, but other serial numbers, so it gets nothing\.' } 15)
Start-Sleep -Seconds 2
Test-Check 'and nothing is backed up to it' (-not (Test-Path 'R:\Backup'))
Dismount-TestDisk 'lookalike' 'R'

# A card encrypted with BitLocker arrives locked and gets nothing until it's unlocked.
Mount-TestDisk 'lockedcard' 'S'
Test-Check 'a card that arrives locked is seen as locked' (Wait-Until { (Get-HostLog) -match 'A drive arrived as "?S"?: "?Locked"?' } 15)
Start-Sleep -Seconds 2
Test-Check 'and gets nothing while it''s locked' (-not (Test-Path "$month\IMG_0101.JPG"))
$unlocking = cmd.exe /c "manage-bde -unlock S: -RecoveryPassword $recoveryPassword 2>&1" | Out-String
Test-Check 'unlocked, it''s recognized as a camera card' (Wait-Until { (Get-HostLog) -match 'A drive arrived as "?S"?: "?CameraCards"?' } 15) ($unlocking -replace "`r?`n", ' | ')
Test-Check 'and its photo is imported' (Wait-Until { Test-Path "$month\IMG_0101.JPG" } 20)
Dismount-TestDisk 'lockedcard' 'S'

# While the session is locked, nothing runs; the card is asked about once it's unlocked.
$cardRuns = Get-MatchCount (Get-HostLog) 'Running the actions of the drive "?P"?,'
$cardAsked = Get-MatchCount (Get-HostLog) 'Asking whether to run the actions of the drive "?P"?\.'
Send-SessionChange 7
Test-Check 'the host hears that the session is locked' (Wait-Until { (Get-HostLog) -match 'The session is locked: [Tt]rue\.' } 10)
Mount-TestDisk 'card' 'P'
Start-Sleep -Seconds 4
Test-Check 'while it''s locked, the card''s actions don''t run' ((Get-MatchCount (Get-HostLog) 'Running the actions of the drive "?P"?,') -eq $cardRuns)
Test-Check 'and nothing asks' ((Get-MatchCount (Get-HostLog) 'Asking whether to run the actions of the drive "?P"?\.') -eq $cardAsked)
Send-SessionChange 8
Test-Check 'once it''s unlocked, the card is asked about, though it acts at once otherwise' (Wait-Until { (Get-MatchCount (Get-HostLog) 'Asking whether to run the actions of the drive "?P"?\.') -gt $cardAsked } 10)
Test-Check 'and its actions still don''t run on their own' ((Get-MatchCount (Get-HostLog) 'Running the actions of the drive "?P"?,') -eq $cardRuns)

# A card attached before the host starts is asked about.
Stop-All
$cardAsked = Get-MatchCount (Get-HostLog) 'Asking whether to run the actions of the drive "?P"?\.'
Start-Process $hostExe
Test-Check 'a card connected before the host starts is asked about' (Wait-Until { (Get-MatchCount (Get-HostLog) 'Asking whether to run the actions of the drive "?P"?\.') -gt $cardAsked } 20)
Test-Check 'not acted on at once' ((Get-MatchCount (Get-HostLog) 'Running the actions of the drive "?P"?,') -eq $cardRuns)

# The settings app's requests, through the pipe: the drives, Run now with a photo chosen, the
# history and Undo.
$card = Get-ConnectedDrive 'P'
Test-Check 'the pipe lists the card as a camera card' ($card.outcome -eq 'CameraCards') ($card | ConvertTo-Json -Compress)
$cardRequest = @{ letter = 'P'; volume = $card.volume; key = 'camera-cards' }
$list = Invoke-DriveActionsPipe (@{ kind = 'RunNowList' } + $cardRequest)
$listed = @(@($list.actions)[0].files)
Test-Check 'Run now lists the card''s photos, imported before' (($listed.Count -eq 2) -and -not ($listed | Where-Object { -not $_.importedBefore })) "$($listed.Count) files"
$runNow = Invoke-DriveActionsPipe (@{ kind = 'RunNow'; chosen = @(@{ action = 0; files = @('100TEST\IMG_0001.JPG') }) } + $cardRequest)
Test-Check 'Run now with a photo chosen imports it again' (($runNow.status -eq 'Done') -and (Wait-Until { Test-Path "$month\IMG_0001 (2).JPG" } 20)) ($runNow | ConvertTo-Json -Compress)
Test-Check 'and only it' (Wait-Until { (Get-HostLog) -match 'The run for the drive "?P"? ended: "?Done"?, 1 files copied, 0 skipped\.' } 15)
$last = @((Invoke-DriveActionsPipe @{ kind = 'History' }).runs)[0]
Test-Check 'the pipe gives the history, newest first' (@($last.actions)[0].copied -eq 1)
$started = if ($last.started -is [DateTime]) { $last.started.ToString('o') } else { $last.started }
$undo = Invoke-DriveActionsPipe @{ kind = 'Undo'; run = $last.id; started = $started }
Test-Check 'Undo sends the copy to the Recycle Bin' (($undo.undone -eq 1) -and -not (Test-Path "$month\IMG_0001 (2).JPG")) ($undo | ConvertTo-Json -Compress)
Test-Check 'and the photo stays on the card' (Test-Path 'P:\DCIM\100TEST\IMG_0001.JPG')

# Add a drive writes a new disk's ID file and gives its entry; a request with another volume gets nothing.
New-TestDisk 'newdrive' 'ntfs' 'NEWDRIVE' 'T'
Test-Check 'a new disk is listed, and can be added' (Wait-Until { (Get-ConnectedDrive 'T').addProblem -eq 'None' } 15)
$newDrive = Get-ConnectedDrive 'T'
$idPath = 'T:\.pccompanion-drive'
$wrong = Invoke-DriveActionsPipe @{ kind = 'AddDrive'; letter = 'T'; volume = $card.volume }
Test-Check 'Add a drive with another disk''s volume is refused' (($wrong.status -eq 'NotConnected') -and -not [IO.File]::Exists($idPath)) ($wrong | ConvertTo-Json -Compress)
$added = Invoke-DriveActionsPipe @{ kind = 'AddDrive'; letter = 'T'; volume = $newDrive.volume; name = 'USB drive' }
$attributes = if ([IO.File]::Exists($idPath)) { [IO.File]::GetAttributes($idPath).ToString() } else { '' }
Test-Check 'Add a drive writes the ID file, hidden and system' (($added.status -eq 'Done') -and ($attributes -match 'Hidden') -and ($attributes -match 'System')) "$($added.status) $attributes"
Test-Check 'and gives the entry with the ID, the label and the serial number''s hash' (($added.entry.id -eq [IO.File]::ReadAllText($idPath)) -and ($added.entry.name -eq 'NEWDRIVE') -and ($added.entry.volumeSerial -eq (Get-SerialHash (Get-VolumeSerial 'T')))) ($added.entry | ConvertTo-Json -Compress)
Dismount-TestDisk 'newdrive' 'T'

# The history has the runs; the logs hold no drive names or folders.
Stop-All
$history = Get-Content $historyFile -Raw -ErrorAction SilentlyContinue | ConvertFrom-Json
Test-Check 'the history file has the runs' (@($history.runs).Count -ge 4) "$(@($history.runs).Count) runs"
Test-Check 'with the photos the card''s first run imported' (@($history.runs)[0].actions[0].copied -eq 2)
$hostLog = Get-HostLog
Test-Check 'the logs hold no drive names, labels or folders' (($hostLog -notmatch 'Backup drive') -and ($hostLog -notmatch 'EOS_DIGITAL') -and ($hostLog -notmatch [regex]::Escape($documents)) -and ($hostLog -notmatch [regex]::Escape($driveId)))

foreach ($disk in @(@('card', 'P'), @('backup', 'Q'), @('lookalike', 'R'), @('lockedcard', 'S'), @('newdrive', 'T'))) {
    if (Test-Path "$($disk[1]):\") { Dismount-TestDisk $disk[0] $disk[1] }
}
