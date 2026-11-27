# Languages: Arabic, right to left with the dark theme, also for lines that start with a name in
# Latin letters, and Bangla with the light theme.

# Arabic with the dark theme, as the General page saves them; the host follows the change.
Start-Process $hostExe
[void] (Wait-Until { (Get-Processes 'PCCompanion.Host').Count -eq 1 } 10)
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
Test-Check 'select Hot Corners in Arabic' (Select-NavigationItem 'الزوايا والحواف النشطة')
Start-Sleep -Seconds 2
Save-Screenshot '05b-hot-corners-arabic-dark'
$windowBounds = (Get-AppWindow).Current.BoundingRectangle
foreach ($width in 1000, $screen.Width) {
    [void] (Set-AppWindowWidth $width)
    foreach ($page in 'الصفحة الرئيسية', 'الزوايا والحواف النشطة', 'نوافذ التطبيق', 'مقاييس النظام', 'خلفية ديناميكية', 'إجراءات الحافظة', 'ملفات تعريف الشبكة', 'قواعد المجلدات', 'ملفات تعريف العرض', 'عام') {
        [void] (Show-Page $page)
        Start-Sleep -Milliseconds 1500
        Test-PageLayout "$page (right to left)" $page $width
    }
}
Restore-AppWindow $windowBounds
Test-Check 'tray menu requested in Arabic' (Send-TrayEvent $WM_CONTEXTMENU $menuX $menuY)
Start-Sleep -Seconds 2
Save-Screenshot '06-tray-menu-arabic'
Test-Check 'the tray menu closes in Arabic' (Close-TrayMenu)

# Lines that start with a name in Latin letters read right to left in Arabic: they start with a
# right-to-left mark (U+200F), so the name comes first, on the right.
$rightToLeftMark = [string] [char] 0x200F
function Test-RightToLeftLine([string] $Name, [string] $Page, [string] $Start, [string] $Screenshot) {
    $shown = Show-Page $Page
    Start-Sleep -Milliseconds 1500
    Save-Screenshot $Screenshot
    $lines = @(Get-WindowText | Where-Object { $_.StartsWith($Start, [StringComparison]::Ordinal) -or $_.StartsWith($rightToLeftMark + $Start, [StringComparison]::Ordinal) })
    $marked = @($lines | Where-Object { $_.StartsWith($rightToLeftMark, [StringComparison]::Ordinal) })
    Test-Check $Name ($shown -and $lines.Count -gt 0 -and $marked.Count -eq $lines.Count) (($lines | ForEach-Object { $_.Replace($rightToLeftMark, '<RLM>') }) -join ' | ')
}
[void] (Close-SettingsApp)
$photos = Join-Path $env:PUBLIC 'Pictures\Photos'
New-Item -ItemType Directory -Force -Path $photos | Out-Null
$display = [ordered] @{
    model = 'DELA0A3'; serial = ''; connection = '\\?\DISPLAY#DELA0A3#5&1f2e3d4c&0&UID4352#{e6f07b5f-ee97-4a90-b076-33f57bf4eaa7}'; builtIn = $false; name = 'DELL U2720Q'
    on = $true; main = $true; x = 0; y = 0; width = 2560; height = 1440; refreshRate = [ordered] @{ numerator = 60000; denominator = 1000 }; orientation = 'Landscape'; hdr = $false
}
Write-Settings (@{
    schemaVersion = 1
    general = @{ language = 'ar'; theme = 'Dark'; startWithWindows = $false }
    features = @{
        displayProfiles = @{ enabled = $false; profiles = @([ordered] @{ name = 'Desk'; displays = @($display) }) }
        dynamicWallpaper = [ordered] @{ enabled = $false; changeWith = 'WindowsMode'; location = 'TimeZone'; fit = 'Fill'; windowsMode = [ordered] @{ light = @{ folder = $photos }; dark = @{ folder = $photos } } }
        windowRules = @{ enabled = $false; rules = @([ordered] @{ name = 'Notes on top'; app = [ordered] @{ name = 'Notepad'; program = 'C:\Windows\notepad.exe' }; onTop = $true }) }
    }
} | ConvertTo-Json -Depth 8)
Start-Sleep -Seconds 2
Start-Process $appExe
[void] (Wait-Until { Get-AppWindow })
Start-Sleep -Seconds 3
Test-RightToLeftLine 'in Arabic, a display''s line reads right to left' 'ملفات تعريف العرض' 'DELL U2720Q: ' '05c-display-profiles-arabic-line'
Test-RightToLeftLine 'in Arabic, a folder''s line reads right to left' 'خلفية ديناميكية' 'Photos (' '05d-dynamic-wallpaper-arabic-line'
Test-RightToLeftLine 'in Arabic, a header that starts with a key reads right to left' 'إجراءات شريط العنوان' 'Ctrl + ' '05e-title-bar-actions-arabic-line'
Test-RightToLeftLine 'in Arabic, a rule''s line reads right to left' 'قواعد النوافذ' 'Notepad: ' '05f-window-rules-arabic-line'

# Bangla with the light theme.
[void] (Close-SettingsApp)
Write-Settings '{ "schemaVersion": 1, "general": { "language": "bn", "theme": "Light", "startWithWindows": false } }'
Start-Sleep -Seconds 2
Start-Process $appExe
[void] (Wait-Until { Get-AppWindow })
Start-Sleep -Seconds 3
Test-Check 'select General in Bangla' (Select-NavigationItem 'সাধারণ')
Start-Sleep -Seconds 2
Save-Screenshot '07-general-bangla-light'
Test-Check 'host loaded the changed settings' ((Get-HostLog) -match 'The settings changed and were loaded again')
