# Languages: Arabic, right to left with the dark theme, and Bangla with the light theme.

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
Test-Check 'select Hot Corners in Arabic' (Select-NavigationItem 'الزوايا النشطة')
Start-Sleep -Seconds 2
Save-Screenshot '05b-hot-corners-arabic-dark'
$windowBounds = (Get-AppWindow).Current.BoundingRectangle
foreach ($width in 1000, $screen.Width) {
    [void] (Set-AppWindowWidth $width)
    foreach ($page in 'الصفحة الرئيسية', 'الزوايا النشطة', 'نوافذ التطبيق', 'مقاييس النظام', 'إجراءات الحافظة', 'ملفات تعريف الشبكة', 'قواعد المجلدات', 'ملفات تعريف العرض', 'عام') {
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
