# App Windows: the Home switch turns it on and its page saves its settings; the key above Tab
# switches between File Explorer windows by its position, also on the German layout after a
# switch in the same window, and does nothing once App Windows is off.
[void] (Close-SettingsApp)
Write-Settings '{ "schemaVersion": 1, "general": { "theme": "Light" } }'
Start-Sleep -Seconds 2
Start-Process $appExe
[void] (Wait-Until { Get-AppWindow })
Start-Sleep -Seconds 3
function Get-AppWindowsSettings { (Get-Content (Join-Path $dataFolder 'settings.json') -Raw | ConvertFrom-Json).features.appWindows }
Test-Check 'turn on App Windows on Home' (Set-Toggle 'App Windows' $true)
Start-Sleep -Seconds 2
$saved = Get-AppWindowsSettings
Test-Check 'the Home switch saves App Windows as on' ($saved.enabled -eq $true) "saved: $($saved | ConvertTo-Json -Compress)"
Test-Check 'select App Windows' (Select-NavigationItem 'App Windows')
Start-Sleep -Seconds 2
Save-Screenshot '13b-app-windows-english'
$text = Get-WindowText
Test-Check 'App Windows shows its settings' (($text -contains 'Shortcut') -and ($text -contains 'Include minimized windows') -and ($text -contains 'Permissions & data')) ($text -join ' | ')
$selected = Get-ListSelection 'Shortcut'
Test-Check 'the shortcut is Alt with the key above Tab, shown as it types' ($selected -eq 'Alt + `') "selected: $selected"
Test-Check 'choose the Windows logo key' (Select-ListItem 'Shortcut' 'Windows logo key + `')
Start-Sleep -Seconds 1
$saved = Get-AppWindowsSettings
Test-Check 'the page saves the shortcut' ($saved.shortcut -eq 'WindowsAboveTab') "saved: $($saved | ConvertTo-Json -Compress)"
Test-Check 'turn on Include minimized windows' (Set-Toggle 'Include minimized windows' $true)
Start-Sleep -Seconds 1
$saved = Get-AppWindowsSettings
Test-Check 'the page saves Include minimized windows' ($saved.includeMinimized -eq $true) "saved: $($saved | ConvertTo-Json -Compress)"
[void] (Close-SettingsApp)

# Detailed logging is on for the host's Debug entries.
$detailedUntil = [DateTimeOffset]::Now.AddHours(1).ToString('o')
function Write-AppWindowsSettings([string] $Section) {
    Write-Settings "{ ""schemaVersion"": 1, ""general"": { ""theme"": ""Light"" }, ""diagnostics"": { ""detailedLoggingUntil"": ""$detailedUntil"" }, ""features"": { ""appWindows"": $Section } }"
    Start-Sleep -Seconds 3
}
function Get-ExplorerWindows { @([Win32]::TopLevelWindows() | Where-Object { [Win32]::WindowTitle($_) -like '* - File Explorer' }) }
function Get-ExplorerOrder { (Get-ExplorerWindows | ForEach-Object { [Win32]::WindowTitle($_) -replace ' - File Explorer$', '' }) -join ' | ' }
function Get-FrontTitle { [Win32]::WindowTitle([Win32]::GetForegroundWindow()) -replace ' - File Explorer$', '' }
$LeftAlt = 0x38; $LeftShift = 0x2A; $KeyAboveTab = 0x29
function Send-ScanKeys([int[]] $Modifiers, [int] $Key) {
    foreach ($modifier in $Modifiers) { [void] [Win32]::ScanKey($modifier, $false, $false) }
    [void] [Win32]::ScanKey($Key, $false, $false)
    [void] [Win32]::ScanKey($Key, $true, $false)
    for ($i = $Modifiers.Count - 1; $i -ge 0; $i--) { [void] [Win32]::ScanKey($Modifiers[$i], $true, $false) }
    Start-Sleep -Milliseconds 700
}

Write-AppWindowsSettings '{ "enabled": true }'
foreach ($folder in 'C:\Windows', 'C:\Users', 'C:\Program Files') {
    Start-Process explorer.exe $folder
    Start-Sleep -Seconds 3
}
$explorers = Get-ExplorerWindows
Test-Check 'three File Explorer windows open' ($explorers.Count -eq 3) "$($explorers.Count) open"
$area = New-Object Win32+RECT
[void] [Win32]::GetWindowRect($explorers[0], [ref] $area)
[void] (Move-Pointer ([int](($area.Left + $area.Right) / 2)) ([int](($area.Top + $area.Bottom) / 2)))
[void] [Win32]::LeftButton($true)
[void] [Win32]::LeftButton($false)
Start-Sleep -Seconds 1
$start = Get-ExplorerOrder
$names = $start -split ' \| '
$fronts = @()
for ($i = 0; $i -lt 3; $i++) { Send-ScanKeys @($LeftAlt) $KeyAboveTab; $fronts += Get-FrontTitle }
Test-Check 'Alt + the key above Tab brings each File Explorer window to the front, the one furthest back first' (($fronts -join ' | ') -eq (@($names[2], $names[1], $names[0]) -join ' | ')) "start: $start; fronts: $($fronts -join ', ')"
Test-Check 'after a press for each window, they are in their first order' ((Get-ExplorerOrder) -eq $start) (Get-ExplorerOrder)
Send-ScanKeys @($LeftAlt, $LeftShift) $KeyAboveTab
Test-Check 'Alt + Shift + the key above Tab brings the second window to the front' ((Get-FrontTitle) -eq $names[1]) "front: $(Get-FrontTitle); order: $(Get-ExplorerOrder)"
Send-ScanKeys @($LeftAlt) $KeyAboveTab
Test-Check 'a press forward undoes it' ((Get-ExplorerOrder) -eq $start) (Get-ExplorerOrder)
Test-Check 'the host logs the switches' (Wait-Until { (Get-HostLog) -match 'App Windows went "?Forward"?; the app has 3 windows' } 5)

# The key above Tab is ^ on the German layout. Windows reports no switch within a window, so the
# host finds it with its check once a second; a press forward first lets it see the new layout.
$languages = Get-WinUserLanguageList
$withGerman = New-WinUserLanguageList en-US
$withGerman.Add('de-DE')
Set-WinUserLanguageList $withGerman -Force
Start-Sleep -Seconds 2
Send-ScanKeys @($LeftAlt) $KeyAboveTab
Send-ScanKeys @($LeftAlt) $LeftShift
Start-Sleep -Milliseconds 1500
$before = Get-FrontTitle
Send-ScanKeys @($LeftAlt) $KeyAboveTab
Test-Check 'after switching to German in the same window, the key above Tab still switches' ((Get-FrontTitle) -ne $before) "front: $before -> $(Get-FrontTitle)"
Send-ScanKeys @($LeftAlt) $LeftShift
Set-WinUserLanguageList $languages -Force

Write-AppWindowsSettings '{ "enabled": false }'
$before = Get-FrontTitle
Send-ScanKeys @($LeftAlt) $KeyAboveTab
Test-Check 'turned off, the key above Tab switches no window' ((Get-FrontTitle) -eq $before) "front: $before -> $(Get-FrontTitle)"
foreach ($window in Get-ExplorerWindows) { [void] [Win32]::PostMessage($window, 0x0010, [UIntPtr]::Zero, [IntPtr]::Zero) } # WM_CLOSE
Start-Sleep -Seconds 2
