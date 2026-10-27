# Display Profiles in a remote session, which Windows Sandbox is: Windows reports the session's
# own display and refuses changes, so PC Companion neither reads nor switches the displays here.
# Turned on from Home, it starts with no profiles. The page shows the remote session note with
# Add from current setup and Use off, shows profiles written into the settings with a line for
# each display and a picture, and renames, moves and deletes them, refusing a name in use, with
# keyboard focus staying on the profile. The PC Companion icon's menu gets the Display profile
# submenu with the note and the profiles dimmed, read with UI Automation, and Display Profiles
# settings opens the page. Turned off, the submenu is gone. The logs hold no profile or display
# names. Detailed logging is on for the Debug entries.
$detailedUntil = [DateTimeOffset]::Now.AddHours(1).ToString('o')
$menuCondition = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ClassNameProperty, '#32768')
$menuItemCondition = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::MenuItem)
$remoteNote = "Display profiles aren't available in a remote session"

function Get-DisplayProfilesSettings { (Get-Content (Join-Path $dataFolder 'settings.json') -Raw | ConvertFrom-Json).features.displayProfiles }
function Get-AppLog { (Get-ChildItem $logs -Filter 'app-*.log' -ErrorAction SilentlyContinue | ForEach-Object { Get-Content $_.FullName -Raw }) -join "`n" }
function Get-FocusedName { try { [System.Windows.Automation.AutomationElement]::FocusedElement.Current.Name } catch { '' } }

# The items of each open menu, as screen readers get them: "name" or "name (dimmed)", one menu per string.
function Get-MenuItems {
    @(foreach ($menu in [System.Windows.Automation.AutomationElement]::RootElement.FindAll([System.Windows.Automation.TreeScope]::Children, $menuCondition)) {
        @($menu.FindAll([System.Windows.Automation.TreeScope]::Descendants, $menuItemCondition) | ForEach-Object {
            if ($_.Current.IsEnabled) { $_.Current.Name } else { "$($_.Current.Name) (dimmed)" }
        }) -join ' | '
    })
}

# Chooses an item of any open menu, such as the submenu's, by its name, with the mouse.
function Select-AnyMenuItem([string] $Name) {
    $itemCondition = New-Object System.Windows.Automation.AndCondition(
        (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, $Name)),
        $menuItemCondition)
    $script:menuItem = $null
    $found = Wait-Until {
        foreach ($menu in [System.Windows.Automation.AutomationElement]::RootElement.FindAll([System.Windows.Automation.TreeScope]::Children, $menuCondition)) {
            $script:menuItem = $menu.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $itemCondition)
            if ($script:menuItem) { break }
        }
        $null -ne $script:menuItem
    } 10
    if (-not $found) { return $false }
    $rectangle = $script:menuItem.Current.BoundingRectangle
    if (-not (Move-Pointer ([int] ($rectangle.X + $rectangle.Width / 2)) ([int] ($rectangle.Y + $rectangle.Height / 2)))) { return $false }
    [void] [Win32]::LeftButton($true)
    [void] [Win32]::LeftButton($false)
    return $true
}

# Opens the PC Companion icon's menu, then its Display profile submenu.
function Open-ProfileMenu {
    if (-not (Send-TrayEvent $WM_CONTEXTMENU $menuX $menuY)) { return $false }
    if (-not (Wait-Until { (Get-MenuItems) -match 'Exit' } 5)) { return $false }
    Start-Sleep -Milliseconds 500
    if (-not (Select-AnyMenuItem 'Display profile')) { return $false }
    $opened = Wait-Until { (Get-MenuItems) -match 'Display Profiles settings' } 5
    Start-Sleep -Milliseconds 300
    return $opened
}

function Write-DisplayProfilesSettings([bool] $Enabled, [object[]] $Profiles) {
    $settings = @{
        schemaVersion = 1
        general = @{ theme = 'Light' }
        updates = @{ checkAutomatically = $false }
        diagnostics = @{ detailedLoggingUntil = $detailedUntil }
        features = @{ displayProfiles = @{ enabled = $Enabled; profiles = $Profiles } }
    }
    Write-Settings ($settings | ConvertTo-Json -Depth 10)
    Start-Sleep -Seconds 3
}

function New-Display([string] $Model, [string] $Name, [string] $Uid, [bool] $Main, [int] $X, [int] $Y, [int] $Width, [int] $Height, [int] $Numerator, [int] $Denominator, $Hdr) {
    [ordered] @{
        model = $Model; serial = ''; connection = "\\?\DISPLAY#$Model#5&1f2e3d4c&0&UID$Uid#{e6f07b5f-ee97-4a90-b076-33f57bf4eaa7}"; builtIn = $false; name = $Name
        on = $true; main = $Main; x = $X; y = $Y; width = $Width; height = $Height
        refreshRate = [ordered] @{ numerator = $Numerator; denominator = $Denominator }; orientation = 'Landscape'; hdr = $Hdr
    }
}

# Three profiles a PC with a desk, a TV and a projector could have.
$desk = [ordered] @{ name = 'Desk'; displays = @(
    (New-Display 'DELA0A3' 'DELL U2720Q' '4352' $true 0 0 2560 1440 60000 1000 $false),
    (New-Display 'SAM7088' 'SAMSUNG' '4357' $false 2560 360 1920 1080 60 1 $null)) }
$tv = [ordered] @{ name = 'TV & sofa'; displays = @(
    (New-Display 'SAM7088' 'SAMSUNG' '4357' $true 0 0 3840 2160 120000 1000 $true),
    [ordered] @{ model = 'DELA0A3'; serial = ''; connection = '\\?\DISPLAY#DELA0A3#5&1f2e3d4c&0&UID4352#{e6f07b5f-ee97-4a90-b076-33f57bf4eaa7}'; builtIn = $false; name = 'DELL U2720Q'; on = $false }) }
$presenting = [ordered] @{ name = 'Presenting'; displays = @(
    (New-Display 'DELA0A3' 'DELL U2720Q' '4352' $true 0 0 1920 1080 60000 1001 $false),
    (New-Display 'EPS0001' 'EPSON PJ' '4358' $false 0 0 1920 1080 60000 1001 $null)) }

[void] (Close-SettingsApp)
Write-Settings (@{ schemaVersion = 1; general = @{ theme = 'Light' }; updates = @{ checkAutomatically = $false }; diagnostics = @{ detailedLoggingUntil = $detailedUntil } } | ConvertTo-Json -Depth 4)
Start-Sleep -Seconds 2
Start-Process $appExe
[void] (Wait-Until { Get-AppWindow })
Start-Sleep -Seconds 3

# First turn-on, from Home: in a remote session the setup can't be read, so there's no Original setup.
Test-Check 'turn on Display Profiles on Home' (Set-Toggle 'Display Profiles' $true)
Start-Sleep -Seconds 2
$saved = Get-DisplayProfilesSettings
Test-Check 'turning it on in a remote session adds no profile' (($saved.enabled -eq $true) -and (@($saved.profiles).Count -eq 0)) ($saved | ConvertTo-Json -Depth 6 -Compress)
Test-Check 'select Display Profiles' (Select-NavigationItem 'Display Profiles')
Start-Sleep -Seconds 2
Test-Check 'the page notes the remote session' (Wait-Until { Find-AppText "${remoteNote}: Windows reports the session's own display, not the PC's, and refuses changes." } 5)
Test-Check 'Add from current setup is off' (-not (Test-AppEnabled 'Add from current setup'))
Test-Check 'the page says there are no profiles' ([bool] (Find-AppText 'No profiles yet. Arrange your displays in Windows display settings, then select Add from current setup.'))
Test-Check 'the host starts Display Profiles' (Wait-Until { (Get-HostLog) -match 'Display Profiles started\.' } 15)
Test-Check 'the host finds the remote session' (Wait-Until { (Get-HostLog) -match 'Remote session: True\.' } 10)

# Profiles written into the settings, as on a PC with these displays.
[void] (Close-SettingsApp)
Write-DisplayProfilesSettings $true @($desk, $tv, $presenting)
Start-Process $appExe
[void] (Wait-Until { Get-AppWindow })
Start-Sleep -Seconds 3
Test-Check 'select Display Profiles again' (Select-NavigationItem 'Display Profiles')
Start-Sleep -Seconds 2
$text = (Get-WindowText) -join "`n"
Test-Check 'the page lists the profiles' ((Find-AppText 'Desk') -and (Find-AppText 'TV & sofa') -and (Find-AppText 'Presenting'))
Test-Check 'each display has a line with its mode' (($text -like "*DELL U2720Q: 2560 × 1440, 60 Hz, main display*") -and ($text -like "*SAMSUNG: 1920 × 1080, 60 Hz*")) $text
Test-Check 'a display that''s off, HDR and identical names are shown' (($text -like "*DELL U2720Q: off*") -and ($text -like "*SAMSUNG: 3840 × 2160, 120 Hz, HDR, main display*") -and ($text -like "*DELL U2720Q: 1920 × 1080, 59.94 Hz, main display*")) $text
Test-Check 'Use is off in a remote session' ((Find-AppElement 'Use Desk') -and -not (Test-AppEnabled 'Use Desk'))
Save-Screenshot '16a-display-profiles-english'

# Rename, refusing a name another profile has.
Test-Check 'More options opens Desk''s menu' (Invoke-AppElement 'More options for Desk')
Start-Sleep -Seconds 1
Test-Check 'Rename opens the name dialog' ((Invoke-AppElement 'Rename') -and (Wait-Until { Find-AppElement 'Rename profile' } 5))
Start-Sleep -Seconds 1
$dialogText = @((Find-AppDialog 'Rename profile').FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition) | ForEach-Object { $_.Current.Name }) -join "`n"
Test-Check 'the dialog shows the profile''s setup' (($dialogText -like '*DELL U2720Q: 2560 × 1440, 60 Hz, main display*') -and ($dialogText -like '*SAMSUNG: 1920 × 1080, 60 Hz*')) $dialogText
Test-Check 'type a name another profile has' (Set-AppBox 'Name' 'tv & SOFA')
Test-Check 'a name in use is refused' ((Wait-Until { Find-AppText 'Another profile has this name.' } 3) -and -not (Test-AppEnabled 'Save'))
Save-Screenshot '16b-display-profiles-rename'
Test-Check 'type a new name' (Set-AppBox 'Name' 'Desk & lamp')
Test-Check 'Save is on for the new name' (Wait-Until { Test-AppEnabled 'Save' } 3)
Test-Check 'Save the name' (Invoke-AppElement 'Save')
Start-Sleep -Seconds 2
Test-Check 'the page saves the new name and keeps the setup' ((@((Get-DisplayProfilesSettings).profiles)[0].name -eq 'Desk & lamp') -and (@(@((Get-DisplayProfilesSettings).profiles)[0].displays).Count -eq 2))
Test-Check 'focus stays on the renamed profile' (Wait-Until { (Get-FocusedName) -eq 'More options for Desk & lamp' } 3) (Get-FocusedName)

# Move and delete.
Test-Check 'More options opens Presenting''s menu' (Invoke-AppElement 'More options for Presenting')
Start-Sleep -Seconds 1
Test-Check 'Move up moves the profile' (Invoke-AppElement 'Move up')
Start-Sleep -Seconds 2
$order = (@((Get-DisplayProfilesSettings).profiles) | ForEach-Object { $_.name }) -join ', '
Test-Check 'the page saves the new order' ($order -eq 'Desk & lamp, Presenting, TV & sofa') $order
Test-Check 'focus stays on the profile that moved' (Wait-Until { (Get-FocusedName) -eq 'More options for Presenting' } 3) (Get-FocusedName)
Test-Check 'More options opens TV & sofa''s menu' (Invoke-AppElement 'More options for TV & sofa')
Start-Sleep -Seconds 1
Test-Check 'Delete asks first' ((Invoke-AppElement 'Delete') -and (Wait-Until { Find-AppElement 'Delete TV & sofa?' } 5))
Start-Sleep -Seconds 1
Test-Check 'confirm Delete' (Invoke-AppElement 'Delete')
Start-Sleep -Seconds 2
Test-Check 'the page deletes the profile' (@((Get-DisplayProfilesSettings).profiles).Count -eq 2)
Test-Check 'focus goes to the profile before the deleted one' (Wait-Until { (Get-FocusedName) -eq 'More options for Presenting' } 3) (Get-FocusedName)

# The PC Companion icon's menu: the note, and every profile dimmed.
[void] (Close-SettingsApp)
Test-Check 'the PC Companion icon''s menu has the Display profile submenu' (Open-ProfileMenu)
# UI Automation lists a submenu's items under its item in the menu too.
$menus = Get-MenuItems
Test-Check 'the submenu notes the remote session and dims the profiles' (($menus -join ' || ') -like "*Display profile | $remoteNote (dimmed) | Desk & lamp (dimmed) | Presenting (dimmed) | Display Profiles settings | Settings | Exit*") ($menus -join ' || ')
Test-Check 'choose Display Profiles settings' (Select-AnyMenuItem 'Display Profiles settings')
Test-Check 'the settings app opens on the Display Profiles page' ((Wait-Until { Get-AppWindow } 15) -and (Wait-Until { Find-AppElement 'Add from current setup' } 10))

# Turned off, the submenu is gone.
[void] (Close-SettingsApp)
Write-DisplayProfilesSettings $false @($desk, $presenting)
Test-Check 'the host stops Display Profiles' (Wait-Until { (Get-HostLog) -match 'Display Profiles stopped\.' } 10)
[void] (Send-TrayEvent $WM_CONTEXTMENU $menuX $menuY)
[void] (Wait-Until { (Get-MenuItems) -match 'Exit' } 5)
$menus = Get-MenuItems
Test-Check 'turned off, the menu has no Display profile submenu' (-not (($menus -join ' ') -match 'Display profile')) ($menus -join ' || ')
[void] (Close-TrayMenu)

$logText = "$(Get-HostLog)`n$(Get-AppLog)"
Test-Check 'the logs hold no profile or display names' (-not ($logText -match 'Desk & lamp|\bPresenting\b|TV & sofa|DELL U2720Q|SAMSUNG|EPSON PJ'))
