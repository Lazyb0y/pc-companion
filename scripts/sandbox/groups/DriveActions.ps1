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
# file has the runs, and the logs hold no drive names or folders. The page shows the drives, their
# actions and status, and its changes save at once. Detailed logging is on for the Debug entries.

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

# The page lists the drives with their actions, notes and status. Use this drive takes a
# look-alike's serial number after asking; Add a drive adds a connected disk with the ID file it
# has; renaming, When connected, Eject when done, moving and removing an action, and removing a
# drive save at once, with focus near what changed. Without the host, the page says so.
function Get-DriveActionsSettings { (Get-Content (Join-Path $dataFolder 'settings.json') -Raw | ConvertFrom-Json).features.driveActions }
function Get-FocusedName { try { [System.Windows.Automation.AutomationElement]::FocusedElement.Current.Name } catch { '' } }

# An element of the settings app with the name and the control type, such as a menu item or a
# radio button, since the page has texts with the same names. The condition's name isn't
# $condition, which in Wait-Until's script block would be Wait-Until's own.
function Find-AppTyped([string] $Name, [System.Windows.Automation.ControlType] $Type) {
    $typed = New-Object System.Windows.Automation.AndCondition(
        (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, $Name)),
        (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, $Type)))
    $window = Get-AppWindow
    if (-not $window) { return $null }
    $window.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $typed)
}
function Invoke-AppMenuItem([string] $Name) {
    $script:item = $null
    if (-not (Wait-Until { ($script:item = Find-AppTyped $Name ([System.Windows.Automation.ControlType]::MenuItem)) -ne $null } 5)) { return $false }
    $script:item.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    return $true
}
function Invoke-DialogButton([string] $Dialog, [string] $Name) {
    $found = Find-AppDialog $Dialog
    if (-not $found) { return $false }
    $button = $found.FindFirst([System.Windows.Automation.TreeScope]::Descendants, (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, $Name)))
    if (-not $button) { return $false }
    $button.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    return $true
}
# A window on the desktop whose title starts with $Name.
function Find-TopWindow([string] $Name) {
    @([System.Windows.Automation.AutomationElement]::RootElement.FindAll([System.Windows.Automation.TreeScope]::Children, [System.Windows.Automation.Condition]::TrueCondition)) |
        Where-Object { $_.Current.Name -like "$Name*" } | Select-Object -First 1
}
# A button of the settings app by its name: a button's tooltip, shown while it has focus, has the same name.
function Invoke-AppButton([string] $Name) {
    $script:button = $null
    if (-not (Wait-Until { ($script:button = Find-AppTyped $Name ([System.Windows.Automation.ControlType]::Button)) -ne $null } 5)) { return $false }
    $script:button.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    return $true
}
# The page's scrolling area: the nearest one around its title, the tallest text with that name.
function Get-PageScroller([string] $Title) {
    $window = Get-AppWindow
    if (-not $window) { return $null }
    $titleText = @($window.FindAll([System.Windows.Automation.TreeScope]::Descendants, (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, $Title)))) |
        Sort-Object { $_.Current.BoundingRectangle.Height } -Descending | Select-Object -First 1
    $walker = [System.Windows.Automation.TreeWalker]::RawViewWalker
    $area = if ($titleText) { $walker.GetParent($titleText) } else { $null }
    while ($area -and -not [bool] $area.GetCurrentPropertyValue([System.Windows.Automation.AutomationElement]::IsScrollPatternAvailableProperty)) { $area = $walker.GetParent($area) }
    $area
}
function Get-ActionKinds([int] $Drive) { (@(@((Get-DriveActionsSettings).drives)[$Drive].actions) | ForEach-Object { $_.action }) -join ',' }

# As a hand edit could, Camera cards gets a second action, to move and remove.
$edited = Get-Content (Join-Path $dataFolder 'settings.json') -Raw | ConvertFrom-Json
$cardsEntry = @($edited.features.driveActions.drives)[0]
$cardsEntry.actions = @($cardsEntry.actions) + @([pscustomobject] @{ action = 'OpenFolder'; folder = 'DCIM' })
Write-Settings ($edited | ConvertTo-Json -Depth 8)
Mount-TestDisk 'newdrive' 'T'
Start-Process $hostExe
[void] (Wait-Until { (Get-HostLog) -match 'Drive Actions applied its settings: 2 drives and 3 actions can be used\.' } 15)
Start-Process $appExe
[void] (Wait-Until { Get-AppWindow })
Start-Sleep -Seconds 3
Test-Check 'select Drive Actions' (Show-Page 'Drive Actions')
Start-Sleep -Seconds 2
$text = Get-WindowText
Test-Check 'the page lists the drives and their actions' (
    ($text -contains 'Camera cards') -and ($text -contains 'Backup drive') -and ($text -contains "Imports from DCIM to $pictures\{year}\{month}") -and
    ($text -contains 'Opens DCIM on the drive') -and ($text -contains 'Permissions & data')) ($text -join ' | ')
# A drive's actions are made as they come into view, so the page is scrolled to the backup drive's first.
$scroller = Get-PageScroller 'Drive Actions'
if ($scroller) { $scroller.GetCurrentPattern([System.Windows.Automation.ScrollPattern]::Pattern).SetScrollPercent([System.Windows.Automation.ScrollPattern]::NoScroll, 100) }
Test-Check 'and the backup drive''s, once in view' (Wait-Until { Find-AppText 'Backs up Documents to Backup on the drive' } 5)
if ($scroller) { $scroller.GetCurrentPattern([System.Windows.Automation.ScrollPattern]::Pattern).SetScrollPercent([System.Windows.Automation.ScrollPattern]::NoScroll, 0) }
Test-Check 'with UAC off, the page doesn''t say Drive Actions is paused' ($text -notcontains 'While PC Companion runs as administrator after a UAC prompt, Drive Actions is paused: it never touches files with administrator rights.')
Test-Check 'it has the note about AutoPlay' ($text -contains 'Windows'' AutoPlay may also ask what to do when a drive is connected. To stop it asking, set removable drives and memory cards to Take no action.')
Test-Check 'and the backup drive the note about BitLocker' ($text -contains 'Anyone who has this drive can read its backups. Turn on BitLocker To Go to protect them.')
Test-Check 'the card shows as connected' (Wait-Until { Find-AppText 'Connected as P:' } 10)
Test-Check 'and the backup drive as not connected' ($null -ne (Find-AppText 'Not connected'))
Save-Screenshot '15i-drive-actions-page'

Mount-TestDisk 'lookalike' 'R'
Test-Check 'a look-alike shows on the backup drive' (Wait-Until { Find-AppText 'A drive with this drive''s ID but other serial numbers is connected as R:, so nothing is copied to or from it.' } 15)
Test-Check 'Use this drive asks first' ((Invoke-AppElement 'Use this drive') -and (Wait-Until { Find-AppDialog 'Use this drive for Backup drive?' } 5))
Start-Sleep -Seconds 1
$lookAlikeSerial = Get-SerialHash (Get-VolumeSerial 'R')
Test-Check 'and records its serial number' ((Invoke-DialogButton 'Use this drive for Backup drive?' 'Use this drive') -and (Wait-Until { @((Get-DriveActionsSettings).drives)[1].volumeSerial -eq $lookAlikeSerial } 10))
Test-Check 'which the host then recognizes as the backup drive' (Wait-Until { Find-AppText 'Connected as R:' } 15)
Dismount-TestDisk 'lookalike' 'R'

Test-Check 'Add a drive lists the connected disks' ((Invoke-AppElement 'Add a drive') -and (Wait-Until { Find-AppTyped 'NEWDRIVE (T:)' ([System.Windows.Automation.ControlType]::RadioButton) } 15))
$cardsChoice = Find-AppTyped 'Camera cards' ([System.Windows.Automation.ControlType]::RadioButton)
Test-Check 'with Camera cards first, which is in the list already' (($null -ne $cardsChoice) -and -not $cardsChoice.Current.IsEnabled -and ($null -ne (Find-AppText 'Camera cards is in the list already.')))
Save-Screenshot '15j-drive-actions-add'
Test-Check 'Add adds the chosen disk' ((Invoke-AppElement 'NEWDRIVE (T:)') -and (Invoke-DialogButton 'Add a drive' 'Add') -and (Wait-Until { @((Get-DriveActionsSettings).drives).Count -eq 3 } 10))
$added = @((Get-DriveActionsSettings).drives)[2]
Test-Check 'with the ID file the disk has, its label and its serial number''s hash' (($added.id -eq [IO.File]::ReadAllText('T:\.pccompanion-drive')) -and ($added.name -eq 'NEWDRIVE') -and ($added.volumeSerial -eq (Get-SerialHash (Get-VolumeSerial 'T')))) ($added | ConvertTo-Json -Compress -Depth 4)
Test-Check 'and the page shows it connected' (Wait-Until { Find-AppText 'Connected as T:' } 10)
Test-Check 'focus goes to the new drive' (Wait-Until { (Get-FocusedName) -eq 'More options for NEWDRIVE' } 3) (Get-FocusedName)

[void] (Invoke-AppButton 'More options for NEWDRIVE')
Start-Sleep -Seconds 1
Test-Check 'Rename opens its dialog' ((Invoke-AppMenuItem 'Rename') -and (Wait-Until { Find-AppDialog 'Rename NEWDRIVE' } 5))
Start-Sleep -Seconds 1
[void] (Set-AppBox 'Name' 'backup DRIVE')
Test-Check 'a name another drive has is refused' ((Wait-Until { Find-AppText 'Another drive in the list has this name.' } 3) -and -not (Test-AppEnabled 'Rename'))
[void] (Set-AppBox 'Name' 'Work drive')
Test-Check 'a new one is saved' ((Invoke-DialogButton 'Rename NEWDRIVE' 'Rename') -and (Wait-Until { @((Get-DriveActionsSettings).drives)[2].name -eq 'Work drive' } 5))
Test-Check 'and focus goes back to the drive' (Wait-Until { (Get-FocusedName) -eq 'More options for Work drive' } 3) (Get-FocusedName)
Test-Check 'When connected saves the choice' ((Select-ListItem 'When Work drive is connected' 'Do nothing') -and (Wait-Until { @((Get-DriveActionsSettings).drives)[2].whenConnected -eq 'Nothing' } 5))
Test-Check 'Eject when done saves the switch' ((Set-Toggle 'Eject Work drive when done' $true) -and (Wait-Until { @((Get-DriveActionsSettings).drives)[2].ejectWhenDone -eq $true } 5))

# The action editor says what's wrong below its boxes, Save is available only for an action that
# can be used, and it saves the action; Edit opens one again.
$myDocuments = [Environment]::GetFolderPath('MyDocuments')
Test-Check 'Add action opens the action editor' ((Invoke-AppButton 'Add an action to Work drive') -and (Wait-Until { Find-AppDialog 'Add an action' } 5))
Start-Sleep -Seconds 1
Test-Check 'a new backup has no folders, so it can''t be saved' ((Wait-Until { Find-AppText 'It has no folders to back up.' } 3) -and -not (Test-AppEnabled 'Save'))
Test-Check 'Add folder adds Documents' ((Invoke-AppElement 'Add folder') -and (Invoke-AppMenuItem 'Documents') -and (Wait-Until { Find-AppText $myDocuments } 3))
[void] (Set-AppBox 'Destination on the drive' '..\Backup')
Test-Check 'a destination outside the drive''s folders is refused' ((Wait-Until { Find-AppText 'Its folder on the drive isn''t valid. Enter it without the drive''s letter, such as DCIM.' } 3) -and -not (Test-AppEnabled 'Save'))
[void] (Set-AppBox 'Destination on the drive' 'Laptop')
Save-Screenshot '15k-drive-actions-editor'
$newAction = { @(@((Get-DriveActionsSettings).drives)[2].actions)[0] }
Test-Check 'Save adds the backup' ((Test-AppEnabled 'Save') -and (Invoke-DialogButton 'Add an action' 'Save') -and (Wait-Until { $a = & $newAction; ($a.action -eq 'BackUp') -and ($a.destination -eq 'Laptop') -and (@($a.folders)[0] -eq $myDocuments) } 5)) ((& $newAction) | ConvertTo-Json -Compress -Depth 4)
Test-Check 'focus goes to the new action' (Wait-Until { (Get-FocusedName) -eq 'Edit action 1 of Work drive' } 3) (Get-FocusedName)

Test-Check 'Edit opens an action' ((Invoke-AppButton 'Edit action 1 of Camera cards') -and (Wait-Until { Find-AppDialog 'Edit the action' } 5) -and ((Get-AppBox 'From') -eq 'DCIM')) (Get-AppBox 'From')
Start-Sleep -Seconds 1
[void] (Set-AppBox 'Types' 'p/df')
Test-Check 'an extension a file name can''t have is refused' ((Wait-Until { Find-AppText 'An extension is empty, has a dot inside it, or has characters a file name can''t have.' } 3) -and -not (Test-AppEnabled 'Save'))
[void] (Set-AppBox 'Types' 'jpg')
[void] (Set-AppBox 'To' $env:windir)
Test-Check 'and the Windows folder' ((Wait-Until { Find-AppText 'It''s in the Windows folder, which only administrators can change.' } 3) -and -not (Test-AppEnabled 'Save'))
[void] (Set-AppBox 'To' "$pictures\{year}")
$cardsImport = { @(@((Get-DriveActionsSettings).drives)[0].actions)[0] }
Test-Check 'Save saves the import' ((Invoke-DialogButton 'Edit the action' 'Save') -and (Wait-Until { $a = & $cardsImport; ((@($a.extensions) -join ',') -eq 'jpg') -and ($a.destination -eq "$pictures\{year}") } 5)) ((& $cardsImport) | ConvertTo-Json -Compress -Depth 4)
Test-Check 'and focus goes back to it' (Wait-Until { (Get-FocusedName) -eq 'Edit action 1 of Camera cards' } 3) (Get-FocusedName)

[void] (Invoke-AppButton 'More options for action 2 of Camera cards')
Start-Sleep -Seconds 1
Test-Check 'Move up moves the action' ((Invoke-AppMenuItem 'Move up') -and (Wait-Until { (Get-ActionKinds 0) -eq 'OpenFolder,Import' } 5)) (Get-ActionKinds 0)
Test-Check 'focus stays on the action that moved' (Wait-Until { (Get-FocusedName) -eq 'More options for action 1 of Camera cards' } 3) (Get-FocusedName)
[void] (Invoke-AppButton 'More options for action 1 of Camera cards')
Start-Sleep -Seconds 1
Test-Check 'removing an action asks first' ((Invoke-AppMenuItem 'Remove') -and (Wait-Until { Find-AppDialog 'Remove this action?' } 5))
Start-Sleep -Seconds 1
Test-Check 'and removes it' ((Invoke-DialogButton 'Remove this action?' 'Remove') -and (Wait-Until { (Get-ActionKinds 0) -eq 'Import' } 5)) (Get-ActionKinds 0)

[void] (Invoke-AppButton 'More options for Work drive')
Start-Sleep -Seconds 1
Test-Check 'removing a drive asks first' ((Invoke-AppMenuItem 'Remove') -and (Wait-Until { Find-AppDialog 'Remove Work drive?' } 5))
Start-Sleep -Seconds 1
Test-Check 'and removes it' ((Invoke-DialogButton 'Remove Work drive?' 'Remove') -and (Wait-Until { @((Get-DriveActionsSettings).drives).Count -eq 2 } 5))
Test-Check 'leaving its ID file on the disk' ([IO.File]::Exists('T:\.pccompanion-drive'))

# Run now lists what the card's import would copy, those imported before unchecked, and runs it with
# the files left checked; the history shows the runs, Undo of a run asks first and sends its copies
# to the Recycle Bin, Open Recycle Bin opens it, and Clear history asks first.
function Find-AppByAutomationId([string] $Id) {
    $window = Get-AppWindow
    if (-not $window) { return $null }
    $window.FindFirst([System.Windows.Automation.TreeScope]::Descendants, (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::AutomationIdProperty, $Id)))
}
function Get-UndoRunCount {
    $window = Get-AppWindow
    if (-not $window) { return 0 }
    @($window.FindAll([System.Windows.Automation.TreeScope]::Descendants, (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::AutomationIdProperty, 'UndoRun')))).Count
}
$importedTo = Join-Path $pictures '2026'
$undoable = Get-UndoRunCount
Test-Check 'Run now lists the card''s photos' ((Invoke-AppButton 'Run now for Camera cards') -and (Wait-Until { Find-AppTyped '100TEST\IMG_0001.JPG' ([System.Windows.Automation.ControlType]::CheckBox) } 15))
$photoBox = Find-AppTyped '100TEST\IMG_0001.JPG' ([System.Windows.Automation.ControlType]::CheckBox)
$photoToggle = $photoBox.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern)
Test-Check 'a photo imported before is unchecked' ($photoToggle.Current.ToggleState -eq [System.Windows.Automation.ToggleState]::Off)
$photoToggle.Toggle()
Save-Screenshot '15l-drive-actions-run-now'
Test-Check 'Run imports the photo checked again' ((Invoke-DialogButton 'Run now: Camera cards' 'Run') -and (Wait-Until { Test-Path "$importedTo\IMG_0001.JPG" } 20))
Test-Check 'the history shows the run, with Undo' ((Invoke-AppElement 'Refresh') -and (Wait-Until { (Get-UndoRunCount) -gt $undoable } 15)) "$(Get-UndoRunCount) runs with Undo, $undoable before"
Save-Screenshot '15m-drive-actions-history'
$undoRun = Find-AppByAutomationId 'UndoRun'
if ($undoRun) { $undoRun.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke() }
Test-Check 'Undo of a run asks first' (($null -ne $undoRun) -and (Wait-Until { Find-AppDialog 'Undo the import?' } 5))
Start-Sleep -Seconds 1
Test-Check 'and sends the copy to the Recycle Bin' ((Invoke-DialogButton 'Undo the import?' 'Undo') -and (Wait-Until { -not (Test-Path "$importedTo\IMG_0001.JPG") } 15))
Test-Check 'and focus goes to Refresh' (Wait-Until { (Get-FocusedName) -eq 'Refresh' } 3) (Get-FocusedName)
Test-Check 'Open Recycle Bin opens it' ((Invoke-AppElement 'Open Recycle Bin') -and (Wait-Until { Find-TopWindow 'Recycle Bin' } 15))
$recycleBin = Find-TopWindow 'Recycle Bin'
if ($recycleBin) { $recycleBin.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).Close() }
Test-Check 'Clear history asks first' ((Invoke-AppElement 'Clear history') -and (Wait-Until { Find-AppDialog 'Clear the history?' } 5))
Start-Sleep -Seconds 1
Test-Check 'and empties it' ((Invoke-DialogButton 'Clear the history?' 'Clear history') -and (Wait-Until { Find-AppText 'No drive''s actions have run yet.' } 10))

Test-Check 'Open AutoPlay settings opens Settings' ((Invoke-AppElement 'Open AutoPlay settings') -and (Wait-Until { Find-TopWindow 'Settings' } 20))
$settingsWindow = Find-TopWindow 'Settings'
if ($settingsWindow) { $settingsWindow.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).Close() }

Get-Process -Name 'PCCompanion.Host' -ErrorAction SilentlyContinue | Stop-Process -Force
Test-Check 'without the host, the page says it isn''t answering' (Wait-Until { Find-AppText 'PC Companion isn''t answering, so the page can''t show which drives are connected or add one. Make sure PC Companion is running.' } 30)
Test-Check 'and shows no drive as connected' ($null -eq (Find-AppText 'Connected as P:'))
Stop-All

foreach ($disk in @(@('card', 'P'), @('backup', 'Q'), @('lookalike', 'R'), @('lockedcard', 'S'), @('newdrive', 'T'))) {
    if (Test-Path "$($disk[1]):\") { Dismount-TestDisk $disk[0] $disk[1] }
}
