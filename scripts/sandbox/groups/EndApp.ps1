# End App: the Home switch turns it on and its page saves the shortcut. Ctrl + Alt + Esc covers the
# screen with an invisible window that takes the clicks and keys: it frames the window in front, the
# window under the pointer and the one Tab reaches, with the crosshair or the Unavailable pointer, and
# Esc, a right-click, the shortcut again, losing the front and 30 seconds without input stop it; End an
# app in the PC Companion icon's menu starts it too. A responding test program is asked about with
# Cancel as the default and isn't ended; a test program that stopped responding is chosen through
# Windows' stand-in and ended with its two helpers of the same program, while its helper of another
# program keeps running; the question changes when a program responds again and closes when it exits;
# a console test program ends with its window; Settings, in Windows' shared frame, is named; and File
# Explorer's taskbar and folder window can't be ended. The checks end only test programs they start:
# they confirm End app only in a question that names one, and compare the running processes before
# and after each ending.
[void] (Close-SettingsApp)

# Detailed logging is on for the host's Debug entries.
$detailedUntil = [DateTimeOffset]::Now.AddHours(1).ToString('o')
function Write-EndSettings([string] $Section) {
    Write-Settings "{ ""schemaVersion"": 1, ""general"": { ""theme"": ""Light"" }, ""diagnostics"": { ""detailedLoggingUntil"": ""$detailedUntil"" }, ""features"": { ""endApp"": $Section } }"
    Start-Sleep -Seconds 3
}
function Start-Host {
    if ((Get-Processes 'PCCompanion.Host').Count -eq 0) {
        Start-Process $hostExe
        [void] (Wait-Until { [Win32]::FindWindow('PCCompanion.Host', $null) -ne [IntPtr]::Zero } 30)
    }
    [void] (Wait-Until { [Win32]::FindWindow('PCCompanion.EndApp', $null) -ne [IntPtr]::Zero } 20)
    Start-Sleep -Seconds 1
}
function Stop-Host {
    [void] [Win32]::PostMessage([Win32]::FindWindow('PCCompanion.Host', $null), 0x0010, [UIntPtr]::Zero, [IntPtr]::Zero)
    [void] (Wait-Until { (Get-Processes 'PCCompanion.Host').Count -eq 0 } 15)
}

# The test program, run as copies with other names, which are other programs: a window titled after
# its first argument at the place in its second, which logs the clicks and keys it gets, other than
# Ctrl and Alt, which reach it as a shortcut is pressed. "helpers" starts two windowless copies of
# itself and one of other-helper.exe, which wait for the group to end. A posted WM_APP + 3 makes it
# stop responding for the milliseconds in wParam; Windows then shows its stand-in.
$endFolder = Join-Path $env:TEMP 'PCCompanionEndApp'
New-Item -ItemType Directory -Force -Path $endFolder | Out-Null
$endExe = Join-Path $endFolder 'end-app.exe'
Add-Type -OutputAssembly $endExe -OutputType WindowsApplication -ReferencedAssemblies System.Windows.Forms, System.Drawing -TypeDefinition @'
using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
public static class EndTestApp {
    [DllImport("user32.dll")] private static extern bool SetProcessDPIAware();

    private sealed class Window : Form {
        protected override void WndProc(ref Message message) {
            if (message.Msg == 0x8003) { Thread.Sleep((int)message.WParam); return; }
            base.WndProc(ref message);
        }
    }

    [STAThread]
    public static void Main(string[] args) {
        if (args.Length > 0 && args[0] == "--helper") {
            using (var quit = new EventWaitHandle(false, EventResetMode.ManualReset, @"Local\PCCompanionEndAppChecksQuit")) { quit.WaitOne(TimeSpan.FromMinutes(15)); }
            return;
        }

        SetProcessDPIAware();
        var name = args[0];
        var place = args[1].Split(',');
        var folder = Path.GetDirectoryName(Application.ExecutablePath);
        var log = Path.Combine(folder, name + ".log");
        if (args.Contains("helpers")) {
            for (var i = 0; i < 2; i++) { Process.Start(new ProcessStartInfo(Application.ExecutablePath, "--helper") { UseShellExecute = false }); }
            Process.Start(new ProcessStartInfo(Path.Combine(folder, "other-helper.exe"), "--helper") { UseShellExecute = false });
        }

        var window = new Window { Text = name, StartPosition = FormStartPosition.Manual, Bounds = new Rectangle(int.Parse(place[0]), int.Parse(place[1]), 420, 300), KeyPreview = true };
        window.MouseDown += delegate { File.AppendAllText(log, "click\r\n"); };
        window.KeyDown += (sender, e) => { if (e.KeyCode != Keys.ControlKey && e.KeyCode != Keys.Menu) { File.AppendAllText(log, "key " + e.KeyCode + "\r\n"); } };
        Application.Run(window);
    }
}
'@
Copy-Item $endExe (Join-Path $endFolder 'other-helper.exe')
$consoleExe = Join-Path $endFolder 'end-console.exe'
Add-Type -OutputAssembly $consoleExe -OutputType ConsoleApplication -TypeDefinition @'
using System;
using System.Threading;
public static class EndTestConsole {
    public static void Main() {
        Console.Title = "end-console";
        Console.WriteLine("End App checks");
        using (var quit = new EventWaitHandle(false, EventResetMode.ManualReset, @"Local\PCCompanionEndAppChecksQuit")) { quit.WaitOne(TimeSpan.FromMinutes(15)); }
    }
}
'@
Add-Type -ReferencedAssemblies System.Drawing -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class EndChecks {
    [StructLayout(LayoutKind.Sequential)] public struct Box { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct Point { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct CursorInfo { public int Size; public int Flags; public IntPtr Cursor; public Point Place; }
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr window, int attribute, out Box value, int size);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr window, out Box box);
    [DllImport("user32.dll")] private static extern bool GetCursorInfo(ref CursorInfo info);
    [DllImport("user32.dll")] private static extern IntPtr LoadCursor(IntPtr instance, IntPtr name);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
    [DllImport("user32.dll")] private static extern void mouse_event(uint flags, uint dx, uint dy, uint data, UIntPtr extraInfo);
    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr window);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr window, IntPtr dc);
    [DllImport("gdi32.dll")] private static extern uint GetPixel(IntPtr dc, int x, int y);

    // The part of the window that shows, without its invisible borders.
    public static Box Visible(IntPtr window) {
        Box box;
        return DwmGetWindowAttribute(window, 9, out box, Marshal.SizeOf(typeof(Box))) == 0 ? box : Rect(window);
    }

    public static Box Rect(IntPtr window) { Box box; GetWindowRect(window, out box); return box; }

    public static uint ProcessOf(IntPtr window) { uint id; GetWindowThreadProcessId(window, out id); return id; }

    public static string Pointer() {
        var info = new CursorInfo { Size = Marshal.SizeOf(typeof(CursorInfo)) };
        if (!GetCursorInfo(ref info)) { return "unknown"; }
        if (info.Cursor == LoadCursor(IntPtr.Zero, new IntPtr(32515))) { return "cross"; }
        if (info.Cursor == LoadCursor(IntPtr.Zero, new IntPtr(32648))) { return "unavailable"; }
        return "other";
    }

    public static void RightClick() { mouse_event(0x0008, 0, 0, 0, UIntPtr.Zero); mouse_event(0x0010, 0, 0, 0, UIntPtr.Zero); }

    // The screen's color at (x, y), as 0xBBGGRR.
    public static uint PixelAt(int x, int y) {
        var dc = GetDC(IntPtr.Zero);
        try { return GetPixel(dc, x, y); } finally { ReleaseDC(IntPtr.Zero, dc); }
    }
}
'@

$coveringClass = 'PCCompanion.EndApp.Covering'
$LeftControl = 0x1D; $LeftAlt = 0x38; $Escape = 0x01
function Send-EndApp {
    foreach ($key in @($LeftControl, $LeftAlt, $Escape)) { [void] [Win32]::ScanKey($key, $false, $false) }
    foreach ($key in @($Escape, $LeftAlt, $LeftControl)) { [void] [Win32]::ScanKey($key, $true, $false) }
    Start-Sleep -Milliseconds 800
}
# The covering window has a title, so it's found by its class alone ([NullString]: PowerShell passes
# `$null as an empty title).
function Find-Covering { [Win32]::FindWindow($coveringClass, [NullString]::Value) }
function Find-FrameTop { [Win32]::FindWindow('PCCompanion.EndApp.Frame.0', [NullString]::Value) }
function Test-Covering { $window = Find-Covering; ($window -ne [IntPtr]::Zero) -and [Win32]::IsWindowVisible($window) }
function Test-CoveringInFront { [Win32]::GetForegroundWindow() -eq (Find-Covering) }
function Test-Logged([string] $Pattern, [int] $Seconds = 5) { Wait-Until { (Get-HostLog) -match $Pattern } $Seconds }
function Get-Count([string] $Pattern) { Get-MatchCount (Get-HostLog) $Pattern }

# Whether the frame's top side lies right above the window's visible frame, and its color.
function Get-FrameText([IntPtr] $Window) {
    $top = Find-FrameTop
    if ($top -eq [IntPtr]::Zero -or -not [Win32]::IsWindowVisible($top)) { return 'no frame' }
    $side = [EndChecks]::Rect($top); $box = [EndChecks]::Visible($Window)
    "frame $($side.Left),$($side.Top)-$($side.Right),$($side.Bottom); window $($box.Left),$($box.Top)-$($box.Right),$($box.Bottom)"
}
function Test-Framed([IntPtr] $Window) {
    $top = Find-FrameTop
    if ($top -eq [IntPtr]::Zero -or -not [Win32]::IsWindowVisible($top)) { return $false }
    $side = [EndChecks]::Rect($top); $box = [EndChecks]::Visible($Window)
    $thickness = $side.Bottom - $side.Top
    ($thickness -gt 0) -and ($side.Bottom -eq $box.Top) -and ($side.Left -eq $box.Left - $thickness) -and ($side.Right -eq $box.Right + $thickness)
}
function Get-FrameColor {
    $side = [EndChecks]::Rect((Find-FrameTop))
    $color = [EndChecks]::PixelAt([int] (($side.Left + $side.Right) / 2), [int] (($side.Top + $side.Bottom) / 2))
    $red = $color -band 0xFF; $green = ($color -shr 8) -band 0xFF; $blue = ($color -shr 16) -band 0xFF
    if ($red -gt 200 -and $green -lt 60 -and $blue -lt 80) { 'red' } elseif ([Math]::Abs($red - $green) -lt 12 -and [Math]::Abs($green - $blue) -lt 12 -and $red -gt 100 -and $red -lt 180) { 'gray' } else { "other $red,$green,$blue" }
}
function Get-Center([IntPtr] $Window) { $box = [EndChecks]::Visible($Window); @([int] (($box.Left + $box.Right) / 2), [int] (($box.Top + $box.Bottom) / 2)) }
# From beside the target, so the pointer moves even when it already rests there.
function Move-To([IntPtr] $Window) { $center = Get-Center $Window; [void] (Move-Pointer ($center[0] + 40) ($center[1] + 40)); [void] (Move-Pointer $center[0] $center[1]) }
function Select-Window([IntPtr] $Window) {
    for ($attempt = 1; $attempt -le 5; $attempt++) {
        [void] [Win32]::Key(0xE8, $false)
        [void] [Win32]::Key(0xE8, $true)
        [void] [Win32]::SetForegroundWindow($Window)
        Start-Sleep -Milliseconds 700
        if ([Win32]::GetForegroundWindow() -eq $Window) { return $true }
    }
    Write-Report "[NOTE] $([Win32]::WindowTitle($Window)) didn't come to the front; $([Win32]::WindowClass([Win32]::GetForegroundWindow())) is."
    return $false
}
function Get-LogLines([string] $Name) { $file = Join-Path $endFolder "$Name.log"; if (Test-Path $file) { @(Get-Content $file).Count } else { 0 } }

# The processes this group started, and the guard that checks that nothing else ended.
$script:ours = New-Object 'System.Collections.Generic.HashSet[uint32]'
$script:ourNames = @()
$script:guardFailed = $false
$keyProcesses = @('explorer.exe', 'dwm.exe', 'csrss.exe', 'winlogon.exe', 'wininit.exe', 'services.exe', 'lsass.exe', 'smss.exe', 'sihost.exe', 'ctfmon.exe', 'fontdrvhost.exe',
    'StartMenuExperienceHost.exe', 'ShellExperienceHost.exe', 'SearchHost.exe', 'TextInputHost.exe', 'ApplicationFrameHost.exe', 'SystemSettings.exe', 'powershell.exe', 'PCCompanion.Host.exe')
function Get-ProcessSnapshot {
    $withWindows = @{}
    foreach ($window in [Win32]::TopLevelWindows()) { if ([Win32]::IsWindowVisible($window)) { $withWindows[[EndChecks]::ProcessOf($window)] = $true } }
    @(Get-CimInstance Win32_Process | ForEach-Object {
        [pscustomobject] @{ Id = [uint32] $_.ProcessId; Parent = [uint32] $_.ParentProcessId; Name = $_.Name; Created = $_.CreationDate; Window = $withWindows.ContainsKey([uint32] $_.ProcessId) }
    })
}
function Get-OursAndTheirs($Snapshot) {
    $byId = @{}
    foreach ($process in $Snapshot) { $byId[$process.Id] = $process }
    $set = New-Object 'System.Collections.Generic.HashSet[uint32]'
    foreach ($process in $Snapshot) { if ($script:ours.Contains($process.Id)) { [void] $set.Add($process.Id) } }
    do {
        $added = $false
        foreach ($process in $Snapshot) {
            $parent = $byId[$process.Parent]
            if (-not $set.Contains($process.Id) -and $set.Contains($process.Parent) -and $parent -and $process.Created -ge $parent.Created) { $added = $set.Add($process.Id) -or $added }
        }
    } while ($added)
    return ,$set
}
function Test-OnlyOursEnded($Before, $After, [string] $Step) {
    $ours = Get-OursAndTheirs $Before
    $still = @{}
    foreach ($process in $After) { $still["$($process.Id)|$($process.Created)"] = $true }
    $gone = @($Before | Where-Object { -not $still.ContainsKey("$($_.Id)|$($_.Created)") })
    $unexpected = @($gone | Where-Object { -not $ours.Contains($_.Id) -and ($_.Window -or $keyProcesses -contains $_.Name) })
    foreach ($process in @($gone | Where-Object { -not $ours.Contains($_.Id) -and -not ($_.Window -or $keyProcesses -contains $_.Name) })) {
        Write-Report "[NOTE] ${Step}: a background process without windows exited by itself: $($process.Name)"
    }
    if ($unexpected.Count -gt 0) { $script:guardFailed = $true }
    Test-Check "${Step}: only test programs this group started ended" ($unexpected.Count -eq 0) (($unexpected | ForEach-Object { "$($_.Name) $($_.Id)" }) -join ', ')
}
function Start-EndApp([string] $Name, [string] $Place, [string[]] $Flags = @()) {
    $exe = Join-Path $endFolder "$Name.exe"
    if (-not (Test-Path $exe)) { Copy-Item $endExe $exe }
    $process = Start-Process $exe -ArgumentList (@($Name, $Place) + $Flags) -PassThru
    [void] $script:ours.Add([uint32] $process.Id)
    $script:ourNames += $Name
    [void] (Wait-Until { [Win32]::FindWindow([NullString]::Value, $Name) -ne [IntPtr]::Zero } 30)
    Start-Sleep -Seconds 1
    return [Win32]::FindWindow([NullString]::Value, $Name)
}
function Get-StandIn([IntPtr] $Window) {
    $rect = [EndChecks]::Rect($Window)
    [Win32]::TopLevelWindows() | Where-Object {
        $box = [EndChecks]::Rect($_)
        [Win32]::WindowClass($_) -eq 'Ghost' -and [Win32]::IsWindowVisible($_) -and $box.Left -eq $rect.Left -and $box.Top -eq $rect.Top -and $box.Right -eq $rect.Right -and $box.Bottom -eq $rect.Bottom
    } | Select-Object -First 1
}

# The question, a task dialog of the host's titled End App, read with UI Automation.
$automation = [System.Windows.Automation.AutomationElement]
function Find-QuestionWindow {
    $window = [Win32]::FindWindow('#32770', 'End App')
    $hostIds = @(Get-Processes 'PCCompanion.Host' | ForEach-Object { [uint32] $_.Id })
    if ($window -ne [IntPtr]::Zero -and [Win32]::IsWindowVisible($window) -and $hostIds -contains [EndChecks]::ProcessOf($window)) { $window } else { [IntPtr]::Zero }
}
function Get-Question([int] $Seconds = 10) {
    $script:questionWindow = [IntPtr]::Zero
    if (-not (Wait-Until { $script:questionWindow = Find-QuestionWindow; $script:questionWindow -ne [IntPtr]::Zero } $Seconds)) { return $null }
    Start-Sleep -Milliseconds 300
    try { $automation::FromHandle($script:questionWindow) } catch { $null }
}
function Get-QuestionText($Question) {
    try { @($Question.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition) | ForEach-Object { $_.Current.Name } | Where-Object { $_ }) }
    catch { @() }
}
function Get-Focused { try { $automation::FocusedElement.Current.Name } catch { '' } }
function Test-QuestionOpen { (Find-QuestionWindow) -ne [IntPtr]::Zero }

# Answers with the keyboard, as a user does: Tab to the button, then Enter. Esc cancels.
function Select-Answer([string] $Button) {
    [void] [Win32]::SetForegroundWindow((Find-QuestionWindow))
    Start-Sleep -Milliseconds 300
    for ($step = 0; $step -lt 4 -and (Get-Focused) -ne $Button; $step++) { Send-Key 0x09; Start-Sleep -Milliseconds 300 }
    if ((Get-Focused) -ne $Button) { return $false }
    Send-Key 0x0D
    return Wait-Until { -not (Test-QuestionOpen) } 5
}
function Close-Question {
    [void] [Win32]::SetForegroundWindow((Find-QuestionWindow))
    Start-Sleep -Milliseconds 300
    Send-Key 0x1B
    [void] (Wait-Until { -not (Test-QuestionOpen) } 5)
}

# Confirms End app only in a question that names a test program this group started, and only while
# the guard has seen nothing else end; otherwise it cancels, and the check fails.
function Confirm-End($Question, [string] $Name) {
    $text = Get-QuestionText $Question
    if ($script:guardFailed -or $script:ourNames -notcontains $Name -or $text -notcontains "End ${Name}?") {
        Close-Question
        Test-Check "the question names the test program $Name, so End app may be chosen" $false ($text -join ' | ')
        return $false
    }
    return Select-Answer 'End app'
}

# Turned on from Home, which saves only that it's on, the page shows the default and saves the shortcut.
function Get-EndSettings { (Get-Content (Join-Path $dataFolder 'settings.json') -Raw | ConvertFrom-Json).features.endApp }
Write-Settings '{ "schemaVersion": 1, "general": { "theme": "Light" } }'
Start-Sleep -Seconds 2
Start-Process $appExe
[void] (Wait-Until { Get-AppWindow })
Start-Sleep -Seconds 3
Test-Check 'turn on End App on Home' (Set-Toggle 'End App' $true)
Start-Sleep -Seconds 2
$saved = Get-EndSettings
Test-Check 'the Home switch saves End App as on' ($saved.enabled -eq $true) "saved: $($saved | ConvertTo-Json -Compress)"
Test-Check 'select End App' (Select-NavigationItem 'End App')
Start-Sleep -Seconds 2
Save-Screenshot '15a-end-app-english'
$text = Get-WindowText
Test-Check 'End App shows its settings' (($text -contains 'Shortcut') -and ($text -contains 'Choosing an app') -and ($text -contains 'Permissions & data')) ($text -join ' | ')
Test-Check 'it says which apps it can''t end' (@($text | Where-Object { $_ -like 'Some apps can''t be ended here*' }).Count -eq 1) ($text -join ' | ')
$selected = Get-ListSelection 'Shortcut'
Test-Check 'the shortcut is Ctrl + Alt + Esc' ($selected -eq 'Ctrl + Alt + Esc') "selected: $selected"
Test-Check 'choose Windows logo key + Ctrl + Esc' (Select-ListItem 'Shortcut' 'Windows logo key + Ctrl + Esc')
Start-Sleep -Seconds 1
Test-Check 'the page saves the shortcut' ((Get-EndSettings).shortcut -eq 'WindowsControlEscape') "saved: $(Get-EndSettings | ConvertTo-Json -Compress)"
[void] (Close-SettingsApp)

Write-EndSettings '{ "enabled": true }'
Start-Host
$quit = New-Object System.Threading.EventWaitHandle($false, [System.Threading.EventResetMode]::ManualReset, 'Local\PCCompanionEndAppChecksQuit')
$first = Start-EndApp 'end-first' '40,40'
$second = Start-EndApp 'end-second' '560,40'
Test-Check 'the test programs show their windows' (($first -ne [IntPtr]::Zero) -and ($second -ne [IntPtr]::Zero))
$start = Get-ProcessSnapshot

# The shortcut covers the screen, takes the front and frames the window in front; the frame follows
# the pointer and Tab, with the crosshair over an app it can end and the Unavailable pointer elsewhere.
[void] (Select-Window $first)
$clicks = (Get-LogLines 'end-first') + (Get-LogLines 'end-second')
Send-EndApp
Test-Check 'Ctrl + Alt + Esc covers the screen and takes the front' ((Wait-Until { Test-Covering } 3) -and (Test-CoveringInFront)) "front: $([Win32]::WindowClass([Win32]::GetForegroundWindow()))"
Test-Check 'the host logs it' (Test-Logged 'End App started choosing an app, from the "?Shortcut"?')
Test-Check 'the frame starts on the window in front' (Wait-Until { Test-Framed $first } 3) (Get-FrameText $first)
Test-Check 'the frame is red for an app End App can end' ((Get-FrameColor) -eq 'red') (Get-FrameColor)
Move-To $second
Test-Check 'the frame follows the pointer' (Wait-Until { Test-Framed $second } 3) (Get-FrameText $second)
Test-Check 'with the crosshair' ((([EndChecks]::Pointer()) -eq 'cross')) ([EndChecks]::Pointer())
Save-Screenshot '15b-end-app-choosing'
[void] (Move-Pointer ([int] ($screen.Width / 2)) ($screen.Height - 15))
Test-Check 'the taskbar, File Explorer''s, gets the Unavailable pointer and a gray frame' ((Wait-Until { ([EndChecks]::Pointer()) -eq 'unavailable' } 3) -and ((Get-FrameColor) -eq 'gray')) "$([EndChecks]::Pointer()), $(Get-FrameColor)"
Send-Key 0x09
Start-Sleep -Milliseconds 500
$tabbed = [Win32]::TopLevelWindows() | Where-Object { [Win32]::IsWindowVisible($_) -and (Test-Framed $_) } | Select-Object -First 1
Write-Report "[NOTE] Tab framed $(if ($tabbed) { "a window of class $([Win32]::WindowClass($tabbed)), '$([Win32]::WindowTitle($tabbed))'" } else { 'no window found' }), in $(Get-FrameColor)."
Test-Check 'Tab frames an app''s window, in red, and not the taskbar' (($null -ne $tabbed) -and ([Win32]::WindowClass($tabbed) -ne 'Shell_TrayWnd') -and ((Get-FrameColor) -eq 'red'))
Send-Key 0x1B
Test-Check 'Esc stops choosing' ((Wait-Until { -not (Test-Covering) } 3) -and (Test-Logged 'stopped choosing an app: "?Escape"?'))
Test-Check 'and leaves no frame' (-not [Win32]::IsWindowVisible((Find-FrameTop)))
Test-Check 'the test windows got none of the clicks or keys' ((Get-LogLines 'end-first') + (Get-LogLines 'end-second') -eq $clicks) "lines: $((Get-LogLines 'end-first') + (Get-LogLines 'end-second')), before: $clicks"

# A right-click, the shortcut again, losing the front and 30 seconds without input stop it too.
Send-EndApp
Move-To $second
[EndChecks]::RightClick()
Test-Check 'a right-click stops choosing' ((Wait-Until { -not (Test-Covering) } 3) -and (Test-Logged 'stopped choosing an app: "?RightClick"?'))
Send-EndApp
Send-EndApp
Test-Check 'the shortcut again stops choosing' ((Wait-Until { -not (Test-Covering) } 3) -and (Test-Logged 'stopped choosing an app: "?Shortcut"?'))
Send-EndApp
[void] (Select-Window $first)
Test-Check 'losing the front stops choosing' ((Wait-Until { -not (Test-Covering) } 3) -and (Test-Logged 'stopped choosing an app: "?LostFront"?'))
Send-EndApp
Start-Sleep -Seconds 32
Test-Check '30 seconds without input stop choosing' ((-not (Test-Covering)) -and (Test-Logged 'stopped choosing an app: "?Idle"?'))
Test-Check 'the test windows still got no clicks' ((Get-LogLines 'end-first') + (Get-LogLines 'end-second') -eq $clicks)

# The PC Companion icon's menu starts it too.
Test-Check 'the PC Companion icon''s menu requested' (Send-TrayEvent $WM_CONTEXTMENU $menuX $menuY)
Test-Check 'choose End an app' (Select-TrayMenuItem "End an app$([char] 0x2026)")
Test-Check 'the menu item starts choosing' ((Wait-Until { Test-Covering } 3) -and (Test-Logged 'End App started choosing an app, from the "?Menu"?'))
Send-Key 0x1B
[void] (Wait-Until { -not (Test-Covering) } 3)

# A responding test program is asked about with Cancel as the default, and Enter ends nothing.
[void] (Select-Window $second)
Send-EndApp
Send-Key 0x0D
$question = Get-Question
Test-Check 'Enter asks about the framed app' ($null -ne $question)
if ($question) {
    $text = Get-QuestionText $question
    Save-Screenshot '15c-end-app-responding'
    Test-Check 'the question names it and says it responds' (($text -contains 'End end-second?') -and ($text -contains 'end-second is responding. To keep your work, close it as usual. Ending it loses anything not saved.')) ($text -join ' | ')
    Test-Check 'with the program and its one process' ($text -contains 'Program: end-second.exe. Ends 1 process.') ($text -join ' | ')
    Test-Check 'Cancel is the default' ((Get-Focused) -eq 'Cancel') "focused: $(Get-Focused)"
    Send-Key 0x0D
    Test-Check 'Enter cancels, and nothing ends' ((Wait-Until { -not (Test-QuestionOpen) } 5) -and [Win32]::IsWindow($second) -and (Test-Logged 'End App ended nothing'))
}

# A test program that stopped responding is chosen through Windows' stand-in, and ending it ends its
# two helpers of the same program; its helper of another program keeps running.
$frozen = Start-EndApp 'end-frozen' '40,420' @('helpers')
Start-Sleep -Seconds 2
$helpers = @(Get-CimInstance Win32_Process | Where-Object { $_.ParentProcessId -eq [EndChecks]::ProcessOf($frozen) })
Test-Check 'the frozen test program started its three helpers' ($helpers.Count -eq 3) (($helpers | ForEach-Object { $_.Name }) -join ', ')
[void] (Select-Window $frozen)
[void] [Win32]::PostMessage($frozen, 0x8003, [UIntPtr]::new([uint32] 600000), [IntPtr]::Zero)
$script:standIn = $null
[void] (Wait-Until { $script:standIn = Get-StandIn $frozen; $null -ne $script:standIn } 15)
if (-not $script:standIn) { Move-To $frozen; [void] (Wait-Until { $script:standIn = Get-StandIn $frozen; $null -ne $script:standIn } 10) }
# Windows doesn't always show its stand-in, nor at a set time (spike), so it's reported; the unit
# tests and the spike cover choosing a stand-in.
Write-Report "[NOTE] Windows $(if ($script:standIn) { 'showed' } else { 'didn''t show' }) its stand-in for the window that stopped responding."
# Simulated keys and clicks don't reach PC Companion while the stand-in is in front (spike), so
# another window comes to the front first.
[void] (Select-Window $first)
Send-EndApp
Move-To $frozen
Test-Check "the frame goes around the program that stopped responding$(if ($script:standIn) { ', and its stand-in' })" (Wait-Until { Test-Framed $frozen } 3) (Get-FrameText $frozen)
[void] [Win32]::LeftButton($true)
[void] [Win32]::LeftButton($false)
$question = Get-Question
Test-Check 'a click asks about the program that stopped responding' ($null -ne $question)
if ($question) {
    $text = Get-QuestionText $question
    Save-Screenshot '15d-end-app-not-responding'
    Test-Check 'the question says it isn''t responding' ($text -contains 'end-frozen isn''t responding. Anything not saved in it will be lost.') ($text -join ' | ')
    Test-Check 'with its three processes' ($text -contains 'Program: end-frozen.exe. Ends 3 processes.') ($text -join ' | ')
    Test-Check 'End app is the default' ((Get-Focused) -eq 'End app') "focused: $(Get-Focused)"
    $before = Get-ProcessSnapshot
    $sameHelpers = @($helpers | Where-Object { $_.Name -eq 'end-frozen.exe' } | ForEach-Object { [uint32] $_.ProcessId })
    $otherHelper = @($helpers | Where-Object { $_.Name -eq 'other-helper.exe' } | ForEach-Object { [uint32] $_.ProcessId })
    if (Confirm-End $question 'end-frozen') {
        Test-Check 'End app ends the program' (Wait-Until { -not [Win32]::IsWindow($frozen) } 10)
        if ($script:standIn) { Test-Check 'its stand-in goes too' (Wait-Until { -not [Win32]::IsWindowVisible($script:standIn) } 5) }
        Test-Check 'and its two helpers of the same program' (Wait-Until { @(Get-CimInstance Win32_Process | Where-Object { $sameHelpers -contains [uint32] $_.ProcessId }).Count -eq 0 } 10)
        Test-Check 'while its helper of another program keeps running' (@(Get-CimInstance Win32_Process | Where-Object { $otherHelper -contains [uint32] $_.ProcessId }).Count -eq 1)
        Test-Check 'the host logs it' (Test-Logged 'End App ended the app and 2 of its 2 helpers in')
        Start-Sleep -Seconds 1
        Test-OnlyOursEnded $before (Get-ProcessSnapshot) 'ending end-frozen'
    }
}

# The question follows the app: a program that responds again while asked changes the default to Cancel.
$later = Start-EndApp 'end-later' '560,420'
[void] (Select-Window $later)
[void] [Win32]::PostMessage($later, 0x8003, [UIntPtr]::new([uint32] 14000), [IntPtr]::Zero)
Start-Sleep -Seconds 6
[void] (Select-Window $first)
Send-EndApp
Move-To $later
[void] [Win32]::LeftButton($true)
[void] [Win32]::LeftButton($false)
$question = Get-Question
if ($question -and ((Get-QuestionText $question) -contains 'end-later isn''t responding. Anything not saved in it will be lost.')) {
    Test-Check 'once it responds again, the question says so' (Wait-Until { (Get-QuestionText (Get-Question 1)) -contains 'end-later is responding. To keep your work, close it as usual. Ending it loses anything not saved.' } 20)
    Test-Check 'with Cancel as the default' (Wait-Until { (Get-Focused) -eq 'Cancel' } 3) "focused: $(Get-Focused)"
    Test-Check 'the host logs it' (Test-Logged 'question changed: the app''s not responding False')
    Close-Question
} else {
    Write-Report "[NOTE] end-later wasn't asked about while it didn't respond: $(if ($question) { (Get-QuestionText $question) -join ' | ' } else { 'no question' })"
    if ($question) { Close-Question }
}
Test-Check 'end-later still runs' ([Win32]::IsWindow($later))

# A program that exits by itself while asked closes the question.
$exits = Start-EndApp 'end-exits' '300,250'
[void] (Select-Window $exits)
Send-EndApp
Send-Key 0x0D
$question = Get-Question
if ($question) {
    [void] [Win32]::PostMessage($exits, 0x0010, [UIntPtr]::Zero, [IntPtr]::Zero)
    Test-Check 'a program that exits while asked closes the question' (Wait-Until { -not (Test-QuestionOpen) } 5)
    Test-Check 'the host logs it' (Test-Logged 'The app End App asked about exited by itself')
}

# A console test program ends with its console window.
$consoleProcess = Start-Process $consoleExe -PassThru
[void] $script:ours.Add([uint32] $consoleProcess.Id)
$script:ourNames += 'end-console'
$script:console = [IntPtr]::Zero
[void] (Wait-Until { $script:console = [Win32]::FindWindow([NullString]::Value, 'end-console'); $script:console -ne [IntPtr]::Zero } 15)
Test-Check 'the console test program shows its window' ($script:console -ne [IntPtr]::Zero) "class: $([Win32]::WindowClass($script:console))"
if ($script:console -ne [IntPtr]::Zero) {
    [void] (Select-Window $script:console)
    Send-EndApp
    Send-Key 0x0D
    $question = Get-Question
    Test-Check 'it''s asked about by its program''s name' ($question -and ((Get-QuestionText $question) -contains 'End end-console?')) $(if ($question) { (Get-QuestionText $question) -join ' | ' })
    if ($question) {
        $before = Get-ProcessSnapshot
        if (Confirm-End $question 'end-console') {
            Test-Check 'End app ends the console program and closes its window' (Wait-Until { $consoleProcess.HasExited -and -not [Win32]::IsWindow($script:console) } 10)
            Start-Sleep -Seconds 1
            Test-OnlyOursEnded $before (Get-ProcessSnapshot) 'ending end-console'
        }
    }
}

# Settings, in Windows' shared frame, is named after the app inside it; the check cancels.
Start-Process 'ms-settings:display'
$script:settingsWindow = [IntPtr]::Zero
[void] (Wait-Until { $script:settingsWindow = [Win32]::TopLevelWindows() | Where-Object { [Win32]::WindowClass($_) -eq 'ApplicationFrameWindow' -and [Win32]::WindowTitle($_) -eq 'Settings' } | Select-Object -First 1; $null -ne $script:settingsWindow } 30)
Start-Sleep -Seconds 3
if ($script:settingsWindow) {
    [void] (Select-Window $script:settingsWindow)
    Send-EndApp
    Send-Key 0x0D
    $question = Get-Question
    $text = if ($question) { Get-QuestionText $question } else { @() }
    Test-Check 'Settings, in the shared frame, is asked about by its name' ($text -contains 'End Settings?') ($text -join ' | ')
    if ($question) { Close-Question }
}

# File Explorer's taskbar and folder window can't be ended: the question says why and offers Task Manager.
Start-Process explorer.exe 'C:\Windows'
$script:explorer = [IntPtr]::Zero
[void] (Wait-Until { $script:explorer = [Win32]::TopLevelWindows() | Where-Object { [Win32]::WindowClass($_) -eq 'CabinetWClass' } | Select-Object -First 1; $null -ne $script:explorer } 20)
$before = Get-ProcessSnapshot
foreach ($place in @(@{ Name = 'the taskbar'; Window = $null }, @{ Name = 'a folder window'; Window = $script:explorer })) {
    if ($place.Window) { [void] (Select-Window $place.Window) } else { [void] (Select-Window $first) }
    Send-EndApp
    if ($place.Window) { Move-To $place.Window } else { [void] (Move-Pointer ([int] ($screen.Width / 2)) ($screen.Height - 15)) }
    [void] [Win32]::LeftButton($true)
    [void] [Win32]::LeftButton($false)
    $question = Get-Question
    $text = if ($question) { Get-QuestionText $question } else { @() }
    Test-Check "File Explorer's $($place.Name) can't be ended, and the question says why" ($text -contains 'File Explorer also runs the desktop and the taskbar. To restart it, select Windows Explorer in Task Manager and choose Restart.') ($text -join ' | ')
    Test-Check 'it offers Task Manager, with Close as the default' (($text -contains 'Open Task Manager') -and ((Get-Focused) -eq 'Close')) "focused: $(Get-Focused)"
    if ($place.Window) { Save-Screenshot '15e-end-app-file-explorer' }
    if ($question) { Close-Question }
}
Test-Check 'the host logs why' (Test-Logged 'can''t end the chosen app: "?FileExplorer"?')
Test-OnlyOursEnded $before (Get-ProcessSnapshot) 'File Explorer'

# Apps running as administrator: in Windows Sandbox, a host labeled Medium integrity still gets the
# right to end them, as the spike showed, so the explanation is tested by hand with UAC on.
Write-Report '[NOTE] Apps running as administrator are tested by hand: in Windows Sandbox, a host without admin rights still gets the right to end them.'

Test-OnlyOursEnded $start (Get-ProcessSnapshot) 'the whole group'

# The page in Arabic with the dark theme.
Stop-Host
Write-Settings '{ "schemaVersion": 1, "general": { "language": "ar", "theme": "Dark" }, "features": { "endApp": { "enabled": true } } }'
Start-Sleep -Seconds 2
Start-Process $appExe
[void] (Wait-Until { Get-AppWindow })
Start-Sleep -Seconds 3
Test-Check 'select End App in Arabic' (Select-NavigationItem 'إنهاء التطبيق')
Start-Sleep -Seconds 2
Save-Screenshot '15f-end-app-arabic-dark'
$text = Get-WindowText
Test-Check 'the page is in Arabic' ($text -contains 'الاختصار') ($text -join ' | ')
[void] (Close-SettingsApp)
Stop-Host

# The test programs close, and the helpers end as the group lets them go.
foreach ($window in @($first, $second, $later, $script:settingsWindow, $script:explorer)) { if ($window) { [void] [Win32]::PostMessage($window, 0x0010, [UIntPtr]::Zero, [IntPtr]::Zero) } }
[void] $quit.Set()
Start-Sleep -Seconds 2
$quit.Dispose()
Remove-Item $endFolder -Recurse -Force -ErrorAction SilentlyContinue
Write-EndSettings '{ "enabled": false }'
