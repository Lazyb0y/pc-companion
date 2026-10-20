# System Meters as notification icons: the host adds an icon per meter with a fixed ID and the
# meter's name, which Windows puts in the hidden icons menu, and each shows its values in its
# tooltip. Selecting a meter opens Task Manager, and its menu, dark in the dark Windows mode, has
# Task Manager, Settings and Exit. The icons come back with their names after Explorer restarts, a
# meter turned off goes away, and turning System Meters off or Exit removes them all. Detailed
# logging is on for the host's Debug entries.
[void] (Close-SettingsApp)
$detailedUntil = [DateTimeOffset]::Now.AddHours(1).ToString('o')
function Write-MetersSettings([string] $Section) {
    Write-Settings "{ ""schemaVersion"": 1, ""general"": { ""theme"": ""Light"" }, ""diagnostics"": { ""detailedLoggingUntil"": ""$detailedUntil"" }, ""features"": { ""systemMeters"": $Section } }"
    Start-Sleep -Seconds 3
}
$metersWindow = 'PCCompanion.SystemMeters'
$started = 'System Meters started on its own thread\.'
$ended = "System Meters' thread ended\."

# What Windows remembers of each of the host's meter icons: its ID, the tooltip it was added with,
# and whether it's on the taskbar.
function Get-MeterIconSettings {
    @(Get-ChildItem 'HKCU:\Control Panel\NotifyIconSettings' -ErrorAction SilentlyContinue | ForEach-Object {
        $values = Get-ItemProperty $_.PSPath
        if ($values.ExecutablePath -like '*\PCCompanion.Host.exe' -and $values.UID -ge 2) {
            [pscustomobject] @{ Key = $_.PSPath; Id = [int] $values.UID; Name = $values.InitialTooltip; Promoted = $values.IsPromoted }
        }
    } | Sort-Object Id)
}
function Get-SettingsText($Settings) { (@($Settings) | ForEach-Object { "$($_.Id) $($_.Name)" }) -join ', ' }

# The meters' buttons, left to right. Each is named by the tooltip it was added with, the meter's
# name, followed by the current one. An icon the user moves is named by the tooltip it had then.
function Get-MeterButtons($Element) {
    if (-not $Element) { return @() }
    $condition = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ClassNameProperty, 'SystemTray.NormalButton')
    @($Element.FindAll([System.Windows.Automation.TreeScope]::Descendants, $condition) |
        Where-Object { $_.Current.Name -match '^(CPU|Memory|Network)(:|\s)' } |
        Sort-Object { $_.Current.BoundingRectangle.X })
}
function Get-ButtonText($Buttons) { (@($Buttons) | ForEach-Object { $_.Current.Name -replace "`r?`n", ' / ' }) -join ' | ' }
function Get-Taskbar {
    $condition = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ClassNameProperty, 'Shell_TrayWnd')
    [System.Windows.Automation.AutomationElement]::RootElement.FindFirst([System.Windows.Automation.TreeScope]::Children, $condition)
}
function Get-MenuItemNames {
    $menuCondition = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ClassNameProperty, '#32768')
    $menu = [System.Windows.Automation.AutomationElement]::RootElement.FindFirst([System.Windows.Automation.TreeScope]::Children, $menuCondition)
    if (-not $menu) { return '' }
    $itemCondition = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::MenuItem)
    (@($menu.FindAll([System.Windows.Automation.TreeScope]::Descendants, $itemCondition)) | ForEach-Object { $_.Current.Name }) -join ', '
}

Write-MetersSettings '{ "enabled": true }'
Start-Process $hostExe
Test-Check 'System Meters starts on its own thread' (Wait-Until { (Get-HostLog) -match $started } 20)
Test-Check 'System Meters shows CPU, memory and network every second' ((Get-HostLog) -match 'System Meters applied its settings: CPU True, memory True, network True, every 1 s\.')
Test-Check 'the meters have a window of their own' ([Win32]::FindWindow($metersWindow, $null) -ne [IntPtr]::Zero)
$script:remembered = @()
Test-Check 'Windows takes an icon per meter, with fixed IDs and the meters'' names' (Wait-Until { $script:remembered = Get-MeterIconSettings; (Get-SettingsText $remembered) -eq '2 CPU, 3 Memory, 4 Network' } 15) (Get-SettingsText $remembered)

# New icons go to the hidden icons menu, with CPU first.
$condition = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, 'Show Hidden Icons')
$chevron = (Get-Taskbar).FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
Test-Check 'the taskbar has the hidden icons menu' ($null -ne $chevron)
if ($chevron) {
    $chevron.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    $overflowCondition = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ClassNameProperty, 'TopLevelWindowForOverflowXamlIsland')
    $script:hidden = @()
    $found = Wait-Until {
        $script:hidden = Get-MeterButtons ([System.Windows.Automation.AutomationElement]::RootElement.FindFirst([System.Windows.Automation.TreeScope]::Children, $overflowCondition))
        $hidden.Count -eq 3
    } 10
    Start-Sleep -Seconds 1
    Save-Screenshot '15-system-meters-hidden-icons'
    $text = Get-ButtonText $hidden
    Test-Check 'the meters are in the hidden icons menu, CPU first' ($found -and $text -match '^CPU CPU: \d+% \| Memory Memory: \d+% / [\d.]+ GB of [\d.]+ GB in use \| Network Network / Receive: [\d.]+ [KMG]bps / Send: [\d.]+ [KMG]bps$') $text
    Send-Key 0x1B
    Start-Sleep -Seconds 1
}

# Bringing the icons onto the taskbar is the user's choice; the group does it to look at them there.
foreach ($setting in $remembered) { Set-ItemProperty $setting.Key -Name IsPromoted -Value 1 -Type DWord }
$script:shown = @()
Test-Check 'the meters show on the taskbar once brought out, CPU first' (Wait-Until { $script:shown = Get-MeterButtons (Get-Taskbar); ($shown.Count -eq 3) -and ((Get-ButtonText $shown) -match '^CPU.* \| Memory.* \| Network') } 15) (Get-ButtonText $shown)
Save-Screenshot '15b-system-meters-taskbar'

# Selecting a meter opens Task Manager.
Test-Check 'select the CPU meter' (Send-TrayEvent $NIN_SELECT -IconId 2 -WindowClass $metersWindow)
Test-Check 'selecting a meter opens Task Manager' (Wait-Until { (Get-Processes 'Taskmgr').Count -gt 0 } 15)
Test-Check 'the host logs that it opened Task Manager' (Wait-Until { (Get-HostLog) -match 'System Meters opened Task Manager\.' } 5)
Get-Processes 'Taskmgr' | Stop-Process -Force -ErrorAction SilentlyContinue

# The meters' menu follows the Windows mode, and its Settings opens the settings app.
$personalize = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize'
$windowsMode = (Get-ItemProperty $personalize -ErrorAction SilentlyContinue).SystemUsesLightTheme
if (-not (Test-Path $personalize)) { [void] (New-Item $personalize -Force) }
[void] (New-ItemProperty $personalize -Name SystemUsesLightTheme -Value 0 -PropertyType DWord -Force)
Test-Check 'the memory meter''s menu requested' (Send-TrayEvent $WM_CONTEXTMENU $menuX $menuY -IconId 3 -WindowClass $metersWindow)
$brightness = Get-TrayMenuBrightness
Test-Check 'the meters'' menu is dark in the dark Windows mode' (($brightness -ge 0) -and ($brightness -lt 100)) "brightness $brightness"
$items = Get-MenuItemNames
Test-Check 'the meters'' menu has Task Manager, Settings and Exit' ($items -eq 'Task Manager, Settings, Exit') $items
Save-Screenshot '15c-system-meters-menu-dark'
Test-Check 'choose Settings in the meters'' menu' (Select-TrayMenuItem 'Settings')
Test-Check 'Settings in the meters'' menu opens the settings app' (Wait-Until { Get-AppWindow } 20)
[void] (Close-SettingsApp)
if ($null -eq $windowsMode) {
    Remove-ItemProperty $personalize -Name SystemUsesLightTheme -ErrorAction SilentlyContinue
}
else {
    [void] (New-ItemProperty $personalize -Name SystemUsesLightTheme -Value $windowsMode -PropertyType DWord -Force)
}

# Explorer restarts: the icons are added again, with their names.
$taskbarBefore = [Win32]::FindWindow('Shell_TrayWnd', $null)
Get-Process explorer -ErrorAction SilentlyContinue | Stop-Process -Force
if (-not (Wait-Until { $taskbar = [Win32]::FindWindow('Shell_TrayWnd', $null); ($taskbar -ne [IntPtr]::Zero) -and ($taskbar -ne $taskbarBefore) } 20)) {
    Start-Process explorer.exe
}
Test-Check 'the taskbar restarts' (Wait-Until { $taskbar = [Win32]::FindWindow('Shell_TrayWnd', $null); ($taskbar -ne [IntPtr]::Zero) -and ($taskbar -ne $taskbarBefore) } 30)
Test-Check 'the meters come back after the taskbar restarts, with their names' (Wait-Until { $script:shown = Get-MeterButtons (Get-Taskbar); ($shown.Count -eq 3) -and ((Get-ButtonText $shown) -match '^CPU CPU: \d+% \| Memory Memory: \d+% / .* \| Network Network / ') } 30) (Get-ButtonText $shown)
Save-Screenshot '15d-system-meters-after-explorer-restart'

# A meter turned off goes away, and turning System Meters off removes them all.
Write-MetersSettings '{ "enabled": true, "memory": false }'
Test-Check 'the host applies the meters turned off' (Wait-Until { (Get-HostLog) -match 'System Meters applied its settings: CPU True, memory False, network True, every 1 s\.' } 10)
Test-Check 'the memory meter goes away' (Wait-Until { $script:shown = Get-MeterButtons (Get-Taskbar); ($shown.Count -eq 2) -and ((Get-ButtonText $shown) -notmatch 'Memory') } 10) (Get-ButtonText $shown)
Write-MetersSettings '{ "enabled": false, "memory": false }'
Test-Check 'turning System Meters off ends its thread' (Wait-Until { (Get-HostLog) -match $ended } 10)
Test-Check 'turning System Meters off removes the meters' (Wait-Until { (Get-MeterButtons (Get-Taskbar)).Count -eq 0 } 10) (Get-ButtonText (Get-MeterButtons (Get-Taskbar)))
Test-Check 'turning System Meters off removes its window' ([Win32]::FindWindow($metersWindow, $null) -eq [IntPtr]::Zero)

# Exit in the meters' menu stops the host, which removes the meters first.
Write-MetersSettings '{ "enabled": true }'
Test-Check 'System Meters starts again' (Wait-Until { (Get-MatchCount (Get-HostLog) $started) -eq 2 } 15)
Test-Check 'the meters show again' (Wait-Until { (Get-MeterButtons (Get-Taskbar)).Count -eq 3 } 15)
Test-Check 'the CPU meter''s menu requested for Exit' (Send-TrayEvent $WM_CONTEXTMENU $menuX $menuY -IconId 2 -WindowClass $metersWindow)
Test-Check 'choose Exit in the meters'' menu' (Select-TrayMenuItem 'Exit')
Test-Check 'Exit in the meters'' menu stops the host' (Wait-Until { (Get-Processes 'PCCompanion.Host').Count -eq 0 } 15)
Test-Check 'the host removes the meters as it exits' (Wait-Until { (Get-MeterButtons (Get-Taskbar)).Count -eq 0 } 5) (Get-ButtonText (Get-MeterButtons (Get-Taskbar)))
Test-Check 'the meters'' thread ends before the host exits' ((Get-MatchCount (Get-HostLog) $ended) -eq 2)
