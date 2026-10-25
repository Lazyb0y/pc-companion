# Networking: on
# System Meters: its page, then the meters on the taskbar strip and as notification icons. The
# Home switch turns the feature on, and the page shows its settings as they start; with
# Notification area, Layout and Icons are off and a note's button opens the taskbar settings.
# The strip shows in the
# taskbar's free space, without covering its buttons, names the meters for screen readers, shows a
# tooltip, and keeps its width as the values change; its layouts and icon styles change its width,
# and its text follows the Windows mode. Clicking it opens Task Manager, right-clicking it shows
# the meters' menu, and it comes back in front after a click on the taskbar and after Explorer
# restarts. With an auto-hiding taskbar the meters use notification icons until it stops hiding.
# As notification icons, chosen with Show on: an icon per meter with a fixed ID and the meter's
# name, which Windows puts in the hidden icons menu, each with its values in its tooltip.
# Selecting a meter opens Task Manager, and its menu, dark in the dark Windows mode, has Task
# Manager, Settings and Exit. The icons come back with their names after Explorer restarts, and
# the host logs each one; a meter turned off goes away and comes back in its place when it's
# turned on again, and turning System Meters off or Exit removes them all. The values follow
# busy processors, memory in use, and network traffic, for which the group needs networking; the
# update check stays off, and api.github.com points at the Sandbox. Detailed logging is on for the
# host's Debug entries.
[void] (Close-SettingsApp)
$detailedUntil = [DateTimeOffset]::Now.AddHours(1).ToString('o')
function Write-MetersSettings([string] $Section) {
    Write-Settings "{ ""schemaVersion"": 1, ""general"": { ""theme"": ""Light"" }, ""updates"": { ""checkAutomatically"": false }, ""diagnostics"": { ""detailedLoggingUntil"": ""$detailedUntil"" }, ""features"": { ""systemMeters"": $Section } }"
    Start-Sleep -Seconds 3
}
$metersWindow = 'PCCompanion.SystemMeters'
$stripClass = 'PCCompanion.SystemMeters.Strip'
$started = 'System Meters started on its own thread\.'
$ended = "System Meters' thread ended\."

Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class MetersChecks {
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr SendMessageTimeout(IntPtr window, uint message, UIntPtr wParam, string lParam, uint flags, uint timeout, out UIntPtr result);
    [StructLayout(LayoutKind.Sequential)] private struct APPBARDATA { public uint cbSize; public IntPtr hWnd; public uint uCallbackMessage, uEdge; public int left, top, right, bottom; public IntPtr lParam; }
    [DllImport("shell32.dll")] private static extern UIntPtr SHAppBarMessage(uint message, ref APPBARDATA data);

    // Tells every window that a setting changed (WM_SETTINGCHANGE), as Settings does.
    public static void BroadcastSettingChange(string area) {
        UIntPtr result;
        SendMessageTimeout((IntPtr)0xFFFF, 0x001A, UIntPtr.Zero, area, 0x0002, 5000, out result);
    }

    // Turns the taskbar's auto-hide on or off (ABM_SETSTATE with ABS_AUTOHIDE).
    public static void SetTaskbarAutoHide(bool on) {
        var data = new APPBARDATA { cbSize = (uint)Marshal.SizeOf(typeof(APPBARDATA)), lParam = (IntPtr)(on ? 1 : 0) };
        SHAppBarMessage(0x0000000A, ref data);
    }
}
'@

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
# Windows' own network icon, named "Network Network / Internet access", has no rates.
function Get-MeterButtons($Element) {
    if (-not $Element) { return @() }
    $condition = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Button)
    @($Element.FindAll([System.Windows.Automation.TreeScope]::Descendants, $condition) |
        Where-Object { $_.Current.Name -match '^(CPU|Memory)(:|\s)' -or ($_.Current.Name -match '^Network(:|\s)' -and $_.Current.Name -match 'Receive: ') } |
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

# The hidden icons menu's windows: their class differs after Explorer restarts.
function Get-OverflowWindows {
    @([System.Windows.Automation.AutomationElement]::RootElement.FindAll([System.Windows.Automation.TreeScope]::Children, [System.Windows.Automation.Condition]::TrueCondition) |
        Where-Object { $_.Current.ClassName -like '*Overflow*' })
}
function Get-OverflowMeterButtons { @(Get-OverflowWindows | ForEach-Object { Get-MeterButtons $_ }) }

# The meters' buttons in the hidden icons menu, which this opens and closes.
function Get-HiddenMeterButtons {
    $condition = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, 'Show Hidden Icons')
    $chevron = (Get-Taskbar).FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
    if (-not $chevron) { return @() }
    $chevron.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    Start-Sleep -Seconds 2
    $buttons = Get-OverflowMeterButtons
    Send-Key 0x1B
    Start-Sleep -Seconds 1
    return $buttons
}

# The strip is a layered window, which FindWindow doesn't find, so it's looked for among the visible windows.
function Get-StripWindow { [Win32]::TopLevelWindows() | Where-Object { [Win32]::WindowClass($_) -eq $stripClass } | Select-Object -First 1 }
function Get-Bounds([IntPtr] $Window) {
    $rect = New-Object Win32+RECT
    [void] [Win32]::GetWindowRect($Window, [ref] $rect)
    return $rect
}
function Get-BoundsText($Rect) { "$($Rect.Left),$($Rect.Top) to $($Rect.Right),$($Rect.Bottom)" }
function Get-StripWidth {
    $strip = Get-StripWindow
    if (-not $strip) { return 0 }
    $bounds = Get-Bounds $strip
    return $bounds.Right - $bounds.Left
}

# What the strip mustn't cover: Start, Search, Task View, Widgets, the app buttons and the notification area.
function Get-TaskbarParts {
    @((Get-Taskbar).FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition) | Where-Object {
        $_.Current.ClassName -in 'Taskbar.TaskListButtonAutomationPeer', 'TrayNotifyWnd' -or $_.Current.AutomationId -in 'StartButton', 'SearchButton', 'TaskViewButton', 'WidgetsButton'
    } | ForEach-Object { $_.Current.BoundingRectangle } | Where-Object { $_.Width -gt 0 })
}

# The meters' values, read from the strip's name for screen readers: CPU and memory in percent,
# and the rates in bits per second.
function Get-StripValues {
    $strip = Get-StripWindow
    $title = if ($strip) { [Win32]::WindowTitle($strip) } else { '' }
    $values = [ordered] @{ Cpu = -1; Memory = -1; Receive = -1; Send = -1 }
    if ($title -match 'CPU: (\d+)%') { $values.Cpu = [int] $Matches[1] }
    if ($title -match 'Memory: (\d+)%') { $values.Memory = [int] $Matches[1] }
    foreach ($name in 'Receive', 'Send') {
        if ($title -match "${name}: ([\d.]+) ([KMG])bps") { $values[$name] = [double] $Matches[1] * @{ K = 1e3; M = 1e6; G = 1e9 }[$Matches[2]] }
    }
    return [pscustomobject] $values
}
function Get-StripValuesText { (Get-StripValues | ConvertTo-Json -Compress) }

# The darkest and brightest pixels of the strip on the screen, from 0 to 1.
function Get-StripBrightness {
    $bounds = Get-Bounds (Get-StripWindow)
    $bitmap = New-Object System.Drawing.Bitmap ($bounds.Right - $bounds.Left), ($bounds.Bottom - $bounds.Top)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.CopyFromScreen($bounds.Left, $bounds.Top, 0, 0, $bitmap.Size)
    $values = for ($x = 0; $x -lt $bitmap.Width; $x++) { for ($y = 0; $y -lt $bitmap.Height; $y++) { $bitmap.GetPixel($x, $y).GetBrightness() } }
    $graphics.Dispose()
    $bitmap.Dispose()
    $measure = $values | Measure-Object -Minimum -Maximum
    return [pscustomobject] @{ Darkest = [math]::Round($measure.Minimum, 2); Brightest = [math]::Round($measure.Maximum, 2) }
}

$personalize = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize'
$windowsMode = (Get-ItemProperty $personalize -ErrorAction SilentlyContinue).SystemUsesLightTheme
if (-not (Test-Path $personalize)) { [void] (New-Item $personalize -Force) }
function Set-WindowsMode([int] $Light) {
    [void] (New-ItemProperty $personalize -Name SystemUsesLightTheme -Value $Light -PropertyType DWord -Force)
    [MetersChecks]::BroadcastSettingChange('ImmersiveColorSet')
    Start-Sleep -Seconds 3
}

# The Home switch and the page.
Write-Settings '{ "schemaVersion": 1, "general": { "theme": "Light" }, "updates": { "checkAutomatically": false } }'
Start-Sleep -Seconds 2
Start-Process $appExe
[void] (Wait-Until { Get-AppWindow })
Start-Sleep -Seconds 3
function Get-MetersSection { (Get-Content (Join-Path $dataFolder 'settings.json') -Raw | ConvertFrom-Json).features.systemMeters }
function Test-ToggleOn([string] $Name) {
    $toggle = Find-Toggle $Name
    return ($null -ne $toggle) -and $toggle.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern).Current.ToggleState -eq [System.Windows.Automation.ToggleState]::On
}
Test-Check 'turn on System Meters on Home' (Set-Toggle 'System Meters' $true)
Start-Sleep -Seconds 2
$saved = Get-MetersSection
Test-Check 'the Home switch saves System Meters as on' ($saved.enabled -eq $true) "saved: $($saved | ConvertTo-Json -Compress)"
Test-Check 'select System Meters' (Select-NavigationItem 'System Meters')
Start-Sleep -Seconds 2
Save-Screenshot '15-system-meters-page'
$text = Get-WindowText
Test-Check 'System Meters shows its settings' (($text -contains 'Meters') -and ($text -contains 'Show on') -and ($text -contains 'Layout') -and ($text -contains 'Update every') -and ($text -contains 'Permissions & data')) ($text -join ' | ')
Test-Check 'the page''s switch shows System Meters on' (Test-ToggleOn 'System Meters')
Test-Check 'every meter starts on' ((Test-ToggleOn 'CPU') -and (Test-ToggleOn 'Memory') -and (Test-ToggleOn 'Network'))
function Get-Choice([string] $Name) {
    $script:choice = ''
    [void] (Wait-Until { Start-Sleep -Milliseconds 300; $script:choice = Get-ListSelection $Name; [bool] $choice } 5)
    return $choice
}
$choices = "$(Get-Choice 'Show on'); $(Get-Choice 'Layout'); $(Get-Choice 'Icons'); $(Get-Choice 'Update every')"
Test-Check 'the meters start on the taskbar, with Values and names, Color icons and an update every second' ($choices -eq 'Taskbar; Values and names; Color; 1 second') $choices
Test-Check 'choose Notification area' (Select-ListItem 'Show on' 'Notification area')
Start-Sleep -Seconds 1
$saved = Get-MetersSection
Test-Check 'the page saves Notification area' ($saved.showOn -eq 'NotificationArea') "saved: $($saved | ConvertTo-Json -Compress)"
$layoutList = Find-AppList 'Layout'
Test-Check 'Layout and Icons are off with Notification area' (($null -ne $layoutList) -and -not $layoutList.Current.IsEnabled -and -not (Find-AppList 'Icons').Current.IsEnabled)
$text = Get-WindowText
Test-Check 'the page says how to bring the icons out of the hidden icons menu' (@($text | Where-Object { $_ -like 'Windows may put new icons in the hidden icons menu*' }).Count -eq 1) ($text -join ' | ')
Save-Screenshot '15-system-meters-page-notification-area'
$button = Find-AppElement 'Open taskbar settings'
if ($button) { $button.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke() }
Test-Check 'Open taskbar settings opens Settings' (Wait-Until { (Get-Processes 'SystemSettings').Count -gt 0 } 15)
Get-Processes 'SystemSettings' | Stop-Process -Force -ErrorAction SilentlyContinue
Test-Check 'turn System Meters off on its page' (Set-Toggle 'System Meters' $false)
Start-Sleep -Seconds 2
$saved = Get-MetersSection
Test-Check 'the page''s switch saves System Meters as off' ($saved.enabled -eq $false) "saved: $($saved | ConvertTo-Json -Compress)"
[void] (Close-SettingsApp)
[void] (Start-Process $hostExe -ArgumentList '--exit' -Wait -PassThru)

# The strip, which shows by default on this Windows 11 build.
Write-MetersSettings '{ "enabled": true }'
Start-Process $hostExe
Test-Check 'System Meters starts on its own thread' (Wait-Until { (Get-HostLog) -match $started } 20)
Test-Check 'System Meters shows CPU, memory and network every second' ((Get-HostLog) -match 'System Meters applied its settings: CPU True, memory True, network True, every 1 s\.')
Test-Check 'the meters have a window of their own' ([Win32]::FindWindow($metersWindow, $null) -ne [IntPtr]::Zero)
Test-Check 'the meters show on the taskbar' (Wait-Until { (Get-HostLog) -match 'System Meters show: "?OnTaskbar"?\.' } 15)
$script:strip = $null
Test-Check 'the strip shows' (Wait-Until { $script:strip = Get-StripWindow; $null -ne $strip } 10)
$taskbarWindow = [Win32]::FindWindow('Shell_TrayWnd', $null)
$taskbarBounds = Get-Bounds $taskbarWindow
$stripBounds = Get-Bounds $strip
Test-Check 'the strip is on the taskbar, as high as it' (($stripBounds.Top -eq $taskbarBounds.Top) -and ($stripBounds.Bottom -eq $taskbarBounds.Bottom) -and ($stripBounds.Left -ge $taskbarBounds.Left) -and ($stripBounds.Right -le $taskbarBounds.Right)) "strip $(Get-BoundsText $stripBounds), taskbar $(Get-BoundsText $taskbarBounds)"
$covered = @(Get-TaskbarParts | Where-Object { $_.Left -lt $stripBounds.Right -and $_.Right -gt $stripBounds.Left })
Test-Check 'the strip covers none of the taskbar''s buttons or its notification area' ($covered.Count -eq 0) "strip $(Get-BoundsText $stripBounds); covers $(($covered | ForEach-Object { "$($_.Left)..$($_.Right)" }) -join ', ')"
$title = [Win32]::WindowTitle($strip)
Test-Check 'the strip names the meters and their values for screen readers' (($title -match 'CPU: \d+%') -and ($title -match 'Memory: \d+%') -and ($title -match 'Receive: ')) ($title -replace "`r?`n", ' / ')
$hidden = Get-HiddenMeterButtons
Test-Check 'no meter has a notification icon while the strip shows' ($hidden.Count -eq 0) (Get-ButtonText $hidden)
Save-Screenshot '15-system-meters-strip'

# On a left-aligned taskbar, the strip stays in the free space, now next to the notification area.
$advanced = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced'
$alignment = (Get-ItemProperty $advanced -ErrorAction SilentlyContinue).TaskbarAl
[void] (New-ItemProperty $advanced -Name TaskbarAl -Value 0 -PropertyType DWord -Force)
[MetersChecks]::BroadcastSettingChange('TraySettings')
$startCondition = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::AutomationIdProperty, 'StartButton')
Test-Check 'the taskbar aligns left' (Wait-Until { $start = (Get-Taskbar).FindFirst([System.Windows.Automation.TreeScope]::Descendants, $startCondition); ($null -ne $start) -and $start.Current.BoundingRectangle.Left -lt 50 } 15)
Start-Sleep -Seconds 3
$leftBounds = Get-Bounds (Get-StripWindow)
$covered = @(Get-TaskbarParts | Where-Object { $_.Left -lt $leftBounds.Right -and $_.Right -gt $leftBounds.Left })
Test-Check 'on a left-aligned taskbar, the strip covers none of the taskbar''s buttons' (($covered.Count -eq 0) -and ($leftBounds.Right -gt $leftBounds.Left)) "strip $(Get-BoundsText $leftBounds); covers $(($covered | ForEach-Object { "$($_.Left)..$($_.Right)" }) -join ', ')"
Save-Screenshot '15-system-meters-strip-left-aligned'
if ($null -eq $alignment) {
    Remove-ItemProperty $advanced -Name TaskbarAl -ErrorAction SilentlyContinue
}
else {
    [void] (New-ItemProperty $advanced -Name TaskbarAl -Value $alignment -PropertyType DWord -Force)
}
[MetersChecks]::BroadcastSettingChange('TraySettings')
Test-Check 'the taskbar is centered again' (Wait-Until { $start = (Get-Taskbar).FindFirst([System.Windows.Automation.TreeScope]::Descendants, $startCondition); ($null -ne $start) -and $start.Current.BoundingRectangle.Left -gt 100 } 15)
Start-Sleep -Seconds 3
$script:strip = Get-StripWindow
$stripBounds = Get-Bounds $strip

# The pointer on the strip shows its highlight and tooltip; a click opens Task Manager.
$stripX = [int](($stripBounds.Left + $stripBounds.Right) / 2)
$stripY = [int](($stripBounds.Top + $stripBounds.Bottom) / 2)
[void] (Move-Pointer $stripX $stripY)
# The tooltip's text isn't readable from another process; the screenshot shows it.
Test-Check 'the strip shows its tooltip' (Wait-Until { $null -ne ([Win32]::TopLevelWindows() | Where-Object { [Win32]::WindowClass($_) -eq 'tooltips_class32' -and [Win32]::Owner($_) -eq $strip }) } 10) (Get-PointerText)
Save-Screenshot '15a-system-meters-strip-hover'
[void] [Win32]::LeftButton($true)
[void] [Win32]::LeftButton($false)
Test-Check 'clicking the strip opens Task Manager' (Wait-Until { (Get-Processes 'Taskmgr').Count -gt 0 } 15)
Get-Processes 'Taskmgr' | Stop-Process -Force -ErrorAction SilentlyContinue
Test-Check 'right-clicking the strip asks for the meters'' menu' ([Win32]::PostMessage($strip, 0x0205, [UIntPtr]::Zero, [IntPtr]::Zero))
$script:items = ''
Test-Check 'the strip''s menu has Task Manager, Settings and Exit' (Wait-Until { $script:items = Get-MenuItemNames; $items -eq 'Task Manager, Settings, Exit' } 10) $items
Test-Check 'the strip''s menu closes' (Close-TrayMenu -WindowClass $metersWindow)

# A click on the taskbar brings the taskbar to the front; the strip comes back in front of it.
[void] (Move-Pointer ($stripBounds.Left - 20) $stripY)
[void] [Win32]::LeftButton($true)
[void] [Win32]::LeftButton($false)
Test-Check 'the strip comes back in front after a click on the taskbar' (Wait-Until { [Win32]::IsInFrontOf($strip, $taskbarWindow) -and [Win32]::IsWindowVisible($strip) } 5) (Get-PointerText)
[void] (Move-Pointer ([int]($screen.Width / 2)) ([int]($screen.Height / 2)))

# Layouts and icon styles change the strip's width; values keep their places as they change.
$widths = [ordered] @{}
foreach ($style in @('ValuesAndNames', 'Color'), @('LargeValues', 'Color'), @('Compact', 'Color'), @('ValuesAndNames', 'None'), @('ValuesAndNames', 'OneColor')) {
    Write-MetersSettings "{ ""enabled"": true, ""layout"": ""$($style[0])"", ""icons"": ""$($style[1])"" }"
    $widths["$($style[0]) $($style[1])"] = Get-StripWidth
    Save-Screenshot ("15b-system-meters-strip-$($style[0])-$($style[1])".ToLowerInvariant())
}
$text = ($widths.Keys | ForEach-Object { "$_ $($widths[$_])" }) -join ', '
Test-Check 'Large values is wider than Values and names, and Compact narrower' (($widths['LargeValues Color'] -gt $widths['ValuesAndNames Color']) -and ($widths['Compact Color'] -lt $widths['ValuesAndNames Color']) -and ($widths['Compact Color'] -gt 0)) $text
Test-Check 'the strip is narrower without icons, and as wide with One color icons' (($widths['ValuesAndNames None'] -lt $widths['ValuesAndNames Color']) -and ($widths['ValuesAndNames OneColor'] -eq $widths['ValuesAndNames Color'])) $text
$before = Get-StripWidth
Start-Sleep -Seconds 3
Test-Check 'the strip keeps its width as the values change' ((Get-StripWidth) -eq $before) "$before, then $(Get-StripWidth)"

# The values follow real use: busy processes on every processor, 1 GB of memory, pings to the
# gateway, which this PC's firewall drops but which count as sent, and requests to Windows'
# connectivity check address, which bring data back.
$busy = @(1..[Environment]::ProcessorCount | ForEach-Object {
    Start-Process powershell.exe -ArgumentList '-NoProfile', '-Command', '$end = [DateTime]::Now.AddSeconds(15); while ([DateTime]::Now -lt $end) { }' -WindowStyle Hidden -PassThru
})
Test-Check 'busy processors raise CPU above 80%' (Wait-Until { (Get-StripValues).Cpu -gt 80 } 12) (Get-StripValuesText)
$busy | Stop-Process -Force -ErrorAction SilentlyContinue
Test-Check 'CPU falls once they stop' (Wait-Until { $cpu = (Get-StripValues).Cpu; ($cpu -ge 0) -and ($cpu -lt 50) } 15) (Get-StripValuesText)
$before = (Get-StripValues).Memory
$share = [int] (100 * 1GB / (Get-CimInstance Win32_ComputerSystem).TotalPhysicalMemory)
$allocation = Start-Process powershell.exe -ArgumentList '-NoProfile', '-Command', '$memory = New-Object byte[] 1GB; for ($i = 0; $i -lt $memory.Length; $i += 4096) { $memory[$i] = 1 }; Start-Sleep -Seconds 60' -WindowStyle Hidden -PassThru
Test-Check 'allocating 1 GB raises memory by about its share' (Wait-Until { (Get-StripValues).Memory -ge $before + [int] ($share * 0.6) } 30) "before $before%, its share $share%, now $((Get-StripValues).Memory)%"
Stop-Process -Id $allocation.Id -Force -ErrorAction SilentlyContinue
$gateway = (Get-NetIPConfiguration | Where-Object { $_.IPv4DefaultGateway } | Select-Object -First 1).IPv4DefaultGateway.NextHop
$pings = Start-Process ping.exe -ArgumentList '-n', '20', '-l', '60000', $gateway -WindowStyle Hidden -PassThru
Test-Check 'pings to the gateway show in Send' (Wait-Until { (Get-StripValues).Send -gt 100000 } 15) "gateway $gateway; $(Get-StripValuesText)"
Stop-Process -Id $pings.Id -Force -ErrorAction SilentlyContinue
$requests = Start-Process powershell.exe -ArgumentList '-NoProfile', '-Command', '$end = [DateTime]::Now.AddSeconds(15); while ([DateTime]::Now -lt $end) { try { [void] (Invoke-WebRequest -UseBasicParsing http://www.msftconnecttest.com/connecttest.txt) } catch { } }' -WindowStyle Hidden -PassThru
Test-Check 'requests to the connectivity check address show in Receive' (Wait-Until { (Get-StripValues).Receive -gt 0 } 15) (Get-StripValuesText)
Stop-Process -Id $requests.Id -Force -ErrorAction SilentlyContinue

# The text follows the Windows mode: black on a light taskbar, white on a dark one.
Write-MetersSettings '{ "enabled": true, "icons": "None" }'
Set-WindowsMode 1
$light = Get-StripBrightness
Save-Screenshot '15c-system-meters-strip-light'
Set-WindowsMode 0
$dark = Get-StripBrightness
Save-Screenshot '15c-system-meters-strip-dark'
Test-Check 'the strip''s text is dark in the light Windows mode and white in the dark mode' (($light.Darkest -lt 0.35) -and ($dark.Brightest -gt 0.75)) "light: $($light.Darkest) to $($light.Brightest); dark: $($dark.Darkest) to $($dark.Brightest)"
if ($null -eq $windowsMode) {
    Remove-ItemProperty $personalize -Name SystemUsesLightTheme -ErrorAction SilentlyContinue
    [MetersChecks]::BroadcastSettingChange('ImmersiveColorSet')
}
else {
    Set-WindowsMode $windowsMode
}

# Explorer restarts: the strip comes back.
Write-MetersSettings '{ "enabled": true }'
$taskbarBefore = [Win32]::FindWindow('Shell_TrayWnd', $null)
Get-Process explorer -ErrorAction SilentlyContinue | Stop-Process -Force
if (-not (Wait-Until { $taskbar = [Win32]::FindWindow('Shell_TrayWnd', $null); ($taskbar -ne [IntPtr]::Zero) -and ($taskbar -ne $taskbarBefore) } 20)) {
    Start-Process explorer.exe
}
Test-Check 'the taskbar restarts with the strip on' (Wait-Until { $taskbar = [Win32]::FindWindow('Shell_TrayWnd', $null); ($taskbar -ne [IntPtr]::Zero) -and ($taskbar -ne $taskbarBefore) } 30)
Test-Check 'the strip comes back after the taskbar restarts' (Wait-Until { $script:strip = Get-StripWindow; ($null -ne $strip) -and [Win32]::IsInFrontOf($strip, [Win32]::FindWindow('Shell_TrayWnd', $null)) } 30)
Start-Sleep -Seconds 3
Save-Screenshot '15d-system-meters-strip-after-explorer-restart'

# An auto-hiding taskbar has no strip; the meters use notification icons until it stops hiding.
# While the meters are paused, the strip doesn't look for room, so the change shows only once they
# resume. The Sandbox can't lock its session, so the meters' window gets the reports Windows sends
# then (WM_WTSSESSION_CHANGE with WTS_SESSION_LOCK, and later WTS_SESSION_UNLOCK).
$stuckRects = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\StuckRects3'
$savedTaskbar = (Get-ItemProperty $stuckRects -ErrorAction SilentlyContinue).Settings
$onTaskbar = Get-MatchCount (Get-HostLog) 'System Meters show: "?OnTaskbar"?\.'
$autoHide = Get-MatchCount (Get-HostLog) 'System Meters show: "?IconsAutoHide"?\.'
$pauses = Get-MatchCount (Get-HostLog) 'System Meters paused: [Tt]rue'
$metersHandle = [Win32]::FindWindow($metersWindow, $null)
[void] [Win32]::PostMessage($metersHandle, 0x02B1, [UIntPtr] [uint32] 7, [IntPtr]::Zero)
Test-Check 'the meters pause when Windows reports the session locked' (Wait-Until { (Get-MatchCount (Get-HostLog) 'System Meters paused: [Tt]rue') -gt $pauses } 10)
[MetersChecks]::SetTaskbarAutoHide($true)
Test-Check 'while the meters are paused, the strip doesn''t look for room' (-not (Wait-Until { (Get-MatchCount (Get-HostLog) 'System Meters show: "?IconsAutoHide"?\.') -gt $autoHide } 5))
$resumes = Get-MatchCount (Get-HostLog) 'System Meters paused: [Ff]alse'
[void] [Win32]::PostMessage($metersHandle, 0x02B1, [UIntPtr] [uint32] 8, [IntPtr]::Zero)
Test-Check 'the meters resume when Windows reports the session unlocked' (Wait-Until { (Get-MatchCount (Get-HostLog) 'System Meters paused: [Ff]alse') -gt $resumes } 10)
Test-Check 'once the meters resume, the strip looks for room at once, and with an auto-hiding taskbar the meters move to the notification area' (Wait-Until { (Get-MatchCount (Get-HostLog) 'System Meters show: "?IconsAutoHide"?\.') -gt $autoHide } 10)
Test-Check 'with an auto-hiding taskbar, the strip is hidden' ($null -eq (Get-StripWindow))
Test-Check 'with an auto-hiding taskbar, Windows takes an icon per meter' (Wait-Until { (Get-SettingsText (Get-MeterIconSettings)) -eq '2 CPU, 3 Memory, 4 Network' } 15) (Get-SettingsText (Get-MeterIconSettings))
[MetersChecks]::SetTaskbarAutoHide($false)
Test-Check 'once the taskbar stops hiding, the strip is back' (Wait-Until { (Get-MatchCount (Get-HostLog) 'System Meters show: "?OnTaskbar"?\.') -gt $onTaskbar -and $null -ne (Get-StripWindow) } 40)

# Windows saves the taskbar's auto-hide for its next start (StuckRects3) some seconds after it's
# turned on, and in the Sandbox hadn't saved turning it off 20 seconds later, so the Explorer
# restarts further down would start with a hiding taskbar. The saved settings go back to how they were.
if ($null -eq $savedTaskbar) {
    Remove-ItemProperty $stuckRects -Name Settings -ErrorAction SilentlyContinue
}
else {
    Set-ItemProperty $stuckRects -Name Settings -Value $savedTaskbar
}

# Notification icons, chosen with Show on.
Write-MetersSettings '{ "enabled": true, "showOn": "NotificationArea" }'
Test-Check 'with Notification area, the meters show as notification icons' (Wait-Until { (Get-HostLog) -match 'System Meters show: "?IconsChosen"?\.' } 10)
Test-Check 'with Notification area, the strip goes away' (Wait-Until { $null -eq (Get-StripWindow) } 10)
$script:remembered = @()
Test-Check 'Windows takes an icon per meter, with fixed IDs and the meters'' names' (Wait-Until { $script:remembered = Get-MeterIconSettings; (Get-SettingsText $remembered) -eq '2 CPU, 3 Memory, 4 Network' } 15) (Get-SettingsText $remembered)

# New icons go to the hidden icons menu: Windows records them as not brought onto the taskbar.
# After an Explorer restart, UI Automation doesn't list the menu's buttons, so the screenshot shows them.
Test-Check 'Windows puts the meters in the hidden icons menu' (($remembered.Count -eq 3) -and -not @($remembered | Where-Object { $_.Promoted -eq 1 })) "$(Get-SettingsText $remembered); brought out: $(@($remembered | Where-Object { $_.Promoted -eq 1 }).Count)"
$condition = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, 'Show Hidden Icons')
$chevron = (Get-Taskbar).FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
Test-Check 'the taskbar has the hidden icons menu' ($null -ne $chevron)
if ($chevron) {
    $chevron.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    Start-Sleep -Seconds 3
    Save-Screenshot '15e-system-meters-hidden-icons'
    Write-Report "[NOTE] The hidden icons menu lists: $(Get-ButtonText (Get-OverflowMeterButtons))"
    Send-Key 0x1B
    Start-Sleep -Seconds 1
}

# Bringing the icons onto the taskbar is the user's choice; the group does it to look at them there.
foreach ($setting in $remembered) { Set-ItemProperty $setting.Key -Name IsPromoted -Value 1 -Type DWord }
$script:shown = @()
Test-Check 'the meters show on the taskbar once brought out, CPU first' (Wait-Until { $script:shown = Get-MeterButtons (Get-Taskbar); ($shown.Count -eq 3) -and ((Get-ButtonText $shown) -match '^CPU.* \| Memory.* \| Network') } 15) (Get-ButtonText $shown)
Save-Screenshot '15f-system-meters-taskbar-icons'

# Selecting a meter opens Task Manager.
Test-Check 'select the CPU meter' (Send-TrayEvent $NIN_SELECT -IconId 2 -WindowClass $metersWindow)
Test-Check 'selecting a meter opens Task Manager' (Wait-Until { (Get-Processes 'Taskmgr').Count -gt 0 } 15)
Test-Check 'the host logs that it opened Task Manager' (Wait-Until { (Get-HostLog) -match 'System Meters opened Task Manager\.' } 5)
Get-Processes 'Taskmgr' | Stop-Process -Force -ErrorAction SilentlyContinue

# The meters' menu follows the Windows mode, and its Settings opens the settings app.
[void] (New-ItemProperty $personalize -Name SystemUsesLightTheme -Value 0 -PropertyType DWord -Force)
Test-Check 'the memory meter''s menu requested' (Send-TrayEvent $WM_CONTEXTMENU $menuX $menuY -IconId 3 -WindowClass $metersWindow)
$brightness = Get-TrayMenuBrightness
Test-Check 'the meters'' menu is dark in the dark Windows mode' (($brightness -ge 0) -and ($brightness -lt 100)) "brightness $brightness"
$items = Get-MenuItemNames
Test-Check 'the meters'' menu has Task Manager, Settings and Exit' ($items -eq 'Task Manager, Settings, Exit') $items
Save-Screenshot '15g-system-meters-menu-dark'
Test-Check 'choose Settings in the meters'' menu' (Select-TrayMenuItem 'Settings')
Test-Check 'Settings in the meters'' menu opens the settings app' (Wait-Until { Get-AppWindow } 20)
[void] (Close-SettingsApp)
if ($null -eq $windowsMode) {
    Remove-ItemProperty $personalize -Name SystemUsesLightTheme -ErrorAction SilentlyContinue
}
else {
    [void] (New-ItemProperty $personalize -Name SystemUsesLightTheme -Value $windowsMode -PropertyType DWord -Force)
}

# Explorer restarts: the icons are added again, with their names, and the host logs each one.
$taskbarBefore = [Win32]::FindWindow('Shell_TrayWnd', $null)
Get-Process explorer -ErrorAction SilentlyContinue | Stop-Process -Force
if (-not (Wait-Until { $taskbar = [Win32]::FindWindow('Shell_TrayWnd', $null); ($taskbar -ne [IntPtr]::Zero) -and ($taskbar -ne $taskbarBefore) } 20)) {
    Start-Process explorer.exe
}
Test-Check 'the taskbar restarts' (Wait-Until { $taskbar = [Win32]::FindWindow('Shell_TrayWnd', $null); ($taskbar -ne [IntPtr]::Zero) -and ($taskbar -ne $taskbarBefore) } 30)
Test-Check 'the meters come back after the taskbar restarts, with their names' (Wait-Until { $script:shown = Get-MeterButtons (Get-Taskbar); ($shown.Count -eq 3) -and ((Get-ButtonText $shown) -match '^CPU CPU: \d+% \| Memory Memory: \d+% / .* \| Network Network / ') } 30) (Get-ButtonText $shown)
Test-Check 'the host logs each icon Windows took again' (Wait-Until { $log = Get-HostLog; @('Cpu', 'Memory', 'Network' | Where-Object { $log -match ('Windows took the "?{0}"? meter''s icon again\.' -f $_) }).Count -eq 3 } 10)
Save-Screenshot '15h-system-meters-icons-after-explorer-restart'

# A meter turned off goes away, and turning System Meters off removes them all.
Write-MetersSettings '{ "enabled": true, "memory": false, "showOn": "NotificationArea" }'
Test-Check 'the host applies the meters turned off' (Wait-Until { (Get-HostLog) -match 'System Meters applied its settings: CPU True, memory False, network True, every 1 s\.' } 10)
Test-Check 'the memory meter goes away' (Wait-Until { $script:shown = Get-MeterButtons (Get-Taskbar); ($shown.Count -eq 2) -and ((Get-ButtonText $shown) -notmatch 'Memory') } 10) (Get-ButtonText $shown)

# A meter turned on again comes back in its place: Windows remembers each icon's place by its
# program and ID, although it puts icons it hasn't seen before first.
Write-MetersSettings '{ "enabled": true, "showOn": "NotificationArea" }'
Test-Check 'the memory meter comes back between CPU and network' (Wait-Until { $script:shown = Get-MeterButtons (Get-Taskbar); ($shown.Count -eq 3) -and ((Get-ButtonText $shown) -match '^CPU.* \| Memory.* \| Network') } 15) (Get-ButtonText $shown)
Write-MetersSettings '{ "enabled": false, "memory": false, "showOn": "NotificationArea" }'
Test-Check 'turning System Meters off ends its thread' (Wait-Until { (Get-HostLog) -match $ended } 10)
Test-Check 'turning System Meters off removes the meters' (Wait-Until { (Get-MeterButtons (Get-Taskbar)).Count -eq 0 } 10) (Get-ButtonText (Get-MeterButtons (Get-Taskbar)))
Test-Check 'turning System Meters off removes its window' ([Win32]::FindWindow($metersWindow, $null) -eq [IntPtr]::Zero)

# Exit in the meters' menu stops the host, which removes the meters first.
Write-MetersSettings '{ "enabled": true, "showOn": "NotificationArea" }'
Test-Check 'System Meters starts again' (Wait-Until { (Get-MatchCount (Get-HostLog) $started) -eq 2 } 15)
Test-Check 'the meters show again' (Wait-Until { (Get-MeterButtons (Get-Taskbar)).Count -eq 3 } 15)
Test-Check 'the CPU meter''s menu requested for Exit' (Send-TrayEvent $WM_CONTEXTMENU $menuX $menuY -IconId 2 -WindowClass $metersWindow)
Test-Check 'choose Exit in the meters'' menu' (Select-TrayMenuItem 'Exit')
Test-Check 'Exit in the meters'' menu stops the host' (Wait-Until { (Get-Processes 'PCCompanion.Host').Count -eq 0 } 15)
Test-Check 'the host removes the meters as it exits' (Wait-Until { (Get-MeterButtons (Get-Taskbar)).Count -eq 0 } 5) (Get-ButtonText (Get-MeterButtons (Get-Taskbar)))
Test-Check 'the meters'' thread ends before the host exits' ((Get-MatchCount (Get-HostLog) $ended) -eq 2)
