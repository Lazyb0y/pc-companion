# Dynamic Wallpaper, with pictures the group makes. Turned on from Home, it starts with Sunrise and
# sunset and Windows' light and dark pictures, remembers the Sandbox's background and sets the
# picture of the period it works out, which the page marks In use; the page shows Windows' time zone
# under Location, and Today when each period starts and ends. A picture for each display, turned on
# on the page, lists the Sandbox's display as connected. With Times of day, the next time's picture
# shows when its time comes, and a new time zone, set with tzutil, moves the times. With Windows
# mode, Light's and Dark's pictures follow the mode as it changes, also after Explorer restarts and
# after Explorer stopped answering for a while, whose call is cancelled with a warning; a folder
# shows each of its pictures once before any repeats; a picture that's gone leaves the background as
# it is, and the page says so; and a display's own picture is set for it. Turned off from the page,
# the Sandbox's background comes back, and so does a solid color. Turned on again, a background
# chosen elsewhere makes it step aside, which the page notes with Resume, and Resume sets the
# period's picture again; turned off then, the background chosen elsewhere comes back. With
# NoChangingWallPaper, nothing changes and the page says the background is managed. The logs hold
# no picture names. Detailed logging is on for the Debug entries.
$detailedUntil = [DateTimeOffset]::Now.AddHours(1).ToString('o')
$personalize = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize'
$desktopPolicies = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Policies\ActiveDesktop'

# Windows' wallpaper function, as the checks read and change the background, and the message
# Windows sends when the Windows mode changes.
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
[StructLayout(LayoutKind.Sequential)]
public struct WallpaperRect { public int Left, Top, Right, Bottom; }
[ComImport, Guid("B92B56A9-8B55-4E14-9A89-0199BBB6F93B"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IDesktopWallpaperChecks {
    void SetWallpaper([MarshalAs(UnmanagedType.LPWStr)] string monitorId, [MarshalAs(UnmanagedType.LPWStr)] string wallpaper);
    [return: MarshalAs(UnmanagedType.LPWStr)] string GetWallpaper([MarshalAs(UnmanagedType.LPWStr)] string monitorId);
    [return: MarshalAs(UnmanagedType.LPWStr)] string GetMonitorDevicePathAt(uint monitorIndex);
    uint GetMonitorDevicePathCount();
    [PreserveSig] int GetMonitorRECT([MarshalAs(UnmanagedType.LPWStr)] string monitorId, out WallpaperRect displayRect);
    [PreserveSig] int SetBackgroundColor(uint color);
    [PreserveSig] int GetBackgroundColor(out uint color);
    [PreserveSig] int SetPosition(int position);
    [PreserveSig] int GetPosition(out int position);
    [PreserveSig] int SetSlideshow(IntPtr items);
    [PreserveSig] int GetSlideshow(out IntPtr items);
    [PreserveSig] int SetSlideshowOptions(int options, uint slideshowTick);
    [PreserveSig] int GetSlideshowOptions(out int options, out uint slideshowTick);
    [PreserveSig] int AdvanceSlideshow([MarshalAs(UnmanagedType.LPWStr)] string monitorId, int direction);
    [PreserveSig] int GetStatus(out int state);
    [PreserveSig] int Enable([MarshalAs(UnmanagedType.Bool)] bool enable);
}
public static class WallpaperChecks {
    private static IDesktopWallpaperChecks Wallpaper() {
        return (IDesktopWallpaperChecks)Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("C2CF3110-460E-4FC1-B9D0-8A1C0C9CC4BD")));
    }
    // The first connected display's ID; Windows also lists displays that were connected before.
    public static string Display() {
        var wallpaper = Wallpaper();
        for (uint i = 0; i < wallpaper.GetMonitorDevicePathCount(); i++) {
            var id = wallpaper.GetMonitorDevicePathAt(i);
            WallpaperRect area;
            if (wallpaper.GetMonitorRECT(id, out area) == 0 && area.Right > area.Left) { return id; }
        }
        return null;
    }
    public static string Shown() { return Wallpaper().GetWallpaper(Display()); }
    public static void Set(string picture) { Wallpaper().SetWallpaper(null, picture); }
    // A solid color instead of a picture, as Windows Settings sets one.
    public static void SetColor(uint color) { var wallpaper = Wallpaper(); wallpaper.SetBackgroundColor(color); wallpaper.Enable(false); }
    public static uint Color() { uint color; Wallpaper().GetBackgroundColor(out color); return color; }
    public static bool PictureShows() { int state; Wallpaper().GetStatus(out state); return (state & 1) != 0; }
    [DllImport("ntdll.dll")] private static extern int NtSuspendProcess(IntPtr process);
    [DllImport("ntdll.dll")] private static extern int NtResumeProcess(IntPtr process);
    // Suspends the processes, and resumes them after the time on a timer of its own, even while the
    // group waits for one of them.
    private static System.Threading.Timer resumeTimer;
    public static bool SuspendFor(IntPtr[] processes, int milliseconds) {
        var suspended = true;
        foreach (var process in processes) { suspended &= NtSuspendProcess(process) == 0; }
        resumeTimer = new System.Threading.Timer(state => { foreach (var process in processes) { NtResumeProcess(process); } }, null, milliseconds, System.Threading.Timeout.Infinite);
        return suspended;
    }
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SendMessageTimeout(IntPtr window, uint message, UIntPtr wParam, string lParam, uint flags, uint timeout, out UIntPtr result);
    public static void SendSettingChange(string area) {
        UIntPtr result;
        SendMessageTimeout(new IntPtr(0xFFFF), 0x001A, UIntPtr.Zero, area, 2, 5000, out result);
    }
}
'@

function Get-Shown { try { [WallpaperChecks]::Shown() } catch { "error: $($_.Exception.Message)" } }
function Test-Shown([string] $Picture, [int] $Seconds = 10) { Wait-Until { (Get-Shown) -eq $Picture } $Seconds }
function Get-WallpaperSettings { (Get-Content (Join-Path $dataFolder 'settings.json') -Raw | ConvertFrom-Json).features.dynamicWallpaper }
function Get-AppLog { (Get-ChildItem $logs -Filter 'app-*.log' -ErrorAction SilentlyContinue | ForEach-Object { Get-Content $_.FullName -Raw }) -join "`n" }
function Get-LastPeriod { $found = [regex]::Matches((Get-HostLog), "Dynamic Wallpaper's period is ([A-Za-z]+\.[A-Za-z0-9]+)\."); if ($found.Count) { $found[$found.Count - 1].Groups[1].Value } }

function Write-WallpaperSettings($Feature) {
    $settings = [ordered] @{
        schemaVersion = 1
        general = @{ theme = 'Light' }
        updates = @{ checkAutomatically = $false }
        diagnostics = @{ detailedLoggingUntil = $detailedUntil }
    }
    if ($Feature) { $settings.features = @{ dynamicWallpaper = $Feature } }
    Write-Settings ($settings | ConvertTo-Json -Depth 10)
}

# Sets the Windows mode that Start and the taskbar use, as Windows Settings does, and tells apps.
function Set-WindowsMode([bool] $Dark) {
    if (-not (Test-Path $personalize)) { [void] (New-Item $personalize -Force) }
    [void] (New-ItemProperty $personalize -Name SystemUsesLightTheme -Value ([int] -not $Dark) -PropertyType DWord -Force)
    [WallpaperChecks]::SendSettingChange('ImmersiveColorSet')
}

# An element of the settings app that opens and closes, such as a group of settings, opened.
function Open-AppGroup([string] $Name) {
    $window = Get-AppWindow
    if (-not $window) { return $false }
    $condition = New-Object System.Windows.Automation.AndCondition(
        (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, $Name)),
        (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::IsExpandCollapsePatternAvailableProperty, $true)))
    $group = $window.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
    if (-not $group) { return $false }
    $group.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern).Expand()
    return $true
}

# Solid pictures to show, in a folder of the user's own.
Add-Type -AssemblyName System.Drawing
$pictures = Join-Path $env:USERPROFILE 'Pictures\DynamicWallpaperChecks'
$folder = Join-Path $pictures 'Folder'
New-Item -ItemType Directory -Force -Path $folder | Out-Null
function New-Picture([string] $Path, [string] $Color) {
    $bitmap = New-Object System.Drawing.Bitmap -ArgumentList 320, 180
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.Clear([System.Drawing.Color]::FromName($Color))
    $graphics.Dispose()
    $format = if ($Path -like '*.png') { [System.Drawing.Imaging.ImageFormat]::Png } else { [System.Drawing.Imaging.ImageFormat]::Jpeg }
    $bitmap.Save($Path, $format)
    $bitmap.Dispose()
}
$day = Join-Path $pictures 'Day.jpg'
$night = Join-Path $pictures 'Night.jpg'
$other = Join-Path $pictures 'Other.jpg'
$own = Join-Path $pictures 'Own.png'
$gone = Join-Path $pictures 'Gone.jpg'
New-Picture $day 'Gold'
New-Picture $night 'MidnightBlue'
New-Picture $other 'ForestGreen'
New-Picture $own 'Crimson'
$folderPictures = @('One.png', 'Two.png', 'Three.png') | ForEach-Object { Join-Path $folder $_ }
New-Picture $folderPictures[0] 'Tomato'
New-Picture $folderPictures[1] 'SeaGreen'
New-Picture $folderPictures[2] 'SteelBlue'
$sandboxBackground = Get-Shown
$modeBefore = (Get-ItemProperty $personalize -ErrorAction SilentlyContinue).SystemUsesLightTheme

# Turned on from Home: the first settings, and the period's picture.
[void] (Close-SettingsApp)
Write-WallpaperSettings $null
Start-Sleep -Seconds 2
Start-Process $appExe
[void] (Wait-Until { Get-AppWindow })
Start-Sleep -Seconds 3
Test-Check 'turn on Dynamic Wallpaper on Home' (Set-Toggle 'Dynamic Wallpaper' $true)
Start-Sleep -Seconds 2
$saved = Get-WallpaperSettings
$windowsPictures = Join-Path $env:WINDIR 'Web\Wallpaper\Windows'
$firstPictures = if (Test-Path (Join-Path $windowsPictures 'img19.jpg')) { ($saved.sun.day.picture -eq (Join-Path $windowsPictures 'img0.jpg')) -and ($saved.sun.night.picture -eq (Join-Path $windowsPictures 'img19.jpg')) } else { $saved.sun.day.picture -eq $saved.sun.night.picture }
Test-Check 'turning it on saves the first settings' ($saved.enabled -and ($saved.changeWith -eq 'Sun') -and ($saved.location -eq 'TimeZone') -and $firstPictures -and (@($saved.times).Count -eq 2)) ($saved | ConvertTo-Json -Depth 6 -Compress)
Test-Check 'the host starts Dynamic Wallpaper' (Wait-Until { (Get-HostLog) -match 'Dynamic Wallpaper started\.' } 15)
Test-Check 'the host remembers the Sandbox''s background' (Wait-Until { (Get-HostLog) -match 'Dynamic Wallpaper remembered the background, a ' } 15) (Get-HostLog)
Test-Check 'the host works out the period' (Wait-Until { (Get-LastPeriod) -match '^Sun\.(Day|Night)$' } 15) (Get-LastPeriod)
$period = (Get-LastPeriod) -replace '^Sun\.', ''
$expected = if ($period -eq 'Day') { $saved.sun.day.picture } else { $saved.sun.night.picture }
Test-Check "Windows shows the $period picture" (Test-Shown $expected) (Get-Shown)

# The page: the period in use, Location and Today.
Test-Check 'select Dynamic Wallpaper' (Select-NavigationItem 'Dynamic Wallpaper')
Start-Sleep -Seconds 3
$inUse = "$period, $(Split-Path $expected -Leaf), in use"
Test-Check 'the page marks the period in use' (Wait-Until { Find-AppElement $inUse } 8) ((Get-WindowText) -join ' | ')
Test-Check 'Location shows Windows'' time zone' (Wait-Until { Find-AppText ([TimeZoneInfo]::Local.DisplayName) } 5) ((Get-WindowText) -join ' | ')
Test-Check 'open Today' (Open-AppGroup 'Today')
Start-Sleep -Seconds 1
$text = (Get-WindowText) -join ' | '
$ranges = Get-MatchCount $text '\d{1,2}:\d{2}(\s?[AP]M)? – \d{1,2}:\d{2}'
Test-Check 'Today says when Night, Day and Night start and end' ($ranges -ge 3) $text
Save-Screenshot '17a-dynamic-wallpaper-page'

# A picture for each display lists the Sandbox's display.
Test-Check 'turn on A picture for each display' (Set-Toggle 'A picture for each display' $true)
Test-Check 'the page lists the connected display' (Wait-Until { (@((Get-WallpaperSettings).displays).Count -ge 1) -and (Find-AppText 'Connected') } 10) ((Get-WallpaperSettings).displays | ConvertTo-Json -Compress)
Test-Check 'the periods'' own pictures are for Other displays' ([bool] (Find-AppText 'Other displays'))
Save-Screenshot '17b-dynamic-wallpaper-each-display'
$displays = @((Get-WallpaperSettings).displays)

# Times of day: the next time's picture shows when its time comes.
[void] (Close-SettingsApp)
$now = Get-Date
$next = $now.Date.AddHours($now.Hour).AddMinutes($now.Minute + 2)
$earlier = $now.AddHours(-2)
Write-WallpaperSettings ([ordered] @{
    enabled = $true; changeWith = 'Times'; location = 'TimeZone'; fit = 'Fill'
    times = @([ordered] @{ at = $earlier.ToString('HH:mm', [Globalization.CultureInfo]::InvariantCulture); picture = $day }, [ordered] @{ at = $next.ToString('HH:mm', [Globalization.CultureInfo]::InvariantCulture); picture = $night })
})
Test-Check 'Times of day shows the earlier time''s picture' (Test-Shown $day 15) (Get-Shown)
Test-Check 'the host logs the period' ((Get-LastPeriod) -eq 'Times.0') (Get-LastPeriod)
$wait = [int] ($next - (Get-Date)).TotalSeconds + 20
Test-Check 'the next time''s picture shows when its time comes' (Test-Shown $night $wait) "$(Get-Shown) at $(Get-Date -Format HH:mm:ss), due $($next.ToString('HH:mm:ss'))"
Test-Check 'the change came on time' ((Get-Date) -lt $next.AddSeconds(20)) (Get-Date -Format HH:mm:ss)

# A new time zone moves the times: Day's time is now, by the old clock, and Night's an hour later;
# a time zone several hours away puts the clock outside Day's hour.
$zoneBefore = (tzutil.exe /g).Trim()
$now = Get-Date
Write-WallpaperSettings ([ordered] @{
    enabled = $true; changeWith = 'Times'; location = 'TimeZone'; fit = 'Fill'
    times = @([ordered] @{ at = $now.AddMinutes(-30).ToString('HH:mm', [Globalization.CultureInfo]::InvariantCulture); picture = $day }, [ordered] @{ at = $now.AddMinutes(30).ToString('HH:mm', [Globalization.CultureInfo]::InvariantCulture); picture = $night })
})
Test-Check 'Times of day shows Day''s picture in the Sandbox''s time zone' (Test-Shown $day 15) (Get-Shown)
$zoneAway = if ([TimeZoneInfo]::Local.BaseUtcOffset.TotalHours -ge 0) { 'Hawaiian Standard Time' } else { 'Tokyo Standard Time' }
tzutil.exe /s $zoneAway
Test-Check 'a new time zone moves the times, and Night''s picture shows' (Test-Shown $night 15) "$(Get-Shown) in $(tzutil.exe /g)"
tzutil.exe /s $zoneBefore
Test-Check 'the Sandbox''s time zone brings Day''s picture back' (Test-Shown $day 15) "$(Get-Shown) in $(tzutil.exe /g)"

# Windows mode: Light's and Dark's pictures follow the mode.
function Write-ModeSettings($Light, $Dark, [object[]] $Displays = @(), [bool] $EachDisplay = $false, [bool] $Enabled = $true) {
    Write-WallpaperSettings ([ordered] @{
        enabled = $Enabled; changeWith = 'WindowsMode'; location = 'TimeZone'; fit = 'Fill'; eachDisplay = $EachDisplay; displays = $Displays
        times = @([ordered] @{ at = '07:00'; picture = $day }, [ordered] @{ at = '19:00'; picture = $night })
        windowsMode = [ordered] @{ light = $Light; dark = $Dark }
    })
}
Set-WindowsMode $false
Write-ModeSettings @{ picture = $day } @{ picture = $night }
Test-Check 'Windows mode shows Light''s picture' (Test-Shown $day 15) (Get-Shown)
Set-WindowsMode $true
Test-Check 'Dark''s picture shows when the mode turns dark' (Test-Shown $night) (Get-Shown)
Set-WindowsMode $false
Test-Check 'Light''s picture shows again when the mode turns light' (Test-Shown $day) (Get-Shown)

# Explorer restarts: the picture stays, and the host connects again.
$explorer = @(Get-Process explorer -ErrorAction SilentlyContinue | ForEach-Object { $_.Id })
Get-Process explorer -ErrorAction SilentlyContinue | Stop-Process -Force
$restarted = Wait-Until { @(Get-Process explorer -ErrorAction SilentlyContinue | Where-Object { $explorer -notcontains $_.Id }).Count -gt 0 } 20
if (-not $restarted) { Start-Process explorer.exe; [void] (Wait-Until { @(Get-Process explorer -ErrorAction SilentlyContinue).Count -gt 0 } 20) }
Test-Check 'Windows starts Explorer again' $restarted
Start-Sleep -Seconds 5
Test-Check 'the picture stays after Explorer restarts' ((Get-Shown) -eq $day) (Get-Shown)
Set-WindowsMode $true
Test-Check 'after Explorer restarts, Dark''s picture shows' (Test-Shown $night 15) (Get-Shown)

# Explorer not answering: the call waits, is cancelled after 10 seconds with a warning, and the
# next change sets the picture.
$shells = @(Get-Process explorer -ErrorAction SilentlyContinue)
$resumed = (Get-Date).AddSeconds(20)
Test-Check 'suspend Explorer for 20 seconds' ([WallpaperChecks]::SuspendFor([IntPtr[]] @($shells | ForEach-Object { $_.Handle }), 20000))
Write-ModeSettings @{ picture = $own } @{ picture = $night }
$cancelled = Wait-Until { (Get-HostLog) -match "Explorer didn't answer within 10 s, so Dynamic Wallpaper's call was cancelled\." } 18
$gaveUp = Wait-Until { (Get-HostLog) -match 'can''t change the background: "?ExplorerNotAnswering' } ([math]::Max(1, ($resumed - (Get-Date)).TotalSeconds - 1))
Test-Check 'a call Explorer doesn''t answer is cancelled with a warning' $cancelled
Test-Check 'the wallpaper thread gives up while Explorer still doesn''t answer' $gaveUp
Start-Sleep -Seconds ([math]::Max(0, ($resumed - (Get-Date)).TotalSeconds + 3))
Set-WindowsMode $false
Test-Check 'after Explorer answers again, the next change sets Light''s picture' (Test-Shown $own 15) (Get-Shown)

# A folder shows each of its pictures once before any repeats.
Write-ModeSettings @{ folder = $folder } @{ picture = $night }
$seen = @()
for ($i = 0; $i -lt 3; $i++) {
    if ($i -gt 0) {
        Set-WindowsMode $true
        [void] (Test-Shown $night)
        Set-WindowsMode $false
    }
    [void] (Wait-Until { $folderPictures -contains (Get-Shown) } 10)
    $seen += Get-Shown
}
Test-Check 'a folder shows each of its pictures once' ((@($seen | Sort-Object -Unique).Count -eq 3) -and -not ($seen | Where-Object { $folderPictures -notcontains $_ })) ($seen -join ', ')
$state = Get-Content (Join-Path $dataFolder 'dynamic-wallpaper.json') -Raw -ErrorAction SilentlyContinue
Test-Check 'dynamic-wallpaper.json keeps the folder''s place' ($state -match 'folders') $state

# A picture that's gone leaves the background as it is, and the page says so.
Write-ModeSettings @{ picture = $day } @{ picture = $gone }
[void] (Test-Shown $day 15)
Set-WindowsMode $true
Start-Sleep -Seconds 3
Test-Check 'a picture that''s gone leaves the background' ((Get-Shown) -eq $day) (Get-Shown)
Start-Process $appExe
[void] (Wait-Until { Get-AppWindow })
Start-Sleep -Seconds 2
[void] (Select-NavigationItem 'Dynamic Wallpaper')
Test-Check 'the page says Dark''s picture isn''t there' (Wait-Until { Find-AppElement 'Dark, Gone.jpg: It isn''t there anymore., in use' } 10) ((Get-WindowText) -join ' | ')
Save-Screenshot '17c-dynamic-wallpaper-picture-gone'
[void] (Close-SettingsApp)
Set-WindowsMode $false

# A display's own picture is set for it, and Other displays' for the rest.
if ($displays.Count -ge 1) {
    Write-ModeSettings @{ picture = $day; displays = @(@{ picture = $own }) } @{ picture = $night } $displays $true
    Test-Check 'the display gets its own picture' (Test-Shown $own 15) (Get-Shown)
}

# Turned off from the page, the Sandbox's background comes back.
Write-ModeSettings @{ picture = $day } @{ picture = $night }
[void] (Test-Shown $day 15)
Start-Process $appExe
[void] (Wait-Until { Get-AppWindow })
Start-Sleep -Seconds 2
[void] (Select-NavigationItem 'Dynamic Wallpaper')
Start-Sleep -Seconds 2
Test-Check 'turn off Dynamic Wallpaper on its page' (Set-Toggle 'Dynamic Wallpaper' $false)
Test-Check 'the host puts back the background' (Wait-Until { (Get-HostLog) -match 'Dynamic Wallpaper put back the background' } 10)
Test-Check 'the Sandbox''s background comes back' (Test-Shown $sandboxBackground 10) "$(Get-Shown), was $sandboxBackground"
Test-Check 'turning off deletes dynamic-wallpaper.json' (Wait-Until { -not (Test-Path (Join-Path $dataFolder 'dynamic-wallpaper.json')) } 5)
[void] (Close-SettingsApp)

# A solid color, put back when the feature turns off.
[WallpaperChecks]::SetColor(0x00805020)
Test-Check 'the Sandbox shows a solid color' (Wait-Until { -not [WallpaperChecks]::PictureShows() } 5)
Write-ModeSettings @{ picture = $day } @{ picture = $night }
Test-Check 'turned on over a solid color, Light''s picture shows' ((Test-Shown $day 15) -and [WallpaperChecks]::PictureShows()) (Get-Shown)
Write-ModeSettings @{ picture = $day } @{ picture = $night } -Enabled $false
Test-Check 'turned off, the solid color comes back' ((Wait-Until { -not [WallpaperChecks]::PictureShows() } 10) -and ([WallpaperChecks]::Color() -eq 0x00805020)) ('color 0x{0:X8}' -f [WallpaperChecks]::Color())

# Turned on again, a background chosen elsewhere: Dynamic Wallpaper steps aside, and Resume sets the
# picture again.
Write-ModeSettings @{ picture = $day } @{ picture = $night }
[void] (Test-Shown $day 15)
[WallpaperChecks]::Set($other)
Test-Check 'a background chosen elsewhere makes it step aside' (Wait-Until { (Get-HostLog) -match 'Someone else changed the background, so Dynamic Wallpaper stepped aside\.' } 10)
Set-WindowsMode $true
Start-Sleep -Seconds 3
Test-Check 'after stepping aside, it changes nothing' ((Get-Shown) -eq $other) (Get-Shown)
Start-Process $appExe
[void] (Wait-Until { Get-AppWindow })
Start-Sleep -Seconds 2
[void] (Select-NavigationItem 'Dynamic Wallpaper')
Test-Check 'the page notes it stepped aside' (Wait-Until { Find-AppText 'You chose another background in Windows, so Dynamic Wallpaper stopped changing it.' } 10)
Save-Screenshot '17d-dynamic-wallpaper-stepped-aside'
Test-Check 'select Resume' (Invoke-AppElement 'Resume')
Test-Check 'Resume sets the period''s picture again' (Test-Shown $night 15) (Get-Shown)
Test-Check 'the host logs that it resumed' ((Get-HostLog) -match 'Dynamic Wallpaper resumed\.')
Test-Check 'the note goes away' (Wait-Until { -not (Find-AppText 'You chose another background in Windows, so Dynamic Wallpaper stopped changing it.') } 5)

# Prevent changing desktop background (NoChangingWallPaper): nothing changes, and the page says the
# background is managed.
if (-not (Test-Path $desktopPolicies)) { [void] (New-Item $desktopPolicies -Force) }
[void] (New-ItemProperty $desktopPolicies -Name NoChangingWallPaper -Value 1 -PropertyType DWord -Force)
[WallpaperChecks]::SendSettingChange('Policy')
Set-WindowsMode $false
Test-Check 'the page says the background is managed' (Wait-Until { Find-AppText 'Your organization manages the desktop background, so Dynamic Wallpaper can''t change it.' } 15)
Test-Check 'with the policy, nothing changes' ((Get-Shown) -eq $night) (Get-Shown)
Save-Screenshot '17e-dynamic-wallpaper-managed'
Remove-ItemProperty $desktopPolicies -Name NoChangingWallPaper -ErrorAction SilentlyContinue
[WallpaperChecks]::SendSettingChange('Policy')
Test-Check 'without the policy, Light''s picture shows' (Test-Shown $day 15) (Get-Shown)

# Turned off after Resume, the background chosen elsewhere comes back.
Test-Check 'turn off Dynamic Wallpaper on its page again' (Set-Toggle 'Dynamic Wallpaper' $false)
Test-Check 'the background chosen elsewhere comes back' (Test-Shown $other 10) (Get-Shown)
[void] (Close-SettingsApp)

$logText = "$(Get-HostLog)`n$(Get-AppLog)"
Test-Check 'the logs hold no picture names' (-not ($logText -match 'Day\.jpg|Night\.jpg|Other\.jpg|Own\.png|Gone\.jpg|One\.png|Two\.png|Three\.png|DynamicWallpaperChecks|img0|img19'))

if ($null -ne $modeBefore) { [void] (New-ItemProperty $personalize -Name SystemUsesLightTheme -Value $modeBefore -PropertyType DWord -Force) }
