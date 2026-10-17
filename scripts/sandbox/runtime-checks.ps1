<#
.SYNOPSIS
Runtime checks of PC Companion inside Windows Sandbox. Started by scripts\sandbox-test.ps1; don't run
it on your own PC: it installs and uninstalls PC Companion and shuts Windows down at the end.

.DESCRIPTION
Inputs:  C:\Test\msi\PCCompanion-*.msi (read-only).
Outputs: C:\Test\results: report.txt with one PASS or FAIL line per check, screenshots, the app and
         host logs, the install logs, and done.txt when finished.

The checks look for English, Arabic and Bangla text from src\PCCompanion.Localization; update them
when those strings change.
#>

$ErrorActionPreference = 'Continue'
$results = 'C:\Test\results'
$report = Join-Path $results 'report.txt'
New-Item -ItemType Directory -Force -Path $results | Out-Null
Set-Content -Path $report -Value "PC Companion runtime checks - $(Get-Date -Format o)" -Encoding utf8

function Write-Report([string] $Text) { Add-Content -Path $report -Value $Text -Encoding utf8 }

function Test-Check([string] $Name, [bool] $Passed, [string] $Detail = '') {
    $status = if ($Passed) { 'PASS' } else { 'FAIL' }
    $suffix = if ($Detail) { " - $Detail" } else { '' }
    Write-Report "[$status] $Name$suffix"
}

Add-Type -AssemblyName System.Windows.Forms, System.Drawing, UIAutomationClient, UIAutomationTypes
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class Win32 {
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindow(string className, string title);
    [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr window, uint message, UIntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
    [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr window);
    [DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint count, INPUT[] inputs, int size);

    [StructLayout(LayoutKind.Sequential)] private struct MOUSEINPUT { public int dx, dy; public uint mouseData, dwFlags, time; public IntPtr extra; }
    [StructLayout(LayoutKind.Sequential)] private struct KEYBDINPUT { public ushort wVk, wScan; public uint dwFlags, time; public IntPtr extra; }
    [StructLayout(LayoutKind.Explicit)] private struct INPUT { [FieldOffset(0)] public uint type; [FieldOffset(8)] public MOUSEINPUT mi; [FieldOffset(8)] public KEYBDINPUT ki; }

    // Moves the pointer as a mouse does, so raw input reports it; (x, y) is on the main display.
    public static bool MovePointer(int x, int y, int width, int height) {
        var input = new INPUT { type = 0 };
        input.mi.dx = (int)((long)x * 65535 / (width - 1));
        input.mi.dy = (int)((long)y * 65535 / (height - 1));
        input.mi.dwFlags = 0x8001; // MOUSEEVENTF_MOVE | MOUSEEVENTF_ABSOLUTE
        return SendInput(1, new[] { input }, Marshal.SizeOf(typeof(INPUT))) == 1;
    }

    // Moves the pointer by (dx, dy), as a mouse does.
    public static bool PushPointer(int dx, int dy) {
        var input = new INPUT { type = 0 };
        input.mi.dx = dx;
        input.mi.dy = dy;
        input.mi.dwFlags = 0x0001; // MOUSEEVENTF_MOVE
        return SendInput(1, new[] { input }, Marshal.SizeOf(typeof(INPUT))) == 1;
    }

    public static bool LeftButton(bool down) {
        var input = new INPUT { type = 0 };
        input.mi.dwFlags = down ? 0x0002u : 0x0004u; // MOUSEEVENTF_LEFTDOWN, MOUSEEVENTF_LEFTUP
        return SendInput(1, new[] { input }, Marshal.SizeOf(typeof(INPUT))) == 1;
    }

    public static bool Key(ushort key, bool up) {
        var input = new INPUT { type = 1 };
        input.ki.wVk = key;
        input.ki.dwFlags = up ? 0x0002u : 0u; // KEYEVENTF_KEYUP
        return SendInput(1, new[] { input }, Marshal.SizeOf(typeof(INPUT))) == 1;
    }
}
'@
[void] [Win32]::SetProcessDPIAware()

function Save-Screenshot([string] $Name) {
    $bounds = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
    $bitmap = New-Object System.Drawing.Bitmap $bounds.Width, $bounds.Height
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.CopyFromScreen($bounds.Location, [System.Drawing.Point]::Empty, $bounds.Size)
    $bitmap.Save((Join-Path $results "$Name.png"), [System.Drawing.Imaging.ImageFormat]::Png)

    # The Sandbox draws nothing while its window is minimized, so screenshots come out blank.
    $first = $bitmap.GetPixel(0, 0)
    $blank = $true
    for ($x = 0; $x -lt $bounds.Width -and $blank; $x += 50) {
        for ($y = 0; $y -lt $bounds.Height -and $blank; $y += 50) {
            if ($bitmap.GetPixel($x, $y) -ne $first) { $blank = $false }
        }
    }
    if ($blank) {
        Write-Report "[NOTE] Screenshot $Name is blank; keep the Sandbox window open and visible while the checks run."
    }

    $graphics.Dispose()
    $bitmap.Dispose()
}

function Wait-Until([scriptblock] $Condition, [int] $Seconds = 30) {
    for ($i = 0; $i -lt ($Seconds * 4); $i++) {
        if (& $Condition) { return $true }
        Start-Sleep -Milliseconds 250
    }
    return $false
}

function Get-Processes([string] $Name) { @(Get-Process -Name $Name -ErrorAction SilentlyContinue) }

function Close-SettingsApp {
    Get-Processes 'PCCompanion' | ForEach-Object { [void] $_.CloseMainWindow() }
    return (Wait-Until { (Get-Processes 'PCCompanion').Count -eq 0 } 15)
}

function Stop-All {
    [void] (Close-SettingsApp)
    foreach ($name in 'PCCompanion', 'PCCompanion.Host') {
        Get-Processes $name | ForEach-Object { Stop-Process -Id $_.Id -Force -ErrorAction SilentlyContinue }
    }
    Start-Sleep -Seconds 1
}

function Get-AppWindow {
    $process = Get-Processes 'PCCompanion' | Select-Object -First 1
    if (-not $process) { return $null }
    $condition = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ProcessIdProperty, $process.Id)
    return [System.Windows.Automation.AutomationElement]::RootElement.FindFirst([System.Windows.Automation.TreeScope]::Children, $condition)
}

function Select-NavigationItem([string] $Name) {
    $window = Get-AppWindow
    if (-not $window) { return $false }
    $condition = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, $Name)
    $item = $window.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
    if (-not $item) { return $false }
    $pattern = $null
    if ($item.TryGetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern, [ref] $pattern)) { $pattern.Select(); return $true }
    if ($item.TryGetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern, [ref] $pattern)) { $pattern.Invoke(); return $true }
    return $false
}

function Find-Toggle([string] $Name) {
    $window = Get-AppWindow
    if (-not $window) { return $null }
    $condition = New-Object System.Windows.Automation.AndCondition(
        (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, $Name)),
        (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::IsTogglePatternAvailableProperty, $true)))
    return $window.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
}

# The paths of the Always run as administrator tasks, from Task Scheduler.
function Get-StartupTasks {
    @(schtasks.exe /Query /TN '\PCCompanion\' /FO CSV /NH 2>$null | ForEach-Object { ($_ -split '","')[0].Trim('"') } | Where-Object { $_ -like '\PCCompanion\*' })
}

function Set-Toggle([string] $Name, [bool] $On) {
    $window = Get-AppWindow
    if (-not $window) { return $false }
    $condition = New-Object System.Windows.Automation.AndCondition(
        (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, $Name)),
        (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::IsTogglePatternAvailableProperty, $true)))
    $toggle = $window.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
    if (-not $toggle) { return $false }
    $pattern = $toggle.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern)
    if (($pattern.Current.ToggleState -eq [System.Windows.Automation.ToggleState]::On) -ne $On) { $pattern.Toggle() }
    return $true
}

# Opens a list to read the name a screen reader gives its selected item, then closes it.
function Get-SelectedItemName([string] $ListName) {
    $window = Get-AppWindow
    if (-not $window) { return $null }
    $condition = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, $ListName)
    $list = $window.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
    if (-not $list) { return $null }
    $expand = $list.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern)
    $expand.Expand()
    Start-Sleep -Milliseconds 500
    $selected = @($list.GetCurrentPattern([System.Windows.Automation.SelectionPattern]::Pattern).Current.GetSelection() | ForEach-Object { $_.Current.Name })
    $expand.Collapse()
    return ($selected -join ', ')
}

function Find-AppElement([string] $Name) {
    $window = Get-AppWindow
    if (-not $window) { return $null }
    $condition = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, $Name)
    return $window.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
}

function Get-BoxText([string] $Name) {
    $box = Find-AppElement $Name
    if (-not $box) { return $null }
    return $box.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value
}

# A dialog of the settings app, which UI Automation lists on the desktop or under the app's window.
function Find-AppDialog([string] $Name) {
    $condition = New-Object System.Windows.Automation.AndCondition(
        (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, $Name)),
        (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Window)))
    $dialog = [System.Windows.Automation.AutomationElement]::RootElement.FindFirst([System.Windows.Automation.TreeScope]::Children, $condition)
    if ($dialog) { return $dialog }
    $window = Get-AppWindow
    if (-not $window) { return $null }
    return $window.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
}

# A tooltip or text of the settings app whose name is exactly $Name.
function Find-AppText([string] $Name) {
    $window = Get-AppWindow
    if (-not $window) { return $null }
    $condition = New-Object System.Windows.Automation.AndCondition(
        (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, $Name)),
        (New-Object System.Windows.Automation.OrCondition(
            (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::ToolTip)),
            (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Text)))))
    $found = $window.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
    if ($found) { return $found }
    return [System.Windows.Automation.AutomationElement]::RootElement.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
}

function Get-PointerText { "pointer at $([System.Windows.Forms.Cursor]::Position)" }

# Presses and releases a key as a keyboard does. Unlike UI Automation's Invoke, it doesn't wait
# for what the key opens, such as a dialog.
function Send-Key([uint16] $Key) {
    [void] [Win32]::Key($Key, $false)
    [void] [Win32]::Key($Key, $true)
}

function Get-WindowText {
    $window = Get-AppWindow
    if (-not $window) { return @() }
    $all = $window.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
    return @($all | ForEach-Object { $_.Current.Name } | Where-Object { $_ })
}

# Sends what Explorer sends when the tray icon is used: the host's window gets WM_APP + 1 with the
# event in the low word of lParam and the icon ID (1) in the high word. The icon itself is usually
# hidden in the overflow area of a new Windows 11 profile.
function Send-TrayEvent([int] $TrayEvent, [int] $X = 0, [int] $Y = 0) {
    $window = [Win32]::FindWindow('PCCompanion.Host', $null)
    if ($window -eq [IntPtr]::Zero) { return $false }
    return [Win32]::PostMessage($window, 0x8001, [UIntPtr] [uint32] (($X -band 0xFFFF) -bor ($Y -shl 16)), [IntPtr] ($TrayEvent -bor (1 -shl 16)))
}

$WM_CONTEXTMENU = 0x007B
$NIN_SELECT = 0x0400

# Moves the pointer in steps from where it is to (X, Y), then waits. Returns whether it got there:
# it doesn't while the Sandbox window is minimized or the mouse is moving over it.
function Move-Pointer([int] $X, [int] $Y) {
    $start = [System.Windows.Forms.Cursor]::Position
    for ($step = 1; $step -le 10; $step++) {
        $stepX = $start.X + [int](($X - $start.X) * $step / 10)
        $stepY = $start.Y + [int](($Y - $start.Y) * $step / 10)
        [void] [Win32]::MovePointer($stepX, $stepY, $screen.Width, $screen.Height)
        Start-Sleep -Milliseconds 15
    }
    Start-Sleep -Milliseconds 1000
    $position = [System.Windows.Forms.Cursor]::Position
    return ($position.X -eq $X) -and ($position.Y -eq $Y)
}

# Moves the pointer into the top-left corner and pushes it against the edges, as a mouse does.
# Windows reports no mouse input for an absolute move to exactly (0, 0), so the push makes the
# host see the pointer arrive.
function Move-IntoTopLeftCorner {
    [void] (Move-Pointer 0 0)
    [void] [Win32]::PushPointer(-10, -10)
    Start-Sleep -Milliseconds 1000
    $position = [System.Windows.Forms.Cursor]::Position
    return ($position.X -eq 0) -and ($position.Y -eq 0)
}

function Get-HostLog {
    (Get-ChildItem $logs -Filter 'host-*.log' -ErrorAction SilentlyContinue | ForEach-Object { Get-Content $_.FullName -Raw }) -join "`n"
}

function Get-MatchCount([string] $Text, [string] $Pattern) { ([regex]::Matches($Text, $Pattern)).Count }

function Get-VirtualDesktopCount {
    $ids = (Get-ItemProperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\VirtualDesktops' -Name VirtualDesktopIDs -ErrorAction SilentlyContinue).VirtualDesktopIDs
    if ($ids) { $ids.Length / 16 } else { 1 }
}

function Write-Settings([string] $Json) {
    New-Item -ItemType Directory -Force -Path $dataFolder | Out-Null
    Set-Content -Path (Join-Path $dataFolder 'settings.json') -Value $Json -Encoding utf8
}

$msi = (Get-ChildItem 'C:\Test\msi' -Filter 'PCCompanion-*.msi' | Select-Object -First 1).FullName
$installFolder = Join-Path $env:LOCALAPPDATA 'Programs\PCCompanion'
$appExe = Join-Path $installFolder 'PCCompanion.exe'
$hostExe = Join-Path $installFolder 'PCCompanion.Host.exe'
$dataFolder = Join-Path $env:LOCALAPPDATA 'PCCompanion'
$logs = Join-Path $dataFolder 'Logs'
$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$screen = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
$menuX = $screen.Width - 300
$menuY = $screen.Height - 100
Write-Report "msi: $msi; user: $env:USERNAME; screen: $($screen.Width)x$($screen.Height); Windows $([Environment]::OSVersion.Version)"

# 1. Install for the current user.
$code = (Start-Process msiexec.exe -ArgumentList @('/i', "`"$msi`"", '/qn', 'ALLUSERS=2', 'MSIINSTALLPERUSER=1', '/l*v', "`"$results\install.log`"") -Wait -PassThru).ExitCode
Test-Check 'install for the current user' ($code -eq 0) "exit code $code"
Test-Check 'settings app and host installed' ((Test-Path $appExe) -and (Test-Path $hostExe))
Test-Check 'third-party notices installed' (Test-Path (Join-Path $installFolder 'ThirdPartyNotices.txt'))
$run = (Get-ItemProperty $runKey -Name PCCompanion -ErrorAction SilentlyContinue).PCCompanion
Test-Check 'startup entry in HKCU Run' ($run -eq "`"$hostExe`" --startup") "value: $run"

# 2. Started at sign-in while Start with Windows is off: the host exits and creates nothing.
$process = Start-Process $hostExe -ArgumentList '--startup' -PassThru
Test-Check 'host --startup exits when Start with Windows is off' ($process.WaitForExit(10000))
Test-Check 'host --startup creates no data folder' (-not (Test-Path $dataFolder))

# 3. The settings app starts the host and shows Home.
Start-Process $appExe
Test-Check 'settings app window opens' (Wait-Until { Get-AppWindow })
Test-Check 'settings app starts the host' (Wait-Until { (Get-Processes 'PCCompanion.Host').Count -eq 1 } 15)
Start-Sleep -Seconds 3
Save-Screenshot '01-home-english'
$text = Get-WindowText
Test-Check 'Home shows the feature cards' (($text -contains 'Home') -and ($text -contains 'Input & Triggers') -and ($text -contains 'Hot Corners')) ($text -join ' | ')

# 4. The General page.
Test-Check 'select General' (Select-NavigationItem 'General')
Start-Sleep -Seconds 2
Save-Screenshot '02-general-english'
$text = Get-WindowText
Test-Check 'General shows its settings' (($text -contains 'Language') -and ($text -contains 'App theme') -and ($text -contains 'Start with Windows')) ($text -join ' | ')
Test-Check 'General shows how long logs are kept' ($text -contains 'Logs are kept for 14 days.')
Test-Check 'General shows the version' (@($text | Where-Object { $_ -match '^Version \d+\.\d+\.\d+' }).Count -eq 1)
$adminToggle = Find-Toggle 'Always run as administrator'
Test-Check 'Always run as administrator is unavailable for a per-user install' (($null -ne $adminToggle) -and -not $adminToggle.Current.IsEnabled)
Test-Check 'General says why' ($text -contains 'Available when PC Companion is installed for all users.') ($text -join ' | ')

# 5. One settings app per user session.
Start-Process $appExe
Start-Sleep -Seconds 5
Test-Check 'a second launch keeps one settings app' ((Get-Processes 'PCCompanion').Count -eq 1) "processes: $((Get-Processes 'PCCompanion').Count)"

# 6. The tray icon's menu, and selecting the icon.
Test-Check 'tray menu requested' (Send-TrayEvent $WM_CONTEXTMENU $menuX $menuY)
Start-Sleep -Seconds 2
Save-Screenshot '03-tray-menu-english'
[System.Windows.Forms.SendKeys]::SendWait('{ESC}')
Start-Sleep -Seconds 1
Test-Check 'settings app closes' (Close-SettingsApp)
Test-Check 'host keeps running after the settings app closes' ((Get-Processes 'PCCompanion.Host').Count -eq 1)
Test-Check 'tray icon selected' (Send-TrayEvent $NIN_SELECT)
Test-Check 'selecting the tray icon opens the settings app' (Wait-Until { Get-AppWindow } 20)
Start-Process $hostExe -Wait
Test-Check 'a second host launch keeps one host' ((Get-Processes 'PCCompanion.Host').Count -eq 1)
Test-Check 'the settings app is open before Exit' ((Get-Processes 'PCCompanion').Count -eq 1)
Test-Check 'tray menu requested for Exit' (Send-TrayEvent $WM_CONTEXTMENU $menuX $menuY)
Start-Sleep -Seconds 2
# The menu opens with nothing selected: Settings, then Exit.
[System.Windows.Forms.SendKeys]::SendWait('{DOWN}{DOWN}{ENTER}')
Test-Check 'Exit in the tray menu stops the host' (Wait-Until { (Get-Processes 'PCCompanion.Host').Count -eq 0 } 15)
Test-Check 'Exit in the tray menu closes the settings app' (Wait-Until { (Get-Processes 'PCCompanion').Count -eq 0 } 15)
Start-Process $hostExe
[void] (Wait-Until { (Get-Processes 'PCCompanion.Host').Count -eq 1 } 10)

# 7. Arabic with the dark theme, as the General page saves them; the host follows the change.
[void] (Close-SettingsApp)
Write-Settings '{ "schemaVersion": 1, "general": { "language": "ar", "theme": "Dark", "startWithWindows": false } }'
Start-Sleep -Seconds 2
Start-Process $appExe
[void] (Wait-Until { Get-AppWindow })
Start-Sleep -Seconds 3
Save-Screenshot '04-home-arabic-dark'
$text = Get-WindowText
Test-Check 'Home in Arabic' ($text -contains 'الصفحة الرئيسية') ($text -join ' | ')
Test-Check 'select General in Arabic' (Select-NavigationItem 'عام')
Start-Sleep -Seconds 2
Save-Screenshot '05-general-arabic-dark'
Test-Check 'select Hot Corners in Arabic' (Select-NavigationItem 'الزوايا النشطة')
Start-Sleep -Seconds 2
Save-Screenshot '05b-hot-corners-arabic-dark'
Test-Check 'tray menu requested in Arabic' (Send-TrayEvent $WM_CONTEXTMENU $menuX $menuY)
Start-Sleep -Seconds 2
Save-Screenshot '06-tray-menu-arabic'
[System.Windows.Forms.SendKeys]::SendWait('{ESC}')
Start-Sleep -Seconds 1

# 8. Bangla with the light theme.
[void] (Close-SettingsApp)
Write-Settings '{ "schemaVersion": 1, "general": { "language": "bn", "theme": "Light", "startWithWindows": false } }'
Start-Sleep -Seconds 2
Start-Process $appExe
[void] (Wait-Until { Get-AppWindow })
Start-Sleep -Seconds 3
Test-Check 'select General in Bangla' (Select-NavigationItem 'সাধারণ')
Start-Sleep -Seconds 2
Save-Screenshot '07-general-bangla-light'

# 9. Hot Corners: the Home switch turns it on; a corner shows the desktop when the pointer reaches
#    it, not while a mouse button is held, and only with the key it requires.
[void] (Close-SettingsApp)
Write-Settings '{ "schemaVersion": 1, "general": { "theme": "Light" } }'
Start-Sleep -Seconds 2
Start-Process $appExe
[void] (Wait-Until { Get-AppWindow })
Start-Sleep -Seconds 3
Test-Check 'turn on Hot Corners on Home' (Set-Toggle 'Hot Corners' $true)
Start-Sleep -Seconds 2
$saved = (Get-Content (Join-Path $dataFolder 'settings.json') -Raw | ConvertFrom-Json).features.hotCorners
Test-Check 'the Home switch saves Hot Corners as on' ($saved.enabled -eq $true) "saved: $($saved | ConvertTo-Json -Compress)"
Test-Check 'select Hot Corners' (Select-NavigationItem 'Hot Corners')
Start-Sleep -Seconds 2
Save-Screenshot '09-hot-corners-english'
$text = Get-WindowText
Test-Check 'Hot Corners shows its settings' (($text -contains 'Top left') -and ($text -contains 'Activation delay') -and ($text -contains 'Permissions & data')) ($text -join ' | ')
$selected = Get-SelectedItemName 'Top left action'
Test-Check 'the top-left corner shows Task View when first turned on' ($selected -eq 'Task View') "selected: $selected"
[void] (Close-SettingsApp)

# The top-left corner shows the desktop; detailed logging is on for the host's Debug entries.
$detailedUntil = [DateTimeOffset]::Now.AddHours(1).ToString('o')
function Write-HotCornersSettings([bool] $Enabled, [string] $Key) {
    Write-Settings "{ ""schemaVersion"": 1, ""general"": { ""theme"": ""Light"" }, ""diagnostics"": { ""detailedLoggingUntil"": ""$detailedUntil"" }, ""features"": { ""hotCorners"": { ""enabled"": $($Enabled.ToString().ToLowerInvariant()), ""topLeft"": { ""action"": ""ShowDesktop"", ""key"": ""$Key"" } } } }"
    Start-Sleep -Seconds 3
}
$middleX = [int]($screen.Width / 2)
$middleY = [int]($screen.Height / 2)
$ran = 'The "?TopLeft"? corner ran "?ShowDesktop"?\.'
Write-HotCornersSettings $true 'None'
Start-Process $appExe
[void] (Wait-Until { Get-AppWindow })
Start-Sleep -Seconds 3
$appWindow = (Get-Processes 'PCCompanion' | Select-Object -First 1).MainWindowHandle
Test-Check 'the pointer can be moved in the Sandbox' (Move-Pointer 100 100) "at $([System.Windows.Forms.Cursor]::Position); keep the Sandbox window open and the mouse outside it"
[void] (Move-Pointer $middleX $middleY)
[void] (Move-IntoTopLeftCorner)
Test-Check 'the top-left corner shows the desktop' (Wait-Until { [Win32]::IsIconic($appWindow) } 5) (Get-PointerText)
Test-Check 'the host logs which corner ran which action' (Wait-Until { (Get-MatchCount (Get-HostLog) $ran) -eq 1 } 5) (Get-PointerText)
Save-Screenshot '10-hot-corners-show-desktop'

# A mouse button held as the pointer reaches the corner, as when dragging a window there.
[void] (Move-Pointer $middleX $middleY)
[void] [Win32]::LeftButton($true)
[void] (Move-IntoTopLeftCorner)
Start-Sleep -Seconds 1
[void] [Win32]::LeftButton($false)
Test-Check 'a held mouse button skips the corner' (Wait-Until { (Get-HostLog) -match "The `"?TopLeft`"? corner didn't run `"?ShowDesktop`"?: `"?MouseButtonHeld`"?\." } 5) (Get-PointerText)

# The corner requires Ctrl. Holding it must not change the shortcut: Ctrl + Windows + D adds a virtual desktop.
Write-HotCornersSettings $true 'Control'
[void] (Move-Pointer $middleX $middleY)
[void] (Move-IntoTopLeftCorner)
Test-Check 'without the required key the corner is skipped' (Wait-Until { (Get-HostLog) -match "The `"?TopLeft`"? corner didn't run `"?ShowDesktop`"?: `"?KeyNotHeld`"?\." } 5) (Get-PointerText)
$desktops = Get-VirtualDesktopCount
[void] (Move-Pointer $middleX $middleY)
[void] [Win32]::Key(0xA2, $false)
[void] (Move-IntoTopLeftCorner)
Start-Sleep -Seconds 1
[void] [Win32]::Key(0xA2, $true)
Test-Check 'with the required key the corner runs' (Wait-Until { (Get-MatchCount (Get-HostLog) $ran) -eq 2 } 5) (Get-PointerText)
Test-Check 'the held key adds no virtual desktop' ((Get-VirtualDesktopCount) -eq $desktops) "before: $desktops, after: $(Get-VirtualDesktopCount)"

# Turned off, Hot Corners stops reading the pointer.
Write-HotCornersSettings $false 'None'
Test-Check 'turning Hot Corners off stops it' ((Get-HostLog) -match 'Hot Corners stopped\.')
[void] (Move-Pointer $middleX $middleY)
[void] (Move-IntoTopLeftCorner)
Start-Sleep -Seconds 1
Test-Check 'a corner does nothing while Hot Corners is off' ((Get-MatchCount (Get-HostLog) $ran) -eq 2)
[void] (Move-Pointer $middleX $middleY)
Save-Screenshot '11-hot-corners-off'

# Run a program: the page shows the program, and Browse opens the Open dialog. Everything in the
# Sandbox runs as administrator, so the program must not start; the host logs why.
[void] (Close-SettingsApp)
$program = Join-Path $env:WINDIR 'System32\charmap.exe'
Test-Check 'the test program exists' (Test-Path $program) $program
Write-Settings "{ ""schemaVersion"": 1, ""general"": { ""theme"": ""Light"" }, ""diagnostics"": { ""detailedLoggingUntil"": ""$detailedUntil"" }, ""features"": { ""hotCorners"": { ""enabled"": true, ""topLeft"": { ""action"": ""RunProgram"", ""key"": ""None"", ""program"": ""$($program.Replace('\', '\\'))"", ""arguments"": ""/A"" } } } }"
Start-Sleep -Seconds 3
Start-Process $appExe
[void] (Wait-Until { Get-AppWindow })
Start-Sleep -Seconds 3
Test-Check 'select Hot Corners for Run a program' (Select-NavigationItem 'Hot Corners')
Start-Sleep -Seconds 2
$selected = Get-SelectedItemName 'Top left action'
Test-Check 'the top-left corner shows Run a program' ($selected -eq 'Run a program') "selected: $selected"
$shown = Get-BoxText 'Top left program'
Test-Check 'the program box shows the program' ($shown -eq $program) "shown: $shown"
Test-Check 'the arguments box shows the arguments' ((Get-BoxText 'Top left arguments') -eq '/A')
Save-Screenshot '12-hot-corners-run-program'
# A path that doesn't fit shows its end after an ellipsis; the tooltip and the box while editing show all of it.
$programBox = Find-AppElement 'Top left program'
if ($programBox) {
    $bounds = $programBox.Current.BoundingRectangle
    [void] (Move-Pointer ([int]($bounds.X + $bounds.Width / 2)) ([int]($bounds.Y + $bounds.Height / 2)))
}
Test-Check 'the program box shows the whole path in its tooltip' (Wait-Until { Find-AppText $program } 5)
Save-Screenshot '12b-hot-corners-program-tooltip'
if ($programBox) { $programBox.SetFocus() }
Start-Sleep -Seconds 1
Save-Screenshot '12c-hot-corners-program-editing'
[void] (Move-Pointer $middleX $middleY)
$browse = Find-AppElement 'Top left program: Browse'
if ($browse) { $browse.SetFocus(); Start-Sleep -Milliseconds 300; Send-Key 0x20 }
Test-Check 'Browse opens the Open dialog' (Wait-Until { Find-AppDialog 'Choose a program' } 10)
Save-Screenshot '13-hot-corners-open-dialog'
Send-Key 0x1B
Test-Check 'the Open dialog closes' (Wait-Until { -not (Find-AppDialog 'Choose a program') } 10)
Test-Check 'cancelling the Open dialog keeps the program' ((Get-BoxText 'Top left program') -eq $program)
$running = (Get-Processes 'charmap').Count
[void] (Move-Pointer $middleX $middleY)
[void] (Move-IntoTopLeftCorner)
Test-Check 'Run a program logs that it doesn''t run as administrator' (Wait-Until { (Get-HostLog) -match "Run a program doesn't run while PC Companion runs as administrator\." } 5) (Get-PointerText)
Test-Check 'no program starts while PC Companion runs as administrator' ((Get-Processes 'charmap').Count -eq $running) "charmap processes before: $running, after: $((Get-Processes 'charmap').Count)"
[void] (Move-Pointer $middleX $middleY)

# 10. Detailed logging: the General page switch turns it on for 24 hours; it turns itself off when the time is up.
[void] (Close-SettingsApp)
Write-Settings '{ "schemaVersion": 1, "general": { "theme": "Light" } }'
Start-Sleep -Seconds 2
Start-Process $appExe
[void] (Wait-Until { Get-AppWindow })
Start-Sleep -Seconds 3
[void] (Select-NavigationItem 'General')
Start-Sleep -Seconds 2
Test-Check 'turn on detailed logging' (Set-Toggle 'Detailed logging' $true)
Start-Sleep -Seconds 3
$saved = (Get-Content (Join-Path $dataFolder 'settings.json') -Raw | ConvertFrom-Json).diagnostics.detailedLoggingUntil
$hoursLeft = if ($saved) { ([DateTimeOffset]::Parse([string] $saved) - [DateTimeOffset]::Now).TotalHours } else { 0 }
Test-Check 'the switch saves an end time 24 hours ahead' (($hoursLeft -gt 23.9) -and ($hoursLeft -le 24)) "saved: $saved"
$text = Get-WindowText
Test-Check 'General shows when detailed logging turns off' (@($text | Where-Object { $_ -like 'On until *' }).Count -ge 1) ($text -join ' | ')
Save-Screenshot '08-general-detailed-logging'
Test-Check 'tray menu requested with detailed logging on' (Send-TrayEvent $WM_CONTEXTMENU $menuX $menuY)
Start-Sleep -Seconds 2
[System.Windows.Forms.SendKeys]::SendWait('{ESC}')
Start-Sleep -Seconds 1
[void] (Close-SettingsApp)
# As if the 24 hours were nearly over.
$soon = [DateTimeOffset]::Now.AddSeconds(20).ToString('o')
Write-Settings "{ ""schemaVersion"": 1, ""general"": { ""theme"": ""Light"" }, ""diagnostics"": { ""detailedLoggingUntil"": ""$soon"" } }"
Start-Sleep -Seconds 25
Test-Check 'tray menu requested after detailed logging ended' (Send-TrayEvent $WM_CONTEXTMENU $menuX $menuY)
Start-Sleep -Seconds 2
[System.Windows.Forms.SendKeys]::SendWait('{ESC}')
Start-Sleep -Seconds 1

# 11. Start with Windows on.
Write-Settings '{ "schemaVersion": 1, "general": { "language": "bn", "theme": "Light", "startWithWindows": true } }'
Start-Sleep -Seconds 2
[void] (Close-SettingsApp)
$process = Start-Process $hostExe -ArgumentList '--startup' -PassThru
Test-Check 'host --startup leaves a running host alone' ($process.WaitForExit(10000))
Start-Sleep -Seconds 3
Test-Check 'host --startup opens no settings window' ((Get-Processes 'PCCompanion').Count -eq 0)
Stop-All
$process = Start-Process $hostExe -ArgumentList '--startup' -PassThru
Start-Sleep -Seconds 5
Test-Check 'host --startup keeps running when Start with Windows is on' (-not $process.HasExited)

# 12. Logs.
Stop-All
Copy-Item -Path (Join-Path $logs '*') -Destination $results -ErrorAction SilentlyContinue
$logText = (Get-ChildItem $logs -File -ErrorAction SilentlyContinue | ForEach-Object { Get-Content $_.FullName -Raw }) -join "`n"
Test-Check 'settings app and host logs written' ((Test-Path (Join-Path $logs 'app-*.log')) -and (Test-Path (Join-Path $logs 'host-*.log')))
Test-Check 'logs have no errors' (-not ($logText -match '\[(ERR|FTL)\]'))
Test-Check 'logs don''t show the user name' (-not ($logText -match [regex]::Escape($env:USERNAME)))
Test-Check 'host loaded the changed settings' ($logText -match 'The settings changed and were loaded again')
Test-Check 'the settings app logged that Exit closed it' ($logText -match 'Exit was chosen in the tray menu, so the settings app closes')

$appLog = (Get-ChildItem $logs -Filter 'app-*.log' -ErrorAction SilentlyContinue | ForEach-Object { Get-Content $_.FullName -Raw }) -join "`n"
$hostLines = @(Get-ChildItem $logs -Filter 'host-*.log' -ErrorAction SilentlyContinue | ForEach-Object { Get-Content $_.FullName })
$turnedOff = @(0..($hostLines.Count - 1) | Where-Object { $hostLines[$_] -match 'Detailed logging is off\.' })
$debugLines = @(0..($hostLines.Count - 1) | Where-Object { $hostLines[$_] -match '\[DBG\]' })
Test-Check 'settings app turned detailed logging on' ($appLog -match 'Detailed logging is on until')
Test-Check 'host turned detailed logging on' (@($hostLines | Where-Object { $_ -match 'Detailed logging is on until' }).Count -ge 1)
Test-Check 'host wrote Debug entries while detailed logging was on' (@($hostLines | Where-Object { $_ -match '\[DBG\] PCCompanion\.Host\.TrayController: Opened the tray menu' }).Count -ge 1)
Test-Check 'detailed logging turned itself off in the host' ($turnedOff.Count -ge 1)
Test-Check 'host wrote no Debug entries after detailed logging turned off' (($turnedOff.Count -ge 1) -and (@($debugLines | Where-Object { $_ -gt $turnedOff[-1] }).Count -eq 0))

# 13. Uninstall.
$code = (Start-Process msiexec.exe -ArgumentList @('/x', "`"$msi`"", '/qn', '/l*v', "`"$results\uninstall.log`"") -Wait -PassThru).ExitCode
Test-Check 'uninstall' ($code -eq 0) "exit code $code"
Test-Check 'startup entry removed' (-not (Get-ItemProperty $runKey -Name PCCompanion -ErrorAction SilentlyContinue))
Test-Check 'install folder removed' (-not (Test-Path $appExe))

# 14. Always run as administrator, in an all-users install. UAC is off in the Sandbox, so every
#     program already runs as administrator: the hand-off to the task and the protected log
#     folder in use are checked by hand on a PC with UAC on.
Stop-All
$code = (Start-Process msiexec.exe -ArgumentList @('/i', "`"$msi`"", '/qn', 'ALLUSERS=1', '/l*v', "`"$results\install-all-users.log`"") -Wait -PassThru).ExitCode
Test-Check 'install for all users' ($code -eq 0) "exit code $code"
$allUsersFolder = Join-Path $env:ProgramW6432 'PCCompanion'
$protectedLogs = Join-Path $env:ProgramData 'PCCompanion\Logs'
$acl = Get-Acl $protectedLogs -ErrorAction SilentlyContinue
$writers = @($acl.Access | Where-Object { $_.AccessControlType -eq 'Allow' -and ($_.FileSystemRights -band [System.Security.AccessControl.FileSystemRights]::WriteData) } | ForEach-Object { $_.IdentityReference.Translate([System.Security.Principal.SecurityIdentifier]).Value })
Test-Check 'the protected logs folder is created' ($null -ne $acl)
Test-Check 'only SYSTEM and Administrators can change it' (($writers.Count -gt 0) -and -not ($writers | Where-Object { $_ -notin 'S-1-5-18', 'S-1-5-32-544' })) "writers: $($writers -join ', ')"
$sid = [System.Security.Principal.WindowsIdentity]::GetCurrent().User.Value
Write-Settings '{ "schemaVersion": 1, "general": { "theme": "Light" } }'
Start-Process (Join-Path $allUsersFolder 'PCCompanion.exe')
[void] (Wait-Until { Get-AppWindow })
Start-Sleep -Seconds 3
[void] (Select-NavigationItem 'General')
Start-Sleep -Seconds 2
$adminToggle = Find-Toggle 'Always run as administrator'
Test-Check 'Always run as administrator is available for an all-users install' (($null -ne $adminToggle) -and $adminToggle.Current.IsEnabled)
Test-Check 'turn on Always run as administrator' (Set-Toggle 'Always run as administrator' $true)
Test-Check 'turning it on creates the user''s two tasks' (Wait-Until { (Get-StartupTasks).Count -eq 2 } 30) "tasks: $((Get-StartupTasks) -join ', ')"
Save-Screenshot '14-general-run-as-administrator'
$saved = (Get-Content (Join-Path $dataFolder 'settings.json') -Raw | ConvertFrom-Json).general.alwaysRunAsAdministrator
Test-Check 'the setting is saved' ($saved -eq $true)
$signInTask = [xml] ((schtasks.exe /Query /TN "\PCCompanion\Host at sign-in $sid" /XML) -join "`n")
$exec = $signInTask.Task.Actions.Exec
$triggerUser = [string] $signInTask.Task.Triggers.LogonTrigger.UserId
$runLevel = [string] $signInTask.Task.Principals.Principal.RunLevel
# Task Scheduler may show the user as an account name rather than a SID.
$triggerSid = try { ([System.Security.Principal.NTAccount] $triggerUser).Translate([System.Security.Principal.SecurityIdentifier]).Value } catch { $triggerUser }
Test-Check 'the sign-in task starts the host from Program Files at sign-in with highest rights' (
    ($exec.Command -eq "`"$allUsersFolder\PCCompanion.Host.exe`"") -and ($exec.Arguments -eq '--startup') -and ($triggerSid -eq $sid) -and ($runLevel -eq 'HighestAvailable')) "$($exec.Command) $($exec.Arguments); user: $triggerUser; run level: $runLevel"
Test-Check 'the host restarts after the change' (Wait-Until { (Get-Processes 'PCCompanion.Host').Count -eq 1 } 20)
[void] (Close-SettingsApp)
Get-Processes 'PCCompanion.Host' | ForEach-Object { Stop-Process -Id $_.Id -Force }
[void] (Wait-Until { (Get-Processes 'PCCompanion.Host').Count -eq 0 } 10)
schtasks.exe /Run /TN "\PCCompanion\Host $sid" | Out-Null
Test-Check 'the on-request task starts the host' (Wait-Until { @(Get-Process -Name 'PCCompanion.Host' -ErrorAction SilentlyContinue | Where-Object { $_.Path -like "$allUsersFolder\*" }).Count -eq 1 } 20)
Start-Process (Join-Path $allUsersFolder 'PCCompanion.exe')
[void] (Wait-Until { Get-AppWindow })
Start-Sleep -Seconds 3
[void] (Select-NavigationItem 'General')
Start-Sleep -Seconds 2
# The switch stays disabled until a change, the host's restart included, has finished.
[void] (Wait-Until { (Find-Toggle 'Always run as administrator').Current.IsEnabled } 20)
Test-Check 'turn off Always run as administrator' (Set-Toggle 'Always run as administrator' $false)
Test-Check 'turning it off deletes the tasks' (Wait-Until { (Get-StartupTasks).Count -eq 0 } 30) "tasks: $((Get-StartupTasks) -join ', ')"
[void] (Wait-Until { (Find-Toggle 'Always run as administrator').Current.IsEnabled } 20)
Test-Check 'turn it on again' ((Set-Toggle 'Always run as administrator' $true) -and (Wait-Until { (Get-StartupTasks).Count -eq 2 } 30))
Stop-All
$allUsersLogs = New-Item -ItemType Directory -Force -Path (Join-Path $results 'logs-all-users')
Copy-Item -Path (Join-Path $logs '*') -Destination $allUsersLogs -ErrorAction SilentlyContinue
$logText = (Get-ChildItem $logs -File -ErrorAction SilentlyContinue | ForEach-Object { Get-Content $_.FullName -Raw }) -join "`n"
Test-Check 'Always run as administrator logs no errors' (-not ($logText -match '\[(ERR|FTL)\]'))
$code = (Start-Process msiexec.exe -ArgumentList @('/x', "`"$msi`"", '/qn', '/l*v', "`"$results\uninstall-all-users.log`"") -Wait -PassThru).ExitCode
Test-Check 'uninstall for all users' ($code -eq 0) "exit code $code"
Test-Check 'uninstalling deletes the tasks' ((Get-StartupTasks).Count -eq 0) "tasks: $((Get-StartupTasks) -join ', ')"
Test-Check 'the all-users folder is removed' (-not (Test-Path (Join-Path $allUsersFolder 'PCCompanion.Host.exe')))
Test-Check 'the empty protected logs folder is removed' (-not (Test-Path (Join-Path $env:ProgramData 'PCCompanion')))

Write-Report "finished $(Get-Date -Format o)"
Set-Content -Path (Join-Path $results 'done.txt') -Value 'done'
shutdown.exe /s /t 5
