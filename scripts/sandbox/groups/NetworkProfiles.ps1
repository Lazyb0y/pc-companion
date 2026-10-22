# Network Profiles: turned on from Home, it starts with Original settings, made from Windows' proxy
# settings. The page adds profiles, from scratch and from the current settings, and edits, moves and
# deletes them in the profile editor, which refuses a bad proxy server, a script address that isn't
# a web address and a name in use; Use switches Windows' settings, and In use follows a change made
# elsewhere. The PC Companion icon's menu gets the Network profile submenu, with a radio mark on the
# profile in use: choosing a profile switches Windows' settings, read back with InternetQueryOption,
# other settings get a note, and Network Profiles settings opens the page, also in a settings app
# that's open. Edge, already open, follows a switch, with two fake proxies on the loopback address.
# Each Group Policy setting dims the profiles and leaves the settings as they are. Turned off, the
# submenu is gone and the settings stay. The logs hold no profile names or proxy servers. Detailed
# logging is on for the Debug entries. At the end, Windows' proxy settings are put back.
Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;

// Windows' proxy settings for the LAN, and the open menus' items, read from another process.
public static class NetworkProfilesCheck
{
    [StructLayout(LayoutKind.Sequential)]
    private struct Option { public int Id; public IntPtr Value; }

    [StructLayout(LayoutKind.Sequential)]
    private struct OptionList { public int Size; public IntPtr Connection; public int Count; public int Error; public IntPtr Options; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MenuItemInfo
    {
        public int Size; public uint Mask; public uint Type; public uint State; public uint Id;
        public IntPtr SubMenu; public IntPtr CheckedBitmap; public IntPtr UncheckedBitmap; public IntPtr Data;
        public IntPtr TypeData; public uint Count; public IntPtr Bitmap;
    }

    [DllImport("wininet.dll", SetLastError = true, EntryPoint = "InternetQueryOptionW")]
    private static extern bool QueryOption(IntPtr handle, int option, ref OptionList list, ref int size);

    [DllImport("wininet.dll", SetLastError = true, EntryPoint = "InternetSetOptionW")]
    private static extern bool SetOption(IntPtr handle, int option, ref OptionList list, int size);

    [DllImport("wininet.dll", SetLastError = true, EntryPoint = "InternetSetOptionW")]
    private static extern bool Notify(IntPtr handle, int option, IntPtr buffer, int size);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GlobalFree(IntPtr memory);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string className, string title);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr window);

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern int GetMenuItemCount(IntPtr menu);

    [DllImport("user32.dll", EntryPoint = "GetMenuItemInfoW")]
    private static extern bool GetMenuItemInfo(IntPtr menu, uint item, bool byPosition, ref MenuItemInfo info);

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect { public int Left; public int Top; public int Right; public int Bottom; }

    [DllImport("user32.dll")]
    private static extern bool GetMenuItemRect(IntPtr window, IntPtr menu, uint item, out Rect rect);

    private static IntPtr At(IntPtr start, int offset) { return new IntPtr(start.ToInt64() + offset); }

    // "flags|server|bypass|script", with the flags as the user set them: 1 direct, 2 proxy server, 4 setup script, 8 detect.
    public static string Read()
    {
        int optionSize = Marshal.SizeOf(typeof(Option));
        IntPtr options = Marshal.AllocHGlobal(optionSize * 4);
        try
        {
            int[] ids = { 10, 2, 3, 4 };
            for (int i = 0; i < 4; i++)
            {
                Option empty = new Option();
                empty.Id = ids[i];
                Marshal.StructureToPtr(empty, At(options, i * optionSize), false);
            }

            OptionList list = new OptionList();
            list.Size = Marshal.SizeOf(typeof(OptionList));
            list.Count = 4;
            list.Options = options;
            int size = list.Size;
            if (!QueryOption(IntPtr.Zero, 75, ref list, ref size))
            {
                throw new Win32Exception();
            }

            string[] values = new string[4];
            for (int i = 0; i < 4; i++)
            {
                Option option = (Option)Marshal.PtrToStructure(At(options, i * optionSize), typeof(Option));
                if (i == 0)
                {
                    values[0] = (option.Value.ToInt64() & 0xFFFFFFFF).ToString();
                }
                else
                {
                    values[i] = option.Value == IntPtr.Zero ? "" : Marshal.PtrToStringUni(option.Value);
                    if (option.Value != IntPtr.Zero) { GlobalFree(option.Value); }
                }
            }

            return string.Join("|", values);
        }
        finally
        {
            Marshal.FreeHGlobal(options);
        }
    }

    // Changes the settings as another program does: the flags, and each value that isn't "-".
    public static void Write(int flags, string server, string bypass, string script)
    {
        int optionSize = Marshal.SizeOf(typeof(Option));
        IntPtr options = Marshal.AllocHGlobal(optionSize * 4);
        List<IntPtr> texts = new List<IntPtr>();
        try
        {
            Option first = new Option();
            first.Id = 1;
            first.Value = new IntPtr(flags);
            Marshal.StructureToPtr(first, options, false);
            int count = 1;
            string[] values = { server, bypass, script };
            int[] ids = { 2, 3, 4 };
            for (int i = 0; i < 3; i++)
            {
                if (values[i] == "-") { continue; }
                IntPtr text = Marshal.StringToHGlobalUni(values[i]);
                texts.Add(text);
                Option option = new Option();
                option.Id = ids[i];
                option.Value = text;
                Marshal.StructureToPtr(option, At(options, count * optionSize), false);
                count++;
            }

            OptionList list = new OptionList();
            list.Size = Marshal.SizeOf(typeof(OptionList));
            list.Count = count;
            list.Options = options;
            if (!SetOption(IntPtr.Zero, 75, ref list, list.Size))
            {
                throw new Win32Exception();
            }

            Notify(IntPtr.Zero, 39, IntPtr.Zero, 0);
            Notify(IntPtr.Zero, 37, IntPtr.Zero, 0);
        }
        finally
        {
            foreach (IntPtr text in texts) { Marshal.FreeHGlobal(text); }
            Marshal.FreeHGlobal(options);
        }
    }

    // The windows of the menus that are open.
    public static IntPtr[] Menus()
    {
        List<IntPtr> menus = new List<IntPtr>();
        IntPtr window = IntPtr.Zero;
        while ((window = FindWindowEx(IntPtr.Zero, window, "#32768", null)) != IntPtr.Zero)
        {
            if (IsWindowVisible(window)) { menus.Add(window); }
        }

        return menus.ToArray();
    }

    // The screen point in the middle of the item with the text shown, in an open menu window, or "" when there's none.
    public static string ItemPoint(IntPtr window, string text)
    {
        IntPtr menu = SendMessage(window, 0x01E1, IntPtr.Zero, IntPtr.Zero); // MN_GETHMENU
        if (menu == IntPtr.Zero) { return ""; }
        IntPtr buffer = Marshal.AllocHGlobal(512);
        try
        {
            int count = GetMenuItemCount(menu);
            for (int i = 0; i < count; i++)
            {
                MenuItemInfo info = new MenuItemInfo();
                info.Size = Marshal.SizeOf(typeof(MenuItemInfo));
                info.Mask = 0x40 | 0x100; // string, type
                info.TypeData = buffer;
                info.Count = 255;
                GetMenuItemInfo(menu, (uint)i, true, ref info);
                if ((info.Type & 0x800) != 0 || Shown(Marshal.PtrToStringUni(buffer)) != text) { continue; }
                Rect rect;
                if (!GetMenuItemRect(IntPtr.Zero, menu, (uint)i, out rect)) { return ""; }
                return ((rect.Left + rect.Right) / 2) + "," + ((rect.Top + rect.Bottom) / 2);
            }

            return "";
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    // An item's text as the menu shows it: && is a &, and a single & marks the access key.
    private static string Shown(string text)
    {
        System.Text.StringBuilder shown = new System.Text.StringBuilder();
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == '&')
            {
                if (i + 1 < text.Length && text[i + 1] == '&') { shown.Append('&'); i++; }
                continue;
            }

            shown.Append(text[i]);
        }

        return shown.ToString();
    }

    // A menu window's items, such as "Home (radio) (checked)", "note (dimmed)", "Network profile >" or "-".
    public static string Items(IntPtr window)
    {
        IntPtr menu = SendMessage(window, 0x01E1, IntPtr.Zero, IntPtr.Zero); // MN_GETHMENU
        if (menu == IntPtr.Zero) { return "no menu"; }
        List<string> items = new List<string>();
        IntPtr buffer = Marshal.AllocHGlobal(512);
        try
        {
            int count = GetMenuItemCount(menu);
            for (int i = 0; i < count; i++)
            {
                MenuItemInfo info = new MenuItemInfo();
                info.Size = Marshal.SizeOf(typeof(MenuItemInfo));
                info.Mask = 0x1 | 0x4 | 0x40 | 0x100; // state, submenu, string, type
                info.TypeData = buffer;
                info.Count = 255;
                GetMenuItemInfo(menu, (uint)i, true, ref info);
                if ((info.Type & 0x800) != 0) { items.Add("-"); continue; }
                string text = Shown(Marshal.PtrToStringUni(buffer));
                if (info.SubMenu != IntPtr.Zero) { text += " >"; }
                if ((info.Type & 0x200) != 0) { text += " (radio)"; }
                if ((info.State & 0x8) != 0) { text += " (checked)"; }
                if ((info.State & 0x3) != 0) { text += " (dimmed)"; }
                items.Add(text);
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }

        return string.Join(" | ", items.ToArray());
    }
}
'@

$detailedUntil = [DateTimeOffset]::Now.AddHours(1).ToString('o')
$menuItemCondition = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::MenuItem)
$userPolicy = 'HKCU:\Software\Policies\Microsoft\Internet Explorer\Control Panel'
$machinePolicy = 'HKLM:\Software\Policies\Microsoft\Internet Explorer\Control Panel'
$perMachinePolicy = 'HKLM:\Software\Policies\Microsoft\Windows\CurrentVersion\Internet Settings'

function Get-NetworkProfilesSettings { (Get-Content (Join-Path $dataFolder 'settings.json') -Raw | ConvertFrom-Json).features.networkProfiles }
function Get-AppLog { (Get-ChildItem $logs -Filter 'app-*.log' -ErrorAction SilentlyContinue | ForEach-Object { Get-Content $_.FullName -Raw }) -join "`n" }
function Get-Proxy { [NetworkProfilesCheck]::Read() }

# The items of the open menu that has an item named $Containing, or 'no menu'.
function Get-MenuText([string] $Containing) {
    foreach ($window in [NetworkProfilesCheck]::Menus()) {
        $items = [NetworkProfilesCheck]::Items($window)
        if ($items -like "*$Containing*") { return $items }
    }
    return 'no menu'
}

# The names UI Automation, which screen readers use, gives the items of each open menu window.
function Get-MenuItemNames {
    @(foreach ($window in [NetworkProfilesCheck]::Menus()) {
        $element = [System.Windows.Automation.AutomationElement]::FromHandle($window)
        $names = @($element.FindAll([System.Windows.Automation.TreeScope]::Descendants, $menuItemCondition) | ForEach-Object { $_.Current.Name })
        "$($element.Current.ClassName) '$($element.Current.Name)': $($names -join ', ')"
    })
}

# Chooses an item of any open menu, such as a submenu's, by the text it shows, with the mouse.
function Select-MenuItem([string] $Name) {
    $script:point = ''
    $found = Wait-Until {
        foreach ($window in [NetworkProfilesCheck]::Menus()) {
            $script:point = [NetworkProfilesCheck]::ItemPoint($window, $Name)
            if ($script:point) { break }
        }
        [bool] $script:point
    } 10
    if (-not $found) { return $false }
    $x, $y = $script:point -split ','
    if (-not (Move-Pointer ([int] $x) ([int] $y))) { return $false }
    [void] [Win32]::LeftButton($true)
    [void] [Win32]::LeftButton($false)
    return $true
}

# Opens the PC Companion icon's menu, then its Network profile submenu.
function Open-ProfileMenu {
    if (-not (Send-TrayEvent $WM_CONTEXTMENU $menuX $menuY)) { return $false }
    if (-not (Wait-Until { (Get-MenuText 'Exit') -ne 'no menu' } 5)) { return $false }
    Start-Sleep -Milliseconds 500
    if (-not (Select-MenuItem 'Network profile')) { return $false }
    $opened = Wait-Until { (Get-MenuText 'Network Profiles settings') -ne 'no menu' } 5
    Start-Sleep -Milliseconds 300
    return $opened
}

function Write-NetworkProfilesSettings([bool] $Enabled, [object[]] $Profiles) {
    $settings = @{
        schemaVersion = 1
        general = @{ theme = 'Light' }
        updates = @{ checkAutomatically = $false }
        diagnostics = @{ detailedLoggingUntil = $detailedUntil }
        features = @{ networkProfiles = @{ enabled = $Enabled; profiles = $Profiles } }
    }
    Write-Settings ($settings | ConvertTo-Json -Depth 8)
    Start-Sleep -Seconds 3
}

function Set-Policy([string] $Key, [string] $Name, [int] $Value) {
    New-Item $Key -Force | Out-Null
    New-ItemProperty $Key -Name $Name -Value $Value -PropertyType DWord -Force | Out-Null
}

function Clear-Policies {
    foreach ($key in $userPolicy, $machinePolicy) { if (Test-Path $key) { Remove-ItemProperty $key -Name Proxy, Autoconfig -ErrorAction SilentlyContinue } }
    if (Test-Path $perMachinePolicy) { Remove-ItemProperty $perMachinePolicy -Name ProxySettingsPerUser -ErrorAction SilentlyContinue }
}

# Windows' settings to start from: a proxy server, which Original settings copies.
Clear-Policies
[NetworkProfilesCheck]::Write(3, 'start.test:3128', '*.start.test;<local>', '-')
Test-Check 'Windows starts with a proxy server' ((Get-Proxy) -eq '3|start.test:3128|*.start.test;<local>|') (Get-Proxy)

[void] (Close-SettingsApp)
Write-Settings (@{ schemaVersion = 1; general = @{ theme = 'Light' }; updates = @{ checkAutomatically = $false }; diagnostics = @{ detailedLoggingUntil = $detailedUntil } } | ConvertTo-Json -Depth 4)
Start-Sleep -Seconds 2
Start-Process $appExe
[void] (Wait-Until { Get-AppWindow })
Start-Sleep -Seconds 3

# First turn-on, from Home: Original settings, with Windows' settings.
Test-Check 'turn on Network Profiles on Home' (Set-Toggle 'Network Profiles' $true)
Start-Sleep -Seconds 2
$saved = Get-NetworkProfilesSettings
$original = @($saved.profiles)[0]
Test-Check 'turning it on adds Original settings with Windows'' settings' (($saved.enabled -eq $true) -and (@($saved.profiles).Count -eq 1) -and ($original.name -eq 'Original settings') -and $original.proxy.useServer -and ($original.proxy.server -eq 'start.test:3128') -and ((@($original.proxy.exceptions) -join ';') -eq '*.start.test') -and $original.proxy.bypassLocal) "saved: $($saved | ConvertTo-Json -Compress -Depth 6)"
Test-Check 'select Network Profiles' (Select-NavigationItem 'Network Profiles')
Start-Sleep -Seconds 2
$text = Get-WindowText
Test-Check 'Network Profiles shows its settings' (($text -contains 'Profiles') -and ($text -contains 'Original settings') -and ($text -contains 'Windows proxy settings') -and ($text -contains 'Permissions & data')) ($text -join ' | ')
Test-Check 'Original settings is in use' (Wait-Until { Find-AppText 'In use' } 5)
Test-Check 'Original settings says what it does' (Wait-Until { Find-AppText 'Proxy server start.test:3128' } 3)
Save-Screenshot '14a-network-profiles-english'

# A new profile with a proxy server: Save is off until it can be used.
Test-Check 'Add profile opens the profile editor' ((Invoke-AppElement 'Add profile') -and (Wait-Until { Find-AppElement 'Add a profile' } 5))
Start-Sleep -Seconds 1
Test-Check 'Save is off for an empty profile' (-not (Test-AppEnabled 'Save'))
[void] (Set-AppBox 'Name' 'Office')
Test-Check 'turn on Use a proxy server' (Set-Toggle 'Use a proxy server' $true)
[void] (Set-AppBox 'Proxy server' 'not a server')
Test-Check 'a proxy server that isn''t address:port is refused' ((Wait-Until { Find-AppText 'Enter an address and port, such as proxy.example.com:8080, or a server for each protocol.' } 3) -and -not (Test-AppEnabled 'Save'))
[void] (Set-AppBox 'Proxy server' 'proxy.office.test:8080')
[void] (Set-AppBox 'Exceptions' '*.office.test;10.*')
Test-Check 'check Don''t use the proxy server for local addresses' (Set-Toggle 'Don''t use the proxy server for local (intranet) addresses' $true)
Test-Check 'Save is on once the profile can be used' (Wait-Until { Test-AppEnabled 'Save' } 3)
Save-Screenshot '14b-network-profiles-editor'
Test-Check 'Save the profile' (Invoke-AppElement 'Save')
Start-Sleep -Seconds 2
$office = @((Get-NetworkProfilesSettings).profiles)[1]
Test-Check 'the page saves the profile' (($office.name -eq 'Office') -and $office.proxy.useServer -and ($office.proxy.server -eq 'proxy.office.test:8080') -and ((@($office.proxy.exceptions) -join ';') -eq '*.office.test;10.*') -and $office.proxy.bypassLocal -and -not $office.proxy.autoDetect) "saved: $($office | ConvertTo-Json -Compress -Depth 5)"

# A profile that detects settings, after a script address that isn't a web address is refused.
[void] (Invoke-AppElement 'Add profile')
[void] (Wait-Until { Find-AppElement 'Add a profile' } 5)
Start-Sleep -Seconds 1
[void] (Set-AppBox 'Name' 'Home')
[void] (Set-Toggle 'Automatically detect settings' $true)
[void] (Set-Toggle 'Use setup script' $true)
[void] (Set-AppBox 'Script address' 'file:///C:/proxy.pac')
Test-Check 'a script address that isn''t a web address is refused' ((Wait-Until { Find-AppText 'Enter a web address that starts with http:// or https://.' } 3) -and -not (Test-AppEnabled 'Save'))
[void] (Set-Toggle 'Use setup script' $false)
Test-Check 'Save is on once the script is off' (Wait-Until { Test-AppEnabled 'Save' } 3)
[void] (Invoke-AppElement 'Save')
Start-Sleep -Seconds 2
$homeProfile = @((Get-NetworkProfilesSettings).profiles)[2]
Test-Check 'the page saves the second profile, keeping the script address' (($homeProfile.name -eq 'Home') -and $homeProfile.proxy.autoDetect -and -not $homeProfile.proxy.useScript -and ($homeProfile.proxy.scriptAddress -eq 'file:///C:/proxy.pac')) "saved: $($homeProfile | ConvertTo-Json -Compress -Depth 5)"

# A name in use, ignoring case.
[void] (Invoke-AppElement 'Add profile')
[void] (Wait-Until { Find-AppElement 'Add a profile' } 5)
Start-Sleep -Seconds 1
[void] (Set-AppBox 'Name' 'HOME')
Test-Check 'a name another profile has is refused' ((Wait-Until { Find-AppText 'Another profile has this name.' } 3) -and -not (Test-AppEnabled 'Save'))
[void] (Invoke-AppElement 'Cancel')
Start-Sleep -Seconds 1

# Add from current settings: the editor with Windows' settings, for a name.
Test-Check 'Add from current settings opens the editor' ((Invoke-AppElement 'Add from current settings') -and (Wait-Until { Find-AppElement 'Add a profile' } 5))
Start-Sleep -Seconds 1
Test-Check 'the editor shows Windows'' current settings' (((Get-AppBox 'Proxy server') -eq 'start.test:3128') -and ((Get-AppBox 'Exceptions') -eq '*.start.test')) "server: $(Get-AppBox 'Proxy server'), exceptions: $(Get-AppBox 'Exceptions')"
[void] (Set-AppBox 'Name' 'Copy')
[void] (Invoke-AppElement 'Save')
Start-Sleep -Seconds 2
Test-Check 'the page saves the copy' ((@((Get-NetworkProfilesSettings).profiles)[3].name -eq 'Copy') -and (@((Get-NetworkProfilesSettings).profiles)[3].proxy.server -eq 'start.test:3128'))

# Edit, move and delete.
Test-Check 'Edit opens the profile' ((Invoke-AppElement 'Edit Office') -and (Wait-Until { Find-AppElement 'Edit the profile' } 5))
Start-Sleep -Seconds 1
Test-Check 'the editor shows the profile' (((Get-AppBox 'Name') -eq 'Office') -and ((Get-AppBox 'Proxy server') -eq 'proxy.office.test:8080')) "name: $(Get-AppBox 'Name')"
[void] (Set-AppBox 'Name' 'Office & lab')
[void] (Invoke-AppElement 'Save')
Start-Sleep -Seconds 2
Test-Check 'the page saves the edited name' (@((Get-NetworkProfilesSettings).profiles)[1].name -eq 'Office & lab')
Test-Check 'More options opens the profile''s menu' (Invoke-AppElement 'More options for Home')
Start-Sleep -Seconds 1
Test-Check 'Move up moves the profile' (Invoke-AppElement 'Move up')
Start-Sleep -Seconds 2
Test-Check 'the page saves the new order' ((@((Get-NetworkProfilesSettings).profiles) | ForEach-Object { $_.name }) -join ', ' -eq 'Original settings, Home, Office & lab, Copy') ((@((Get-NetworkProfilesSettings).profiles) | ForEach-Object { $_.name }) -join ', ')
[void] (Invoke-AppElement 'More options for Copy')
Start-Sleep -Seconds 1
Test-Check 'Delete asks first' ((Invoke-AppElement 'Delete') -and (Wait-Until { Find-AppElement 'Delete Copy?' } 5))
Start-Sleep -Seconds 1
[void] (Invoke-AppElement 'Delete')
Start-Sleep -Seconds 2
Test-Check 'the page deletes the profile' (@((Get-NetworkProfilesSettings).profiles).Count -eq 3)

# Use switches Windows' settings; In use follows them, also when another program changes them.
Test-Check 'Use switches to the profile' (Invoke-AppElement 'Use Office & lab')
Test-Check 'Windows uses the profile''s settings' (Wait-Until { (Get-Proxy) -eq '3|proxy.office.test:8080|*.office.test;10.*;<local>|' } 5) (Get-Proxy)
Test-Check 'In use moves to the profile' (Wait-Until { -not (Find-AppElement 'Use Office & lab') } 5)
Save-Screenshot '14c-network-profiles-in-use'
[NetworkProfilesCheck]::Write(9, '-', '-', '-')
Test-Check 'In use follows a change another program makes' (Wait-Until { (-not (Find-AppElement 'Use Home')) -and (Find-AppElement 'Use Office & lab') } 5)
Test-Check 'Open opens Windows'' Proxy page' ((Invoke-AppElement 'Open') -and (Wait-Until { (Get-Processes 'SystemSettings').Count -gt 0 } 15))
Start-Sleep -Seconds 2
Get-Processes 'SystemSettings' | ForEach-Object { Stop-Process -Id $_.Id -Force -ErrorAction SilentlyContinue }
[void] (Close-SettingsApp)

# The menu.
if ((Get-Processes 'PCCompanion.Host').Count -eq 0) { Start-Process $hostExe }
Test-Check 'the host starts Network Profiles' (Wait-Until { (Get-HostLog) -match 'Network Profiles started\.' } 15)
Test-Check 'the host finds the profile in use' (Wait-Until { (Get-HostLog) -match "Windows' proxy settings match network profile 2\." } 10)
Test-Check 'the PC Companion icon''s menu has the Network profile submenu' (Open-ProfileMenu)
$items = Get-MenuText 'Exit'
Test-Check 'the menu has the submenu above Settings and Exit' ($items -eq 'Network profile > | - | Settings | Exit') $items
$items = Get-MenuText 'Network Profiles settings'
Test-Check 'the submenu has the profiles, with the one in use marked' ($items -eq 'Original settings | Home (radio) (checked) | Office & lab | - | Network Profiles settings') $items
Save-Screenshot '14d-network-profiles-menu'
# What a screen reader gets, which follows the menu as it opens: UI Automation reads the menu first,
# then the submenu that opens from it.
[void] (Close-TrayMenu)
[void] (Send-TrayEvent $WM_CONTEXTMENU $menuX $menuY)
[void] (Wait-Until { (Get-MenuText 'Exit') -ne 'no menu' } 5)
Start-Sleep -Milliseconds 500
$topNames = Get-MenuItemNames
[void] (Select-MenuItem 'Network profile')
[void] (Wait-Until { (Get-MenuText 'Network Profiles settings') -ne 'no menu' } 5)
Start-Sleep -Seconds 1
$names = Get-MenuItemNames
Test-Check 'screen readers get the profiles'' names as written' (($names -join ' | ') -match 'Office & lab') "first: $($topNames -join ' | '); then: $($names -join ' | ')"
Test-Check 'choose a profile in the submenu' (Select-MenuItem 'Office & lab')
Test-Check 'choosing it switches Windows'' settings' (Wait-Until { (Get-Proxy) -eq '3|proxy.office.test:8080|*.office.test;10.*;<local>|' } 5) (Get-Proxy)
Test-Check 'the host logs the switch by number' (Wait-Until { (Get-HostLog) -match 'Switched to network profile 3\.' } 5)
[void] (Open-ProfileMenu)
$items = Get-MenuText 'Network Profiles settings'
Test-Check 'the radio mark moves to the profile chosen' ($items -eq 'Original settings | Home | Office & lab (radio) (checked) | - | Network Profiles settings') $items
[void] (Close-TrayMenu)
[NetworkProfilesCheck]::Write(3, 'other.test:3128', '', '-')
Start-Sleep -Seconds 2
[void] (Open-ProfileMenu)
$items = Get-MenuText 'Network Profiles settings'
Test-Check 'other settings get a note and no mark' ($items -eq 'Other proxy settings in use (dimmed) | Original settings | Home | Office & lab | - | Network Profiles settings') $items

# Network Profiles settings opens the page, in a new settings app and in one that's open.
Test-Check 'choose Network Profiles settings' (Select-MenuItem 'Network Profiles settings')
Test-Check 'the settings app opens on the Network Profiles page' ((Wait-Until { Get-AppWindow } 15) -and (Wait-Until { Find-AppElement 'Add from current settings' } 10))
Test-Check 'select Home' (Select-NavigationItem 'Home')
Start-Sleep -Seconds 2
[void] (Open-ProfileMenu)
[void] (Select-MenuItem 'Network Profiles settings')
Test-Check 'a settings app that''s open shows the page' (Wait-Until { Find-AppElement 'Add from current settings' } 10)
Test-Check 'the settings app logs both requests' (Wait-Until { (Get-MatchCount (Get-AppLog) 'Asked to show the networkProfiles page\.') -ge 2 } 5)
[void] (Close-SettingsApp)

# Edge, already open, follows a switch: two fake proxies on the loopback address.
$listen = {
    param([int] $Port, [string] $File)
    $listener = New-Object System.Net.Sockets.TcpListener ([System.Net.IPAddress]::Loopback, $Port)
    $listener.Start()
    Add-Content -Path $File -Value "listening on $Port"
    $end = (Get-Date).AddSeconds(150)
    while ((Get-Date) -lt $end) {
        if (-not $listener.Pending()) { Start-Sleep -Milliseconds 50; continue }
        $client = $listener.AcceptTcpClient()
        try {
            $stream = $client.GetStream()
            $stream.ReadTimeout = 2000
            $buffer = New-Object byte[] 4096
            $read = $stream.Read($buffer, 0, $buffer.Length)
            Add-Content -Path $File -Value (([System.Text.Encoding]::ASCII.GetString($buffer, 0, $read) -split "`r`n")[0])
            $body = [System.Text.Encoding]::ASCII.GetBytes("<html><body>proxy $Port</body></html>")
            $head = [System.Text.Encoding]::ASCII.GetBytes("HTTP/1.1 200 OK`r`nContent-Type: text/html`r`nContent-Length: $($body.Length)`r`nConnection: close`r`n`r`n")
            $stream.Write($head, 0, $head.Length)
            $stream.Write($body, 0, $body.Length)
        }
        catch { }
        finally { $client.Close() }
    }
    $listener.Stop()
}
$listenA = Join-Path $results 'proxy-a.txt'
$listenB = Join-Path $results 'proxy-b.txt'
$jobs = @((Start-Job -ScriptBlock $listen -ArgumentList 18081, $listenA), (Start-Job -ScriptBlock $listen -ArgumentList 18082, $listenB))
[void] (Wait-Until { (Test-Path $listenA) -and (Test-Path $listenB) } 20)
Write-NetworkProfilesSettings $true @(
    @{ name = 'Proxy A'; proxy = @{ useServer = $true; server = '127.0.0.1:18081' } },
    @{ name = 'Proxy B'; proxy = @{ useServer = $true; server = '127.0.0.1:18082' } })
[void] (Open-ProfileMenu)
[void] (Select-MenuItem 'Proxy A')
Test-Check 'the menu switches to proxy A' (Wait-Until { (Get-Proxy) -like '3|127.0.0.1:18081|*' } 5) (Get-Proxy)
$edge = 'C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe'
$edgeProfile = 'C:\Users\Public\edge-network-profiles'
Start-Process $edge -ArgumentList '--no-first-run', '--no-default-browser-check', "--user-data-dir=$edgeProfile", 'http://first.network-profiles.test/'
Test-Check 'Edge uses proxy A' (Wait-Until { (Get-Content $listenA -Raw) -match 'GET http://first\.network-profiles\.test/' } 40)
[void] (Open-ProfileMenu)
[void] (Select-MenuItem 'Proxy B')
Test-Check 'the menu switches to proxy B' (Wait-Until { (Get-Proxy) -like '3|127.0.0.1:18082|*' } 5) (Get-Proxy)
Start-Sleep -Seconds 1
Start-Process $edge -ArgumentList '--no-first-run', '--no-default-browser-check', "--user-data-dir=$edgeProfile", 'http://second.network-profiles.test/'
Test-Check 'Edge, already open, uses proxy B after the switch' (Wait-Until { (Get-Content $listenB -Raw) -match 'GET http://second\.network-profiles\.test/' } 40) "proxy A: $((Get-Content $listenA) -join ' / ')"
Save-Screenshot '14e-network-profiles-edge'
Get-Processes 'msedge' | ForEach-Object { Stop-Process -Id $_.Id -Force -ErrorAction SilentlyContinue }
$jobs | Stop-Job -PassThru | Remove-Job -Force

# Group Policy: each setting dims the profiles and leaves the settings as they are. The policies that
# lock the settings aren't watched, so another program's change makes the host read them.
$before = Get-Proxy
$policies = @(
    @{ Name = 'Prevent changing proxy settings for the user'; Key = $userPolicy; Value = 'Proxy'; Set = 1; Restriction = 'PreventChanges' },
    @{ Name = 'Prevent changing proxy settings for the computer'; Key = $machinePolicy; Value = 'Proxy'; Set = 1; Restriction = 'PreventChanges' },
    @{ Name = 'Prevent changing the setup script for the user'; Key = $userPolicy; Value = 'Autoconfig'; Set = 1; Restriction = 'PreventScriptChanges' },
    @{ Name = 'Make proxy settings per-machine'; Key = $perMachinePolicy; Value = 'ProxySettingsPerUser'; Set = 0; Restriction = 'PerMachine' })
# Serilog's file log quotes enum values.
function Get-RestrictionsCount([string] $Restrictions) { Get-MatchCount (Get-HostLog) "Group Policy restrictions on the proxy settings: ""?$Restrictions""?\." }
foreach ($policy in $policies) {
    $restrictionsBefore = Get-RestrictionsCount $policy.Restriction
    Set-Policy $policy.Key $policy.Value $policy.Set
    if ($policy.Restriction -ne 'PerMachine') { [NetworkProfilesCheck]::Write(3, '127.0.0.1:18082', '-', '-') }
    Test-Check "$($policy.Name): the host finds the policy" (Wait-Until { (Get-RestrictionsCount $policy.Restriction) -gt $restrictionsBefore } 10)
    [void] (Open-ProfileMenu)
    $items = Get-MenuText 'Network Profiles settings'
    Test-Check "$($policy.Name): the profiles are dimmed" ($items -like 'Your organization manages the proxy settings (dimmed) | Proxy A*(dimmed) | Proxy B*(dimmed) | - | Network Profiles settings') $items
    [void] (Close-TrayMenu)
    if ($policy.Restriction -eq 'PreventChanges' -and $policy.Key -eq $userPolicy) {
        Start-Process $appExe
        [void] (Wait-Until { Get-AppWindow })
        Start-Sleep -Seconds 3
        [void] (Select-NavigationItem 'Network Profiles')
        Test-Check 'the page says the organization manages the settings' (Wait-Until { Find-AppText 'Your organization manages the proxy settings, so profiles can''t be used. You can still edit them.' } 5)
        Test-Check 'and Use is off' (-not (Test-AppEnabled 'Use Proxy A'))
        Save-Screenshot '14f-network-profiles-managed'
        [void] (Close-SettingsApp)
    }
    $noneBefore = Get-RestrictionsCount 'None'
    Clear-Policies
    [NetworkProfilesCheck]::Write(3, '127.0.0.1:18082', '-', '-')
    Test-Check "$($policy.Name): removed, the host finds none" (Wait-Until { (Get-RestrictionsCount 'None') -gt $noneBefore } 10)
}
Test-Check 'the policies left Windows'' settings as they were' ((Get-Proxy) -eq $before) "before: $before, after: $(Get-Proxy)"

# Turned off: the submenu is gone and the settings stay.
$before = Get-Proxy
Write-NetworkProfilesSettings $false @(@{ name = 'Proxy A'; proxy = @{ useServer = $true; server = '127.0.0.1:18081' } })
Test-Check 'turning it off stops it' (Wait-Until { (Get-HostLog) -match 'Network Profiles stopped\.' } 10)
[void] (Send-TrayEvent $WM_CONTEXTMENU $menuX $menuY)
[void] (Wait-Until { (Get-MenuText 'Exit') -ne 'no menu' } 5)
$items = Get-MenuText 'Exit'
Test-Check 'turned off, the menu has no submenu' ($items -eq 'Settings | Exit') $items
[void] (Close-TrayMenu)
Test-Check 'turned off, Windows'' settings stay' ((Get-Proxy) -eq $before) (Get-Proxy)

$allLogs = (Get-HostLog) + (Get-AppLog)
Test-Check 'the logs hold no profile names or proxy servers' (($allLogs -notmatch 'office\.test') -and ($allLogs -notmatch 'start\.test') -and ($allLogs -notmatch '127\.0\.0\.1:1808') -and ($allLogs -notmatch 'Office & lab'))

# Windows' settings as the Sandbox started.
Clear-Policies
[NetworkProfilesCheck]::Write(9, '', '', '')
Stop-All
