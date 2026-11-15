# Needs: OpenSSH
# Remote Folders, against a test SFTP server in this Sandbox only: OpenSSH for Windows, from the
# release sandbox-test.ps1 downloads, with networking off. Remote Folders is on a virtual NTFS disk,
# since Windows Sandbox can't register such folders on its own drive. The page adds a server
# through the dialog: Connect reads the server's key, I checked this key signs in, and Save saves
# the server and its password in Credential Manager. Its folder then works as File Explorer uses it:
# files are online only with a Mark of the Web until they're read, changes on either side reach the
# other, a file changed on both sides becomes a conflict copy, and renames, moves and deletes reach
# the server, also those made while the server or the provider was stopped. A changed password
# shows Sign in again, and a changed key Trust the new key. Edit renames the server; Remove asks
# first, deletes the credential and offers the folder. A server with an SSH key file is added too.
# The group runs near the end, since it uninstalls PC Companion: just for the user, and then for
# all users, which also removes a signed-out user's folders.

# An element of the settings app with the name and the control type, since the page and its
# dialogs have texts with the same names as buttons.
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
function Invoke-AppButton([string] $Name) {
    $script:button = $null
    if (-not (Wait-Until { ($script:button = Find-AppTyped $Name ([System.Windows.Automation.ControlType]::Button)) -ne $null } 5)) { return $false }
    $script:button.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    return $true
}
function Invoke-DialogButton([string] $Dialog, [string] $Name) {
    $found = Find-AppDialog $Dialog
    if (-not $found) { return $false }
    $button = $found.FindFirst([System.Windows.Automation.TreeScope]::Descendants, (New-Object System.Windows.Automation.AndCondition(
        (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, $Name)),
        (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Button)))))
    if (-not $button) { return $false }
    $button.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    return $true
}
function Test-ButtonEnabled([string] $Name) { $found = Find-AppTyped $Name ([System.Windows.Automation.ControlType]::Button); ($null -ne $found) -and $found.Current.IsEnabled }
function Set-Password([string] $Value) {
    $window = Get-AppWindow
    if (-not $window) { return $false }
    $box = @($window.FindAll([System.Windows.Automation.TreeScope]::Descendants, (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::IsPasswordProperty, $true)))) | Select-Object -First 1
    if (-not $box) { return $false }
    $box.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue($Value)
    Start-Sleep -Milliseconds 300
    return $true
}
function Set-KeyChecked {
    $box = Find-AppTyped 'I checked this key' ([System.Windows.Automation.ControlType]::CheckBox)
    if (-not $box) { return $false }
    $box.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern).Toggle()
    return $true
}
function Select-SignIn([string] $Choice) {
    $combo = Find-AppTyped 'Sign in with' ([System.Windows.Automation.ControlType]::ComboBox)
    if (-not $combo) { return $false }
    $combo.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern).Expand()
    Start-Sleep -Milliseconds 500
    $choiceItem = $combo.FindFirst([System.Windows.Automation.TreeScope]::Descendants, (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, $Choice)))
    if (-not $choiceItem) { return $false }
    $choiceItem.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    Start-Sleep -Milliseconds 300
    $combo.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern).Collapse()
    return $true
}
# The page's texts, without the marks that keep addresses and keys left to right.
function Get-PageText { @(Get-WindowText | ForEach-Object { $_ -replace "[\u2066\u2069]", '' }) }
function Get-Servers { @((Get-Content (Join-Path $dataFolder 'settings.json') -Raw | ConvertFrom-Json).features.remoteFolders.servers) }
function Test-Credential([string] $Target) { [bool] ((cmdkey /list) -match [regex]::Escape($Target)) }
function Test-RegistryKey([string] $Key) { & reg.exe query $Key 2>$null | Out-Null; $LASTEXITCODE -eq 0 }

# The folders Windows keeps for Remote Folders, as SyncRootManager lists them for every user.
$syncRootManager = 'HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\SyncRootManager'
function Get-SyncRoots { @(Get-ChildItem "Registry::$syncRootManager" -ErrorAction SilentlyContinue | Where-Object { $_.PSChildName -like 'PCCompanion!*' } | ForEach-Object { $_.PSChildName }) }

# Asks the provider through its pipe, as the settings app and the host do.
function Invoke-Provider([string] $Json) {
    $client = New-Object System.IO.Pipes.NamedPipeClientStream('.', "PCCompanion.RemoteFolders.$((Get-Process -Id $PID).SessionId)", [System.IO.Pipes.PipeDirection]::InOut)
    try {
        $client.Connect(3000)
        $client.ReadMode = [System.IO.Pipes.PipeTransmissionMode]::Message
        $bytes = [Text.Encoding]::UTF8.GetBytes($Json)
        $client.Write($bytes, 0, $bytes.Length)
        $message = New-Object IO.MemoryStream
        $buffer = New-Object byte[] 65536
        do { $read = $client.Read($buffer, 0, $buffer.Length); $message.Write($buffer, 0, $read) } while ($read -gt 0 -and -not $client.IsMessageComplete)
        [Text.Encoding]::UTF8.GetString($message.ToArray()) | ConvertFrom-Json
    } catch { $null } finally { $client.Dispose() }
}
function Get-ServerState([string] $Id) { (@((Invoke-Provider '{"kind":"Status"}').servers) | Where-Object { $_.id -eq $Id }).state }
# Waits until the server is connected, asking the provider to sync every 5 seconds.
function Wait-Connected([string] $Id) {
    for ($try = 0; $try -lt 6; $try++) {
        [void] (Invoke-Provider '{"kind":"SyncNow"}')
        if (Wait-Until { (Get-ServerState $Id) -eq 'Connected' } 5) { return $true }
    }
    return $false
}
function Start-Host {
    Start-Process $hostExe
    return (Wait-Until { $null -ne (Invoke-Provider '{"kind":"Status"}') } 30)
}
function Stop-Provider {
    Stop-All
    Get-Processes 'PCCompanion.RemoteFolders' | ForEach-Object { Stop-Process -Id $_.Id -Force -ErrorAction SilentlyContinue }
    [void] (Wait-Until { (Get-Processes 'PCCompanion.RemoteFolders').Count -eq 0 } 10)
}
function Open-Page {
    Start-Process $appExe
    [void] (Wait-Until { Get-AppWindow })
    Start-Sleep -Seconds 3
    return (Show-Page 'Remote Folders')
}

# Files on the server and in its folder on this PC. An online-only file has the attribute
# FILE_ATTRIBUTE_RECALL_ON_DATA_ACCESS (0x400000); every file of the folder is a placeholder, a
# reparse point (0x400).
$serverData = 'C:\sftpdata'
function Get-ServerText([string] $Relative) { $path = Join-Path $serverData $Relative; if (Test-Path -LiteralPath $path) { [IO.File]::ReadAllText($path) } else { $null } }
function Get-Attributes([string] $Path) { (Get-Item -LiteralPath $Path -Force -ErrorAction SilentlyContinue).Attributes.value__ }
function Test-OnlineOnly([string] $Path) { ((Get-Attributes $Path) -band 0x400000) -ne 0 }
function Test-Downloaded([string] $Path) { (Test-Path -LiteralPath $Path) -and -not (Test-OnlineOnly $Path) }
function Get-Zone([string] $Path) { (Get-Content -LiteralPath $Path -Stream Zone.Identifier -ErrorAction SilentlyContinue) -join '|' }
function Get-Names([string] $Path) { (Get-ChildItem -LiteralPath $Path -Force -ErrorAction SilentlyContinue | ForEach-Object { $_.Name } | Sort-Object) -join ', ' }
function Test-Names([string] $Path, [string[]] $Expected) { (Get-Names $Path) -eq (($Expected | Sort-Object) -join ', ') }
# Reads a file and returns the message of the error it fails with, or nothing.
function Get-ReadError([string] $Path) {
    try { [void] [IO.File]::ReadAllText($Path); return '' }
    catch { $inner = $_.Exception; while ($inner.InnerException) { $inner = $inner.InnerException }; return $inner.Message.Trim() }
}

# The test server: OpenSSH for Windows, with a user that signs in with a password or a key, and a
# folder of files. The checks sign in wrongly on purpose, so the server doesn't penalize this PC;
# a global option goes before the Match blocks at the end of sshd_config.
$work = 'C:\RemoteFoldersTest'
New-Item -ItemType Directory $work -Force | Out-Null
$zip = Get-ChildItem 'C:\Test\openssh' -Filter 'OpenSSH-*.zip' -ErrorAction SilentlyContinue | Select-Object -First 1
Expand-Archive $zip.FullName 'C:\OpenSSH' -Force
$openSsh = (Get-ChildItem 'C:\OpenSSH' -Filter 'install-sshd.ps1' -Recurse | Select-Object -First 1).DirectoryName
& powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $openSsh 'install-sshd.ps1') | Out-Null
$keygen = Join-Path $openSsh 'ssh-keygen.exe'
$password = 'Rf1!' + [guid]::NewGuid().ToString('N').Substring(0, 10)
net user sftpuser $password /add | Out-Null
New-Item -ItemType Directory $serverData, "$serverData\docs", "$serverData\docs\sub" -Force | Out-Null
[IO.File]::WriteAllText("$serverData\a.txt", "Hello from the server`r`n")
[IO.File]::WriteAllText("$serverData\never.txt", "never read`r`n")
[IO.File]::WriteAllText("$serverData\gone.txt", "deleted on the server later`r`n")
[IO.File]::WriteAllText("$serverData\.profile", "a dot file`r`n")
[IO.File]::WriteAllText("$serverData\docs\readme.txt", "read me`r`n")
[IO.File]::WriteAllText("$serverData\docs\sub\deep.txt", "deep`r`n")
$big = New-Object byte[] (3MB + 77)
(New-Object Random 47).NextBytes($big)
[IO.File]::WriteAllBytes("$serverData\big.bin", $big)
icacls $serverData /grant 'sftpuser:(OI)(CI)M' /T | Out-Null
& $keygen -t ed25519 -f "$work\id_test" -N '""' -q
Start-Service sshd
$authorized = 'C:\ProgramData\ssh\sftpuser_authorized_keys'
Get-Content "$work\id_test.pub" | Set-Content $authorized -Encoding ascii
icacls $authorized /inheritance:r /grant 'SYSTEM:F' /grant 'Administrators:F' /grant 'sftpuser:R' | Out-Null
$sshdConfig = 'C:\ProgramData\ssh\sshd_config'
Set-Content $sshdConfig ("PerSourcePenaltyExemptList 127.0.0.1,::1`r`n" + (Get-Content $sshdConfig -Raw)) -Encoding ascii
Add-Content $sshdConfig "`r`nMatch User sftpuser`r`n    AuthorizedKeysFile __PROGRAMDATA__/ssh/sftpuser_authorized_keys`r`n"
Restart-Service sshd
function Get-HostKeyFingerprint([string] $Type = 'ed25519') {
    $line = & $keygen -lf "C:\ProgramData\ssh\ssh_host_$($Type)_key.pub" -E sha256
    if ($line -match 'SHA256:(\S+)') { $Matches[1] } else { '' }
}
$fingerprint = Get-HostKeyFingerprint
Test-Check 'the test SFTP server runs' (((Get-Service sshd).Status -eq 'Running') -and $fingerprint.Length -gt 0) "OpenSSH from $($zip.Name)"

# Windows Sandbox can't register these folders on its own C: drive, so Remote Folders is a virtual disk.
$remoteFolders = Join-Path $env:USERPROFILE 'Remote Folders'
New-Item -ItemType Directory $remoteFolders -Force | Out-Null
Set-Content "$work\disk.txt" "create vdisk file=$work\remote-folders.vhdx maximum=1024 type=expandable`r`nselect vdisk file=$work\remote-folders.vhdx`r`nattach vdisk`r`ncreate partition primary`r`nformat fs=ntfs quick label=RemoteFolders`r`nassign mount=`"$remoteFolders`"`r`n" -Encoding ascii
$diskpart = diskpart /s "$work\disk.txt" | Out-String
Test-Check 'Remote Folders is on an NTFS virtual disk' ($diskpart -match 'successfully assigned') ($diskpart -replace '\s+', ' ')

$detailedUntil = [DateTimeOffset]::Now.AddHours(1).ToString('o')
Write-Settings (@{ schemaVersion = 1; general = @{ theme = 'Light' }; updates = @{ checkAutomatically = $false }; diagnostics = @{ detailedLoggingUntil = $detailedUntil }; features = @{ remoteFolders = @{ enabled = $true; servers = @() } } } | ConvertTo-Json -Depth 6)
Test-Check 'the host starts the provider' (Start-Host)
Test-Check 'select Remote Folders' (Open-Page)
Start-Sleep -Seconds 2
$text = Get-PageText
Test-Check 'the page says there are no servers' (($text -contains 'No servers yet.') -and ($text -contains 'Permissions & data') -and ($text -contains 'Where the folders are')) ($text -join ' | ')
Test-Check 'and where the folders are' ($text -contains $remoteFolders)
Save-Screenshot 'rf-01-page-empty'

# Adding a server: Connect reads the key, I checked this key signs in, and Save saves it.
Test-Check 'Add server opens the dialog' ((Invoke-AppButton 'Add server') -and (Wait-Until { Find-AppDialog 'Add a server' } 5))
Start-Sleep -Seconds 1
Test-Check 'Save is off at first' (-not (Test-ButtonEnabled 'Save'))
Test-Check 'Connect is off without an address' (-not (Test-ButtonEnabled 'Connect'))
[void] (Set-AppBox 'Address' 'nas example')
Test-Check 'a bad address is refused' (Wait-Until { Find-AppText 'Enter a host name or an IP address, such as nas.example.lan or 192.168.1.10.' } 3)
[void] (Set-AppBox 'Address' '127.0.0.1')
[void] (Set-AppBox 'User name' 'sftpuser')
[void] (Set-AppBox 'Name' 'Home server')
[void] (Set-Password $password)
[void] (Set-AppBox 'Folder on the server' '/C:/sftpdata')
Test-Check 'Connect is on with the details' (Wait-Until { Test-ButtonEnabled 'Connect' } 3)
Test-Check 'Connect shows the server''s key' ((Invoke-AppButton 'Connect') -and (Wait-Until { Find-AppText "ED25519 SHA256:$fingerprint" } 30)) ((Get-PageText) -join ' | ')
Test-Check 'and asks to check it' ($null -ne (Find-AppText 'Check the server''s key below. Once you select I checked this key, PC Companion signs in.'))
Test-Check 'Save is still off' (-not (Test-ButtonEnabled 'Save'))
Save-Screenshot 'rf-02-dialog-key'
Test-Check 'I checked this key signs in, and Save comes on' ((Set-KeyChecked) -and (Wait-Until { Test-ButtonEnabled 'Save' } 30)) ((Get-PageText) -join ' | ')
Save-Screenshot 'rf-03-dialog-ready'
Test-Check 'Save saves the server' ((Invoke-AppButton 'Save') -and (Wait-Until { @(Get-Servers).Count -eq 1 } 5))
$server = @(Get-Servers)[0]
$homeServer = Join-Path $remoteFolders 'Home server'
$credential = "PCCompanion/RemoteFolders/$($server.id)"
$mySid = [Security.Principal.WindowsIdentity]::GetCurrent().User.Value
Test-Check 'with its details' (($server.name -eq 'Home server') -and ($server.host -eq '127.0.0.1') -and ($server.port -eq 22) -and ($server.userName -eq 'sftpuser') -and ($server.signIn -eq 'Password') -and ($server.remoteFolder -eq '/C:/sftpdata') -and ($server.folder -eq $homeServer) -and ($server.hostKey.type -eq 'ssh-ed25519') -and ($server.hostKey.sha256 -eq $fingerprint) -and ($server.id -match '^[0-9a-f]{32}$')) ($server | ConvertTo-Json -Compress)
Test-Check 'and its password in Credential Manager' (Test-Credential $credential)
Test-Check 'and not in settings.json' (-not ((Get-Content (Join-Path $dataFolder 'settings.json') -Raw) -match [regex]::Escape($password)))
Test-Check 'the folder is registered' (Wait-Until { (Get-SyncRoots) -contains "PCCompanion!$mySid!$($server.id)" } 15) ((Get-SyncRoots) -join ', ')
Test-Check 'and lists the server' (Wait-Until { Test-Path (Join-Path $homeServer 'a.txt') } 15) (Get-Names $homeServer)
Test-Check 'the page shows it connected' (Wait-Until { (Get-PageText) -contains 'Home server, Connected' } 15) ((Get-PageText) -join ' | ')
Test-Check 'with the folder and the address' (((Get-PageText) -join '|') -match [regex]::Escape('/C:/sftpdata on sftpuser@127.0.0.1:22'))
Save-Screenshot 'rf-04-page-connected'
Test-Check 'a second server can''t have the same name' ((Invoke-AppButton 'Add server') -and (Wait-Until { Find-AppDialog 'Add a server' } 5) -and (Set-AppBox 'Name' 'home server') -and (Wait-Until { Find-AppText 'Another server has this name.' } 3))
[void] (Invoke-AppButton 'Cancel')
Start-Sleep -Seconds 1

# The folder lists the server's files online only, with a Mark of the Web, and hides dot files.
# Reading a file downloads it, and it keeps the mark.
Test-Check 'the folder lists the server''s files' (Test-Names $homeServer '.profile', 'a.txt', 'big.bin', 'docs', 'gone.txt', 'never.txt') (Get-Names $homeServer)
Test-Check 'a dot file is hidden' (((Get-Attributes (Join-Path $homeServer '.profile')) -band 2) -ne 0)
$file = Join-Path $homeServer 'a.txt'
Test-Check 'a file is online only' (Test-OnlineOnly $file) ('attributes 0x{0:X}' -f (Get-Attributes $file))
Test-Check 'with a Mark of the Web, without the server''s address' (((Get-Zone $file) -match 'ZoneId=3') -and -not ((Get-Zone $file) -match 'HostUrl')) (Get-Zone $file)
Test-Check 'reading it downloads it' (([IO.File]::ReadAllText($file) -eq "Hello from the server`r`n") -and (Test-Downloaded $file))
Test-Check 'and it keeps the mark' ((Get-Zone $file) -match 'ZoneId=3')
Test-Check 'a large file downloads whole' ((Get-FileHash (Join-Path $homeServer 'big.bin')).Hash -eq (Get-FileHash "$serverData\big.bin").Hash)
Test-Check 'a folder lists its files' (Test-Names (Join-Path $homeServer 'docs') 'readme.txt', 'sub') (Get-Names (Join-Path $homeServer 'docs'))
Test-Check 'a file two folders down reads' ([IO.File]::ReadAllText((Join-Path $homeServer 'docs\sub\deep.txt')) -eq "deep`r`n")

# Changes on the server show when the folder is opened again, 30 seconds after it was listed.
[IO.File]::AppendAllText("$serverData\a.txt", "changed on the server`r`n")
Remove-Item "$serverData\gone.txt"
[IO.File]::WriteAllText("$serverData\new-on-server.txt", "new`r`n")
Start-Sleep -Seconds 31
[void] (Get-ChildItem $homeServer -Force)
Start-Sleep -Seconds 3
$names = Get-Names $homeServer
Test-Check 'a new file on the server appears' ($names -match 'new-on-server\.txt') $names
Test-Check 'a file deleted on the server leaves' ($names -notmatch 'gone\.txt') $names
Test-Check 'a downloaded file that changed on the server is downloaded again' ([IO.File]::ReadAllText($file) -match 'changed on the server')

# Changes on this PC reach the server once a file has been quiet for 2 seconds.
[IO.File]::WriteAllText((Join-Path $homeServer 'new-on-pc.txt'), "made on the PC`r`n")
Test-Check 'a new file is uploaded' (Wait-Until { (Get-ServerText 'new-on-pc.txt') -eq "made on the PC`r`n" } 15)
Test-Check 'and becomes a placeholder in sync' (Wait-Until { (((Get-Attributes (Join-Path $homeServer 'new-on-pc.txt')) -band 0x400) -ne 0) -and (Test-Downloaded (Join-Path $homeServer 'new-on-pc.txt')) } 5) ('attributes 0x{0:X}' -f (Get-Attributes (Join-Path $homeServer 'new-on-pc.txt')))
[IO.File]::AppendAllText($file, "edited on the PC`r`n")
Test-Check 'an edited file is uploaded' (Wait-Until { (Get-ServerText 'a.txt') -match 'edited on the PC' } 15)
New-Item -ItemType Directory (Join-Path $homeServer 'made-on-pc') | Out-Null
[IO.File]::WriteAllText((Join-Path $homeServer 'made-on-pc\inside.txt'), "inside`r`n")
Test-Check 'a new folder and its file are uploaded' (Wait-Until { (Get-ServerText 'made-on-pc\inside.txt') -eq "inside`r`n" } 15)
[IO.File]::WriteAllText((Join-Path $homeServer 'Thumbs.db'), 'thumbnails')
$open = [IO.File]::Open((Join-Path $homeServer 'kept-open.txt'), 'Create', 'ReadWrite', 'Read')
$bytes = [Text.Encoding]::ASCII.GetBytes("written while open`r`n")
$open.Write($bytes, 0, $bytes.Length)
$open.Flush()
Start-Sleep -Seconds 6
Test-Check 'Thumbs.db stays on this PC' (-not (Test-Path "$serverData\Thumbs.db"))
Test-Check 'a file a program keeps open waits' (-not (Test-Path "$serverData\kept-open.txt"))
$open.Close()
Test-Check 'and is uploaded once the program closes it' (Wait-Until { (Get-ServerText 'kept-open.txt') -eq "written while open`r`n" } 15)

# A file changed on both sides: the server's version keeps the name, and this PC's becomes a
# conflict copy on both sides.
$readme = Join-Path $homeServer 'docs\readme.txt'
[void] [IO.File]::ReadAllText($readme)
[IO.File]::WriteAllText("$serverData\docs\readme.txt", "changed on the server`r`n")
[IO.File]::AppendAllText($readme, "edited on the PC`r`n")
Start-Sleep -Seconds 6
Test-Check 'a file changed on both sides isn''t overwritten on the server' ((Get-ServerText 'docs\readme.txt') -eq "changed on the server`r`n") (Get-ServerText 'docs\readme.txt')
$copies = @(Get-ChildItem "$serverData\docs" -Filter 'readme (*).txt')
Test-Check 'this PC''s version is a conflict copy on the server' (($copies.Count -eq 1) -and ([IO.File]::ReadAllText($copies[0].FullName) -match 'edited on the PC')) (Get-Names "$serverData\docs")
$copies = @(Get-ChildItem (Join-Path $homeServer 'docs') -Filter 'readme (*).txt')
Test-Check 'and on this PC' (($copies.Count -eq 1) -and ([IO.File]::ReadAllText($copies[0].FullName) -match 'edited on the PC')) (Get-Names (Join-Path $homeServer 'docs'))
Test-Check 'the server''s version is online only, with a Mark of the Web' ((Test-OnlineOnly $readme) -and ((Get-Zone $readme) -match 'ZoneId=3') -and ([IO.File]::ReadAllText($readme) -eq "changed on the server`r`n"))
[IO.File]::WriteAllText("$serverData\both-new.txt", "made on the server`r`n")
[IO.File]::WriteAllText((Join-Path $homeServer 'both-new.txt'), "made on the PC`r`n")
Test-Check 'a new file whose name was taken on the server becomes a conflict copy' (Wait-Until { @(Get-ChildItem $serverData -Filter 'both-new (*).txt').Count -eq 1 } 15) (Get-Names $serverData)
Test-Check 'and the server''s file stays' ((Get-ServerText 'both-new.txt') -eq "made on the server`r`n")

# Renames, moves and deletes reach the server at once.
Rename-Item (Join-Path $homeServer 'made-on-pc') 'renamed-folder'
Test-Check 'a renamed folder is renamed on the server' (Wait-Until { (Test-Path "$serverData\renamed-folder\inside.txt") -and -not (Test-Path "$serverData\made-on-pc") } 15)
Rename-Item (Join-Path $homeServer 'kept-open.txt') 'renamed.txt'
Test-Check 'a renamed file is renamed on the server' (Wait-Until { ((Get-ServerText 'renamed.txt') -eq "written while open`r`n") -and -not (Test-Path "$serverData\kept-open.txt") } 15)
Move-Item (Join-Path $homeServer 'renamed.txt') (Join-Path $homeServer 'docs\renamed.txt')
Test-Check 'a moved file is moved on the server' (Wait-Until { ((Get-ServerText 'docs\renamed.txt') -eq "written while open`r`n") -and -not (Test-Path "$serverData\renamed.txt") } 15)
Test-Check 'and stays a placeholder' (Wait-Until { ((Get-Attributes (Join-Path $homeServer 'docs\renamed.txt')) -band 0x400) -ne 0 } 5)
Test-Check 'a file in a renamed folder still reads' ((Get-ReadError (Join-Path $homeServer 'renamed-folder\inside.txt')) -eq '')
[IO.File]::WriteAllText((Join-Path $homeServer 'move-out.txt'), "moved out`r`n")
[void] (Wait-Until { (Get-ServerText 'move-out.txt') -eq "moved out`r`n" } 15)
Move-Item (Join-Path $homeServer 'move-out.txt') (Join-Path $remoteFolders 'moved-out.txt')
Test-Check 'a file moved out of the folder is deleted on the server' (Wait-Until { -not (Test-Path "$serverData\move-out.txt") } 15)
Test-Check 'and is an ordinary file where it went' ([IO.File]::ReadAllText((Join-Path $remoteFolders 'moved-out.txt')) -eq "moved out`r`n")
Remove-Item (Join-Path $homeServer 'big.bin')
Test-Check 'a deleted file is deleted on the server' (Wait-Until { -not (Test-Path "$serverData\big.bin") } 15)
Remove-Item (Join-Path $homeServer '.profile') -Force
Test-Check 'a deleted online-only file is deleted on the server' (Wait-Until { -not (Test-Path "$serverData\.profile") } 15)
Add-Type -AssemblyName Microsoft.VisualBasic
[Microsoft.VisualBasic.FileIO.FileSystem]::DeleteFile($copies[0].FullName, 'OnlyErrorDialogs', 'SendToRecycleBin')
Test-Check 'a file moved to the Recycle Bin is deleted on the server' (Wait-Until { @(Get-ChildItem "$serverData\docs" -Filter 'readme (*).txt').Count -eq 0 } 15)
[void] [IO.File]::ReadAllText((Join-Path $homeServer 'docs\sub\deep.txt'))
[IO.File]::WriteAllText("$serverData\docs\sub\deep.txt", "changed on the server`r`n")
Remove-Item (Join-Path $homeServer 'docs\sub\deep.txt')
Start-Sleep -Seconds 3
Test-Check 'a deleted file that changed on the server stays there' ((Get-ServerText 'docs\sub\deep.txt') -eq "changed on the server`r`n")

# Always keep on this device and Free up space, as File Explorer's menu sets them.
attrib +P (Join-Path $homeServer 'docs')
attrib +P (Join-Path $homeServer 'docs\*') /s /d
Test-Check 'a kept folder''s files are downloaded, also one that comes back from the server' (Wait-Until { (Test-Downloaded $readme) -and (Test-Downloaded (Join-Path $homeServer 'docs\sub\deep.txt')) } 30) (Get-Names (Join-Path $homeServer 'docs\sub'))
$space = Join-Path $homeServer 'space.txt'
[IO.File]::WriteAllText($space, "made to free up space`r`n")
[void] (Wait-Until { ((Get-ServerText 'space.txt') -eq "made to free up space`r`n") -and (((Get-Attributes $space) -band 0x400) -ne 0) } 15)
attrib +U $space
Test-Check 'Free up space removes a file''s copy on this PC' (Wait-Until { Test-OnlineOnly $space } 20) ('attributes 0x{0:X}' -f (Get-Attributes $space))
$left = @(Get-ChildItem $serverData -Recurse -Force | Where-Object { $_.Name -like '.pcc-*' })
Test-Check 'no temporary files are left on the server' ($left.Count -eq 0) (($left | ForEach-Object { $_.Name }) -join ', ')

# The server stops: an online-only file fails at once after the first, a downloaded one opens,
# an edit waits, and a rename and a delete wait in the journal until the server is back, 30 seconds
# later. A kept file that changed on the server meanwhile is downloaded again.
Stop-Service sshd -Force
Get-Process sshd*, sftp-server -ErrorAction SilentlyContinue | Stop-Process -Force
$failure = Get-ReadError (Join-Path $homeServer 'never.txt')
Test-Check 'an online-only file fails while the server is stopped' ($failure -match 'network') $failure
$timer = [Diagnostics.Stopwatch]::StartNew()
$failure = Get-ReadError (Join-Path $homeServer 'new-on-server.txt')
Test-Check 'and the next one at once' (($failure -match 'network') -and $timer.ElapsedMilliseconds -lt 1000) "$($timer.ElapsedMilliseconds) ms: $failure"
Test-Check 'a downloaded file still opens' ([IO.File]::ReadAllText($file) -match 'Hello from the server')
[IO.File]::AppendAllText($file, "edited while the server was stopped`r`n")
Rename-Item (Join-Path $homeServer 'renamed-folder\inside.txt') 'inside-renamed.txt'
Remove-Item (Join-Path $homeServer 'docs\renamed.txt')
Test-Check 'an edit, a rename and a delete go ahead on this PC' (([IO.File]::ReadAllText($file) -match 'edited while the server was stopped') -and (Test-Path (Join-Path $homeServer 'renamed-folder\inside-renamed.txt')) -and -not (Test-Path (Join-Path $homeServer 'docs\renamed.txt')))
Start-Service sshd
[IO.File]::WriteAllText("$serverData\docs\readme.txt", "changed again on the server`r`n")
Start-Sleep -Seconds 31
Test-Check 'after 30 seconds the server is tried again' ((Get-ReadError (Join-Path $homeServer 'new-on-server.txt')) -eq '')
Test-Check 'and the edit reaches it' (Wait-Until { (Get-ServerText 'a.txt') -match 'edited while the server was stopped' } 40)
Test-Check 'and the rename and the delete' (Wait-Until { (Test-Path "$serverData\renamed-folder\inside-renamed.txt") -and -not (Test-Path "$serverData\docs\renamed.txt") } 40)
[void] (Get-ChildItem (Join-Path $homeServer 'docs'))
Test-Check 'a kept file that changed on the server is downloaded again' (Wait-Until { (Test-Downloaded $readme) -and ((Get-ReadError $readme) -eq '') -and ([IO.File]::ReadAllText($readme) -eq "changed again on the server`r`n") } 20)

# The provider stops: an online-only file can't be read, and an edit and a rename made meanwhile
# reach the server once the host starts it again.
Stop-Provider
$failure = Get-ReadError (Join-Path $homeServer 'never.txt')
Test-Check 'an online-only file can''t be read without the provider' ($failure -match 'provider is not running|provider exited unexpectedly') $failure
[IO.File]::AppendAllText((Join-Path $homeServer 'new-on-pc.txt'), "edited while stopped`r`n")
Rename-Item $readme 'readme-renamed.txt'
Test-Check 'the host starts the provider again' (Start-Host)
Test-Check 'an edit made while the provider was stopped is uploaded' (Wait-Until { (Get-ServerText 'new-on-pc.txt') -match 'edited while stopped' } 30)
Test-Check 'and a rename is made on the server' (Wait-Until { (Test-Path "$serverData\docs\readme-renamed.txt") -and -not (Test-Path "$serverData\docs\readme.txt") } 30)

# A provider that ends unexpectedly is started again, and the host's --exit, as Exit in its menu,
# stops it.
$provider = Get-Processes 'PCCompanion.RemoteFolders' | Select-Object -First 1
Stop-Process -Id $provider.Id -Force
Test-Check 'the host starts the provider again when it ends' (Wait-Until { (@(Get-Processes 'PCCompanion.RemoteFolders' | Where-Object { $_.Id -ne $provider.Id }).Count -eq 1) -and ($null -ne (Invoke-Provider '{"kind":"Status"}')) } 15)
Start-Process $hostExe -ArgumentList '--exit' -Wait
Test-Check 'the host''s --exit stops it' (Wait-Until { (Get-Processes 'PCCompanion.RemoteFolders').Count -eq 0 } 10)

# A changed password: the server refuses, and Sign in again saves the new one.
Stop-Provider
$newPassword = 'Rf2!' + [guid]::NewGuid().ToString('N').Substring(0, 10)
net user sftpuser $newPassword | Out-Null
[void] (Start-Host)
[void] (Invoke-Provider '{"kind":"SyncNow"}')
Test-Check 'the provider says the sign-in failed' (Wait-Until { (Get-ServerState $server.id) -eq 'SignInFailed' } 30) (Get-ServerState $server.id)
[void] (Open-Page)
Test-Check 'the page offers Sign in again' (Wait-Until { $null -ne (Find-AppTyped 'Sign in again' ([System.Windows.Automation.ControlType]::Button)) } 15) ((Get-PageText) -join ' | ')
Save-Screenshot 'rf-05-sign-in-failed'
Test-Check 'Sign in again opens the dialog' ((Invoke-AppButton 'Sign in again') -and (Wait-Until { Find-AppDialog 'Sign in again' } 5))
Start-Sleep -Seconds 1
[void] (Set-Password $newPassword)
Test-Check 'a new password needs Connect before Save' (-not (Test-ButtonEnabled 'Save'))
Test-Check 'Connect signs in with it' ((Invoke-AppButton 'Connect') -and (Wait-Until { Test-ButtonEnabled 'Save' } 30)) ((Get-PageText) -join ' | ')
Test-Check 'Save saves it, and the server connects' ((Invoke-AppButton 'Save') -and (Wait-Until { (Get-ServerState $server.id) -eq 'Connected' } 30)) (Get-ServerState $server.id)

# A changed key: nothing is sent, and Trust the new key asks first and saves it. sshd makes a
# missing host key when it starts.
Stop-Provider
Stop-Service sshd
Remove-Item 'C:\ProgramData\ssh\ssh_host_ed25519_key', 'C:\ProgramData\ssh\ssh_host_ed25519_key.pub' -Force
Start-Service sshd
[void] (Wait-Until { Test-Path 'C:\ProgramData\ssh\ssh_host_ed25519_key.pub' } 15)
$newFingerprint = Get-HostKeyFingerprint
Test-Check 'the server has a new key' (($newFingerprint.Length -gt 0) -and ($newFingerprint -ne $fingerprint))
[void] (Start-Host)
[void] (Invoke-Provider '{"kind":"SyncNow"}')
Test-Check 'the provider says the key changed' (Wait-Until { (Get-ServerState $server.id) -eq 'KeyChanged' } 30) (Get-ServerState $server.id)
[void] (Open-Page)
Test-Check 'the page offers Trust the new key' (Wait-Until { $null -ne (Find-AppTyped 'Trust the new key' ([System.Windows.Automation.ControlType]::Button)) } 15) ((Get-PageText) -join ' | ')
Test-Check 'with the new key' (((Get-PageText) -join '|') -match [regex]::Escape("ED25519 SHA256:$newFingerprint")) ((Get-PageText) -join ' | ')
Save-Screenshot 'rf-06-key-changed'
Test-Check 'Trust the new key asks first' ((Invoke-AppButton 'Trust the new key') -and (Wait-Until { Find-AppDialog 'Trust the new key of Home server?' } 5))
Start-Sleep -Seconds 1
Save-Screenshot 'rf-07-trust-dialog'
[void] (Invoke-DialogButton 'Trust the new key of Home server?' 'Trust the new key')
Test-Check 'and saves the new key' (Wait-Until { @(Get-Servers)[0].hostKey.sha256 -eq $newFingerprint } 15) (@(Get-Servers)[0].hostKey | ConvertTo-Json -Compress)
Test-Check 'the server connects again' (Wait-Connected $server.id) (Get-ServerState $server.id)

# Edit renames it; the folder keeps its path.
Test-Check 'Edit opens the dialog' ((Invoke-AppButton 'More options for Home server') -and (Invoke-AppMenuItem 'Edit') -and (Wait-Until { Find-AppDialog 'Edit server' } 5))
Start-Sleep -Seconds 1
Test-Check 'the address can''t change' (-not (Find-AppBox 'Address').Current.IsEnabled)
[void] (Set-AppBox 'Name' 'Home')
Test-Check 'a new name saves without Connect' ((Wait-Until { Test-ButtonEnabled 'Save' } 3) -and (Invoke-AppButton 'Save') -and (Wait-Until { @(Get-Servers)[0].name -eq 'Home' } 5))
Test-Check 'and the folder keeps its path' (@(Get-Servers)[0].folder -eq $homeServer)
Test-Check 'focus stays on the server' (Wait-Until { (Get-AppFocusedName) -eq 'More options for Home' } 3) (Get-AppFocusedName)

# A server that no longer offers the confirmed key's type, as after it was set up again with other
# keys: its key changed too, and its new key is trusted.
Stop-Provider
Stop-Service sshd
Set-Content $sshdConfig ("HostKey __PROGRAMDATA__/ssh/ssh_host_ecdsa_key`r`n" + (Get-Content $sshdConfig -Raw)) -Encoding ascii
Start-Service sshd
$ecdsaFingerprint = Get-HostKeyFingerprint 'ecdsa'
[void] (Start-Host)
[void] (Invoke-Provider '{"kind":"SyncNow"}')
Test-Check 'a server without the confirmed key''s type shows a changed key' (Wait-Until { (Get-ServerState $server.id) -eq 'KeyChanged' } 30) (Get-ServerState $server.id)
[void] (Open-Page)
Test-Check 'with its key of another type' (Wait-Until { ((Get-PageText) -join '|') -match [regex]::Escape("ECDSA SHA256:$ecdsaFingerprint") } 15) ((Get-PageText) -join ' | ')
Save-Screenshot 'rf-08-key-type-dropped'
Test-Check 'Trust the new key saves it' ((Invoke-AppButton 'Trust the new key') -and (Wait-Until { Find-AppDialog 'Trust the new key of Home?' } 5) -and (Invoke-DialogButton 'Trust the new key of Home?' 'Trust the new key') -and
    (Wait-Until { (@(Get-Servers)[0].hostKey.type -like 'ecdsa-*') -and (@(Get-Servers)[0].hostKey.sha256 -eq $ecdsaFingerprint) } 15)) (@(Get-Servers)[0].hostKey | ConvertTo-Json -Compress)
Test-Check 'and the server connects with it' (Wait-Connected $server.id) (Get-ServerState $server.id)

# Remove asks first, deletes the credential, and offers the folder, whose files stay. Every change
# reached the server, and the page shows it at its next look.
Test-Check 'no changes wait for the server' (Wait-Until { (@((Invoke-Provider '{"kind":"Status"}').servers) | Where-Object { $_.id -eq $server.id }).changesWaiting -eq 0 } 15) ((Invoke-Provider '{"kind":"Status"}') | ConvertTo-Json -Compress -Depth 6)
Start-Sleep -Seconds 3
Test-Check 'Remove asks first' ((Invoke-AppButton 'More options for Home') -and (Invoke-AppMenuItem 'Remove') -and (Wait-Until { Find-AppDialog 'Remove Home?' } 5))
Start-Sleep -Seconds 1
Save-Screenshot 'rf-09-remove-dialog'
[void] (Invoke-DialogButton 'Remove Home?' 'Remove')
Test-Check 'the server is removed' (Wait-Until { @(Get-Servers).Count -eq 0 } 5)
Test-Check 'and its credential' (-not (Test-Credential $credential))
Test-Check 'and its folder unregistered' (Wait-Until { (Get-SyncRoots).Count -eq 0 } 15) ((Get-SyncRoots) -join ', ')
Test-Check 'its downloaded files stay' ([IO.File]::ReadAllText($file) -match 'edited on the PC')
Test-Check 'the page offers the folder' (Wait-Until { Find-AppText 'Home was removed. Its files on this PC are in an ordinary folder now.' } 5)
Test-Check 'focus goes to Add server' (Wait-Until { (Get-AppFocusedName) -eq 'Add server' } 3) (Get-AppFocusedName)
Save-Screenshot 'rf-10-removed'

# A server with an SSH key file and no passphrase: no credential, the address as its name, and
# its home folder.
Test-Check 'Add server again' ((Invoke-AppButton 'Add server') -and (Wait-Until { Find-AppDialog 'Add a server' } 5))
Start-Sleep -Seconds 1
[void] (Set-AppBox 'Address' '127.0.0.1')
[void] (Set-AppBox 'User name' 'sftpuser')
Test-Check 'Sign in with SSH key' (Select-SignIn 'SSH key')
[void] (Set-AppBox 'Key file' "$work\missing")
Test-Check 'a key file that isn''t there is refused' (Wait-Until { Find-AppText 'The key file isn''t there. Enter its full path, or select Browse.' } 3)
[void] (Set-AppBox 'Key file' "$work\id_test")
[void] (Set-AppBox 'Folder on the server' '')
Test-Check 'Connect shows the server''s key' ((Invoke-AppButton 'Connect') -and (Wait-Until { Find-AppText "ECDSA SHA256:$ecdsaFingerprint" } 30)) ((Get-PageText) -join ' | ')
Test-Check 'I checked this key signs in and fills in the home folder' ((Set-KeyChecked) -and (Wait-Until { Test-ButtonEnabled 'Save' } 30) -and ((Get-AppBox 'Folder on the server') -ne '')) "$(Get-AppBox 'Folder on the server') | $((Get-PageText) -join ' | ')"
Save-Screenshot 'rf-11-key-dialog'
Test-Check 'the name is the address' ((Invoke-AppButton 'Save') -and (Wait-Until { @(Get-Servers).Count -eq 1 } 5) -and (@(Get-Servers)[0].name -eq '127.0.0.1') -and (@(Get-Servers)[0].signIn -eq 'Key') -and (@(Get-Servers)[0].keyFile -eq "$work\id_test")) (@(Get-Servers) | ConvertTo-Json -Compress)
$keyServer = @(Get-Servers)[0]
Test-Check 'without a credential' (-not (Test-Credential "PCCompanion/RemoteFolders/$($keyServer.id)"))
Test-Check 'and the folder lists the server' (Wait-Until { (Get-ChildItem (Join-Path $remoteFolders '127.0.0.1') -ErrorAction SilentlyContinue).Count -gt 0 } 15)
Test-Check 'Sync now asks the provider' ((Invoke-AppButton 'Sync 127.0.0.1 now') -and (Wait-Until { (Get-PageText) -contains '127.0.0.1, Connected' } 15)) ((Get-PageText) -join ' | ')
Save-Screenshot 'rf-12-page-key-server'

# The page in Arabic with the dark theme.
function Set-Look([object] $Language, [string] $Theme) {
    $settings = Get-Content (Join-Path $dataFolder 'settings.json') -Raw | ConvertFrom-Json
    $settings.general | Add-Member -NotePropertyName language -NotePropertyValue $Language -Force
    $settings.general.theme = $Theme
    Write-Settings ($settings | ConvertTo-Json -Depth 10)
}
[void] (Close-SettingsApp)
Set-Look 'ar' 'Dark'
Start-Process $appExe
[void] (Wait-Until { Get-AppWindow })
Start-Sleep -Seconds 3
Test-Check 'select Remote Folders in Arabic' (Show-Page 'المجلدات البعيدة')
Start-Sleep -Seconds 2
Save-Screenshot 'rf-12b-page-arabic-dark'
Test-Check 'the page is in Arabic' ((Get-PageText) -contains 'الخوادم') ((Get-PageText) -join ' | ')
[void] (Close-SettingsApp)
Set-Look $null 'Light'
[void] (Open-Page)
Test-Check 'turned off, Add server is off' ((Set-Toggle 'Remote Folders' $false) -and (Wait-Until { -not (Test-ButtonEnabled 'Add server') } 5))
Save-Screenshot 'rf-13-page-off'

# The logs, before uninstalling removes them: detailed logging was on, and they still name no
# server, user, folder, file or password.
$logText = (Get-ChildItem $logs -File -ErrorAction SilentlyContinue | ForEach-Object { Get-Content $_.FullName -Raw }) -join "`n"
Test-Check 'the provider logs the servers'' states' (($logText -match 'Server \d+: .?Connected') -and ($logText -match 'Server \d+: .?Unreachable'))
Test-Check 'the logs hold no server address, user name, folder or file name' (-not ($logText -match '127\.0\.0\.1|sftpuser|sftpdata|Home server|a\.txt|big\.bin|readme|new-on-pc|made-on-pc|kept-open|id_test'))
Test-Check 'or password' (-not ($logText.Contains($password) -or $logText.Contains($newPassword)))
# The checks run-checks.ps1 makes after each group, which then finds no logs.
Test-GroupLogs 'RemoteFolders' $results

# Uninstalling just for the user stops the provider, unregisters the folders and deletes Remote
# Folders' credentials, but not another program's; the files on this PC stay.
$keyFolder = Join-Path $remoteFolders '127.0.0.1'
$keyRoot = "PCCompanion!$mySid!$($keyServer.id)"
$namespace = (Get-ItemProperty "Registry::$syncRootManager\$keyRoot" -ErrorAction SilentlyContinue).NamespaceCLSID
$navigationPane = 'HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Desktop\NameSpace'
Test-Check 'the folder is in File Explorer''s navigation pane' (($null -ne $namespace) -and (Test-RegistryKey "$navigationPane\$namespace")) "CLSID $namespace"
cmdkey /generic:PCCompanion/RemoteFolders/00000000000000000000000000000001 /user:sftpuser /pass:stored | Out-Null
cmdkey /generic:PCCompanionTest/Other /user:other /pass:stored | Out-Null
$code = (Start-Process msiexec.exe -ArgumentList @('/x', "`"$msi`"", '/qn', '/l*v', "`"$results\uninstall.log`"") -Wait -PassThru).ExitCode
Test-Check 'uninstall PC Companion just for the user' ($code -eq 0) "exit code $code"
Test-Check 'the provider stopped' ((Get-Processes 'PCCompanion.RemoteFolders').Count -eq 0)
Test-Check 'the folder is unregistered' ((Get-SyncRoots).Count -eq 0) ((Get-SyncRoots) -join ', ')
Test-Check 'and gone from the navigation pane' (-not (Test-RegistryKey "$navigationPane\$namespace"))
Test-Check 'Remote Folders'' credentials are deleted' (-not (Test-Credential 'PCCompanion/RemoteFolders/'))
Test-Check 'another program''s stays' (Test-Credential 'PCCompanionTest/Other')
cmdkey /delete:PCCompanionTest/Other | Out-Null
Test-Check 'the folder stays, with its files' ((Test-Path $keyFolder) -and (Test-Path $file))

# Uninstalling for all users does the same for every user, as SYSTEM: the signed-in user's
# folder, and those of a user who is signed out, in their own registry hives. Another program's
# folders and entries stay.
$code = (Start-Process msiexec.exe -ArgumentList @('/i', "`"$msi`"", '/qn', 'ALLUSERS=1', '/l*v', "`"$results\install-all-users.log`"") -Wait -PassThru).ExitCode
Test-Check 'install PC Companion for all users' ($code -eq 0) "exit code $code"
$allUsersHost = Join-Path $env:ProgramW6432 'PCCompanion\PCCompanion.Host.exe'
$allUsersServer = [ordered] @{ id = '0000000000000000000000000000000a'; name = 'All users'; protocol = 'Sftp'; host = '127.0.0.1'; port = 22; userName = 'sftpuser'; signIn = 'Key'
    keyFile = "$work\id_test"; remoteFolder = $keyServer.remoteFolder; folder = (Join-Path $remoteFolders 'All users'); hostKey = $keyServer.hostKey }
Write-Settings (@{ schemaVersion = 1; general = @{ theme = 'Light' }; updates = @{ checkAutomatically = $false }; features = @{ remoteFolders = @{ enabled = $true; servers = @($allUsersServer) } } } | ConvertTo-Json -Depth 6)
Start-Process $allUsersHost
$myRoot = "PCCompanion!$mySid!$($allUsersServer.id)"
Test-Check 'the host of an all-users install registers the folder' (Wait-Until { (Get-SyncRoots) -contains $myRoot } 30) ((Get-SyncRoots) -join ', ')
$myNamespace = (Get-ItemProperty "Registry::$syncRootManager\$myRoot" -ErrorAction SilentlyContinue).NamespaceCLSID

# Another user, with a profile, signed out, and their folder as Windows keeps it: the sync root, the
# navigation pane entry and the class registration, next to another program's.
$otherPassword = 'Ot1!' + [guid]::NewGuid().ToString('N').Substring(0, 10)
net user other $otherPassword /add | Out-Null
$otherCredential = New-Object PSCredential('other', (ConvertTo-SecureString $otherPassword -AsPlainText -Force))
Start-Process cmd.exe -ArgumentList '/c exit' -Credential $otherCredential -WorkingDirectory 'C:\' -LoadUserProfile -Wait
$otherSid = (New-Object Security.Principal.NTAccount('other')).Translate([Security.Principal.SecurityIdentifier]).Value
$otherProfile = (Get-ItemProperty "HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList\$otherSid").ProfileImagePath
Test-Check 'the other user is signed out' (Wait-Until { -not (Test-RegistryKey "HKU\$otherSid") } 120) $otherProfile
$ours = '{11111111-2222-3333-4444-555555555555}'
$theirs = '{66666666-7777-8888-9999-AAAAAAAAAAAA}'
$userNamespace = 'Software\Microsoft\Windows\CurrentVersion\Explorer\Desktop\NameSpace'
$desktopIcons = 'Software\Microsoft\Windows\CurrentVersion\Explorer\HideDesktopIcons\NewStartPanel'
& reg.exe load HKU\RemoteFoldersOther "$otherProfile\NTUSER.DAT" | Out-Null
foreach ($clsid in $ours, $theirs) {
    & reg.exe add "HKU\RemoteFoldersOther\$userNamespace\$clsid" /ve /d 'Home' /f | Out-Null
    & reg.exe add "HKU\RemoteFoldersOther\$desktopIcons" /v $clsid /t REG_DWORD /d 1 /f | Out-Null
}
& reg.exe unload HKU\RemoteFoldersOther | Out-Null
& reg.exe load HKU\RemoteFoldersOtherClasses "$otherProfile\AppData\Local\Microsoft\Windows\UsrClass.dat" | Out-Null
foreach ($clsid in $ours, $theirs) { & reg.exe add "HKU\RemoteFoldersOtherClasses\CLSID\$clsid\Instance" /v CLSID /d '{0E5AAE11-A475-4C5B-AB00-C66DE400274E}' /f | Out-Null }
& reg.exe unload HKU\RemoteFoldersOtherClasses | Out-Null
$otherRoot = "PCCompanion!$otherSid!0000000000000000000000000000000b"
& reg.exe add "$syncRootManager\$otherRoot" /v NamespaceCLSID /d $ours /f | Out-Null
& reg.exe add "$syncRootManager\$otherRoot\UserSyncRoots" /v $otherSid /d "$otherProfile\Remote Folders\Home" /f | Out-Null
& reg.exe add "$syncRootManager\RemoteFoldersTestProvider!$otherSid!Personal" /v NamespaceCLSID /d $theirs /f | Out-Null
Test-Check 'the other user''s folders are in place, their hives unloaded again' ((Test-RegistryKey "$syncRootManager\$otherRoot") -and -not (Test-RegistryKey 'HKU\RemoteFoldersOther') -and -not (Test-RegistryKey 'HKU\RemoteFoldersOtherClasses'))

$code = (Start-Process msiexec.exe -ArgumentList @('/x', "`"$msi`"", '/qn', '/l*v', "`"$results\uninstall-all-users.log`"") -Wait -PassThru).ExitCode
Test-Check 'uninstall PC Companion for all users' ($code -eq 0) "exit code $code"
Test-Check 'the signed-in user''s folder is unregistered' (-not ((Get-SyncRoots) -contains $myRoot)) ((Get-SyncRoots) -join ', ')
Test-Check 'and gone from their navigation pane' (($null -ne $myNamespace) -and -not (Test-RegistryKey "$navigationPane\$myNamespace")) "CLSID $myNamespace"
Test-Check 'the signed-out user''s folder is unregistered' (-not (Test-RegistryKey "$syncRootManager\$otherRoot"))
Test-Check 'another program''s folder stays' (Test-RegistryKey "$syncRootManager\RemoteFoldersTestProvider!$otherSid!Personal")
Test-Check 'no hive is left loaded' (-not (Test-RegistryKey "HKU\PCCompanion.Cleanup.$otherSid") -and -not (Test-RegistryKey "HKU\PCCompanion.Cleanup.$($otherSid)_Classes"))
& reg.exe load HKU\RemoteFoldersOther "$otherProfile\NTUSER.DAT" | Out-Null
Test-Check 'the signed-out user''s navigation pane entry is gone' (-not (Test-RegistryKey "HKU\RemoteFoldersOther\$userNamespace\$ours"))
Test-Check 'and another program''s stays' (Test-RegistryKey "HKU\RemoteFoldersOther\$userNamespace\$theirs")
$values = (& reg.exe query "HKU\RemoteFoldersOther\$desktopIcons" 2>$null) -join ' '
Test-Check 'their desktop entry is gone, and another program''s stays' (($values -notmatch [regex]::Escape($ours)) -and ($values -match [regex]::Escape($theirs)))
& reg.exe unload HKU\RemoteFoldersOther | Out-Null
& reg.exe load HKU\RemoteFoldersOtherClasses "$otherProfile\AppData\Local\Microsoft\Windows\UsrClass.dat" | Out-Null
Test-Check 'their class registration is gone, and another program''s stays' (-not (Test-RegistryKey "HKU\RemoteFoldersOtherClasses\CLSID\$ours") -and (Test-RegistryKey "HKU\RemoteFoldersOtherClasses\CLSID\$theirs"))
& reg.exe unload HKU\RemoteFoldersOtherClasses | Out-Null
& reg.exe delete "$syncRootManager\RemoteFoldersTestProvider!$otherSid!Personal" /f | Out-Null
net user other /delete | Out-Null
