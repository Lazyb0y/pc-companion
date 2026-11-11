# Middle-Click Paste: the Home switch turns it on and its page shows how it works and saves its list
# of apps. With a test program's Windows Forms and WPF boxes, Edge, Character Map and the Run box,
# middle-clicks sent by SendInput: text selected in Edge goes in where the pointer is in a WPF box (a
# drop), Windows' own plain and rich text boxes, Character Map's box and the Run box (messages), and
# text selected in a test box goes into Edge's text area, single-line box and editable text; one
# Ctrl+Z takes each out, and the window comes to the front. A link, a tab and page text keep their
# middle-click, as do a title bar with Title Bar Actions on, read-only and password boxes, the
# console, and a box right after the wheel turned. Nothing selected, a password box, text that's too
# long, a box that doesn't take dropped text, an app that stops responding with the selection or
# with the box, and a second click while a paste waits each end with their reason in the host's log
# and a notice. A host without admin rights leaves a program with them alone, a listed app keeps its
# clicks, the clipboard never changes and the log never holds the text. The page in Arabic with the
# dark theme, with a screenshot.
[void] (Close-SettingsApp)

# Detailed logging is on for the host's Debug entries.
$detailedUntil = [DateTimeOffset]::Now.AddHours(1).ToString('o')
function Write-PasteSettings([string] $Section, [string] $Others = '') {
    Write-Settings "{ ""schemaVersion"": 1, ""general"": { ""theme"": ""Light"" }, ""diagnostics"": { ""detailedLoggingUntil"": ""$detailedUntil"" }, ""features"": { ""middleClickPaste"": $Section$Others } }"
    Start-Sleep -Seconds 3
}
function Start-Host {
    if ((Get-Processes 'PCCompanion.Host').Count -eq 0) {
        Start-Process $hostExe
        [void] (Wait-Until { [Win32]::FindWindow('PCCompanion.Host', $null) -ne [IntPtr]::Zero } 30)
    }
    [void] (Wait-Until { [Win32]::FindWindow('PCCompanion.MiddleClickPaste', $null) -ne [IntPtr]::Zero } 20)
    Start-Sleep -Seconds 1
}
function Stop-Host {
    [void] [Win32]::PostMessage([Win32]::FindWindow('PCCompanion.Host', $null), 0x0010, [UIntPtr]::Zero, [IntPtr]::Zero)
    [void] (Wait-Until { (Get-Processes 'PCCompanion.Host').Count -eq 0 } 15)
}

# The test program, built once: "forms" shows Windows Forms boxes, a plain text box (Windows' Edit), a
# rich text box, a read-only box and a password box, which on .NET Framework share no text through
# UI Automation; "wpf" shows WPF boxes, one that takes drops, one that doesn't and one with 70,000
# characters. Each logs a middle-click on a box to the program's .log file, and a posted message
# (WM_APP + 3) makes it stop responding for the milliseconds in wParam. mcp-medium is a copy that
# runs without admin rights.
$appFolder = Join-Path $env:TEMP 'PCCompanionPasteApp'
New-Item -ItemType Directory -Force -Path $appFolder | Out-Null
$pasteExe = Join-Path $appFolder 'mcp-app.exe'
$mediumExe = Join-Path $appFolder 'mcp-medium.exe'
$pasteLog = Join-Path $appFolder 'mcp-app.log'
Add-Type -AssemblyName PresentationFramework, PresentationCore, WindowsBase, System.Xaml
$buildError = ''
try {
Add-Type -OutputAssembly $pasteExe -OutputType WindowsApplication -ErrorAction Stop -ReferencedAssemblies System.Windows.Forms, System.Drawing, ([System.Windows.Window].Assembly.Location), ([System.Windows.UIElement].Assembly.Location), ([System.Windows.DependencyObject].Assembly.Location), ([System.Windows.Markup.IQueryAmbient].Assembly.Location) -TypeDefinition @'
using System;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
public static class PasteApp {
    [DllImport("user32.dll")] private static extern void DisableProcessWindowsGhosting();
    [DllImport("user32.dll")] private static extern bool SetProcessDPIAware();
    private static string s_log;

    private static void Log(string what) { File.AppendAllText(s_log, what + Environment.NewLine); }

    private sealed class FormsWindow : Form {
        protected override void WndProc(ref Message message) {
            if (message.Msg == 0x8003) { Thread.Sleep((int)message.WParam); return; }
            base.WndProc(ref message);
        }
    }

    private static void Add(Form form, Control control, string name, int top, int height) {
        control.Name = name;
        control.AccessibleName = name;
        control.SetBounds(10, top, 480, height);
        control.Font = new Font("Segoe UI", 11f);
        control.MouseDown += delegate(object sender, MouseEventArgs e) { if (e.Button == MouseButtons.Middle) Log(name + " middle-click"); };
        form.Controls.Add(control);
    }

    private static System.Windows.Controls.TextBox WpfBox(System.Windows.Controls.Panel panel, string name, string text, bool multiLine) {
        var box = new System.Windows.Controls.TextBox();
        box.Text = text;
        box.AcceptsReturn = multiLine;
        box.Height = multiLine ? 70 : 32;
        box.FontSize = 15;
        box.Margin = new System.Windows.Thickness(0, 0, 0, 10);
        System.Windows.Automation.AutomationProperties.SetAutomationId(box, name);
        box.PreviewMouseDown += delegate(object sender, System.Windows.Input.MouseButtonEventArgs e) { if (e.ChangedButton == System.Windows.Input.MouseButton.Middle) Log(name + " middle-click"); };
        panel.Children.Add(box);
        return box;
    }

    [STAThread]
    public static void Main(string[] arguments) {
        // Otherwise Windows puts a window of its own in place of one that doesn't respond for 5 seconds.
        DisableProcessWindowsGhosting();
        SetProcessDPIAware();
        var name = Path.GetFileNameWithoutExtension(Application.ExecutablePath);
        s_log = Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), name + ".log");
        var kind = arguments.Length > 0 ? arguments[0] : "forms";
        var left = arguments.Length > 1 ? int.Parse(arguments[1]) : 820;
        if (kind == "wpf") {
            var panel = new System.Windows.Controls.StackPanel();
            panel.Margin = new System.Windows.Thickness(10);
            WpfBox(panel, "wpfbox", "wpf first line\nwpf second line", true);
            WpfBox(panel, "nodrop", "no drop words", false).AllowDrop = false;
            WpfBox(panel, "long", new string('x', 70000), false);
            var window = new System.Windows.Window();
            window.Title = name + "-wpf";
            window.WindowStartupLocation = System.Windows.WindowStartupLocation.Manual;
            window.Left = left;
            window.Top = 400;
            window.Width = 520;
            window.Height = 270;
            window.Content = panel;
            window.SourceInitialized += delegate {
                var source = System.Windows.Interop.HwndSource.FromHwnd(new System.Windows.Interop.WindowInteropHelper(window).Handle);
                source.AddHook(delegate(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled) {
                    if (message == 0x8003) { Thread.Sleep((int)wParam); handled = true; }
                    return IntPtr.Zero;
                });
            };
            new System.Windows.Application().Run(window);
            return;
        }

        var form = new FormsWindow();
        form.Text = name + "-forms";
        form.StartPosition = FormStartPosition.Manual;
        form.Bounds = new Rectangle(left, 20, 520, 330);
        Add(form, new TextBox { Text = "alpha beta gamma delta" }, "plain", 10, 30);
        Add(form, new RichTextBox { Text = "rich words here" }, "rich", 50, 40);
        Add(form, new TextBox { ReadOnly = true, Text = "read only words" }, "readonly", 100, 30);
        Add(form, new TextBox { UseSystemPasswordChar = true, Text = "secret123" }, "password", 140, 30);
        Application.Run(form);
    }
}
'@
} catch {
    $buildError = "$_"
}
Test-Check 'the test program builds' (Test-Path $pasteExe) $buildError
Copy-Item $pasteExe $mediumExe -Force
& icacls.exe $mediumExe /setintegritylevel medium | Out-Null
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class PasteChecks {
    [DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint count, INPUT[] inputs, int size);
    [StructLayout(LayoutKind.Sequential)] private struct MOUSEINPUT { public int dx, dy; public uint mouseData, dwFlags, time; public IntPtr extra; }
    [StructLayout(LayoutKind.Explicit)] private struct INPUT { [FieldOffset(0)] public uint type; [FieldOffset(8)] public MOUSEINPUT mi; }
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] public static extern uint GetClipboardSequenceNumber();

    // A mouse button or the wheel, as a mouse sends it: MOUSEEVENTF_MIDDLEDOWN, MIDDLEUP or WHEEL.
    public static bool Mouse(uint flags, int data) {
        var input = new INPUT { type = 0 };
        input.mi.dwFlags = flags;
        input.mi.mouseData = unchecked((uint)data);
        return SendInput(1, new[] { input }, Marshal.SizeOf(typeof(INPUT))) == 1;
    }

    public static void Place(IntPtr window, int x, int y, int width, int height) { SetWindowPos(window, IntPtr.Zero, x, y, width, height, 0x0044); } // SWP_NOZORDER | SWP_SHOWWINDOW

    [StructLayout(LayoutKind.Sequential)] private struct GUITHREADINFO { public int cbSize, flags; public IntPtr active, focus, capture, menuOwner, moveSize, caret; public int left, top, right, bottom; }
    [DllImport("user32.dll")] private static extern bool GetGUIThreadInfo(uint thread, ref GUITHREADINFO info);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, IntPtr processId);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr window, System.Text.StringBuilder name, int size);

    private static string ClassOf(IntPtr window) { var name = new System.Text.StringBuilder(256); GetClassName(window, name, name.Capacity); return name.ToString(); }

    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr window, uint flags);

    // The class of the window in front, and of its top-level window when it's a child window.
    public static string Front() {
        var front = GetForegroundWindow();
        var root = GetAncestor(front, 2); // GA_ROOT
        return ClassOf(front) + (root != front ? " (in " + ClassOf(root) + ")" : "");
    }

    // The classes of the windows that are active and have the focus in a window's thread.
    public static string FocusOf(IntPtr window) {
        var info = new GUITHREADINFO { cbSize = Marshal.SizeOf(typeof(GUITHREADINFO)) };
        if (!GetGUIThreadInfo(GetWindowThreadProcessId(window, IntPtr.Zero), ref info)) return "unknown";
        return "active " + ClassOf(info.active) + ", focus " + ClassOf(info.focus);
    }
}
'@
function Find-AppWindow([string] $Name) { [Win32]::FindWindow([NullString]::Value, $Name) }
function Start-PasteApp([string] $Exe, [string] $Kind, [int] $Left = 820) {
    $name = [IO.Path]::GetFileNameWithoutExtension($Exe)
    $process = Start-Process $Exe -ArgumentList $Kind, "$Left" -PassThru
    [void] (Wait-Until { (Find-AppWindow "$name-$Kind") -ne [IntPtr]::Zero } 30)
    Start-Sleep -Seconds 1
    return $process
}
function Test-Logged([string] $Pattern, [int] $Seconds = 5) { Wait-Until { (Get-HostLog) -match $Pattern } $Seconds }
function Get-AppLog { if (Test-Path $pasteLog) { @(Get-Content $pasteLog) } else { @() } }
function Get-AppLogSince([int] $Lines) { @(Get-AppLog | Select-Object -Skip $Lines) }
function Get-PasteSettings { (Get-Content (Join-Path $dataFolder 'settings.json') -Raw | ConvertFrom-Json).features.middleClickPaste }

# Brings a window to the front: a press of a key that has no use first gives this script the right to.
function Select-Window([IntPtr] $Window) {
    for ($attempt = 1; $attempt -le 5; $attempt++) {
        [void] [Win32]::Key(0xE8, $false)
        [void] [Win32]::Key(0xE8, $true)
        [void] [Win32]::SetForegroundWindow($Window)
        Start-Sleep -Milliseconds 700
        if ([Win32]::GetForegroundWindow() -eq $Window) { return }
    }
    Write-Report "[NOTE] $([Win32]::WindowTitle($Window)) didn't come to the front; $([Win32]::WindowTitle([Win32]::GetForegroundWindow())) is."
}

# UI Automation: an element by its ID in the raw view, where Chromium keeps paragraphs and editable
# parts of a page, its text, and the character the point is before.
$automation = [System.Windows.Automation.AutomationElement]
function Find-ById([IntPtr] $Window, [string] $Id) {
    $request = New-Object System.Windows.Automation.CacheRequest
    $request.TreeFilter = [System.Windows.Automation.Automation]::RawViewCondition
    $activation = $request.Activate()
    try {
        $automation::FromHandle($Window).FindFirst([System.Windows.Automation.TreeScope]::Descendants, (New-Object System.Windows.Automation.PropertyCondition($automation::AutomationIdProperty, $Id)))
    } finally {
        $activation.Dispose()
    }
}
# The same, waiting for it: Edge builds its elements once a program first asks for them.
function Wait-ById([IntPtr] $Window, [string] $Id, [int] $Seconds = 10) {
    $script:foundElement = $null
    [void] (Wait-Until { $null -ne ($script:foundElement = Find-ById $Window $Id) } $Seconds)
    if ($null -eq $script:foundElement) { Write-Report "[NOTE] no element '$Id' in $([Win32]::WindowTitle($Window))." }
    return $script:foundElement
}
function Get-TextPattern($Element) {
    $pattern = $null
    if ($Element.TryGetCurrentPattern([System.Windows.Automation.TextPattern]::Pattern, [ref] $pattern)) { return $pattern }
    return $null
}
function Get-ElementText($Element) {
    $pattern = $null
    if ($Element.TryGetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern, [ref] $pattern)) { return $pattern.Current.Value }
    $text = Get-TextPattern $Element
    if ($text) { return $text.DocumentRange.GetText(-1) }
    return ''
}
function Get-Rectangle($Element) { $Element.Current.BoundingRectangle }
# A point on the element's first line, $X pixels in.
function Get-BoxPoint($Element, [int] $X = 30, [int] $Down = 0) {
    $bounds = Get-Rectangle $Element
    $y = if ($Down -gt 0) { $bounds.Y + $Down } else { $bounds.Y + $bounds.Height / 2 }
    New-Object System.Drawing.Point ([int] ($bounds.X + $X)), ([int] $y)
}
function Get-IndexAt($Element, [System.Drawing.Point] $Point) {
    try {
        $pattern = Get-TextPattern $Element
        $at = $pattern.RangeFromPoint((New-Object System.Windows.Point $Point.X, $Point.Y))
        $before = $pattern.DocumentRange.Clone()
        $before.MoveEndpointByRange([System.Windows.Automation.Text.TextPatternRangeEndpoint]::End, $at, [System.Windows.Automation.Text.TextPatternRangeEndpoint]::Start)
        return $before.GetText(-1).Length
    } catch {
        return -1
    }
}

$VK_CONTROL = 0x11; $VK_SHIFT = 0x10; $VK_HOME = 0x24; $VK_RIGHT = 0x27; $VK_ESCAPE = 0x1B
function Send-Keys([uint16[]] $Keys) {
    foreach ($key in $Keys) { [void] [Win32]::Key($key, $false) }
    for ($at = $Keys.Count - 1; $at -ge 0; $at--) { [void] [Win32]::Key($Keys[$at], $true) }
    Start-Sleep -Milliseconds 300
}
function Send-MiddleClick([int] $Wait = 300) {
    [void] [PasteChecks]::Mouse(0x0020, 0)
    Start-Sleep -Milliseconds 50
    [void] [PasteChecks]::Mouse(0x0040, 0)
    Start-Sleep -Milliseconds $Wait
}
function Send-DoubleClick([System.Drawing.Point] $Point) {
    [void] (Move-Pointer $Point.X $Point.Y)
    foreach ($click in 1, 2) {
        [void] [Win32]::LeftButton($true)
        [void] [Win32]::LeftButton($false)
        Start-Sleep -Milliseconds 60
    }
    Start-Sleep -Milliseconds 400
}

# The host's outcomes and claims, counted so each check sees only its own.
$outcomePattern = 'Middle-Click Paste: "?(\w+)"?, by "?(\w+)"?'
function Get-Outcomes { @([regex]::Matches((Get-HostLog), $outcomePattern) | ForEach-Object { "$($_.Groups[1].Value) by $($_.Groups[2].Value)" }) }
function Get-Claims { Get-MatchCount (Get-HostLog) 'claimed the text box under the pointer' }
# Rests the pointer on a point and waits until the host claims it; says whether it did. A claim
# stands while the pointer stays, as between clicks at the same place. The host looks once each
# time the pointer comes to rest, so when an app that's busy shows its text pointer late, the
# pointer moves by a pixel and back, as a hand does.
function Test-ClaimStands { (Get-Claims) -gt (Get-MatchCount (Get-HostLog) 'Middle-Click Paste removed its claim') }
function Move-ToBox([System.Drawing.Point] $Point) {
    $position = [System.Windows.Forms.Cursor]::Position
    if (($position.X -eq $Point.X) -and ($position.Y -eq $Point.Y) -and (Test-ClaimStands)) { return $true }
    $claims = Get-Claims
    [void] (Move-Pointer $Point.X $Point.Y)
    if (Wait-Until { (Get-Claims) -gt $claims } 3) { return $true }
    Write-Report "[NOTE] nothing claimed at $($Point.X), $($Point.Y) within 3 seconds; $(Get-PointerText); the pointer moves by a pixel."
    [void] (Move-Pointer ($Point.X + 1) $Point.Y)
    [void] (Move-Pointer $Point.X $Point.Y)
    return (Wait-Until { (Get-Claims) -gt $claims } 5)
}
# Middle-clicks where the pointer rests and waits for the host's next outcome; returns it. Counts the
# pastes during which the clipboard changed: Windows Sandbox shares the clipboard with the PC that
# runs it, so only the clipboard's changes during a paste count.
$script:clipboardChanges = 0
function Invoke-Paste([int] $Seconds = 6) {
    $count = (Get-Outcomes).Count
    $sequence = [PasteChecks]::GetClipboardSequenceNumber()
    Send-MiddleClick
    [void] (Wait-Until { (Get-Outcomes).Count -gt $count } $Seconds)
    if ([PasteChecks]::GetClipboardSequenceNumber() -ne $sequence) { $script:clipboardChanges++ }
    $outcomes = @(Get-Outcomes)
    if ($outcomes.Count -gt $count) { return $outcomes[$count] }
    return 'none'
}
# Selects the word in Edge's paragraph with a double-click, as a user does, until Edge says it's
# selected.
function Select-PageWord([string] $Word) {
    $selected = ''
    for ($attempt = 1; $attempt -le 3; $attempt++) {
        Select-Window $edge
        try {
            $document = $automation::FromHandle($edge).FindFirst([System.Windows.Automation.TreeScope]::Descendants, (New-Object System.Windows.Automation.PropertyCondition($automation::ControlTypeProperty, [System.Windows.Automation.ControlType]::Document)))
            $pattern = Get-TextPattern $document
            $range = $pattern.RangeFromChild((Wait-ById $edge 'para')).FindText($Word, $false, $false)
            $rectangle = $range.GetBoundingRectangles()[0]
            Send-DoubleClick (New-Object System.Drawing.Point ([int] ($rectangle.X + $rectangle.Width / 2)), ([int] ($rectangle.Y + $rectangle.Height / 2)))
            $selected = (@($pattern.GetSelection()) | ForEach-Object { $_.GetText(-1) }) -join ''
        } catch {
            $selected = "error: $_"
        }
        if (($selected.Trim() -eq $Word) -and ([Win32]::GetForegroundWindow() -eq $edge)) { return }
    }
    Write-Report "[NOTE] Edge's selection is '$selected', not $Word; $([Win32]::WindowTitle([Win32]::GetForegroundWindow())) is in front."
}
# Selects the first word of a test box with the keyboard: Home, then Ctrl + Shift + Right.
function Select-BoxStart([IntPtr] $Window, $Box) {
    Select-Window $Window
    $Box.SetFocus()
    Start-Sleep -Milliseconds 300
    Send-Keys @($VK_HOME)
    Send-Keys @($VK_CONTROL, $VK_SHIFT, $VK_RIGHT)
}
function Select-BoxAll([IntPtr] $Window, $Box) {
    Select-Window $Window
    $Box.SetFocus()
    Start-Sleep -Milliseconds 300
    Send-Keys @($VK_CONTROL, 0x41)
}
# Edge's page puts the value of the box that changed last in its title, with line breaks as " / ":
# after a drop, UI Automation's reads of a text area or single-line box in Edge no longer follow its
# value, though the box shows the change (Sandbox checks).
function Get-PageValue([string] $Id) {
    if ([Win32]::WindowTitle($edge) -match "^Paste page, $Id`: (.*?) - (Profile|Microsoft)") { return $Matches[1] }
    return $null
}
# Pastes into a box where the point is, then checks the text went in there, that the box's window
# came to the front with the focus in the box and that one Ctrl+Z takes the paste out again.
# $Select selects the text, after the box's text and the place under the point are measured: .NET's
# UI Automation brings a Windows Forms box's window to the front when it measures it. The text goes
# in at the character boundary nearest the pointer, on either side of the character under it. For
# a box in Edge's page, $PageId is its ID, and the undo is read from the page's title.
function Get-FrontTitle { [Win32]::WindowTitle([Win32]::GetForegroundWindow()) }
function Test-PasteInto([string] $Name, $Box, [IntPtr] $Window, [System.Drawing.Point] $Point, [string] $Text, [string] $Way, [scriptblock] $Select, [string] $PageId, [switch] $NoUndo) {
    $before = Get-ElementText $Box
    $expected = Get-IndexAt $Box $Point
    & $Select
    $selected = Get-FrontTitle
    Test-Check "the host claims $Name" (Move-ToBox $Point) (Get-PointerText)
    $outcome = Invoke-Paste
    $front = [PasteChecks]::Front()
    $after = Get-ElementText $Box
    $at = $after.IndexOf($Text)
    Test-Check "a middle-click pastes into $Name" ($outcome -eq "Pasted by $Way") "outcome: $outcome; in front after selecting: $selected; before: $before; after: $after"
    Test-Check "the text goes in where the pointer is in $Name" (($at -ge 0) -and (($expected -lt 0) -or ($at -eq $expected) -or ($at -eq $expected + 1))) "at $at, expected $expected; after: $after"
    Test-Check "$Name's window comes to the front" (Wait-Until { [Win32]::GetForegroundWindow() -eq $Window } 3) "front: $(Get-FrontTitle); right after the paste: $front"
    Test-Check "$Name has the keyboard focus" (Wait-Until { $Box.Current.HasKeyboardFocus } 3) ([PasteChecks]::FocusOf($Window))
    if (-not $NoUndo) {
        Start-Sleep -Milliseconds 300
        Send-Keys @($VK_CONTROL, 0x5A)
        if ($PageId) {
            $original = $before -replace "`r?`n", ' / '
            Test-Check "one Ctrl+Z takes the paste out of $Name" (Wait-Until { (Get-PageValue $PageId) -eq $original } 3) "page: $([Win32]::WindowTitle($edge)); before: $original"
        } else {
            Start-Sleep -Milliseconds 500
            $undone = Get-ElementText $Box
            Test-Check "one Ctrl+Z takes the paste out of $Name" ($undone -eq $before) "after Ctrl+Z: $undone; before: $before"
        }
    }
}

# Edge with a local page: a paragraph to select from, a link, a form's single-line box, a text area,
# editable text, a password box and a read-only box. The page notes each middle-click it gets in its
# title, and each change to a box's text, with the box's ID and text.
$policy = 'HKLM:\SOFTWARE\Policies\Microsoft\Edge'
New-Item -Path $policy -Force | Out-Null
Set-ItemProperty -Path $policy -Name HideFirstRunExperience -Value 1 -Type DWord
$page = Join-Path $appFolder 'paste.html'
$other = Join-Path $appFolder 'other.html'
Set-Content -Path $other -Encoding utf8 -Value '<!doctype html><title>Other page</title><p>Other page.</p>'
Set-Content -Path $page -Encoding utf8 -Value @"
<!doctype html><html><head><meta charset="utf-8"><title>Paste page</title>
<style>body { font: 16px 'Segoe UI', sans-serif; margin: 8px; } input, textarea, div.box { width: 90%; font: 16px 'Segoe UI', sans-serif; } textarea { height: 70px; } div.box { border: 1px solid #888; min-height: 26px; }</style></head>
<body id="page">
<p id="para">Plain words Zebrafinch on the page for selecting.</p>
<p><a id="link" href="$(([Uri] $other).AbsoluteUri)">A link to another page</a></p>
<input id="single" value="one two three"><br>
<textarea id="area">line one words
line two words</textarea>
<div id="editable" class="box" contenteditable="true">editable words here</div>
<input id="password" type="password" value="secret123"><br>
<input id="readonly" readonly value="read only words">
<script>var clicks = 0; document.addEventListener('mousedown', function (e) { if (e.button === 1) { clicks++; document.title = 'Paste page, middle ' + clicks; } });
document.addEventListener('input', function (e) { var box = e.target; var text = box.value !== undefined ? box.value : box.textContent; document.title = 'Paste page, ' + box.id + ': ' + text.replace(/\n/g, ' / '); });</script>
</body></html>
"@
$edgeExe = 'C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe'

# Turned on from Home, which saves only that it's on, the page says how it works and saves the
# list: Running app adds the test program and Remove takes it out. Program file is the same list as
# Hot Corners', whose checks cover it.
Write-Settings '{ "schemaVersion": 1, "general": { "theme": "Light" } }'
Start-Sleep -Seconds 2
$wpfProcess = Start-PasteApp $pasteExe 'wpf'
Start-Process $appExe
[void] (Wait-Until { Get-AppWindow })
Start-Sleep -Seconds 3
Test-Check 'turn on Middle-Click Paste on Home' (Set-Toggle 'Middle-Click Paste' $true)
Start-Sleep -Seconds 2
$saved = Get-PasteSettings
Test-Check 'the Home switch saves Middle-Click Paste as on' ($saved.enabled -eq $true) "saved: $($saved | ConvertTo-Json -Compress)"
Test-Check 'select Middle-Click Paste' (Select-NavigationItem 'Middle-Click Paste')
Start-Sleep -Seconds 2
Save-Screenshot '19a-middle-click-paste-english'
$text = Get-WindowText
Test-Check 'Middle-Click Paste shows how it works, its list and what it reads' (($text -contains 'How it works') -and ($text -contains 'Apps that keep their middle-click') -and ($text -contains 'Permissions & data')) ($text -join ' | ')
Test-Check 'no app is listed yet' ($text -contains 'No apps are listed.') ($text -join ' | ')
Test-Check 'it says what apps share and what''s left alone' (@($text | Where-Object { $_ -like 'Apps share their selected text*' }).Count -eq 1) ($text -join ' | ')
# The host runs as administrator here, as everything in Windows Sandbox does.
$adminNote = if ($elevated) { 'PC Companion runs as administrator, so it pastes only between apps*' } else { 'Apps running as administrator keep their middle-click*' }
Test-Check 'it says which apps keep their middle-click for its rights' (@($text | Where-Object { $_ -like $adminNote }).Count -eq 1) ($text -join ' | ')
Test-Check 'it names the keyboard way' (@($text | Where-Object { $_ -like 'Middle-Click Paste is for the mouse*' }).Count -eq 1) ($text -join ' | ')
Test-Check 'Add an app offers Running app' ((Invoke-AppElement 'Add an app') -and (Wait-Until { Find-AppElement 'Running app…' } 5))
[void] (Invoke-AppElement 'Running app…')
$appItem = $null
Test-Check 'Running app lists the test program' (Wait-Until { ($script:appItem = Find-AppListItem 'mcp-app') -ne $null } 15)
if ($appItem) { $appItem.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select(); Start-Sleep -Milliseconds 300 }
[void] (Invoke-AppElement 'Add')
Test-Check 'Running app adds it' (Wait-Until { $apps = @((Get-PasteSettings).excludedApps); ($apps.Count -eq 1) -and ($apps[0].program -eq $pasteExe) } 5) "saved: $(Get-PasteSettings | ConvertTo-Json -Compress)"
Test-Check 'Remove takes it out' ((Invoke-AppElement 'Remove mcp-app') -and (Wait-Until { @((Get-PasteSettings).excludedApps).Count -eq 0 } 5)) "saved: $(Get-PasteSettings | ConvertTo-Json -Compress)"
[void] (Close-SettingsApp)

# The windows: Edge on the left, the test program's Windows Forms and WPF windows on the right.
Write-PasteSettings '{ "enabled": true }'
Start-Host
$formsProcess = Start-PasteApp $pasteExe 'forms'
$forms = Find-AppWindow 'mcp-app-forms'
$wpf = Find-AppWindow 'mcp-app-wpf'
Start-Process $edgeExe -ArgumentList '--no-first-run', '--no-default-browser-check', "--user-data-dir=$appFolder\edge", '--new-window', ([Uri] $page).AbsoluteUri
$script:edge = [IntPtr]::Zero
[void] (Wait-Until { $script:edge = [Win32]::TopLevelWindows() | Where-Object { [Win32]::WindowClass($_) -eq 'Chrome_WidgetWin_1' -and [Win32]::WindowTitle($_) -like 'Paste page*' } | Select-Object -First 1; $null -ne $script:edge } 40)
Test-Check 'Edge opens the page' ($edge -ne [IntPtr]::Zero)
[PasteChecks]::Place($edge, 0, 0, 800, 700)
Start-Sleep -Seconds 2
$plain = Wait-ById $forms 'plain'
$rich = Wait-ById $forms 'rich'
$wpfBox = Wait-ById $wpf 'wpfbox'
$noDrop = Wait-ById $wpf 'nodrop'
$area = Wait-ById $edge 'area'
$single = Wait-ById $edge 'single'
$editable = Wait-ById $edge 'editable'

# Text selected in Edge goes into a WPF box as a drop, and into Windows' own plain and rich text
# boxes by messages, where the pointer is.
$claims = Get-Claims
Test-PasteInto 'a WPF box' $wpfBox $wpf (Get-BoxPoint $wpfBox 40 14) 'Zebrafinch' 'Drop' { Select-PageWord 'Zebrafinch' }
Save-Screenshot '19b-middle-click-paste-wpf'
Test-PasteInto 'a plain text box' $plain $forms (Get-BoxPoint $plain 60) 'Zebrafinch' 'Message' { Select-PageWord 'Zebrafinch' }
Test-PasteInto 'a rich text box' $rich $forms (Get-BoxPoint $rich 50 12) 'Zebrafinch' 'Message' { Select-PageWord 'Zebrafinch' }
[void] (Move-Pointer 400 760)
Test-Check 'leaving a box removes the claim, so the pointer hook is removed' (Test-Logged 'Middle-Click Paste removed its claim')

# Character Map's rich edit box and the Run box get messages too.
Start-Process charmap.exe
$script:charmap = [IntPtr]::Zero
[void] (Wait-Until { $script:charmap = Find-AppWindow 'Character Map'; $script:charmap -ne [IntPtr]::Zero } 15)
[PasteChecks]::Place($charmap, 820, 380, 520, 420)
Start-Sleep -Seconds 1
$charmapBox = Wait-ById $charmap '104'
Test-PasteInto 'Character Map''s box' $charmapBox $charmap (Get-BoxPoint $charmapBox 10) 'Zebrafinch' 'Message' { Select-PageWord 'Zebrafinch' } -NoUndo
Stop-Process -Name charmap -Force -ErrorAction SilentlyContinue
Start-Process explorer.exe -ArgumentList 'shell:::{2559a1f3-21d7-11d4-bdaf-00c04f60b9f0}'
$script:run = [IntPtr]::Zero
[void] (Wait-Until { $script:run = [Win32]::FindWindow('#32770', 'Run'); $script:run -ne [IntPtr]::Zero } 15)
if ($run -ne [IntPtr]::Zero) {
    [PasteChecks]::Place($run, 820, 420, 420, 200)
    Start-Sleep -Seconds 1
    $runBox = Wait-ById $run '1001'
    # The Run box opens with the last command, such as the one the App checks ran, and shows a
    # long one from its start once it loses the focus, so the characters under the pointer change.
    # A short command of the checks' own fits in the box.
    $runBox.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue('run words here')
    Test-PasteInto 'the Run box' $runBox $run (Get-BoxPoint $runBox 40) 'Zebrafinch' 'Message' { Select-PageWord 'Zebrafinch' } -NoUndo
    [void] [Win32]::PostMessage($run, 0x0010, [UIntPtr]::Zero, [IntPtr]::Zero)
} else {
    Test-Check 'the Run box opens' $false
}

# Text selected in a WPF box goes into Edge's text area, single-line box and editable text.
Test-PasteInto 'Edge''s text area' $area $edge (Get-BoxPoint $area 40 14) 'wpf' 'Drop' { Select-BoxStart $wpf $wpfBox } -PageId area
Test-PasteInto 'Edge''s single-line box' $single $edge (Get-BoxPoint $single 40) 'wpf' 'Drop' { Select-BoxStart $wpf $wpfBox } -PageId single
Test-PasteInto 'Edge''s editable text' $editable $edge (Get-BoxPoint $editable 60) 'wpf' 'Drop' { Select-BoxStart $wpf $wpfBox } -PageId editable
Save-Screenshot '19c-middle-click-paste-edge'

# Left alone: a link opens a page in a tab, a middle-click on that tab closes it, and the page's text
# gets its middle-click.
Select-Window $edge
$tabs = { @($automation::FromHandle($edge).FindAll([System.Windows.Automation.TreeScope]::Descendants, (New-Object System.Windows.Automation.PropertyCondition($automation::ControlTypeProperty, [System.Windows.Automation.ControlType]::TabItem)))) }
$tabCount = (& $tabs).Count
$link = Get-BoxPoint (Wait-ById $edge 'link') 20
[void] (Move-Pointer $link.X $link.Y)
Send-MiddleClick 1500
Test-Check 'a middle-click on a link still opens it in a tab' (Wait-Until { (& $tabs).Count -eq $tabCount + 1 } 5) "tabs: $((& $tabs).Count), before: $tabCount"
$newTab = @(& $tabs)[-1]
$tabPoint = Get-BoxPoint $newTab 30
[void] (Move-Pointer $tabPoint.X $tabPoint.Y)
Send-MiddleClick 1500
Test-Check 'a middle-click on a tab still closes it' (Wait-Until { (& $tabs).Count -eq $tabCount } 5) "tabs: $((& $tabs).Count), before: $tabCount"
$claims = Get-Claims
$paragraph = Get-BoxPoint (Wait-ById $edge 'para') 40
[void] (Move-Pointer $paragraph.X $paragraph.Y)
Send-MiddleClick 800
Test-Check 'the page''s text gets its middle-click' (Wait-Until { [Win32]::WindowTitle($edge) -like 'Paste page, middle*' } 5) "Edge: $([Win32]::WindowTitle($edge))"
Test-Check 'and isn''t claimed' ((Get-Claims) -eq $claims)
Send-Keys @($VK_ESCAPE)

# Read-only and password boxes get their middle-click, and the host says why it left them alone.
foreach ($name in 'readonly', 'password') {
    $box = Wait-ById $forms $name
    $lines = (Get-AppLog).Count
    $left = Get-MatchCount (Get-HostLog) 'left the place under the pointer alone'
    [void] (Move-Pointer (Get-BoxPoint $box 40).X (Get-BoxPoint $box 40).Y)
    [void] (Wait-Until { (Get-MatchCount (Get-HostLog) 'left the place under the pointer alone') -gt $left } 3)
    Send-MiddleClick 1000
    Test-Check "a $name box gets its middle-click" (Wait-Until { @(Get-AppLogSince $lines) -contains "$name middle-click" } 5) "app log: $((Get-AppLogSince $lines) -join ' | '); front: $(Get-FrontTitle); $(Get-PointerText)"
}
Test-Check 'the host says why it left them alone' ((Test-Logged 'left the place under the pointer alone: "?ReadOnly') -and (Test-Logged 'left the place under the pointer alone: "?Password'))

# The console shows an arrow over its text, so it's never asked, and its middle-click isn't a paste.
$consoleProcess = Start-Process cmd.exe -ArgumentList '/k', 'echo console words' -PassThru
$script:console = [IntPtr]::Zero
[void] (Wait-Until { $script:console = (Get-Process -Id $consoleProcess.Id).MainWindowHandle; $script:console -ne [IntPtr]::Zero } 15)
[PasteChecks]::Place($console, 820, 420, 520, 240)
Select-Window $console
$claims = Get-Claims
$count = (Get-Outcomes).Count
$frame = New-Object Win32+RECT
[void] [Win32]::GetWindowRect($console, [ref] $frame)
[void] (Move-Pointer ($frame.Left + 60) ($frame.Top + 60))
Start-Sleep -Milliseconds 1500
Send-MiddleClick 800
Test-Check 'the console''s text isn''t claimed, and its middle-click pastes nothing' (((Get-Claims) -eq $claims) -and ((Get-Outcomes).Count -eq $count)) "window: $([Win32]::WindowClass($console)); $(Get-PointerText)"
Stop-Process -Id $consoleProcess.Id -Force -ErrorAction SilentlyContinue

# Right after the wheel turns, a middle-click on a box isn't a paste: the box gets it.
Select-PageWord 'Zebrafinch'
[void] (Move-ToBox (Get-BoxPoint $wpfBox 40 14))
$lines = (Get-AppLog).Count
$count = (Get-Outcomes).Count
[void] [PasteChecks]::Mouse(0x0800, -120)
Start-Sleep -Milliseconds 150
Send-MiddleClick 1500
Test-Check 'a middle-click right after the wheel reaches the box' (@(Get-AppLogSince $lines) -contains 'wpfbox middle-click') ((Get-AppLogSince $lines) -join ' | ')
Test-Check 'and pastes nothing' ((Get-Outcomes).Count -eq $count) ((Get-Outcomes) -join ', ')

# With Title Bar Actions on too, a middle-click on a title bar minimizes the window.
Write-PasteSettings '{ "enabled": true }' ', "titleBarActions": { "enabled": true }'
Start-Sleep -Seconds 2
Select-Window $edge
$frame = New-Object Win32+RECT
[void] [Win32]::GetWindowRect($forms, [ref] $frame)
[void] (Move-Pointer ([int] (($frame.Left + $frame.Right) / 2)) ($frame.Top + 15))
Send-MiddleClick 1500
Test-Check 'with Title Bar Actions on, a middle-click on a title bar minimizes the window' (Wait-Until { [Win32]::IsIconic($forms) } 5) "front: $([Win32]::WindowTitle([Win32]::GetForegroundWindow())); $(Get-PointerText)"
[void] [Win32]::ShowWindow($forms, 4)
Start-Sleep -Milliseconds 500
Write-PasteSettings '{ "enabled": true }'

# Each reason for not pasting is logged and shown in a notice next to the pointer.
function Test-Refused([string] $Name, [string] $Expected, [System.Drawing.Point] $Point) {
    [void] (Move-ToBox $Point)
    $outcome = Invoke-Paste
    Test-Check $Name ($outcome -like "$Expected by *") "outcome: $outcome"
}
Select-Window $edge
$blank = Get-BoxPoint (Wait-ById $edge 'para') 600
[void] (Move-Pointer $blank.X $blank.Y)
[void] [Win32]::LeftButton($true); [void] [Win32]::LeftButton($false)
Start-Sleep -Milliseconds 400
Test-Refused 'with nothing selected, nothing is pasted' 'NothingSelected' (Get-BoxPoint $wpfBox 40 14)
Test-Check 'and a notice says why' (Wait-Until { $notice = [Win32]::FindWindow('PCCompanion.MiddleClickPaste.Notice', [NullString]::Value); ($notice -ne [IntPtr]::Zero) -and [Win32]::IsWindowVisible($notice) } 2)
Save-Screenshot '19d-middle-click-paste-notice'
Select-BoxAll $forms (Wait-ById $forms 'password')
Test-Refused 'a selection in a password box isn''t read' 'Password' (Get-BoxPoint $wpfBox 40 14)
Select-BoxStart $forms $plain
Test-Refused 'a selection in an app that doesn''t share it isn''t read' 'NotShared' (Get-BoxPoint $wpfBox 40 14)
Select-BoxAll $wpf (Wait-ById $wpf 'long')
Test-Refused 'a selection of more than 65,536 characters isn''t pasted' 'TooLong' (Get-BoxPoint $wpfBox 40 14)
Select-PageWord 'Zebrafinch'
Test-Refused 'a box that doesn''t take dropped text gets nothing' 'BoxRefused' (Get-BoxPoint $noDrop 40)

# An app that stops responding: with the selection, the read gives up after 1.5 seconds, and a
# second click meanwhile is busy; with the box, the drop isn't tried.
Select-BoxStart $wpf $wpfBox
[void] (Move-ToBox (Get-BoxPoint $area 40 14))
[void] [Win32]::PostMessage($wpf, 0x8003, [UIntPtr]::new(6000), [IntPtr]::Zero)
Start-Sleep -Milliseconds 300
$count = (Get-Outcomes).Count
Send-MiddleClick 300
Send-MiddleClick 2500
$outcomes = @(Get-Outcomes | Select-Object -Skip $count)
Test-Check 'a second click while the first waits is busy' ($outcomes -contains 'Busy by None') ($outcomes -join ', ')
Test-Check 'an app with the selection that stopped responding gives up in time' ($outcomes -contains 'AppNotResponding by None') ($outcomes -join ', ')
Start-Sleep -Seconds 5
Select-PageWord 'Zebrafinch'
[void] (Move-ToBox (Get-BoxPoint $wpfBox 40 14))
[void] [Win32]::PostMessage($wpf, 0x8003, [UIntPtr]::new(4000), [IntPtr]::Zero)
Start-Sleep -Milliseconds 300
$outcome = Invoke-Paste
Test-Check 'a box whose app stopped responding gets nothing' ($outcome -eq 'BoxNotResponding by Drop') "outcome: $outcome"
Start-Sleep -Seconds 5
Test-Check 'and nothing arrives once it responds again' ((Get-ElementText $wpfBox) -notlike '*Zebrafinch*') (Get-ElementText $wpfBox)

# A listed app keeps its middle-click, and the host says why.
Write-PasteSettings "{ ""enabled"": true, ""excludedApps"": [ { ""name"": ""mcp-app"", ""program"": ""$($pasteExe -replace '\\', '\\')"" } ] }"
$lines = (Get-AppLog).Count
$point = Get-BoxPoint $wpfBox 40 14
[void] (Move-Pointer ($point.X + 20) $point.Y)
[void] (Move-Pointer $point.X $point.Y)
Send-MiddleClick 1000
Test-Check 'a listed app gets its middle-click' (@(Get-AppLogSince $lines) -contains 'wpfbox middle-click') ((Get-AppLogSince $lines) -join ' | ')
Test-Check 'the host says it''s listed' (Test-Logged 'left a window of a listed app alone')

# Nothing went through the clipboard, and the host's log never holds the pasted text.
Test-Check 'pasting never changed the clipboard' ($clipboardChanges -eq 0) "pastes during which it changed: $clipboardChanges"
Test-Check 'the host''s log never holds the text' ((Get-HostLog) -notmatch 'Zebrafinch')

# Everything in Windows Sandbox runs as administrator; a host whose file is labeled Medium integrity
# runs without admin rights, and leaves the test program's box alone. Windows gives such a host
# pointer input only while a window without admin rights is in front, so a medium-labeled copy is.
Stop-Host
& icacls.exe $hostExe /setintegritylevel medium | Out-Null
Write-PasteSettings '{ "enabled": true }'
Start-Host
$mediumProcess = Start-PasteApp $mediumExe 'forms' 0
Select-Window (Find-AppWindow 'mcp-medium-forms')
$lines = (Get-AppLog).Count
$point = Get-BoxPoint $wpfBox 40 14
[void] (Move-Pointer $point.X $point.Y)
Send-MiddleClick 1000
Test-Check 'a host without admin rights leaves a box of a program with them alone' (@(Get-AppLogSince $lines) -contains 'wpfbox middle-click') ((Get-AppLogSince $lines) -join ' | ')
Test-Check 'the host logs why' (Test-Logged 'left a window alone: "?RunsAsAdministrator')
Stop-Host
& icacls.exe $hostExe /setintegritylevel high | Out-Null
Stop-Process -Id $mediumProcess.Id -Force -ErrorAction SilentlyContinue

# The page in Arabic with the dark theme.
Write-Settings '{ "schemaVersion": 1, "general": { "language": "ar", "theme": "Dark" }, "features": { "middleClickPaste": { "enabled": true } } }'
Start-Sleep -Seconds 2
Start-Process $appExe
[void] (Wait-Until { Get-AppWindow })
Start-Sleep -Seconds 3
Test-Check 'select Middle-Click Paste in Arabic' (Select-NavigationItem 'اللصق بالنقر الأوسط')
Start-Sleep -Seconds 2
Save-Screenshot '19e-middle-click-paste-arabic-dark'
$text = Get-WindowText
Test-Check 'the page is in Arabic' ($text -contains 'طريقة العمل') ($text -join ' | ')
[void] (Close-SettingsApp)

Stop-Host
Get-Process 'mcp-app', 'mcp-medium', 'msedge' -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Seconds 1
Remove-Item $appFolder -Recurse -Force -ErrorAction SilentlyContinue
Write-PasteSettings '{ "enabled": false }'
