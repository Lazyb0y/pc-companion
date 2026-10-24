# App Windows: the Home switch turns it on and its page saves its settings; the key above Tab
# switches between File Explorer windows by its position, also on the German layout after a
# switch in the same window, restores a minimized window, doesn't wait for a program that stopped
# responding, and does nothing once App Windows is off.
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
# Clicks the middle of a window, as a user brings it to the front.
function Select-Window([IntPtr] $Window) {
    $area = New-Object Win32+RECT
    [void] [Win32]::GetWindowRect($Window, [ref] $area)
    [void] (Move-Pointer ([int](($area.Left + $area.Right) / 2)) ([int](($area.Top + $area.Bottom) / 2)))
    [void] [Win32]::LeftButton($true)
    [void] [Win32]::LeftButton($false)
    Start-Sleep -Seconds 1
}
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
Select-Window $explorers[0]
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

# With Include minimized windows on, a minimized window comes back restored and in front.
Write-AppWindowsSettings '{ "enabled": true, "includeMinimized": true }'
$minimized = (Get-ExplorerWindows)[-1]
[void] [Win32]::ShowWindow($minimized, 7) # SW_SHOWMINNOACTIVE
Start-Sleep -Seconds 1
$before = Get-FrontTitle
Send-ScanKeys @($LeftAlt) $KeyAboveTab
Test-Check 'a minimized File Explorer window comes back restored and in front' (([Win32]::GetForegroundWindow() -eq $minimized) -and (-not [Win32]::IsIconic($minimized))) "front: $before -> $(Get-FrontTitle); minimized: $([Win32]::IsIconic($minimized))"

# A program that stopped responding doesn't hold the host: App Windows asks Windows to send its
# window back or restore it, and Windows passes that on to the program for when it responds again.
# The test program has two windows, and stops responding for the milliseconds that a posted
# WM_APP + 1 gives. A press is done when the host logs it; a host that waits for the program logs
# it only once the program responds. Asking the host itself wouldn't tell: a thread that waits for
# another program's window still answers the messages sent to its own.
$frozenFolder = Join-Path $env:TEMP 'PCCompanionFrozenApp'
New-Item -ItemType Directory -Force -Path $frozenFolder | Out-Null
$frozenExe = Join-Path $frozenFolder 'frozen-app.exe'
Add-Type -OutputAssembly $frozenExe -OutputType WindowsApplication -ReferencedAssemblies System.Windows.Forms, System.Drawing -TypeDefinition @'
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
public static class FrozenApp {
    [DllImport("user32.dll")] private static extern void DisableProcessWindowsGhosting();
    [DllImport("user32.dll")] private static extern bool SetProcessDPIAware();

    private sealed class Window : Form {
        protected override void WndProc(ref Message message) {
            if (message.Msg == 0x8001) { Thread.Sleep((int)message.WParam); return; }
            base.WndProc(ref message);
        }
    }

    [System.STAThread]
    public static void Main() {
        // Otherwise Windows puts a window of its own in place of one that doesn't respond for 5 seconds.
        DisableProcessWindowsGhosting();
        SetProcessDPIAware();
        var second = new Window { Text = "PCCompanionFrozenApp-2", StartPosition = FormStartPosition.Manual, Bounds = new Rectangle(440, 40, 360, 240) };
        var first = new Window { Text = "PCCompanionFrozenApp-1", StartPosition = FormStartPosition.Manual, Bounds = new Rectangle(40, 40, 360, 240) };
        second.Show();
        Application.Run(first);
    }
}
'@
# PowerShell would pass $null to FindWindow as an empty class name, which no window has.
function Find-FrozenWindow([int] $Number) { [Win32]::FindWindow([NullString]::Value, "PCCompanionFrozenApp-$Number") }
function Stop-Responding([IntPtr] $Window, [int] $Milliseconds) {
    [void] [Win32]::PostMessage($Window, 0x8001, [UIntPtr]::new([uint32] $Milliseconds), [IntPtr]::Zero) # WM_APP + 1
    Start-Sleep -Milliseconds 300
}
# Presses the shortcut and returns whether the host logged the switch within 2 seconds.
function Test-SwitchLogged([int[]] $Modifiers, [string] $Direction) {
    $pattern = "App Windows went `"?$Direction`"?; the app has 2 windows"
    $count = Get-MatchCount (Get-HostLog) $pattern
    Send-ScanKeys $Modifiers $KeyAboveTab
    return (Wait-Until { (Get-MatchCount (Get-HostLog) $pattern) -gt $count } 2)
}
$frozenProcess = Start-Process $frozenExe -PassThru
[void] (Wait-Until { ((Find-FrozenWindow 1) -ne [IntPtr]::Zero) -and ((Find-FrozenWindow 2) -ne [IntPtr]::Zero) } 30)
Start-Sleep -Seconds 1
$frozen1 = Find-FrozenWindow 1
$frozen2 = Find-FrozenWindow 2
Test-Check 'the test program shows its two windows' (($frozen1 -ne [IntPtr]::Zero) -and ($frozen2 -ne [IntPtr]::Zero)) "windows: $frozen1, $frozen2"
Select-Window $frozen1
Test-Check "the test program's first window is in front" ([Win32]::GetForegroundWindow() -eq $frozen1) "front: $([Win32]::WindowTitle([Win32]::GetForegroundWindow()))"
Stop-Responding $frozen1 8000
Test-Check 'the test program stopped responding' (-not [Win32]::Responds($frozen1, 200))
Test-Check "going back from a window whose program doesn't respond doesn't wait for it" (Test-SwitchLogged @($LeftAlt, $LeftShift) 'Back')
[void] (Wait-Until { [Win32]::Responds($frozen1, 500) } 20)
Start-Sleep -Seconds 1
Test-Check "once the program responds, its second window is in front and the first is behind File Explorer's" (([Win32]::GetForegroundWindow() -eq $frozen2) -and [Win32]::IsInFrontOf((Get-ExplorerWindows)[0], $frozen1)) "front: $([Win32]::WindowTitle([Win32]::GetForegroundWindow()))"
[void] [Win32]::ShowWindow($frozen1, 7) # SW_SHOWMINNOACTIVE
Start-Sleep -Seconds 1
Stop-Responding $frozen2 8000
Test-Check "restoring a minimized window whose program doesn't respond doesn't wait for it" (Test-SwitchLogged @($LeftAlt) 'Forward')
[void] (Wait-Until { [Win32]::Responds($frozen2, 500) } 20)
Start-Sleep -Seconds 1
Test-Check 'once the program responds, its minimized window is restored and in front' (([Win32]::GetForegroundWindow() -eq $frozen1) -and (-not [Win32]::IsIconic($frozen1))) "front: $([Win32]::WindowTitle([Win32]::GetForegroundWindow())); minimized: $([Win32]::IsIconic($frozen1))"
Stop-Process -Id $frozenProcess.Id -Force -ErrorAction SilentlyContinue
[void] (Wait-Until { (Find-FrozenWindow 1) -eq [IntPtr]::Zero } 10)
Remove-Item $frozenFolder -Recurse -Force -ErrorAction SilentlyContinue
Select-Window (Get-ExplorerWindows)[0]

Write-AppWindowsSettings '{ "enabled": false }'
$before = Get-FrontTitle
Send-ScanKeys @($LeftAlt) $KeyAboveTab
Test-Check 'turned off, the key above Tab switches no window' ((Get-FrontTitle) -eq $before) "front: $before -> $(Get-FrontTitle)"
foreach ($window in Get-ExplorerWindows) { [void] [Win32]::PostMessage($window, 0x0010, [UIntPtr]::Zero, [IntPtr]::Zero) } # WM_CLOSE
Start-Sleep -Seconds 2
