# Logging: detailed logging, which the General page switch turns on for 24 hours; it turns itself
# off when the time is up.

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
Test-Check 'the tray menu closes with detailed logging on' (Close-TrayMenu)
[void] (Close-SettingsApp)
# As if the 24 hours were nearly over.
$soon = [DateTimeOffset]::Now.AddSeconds(20).ToString('o')
Write-Settings "{ ""schemaVersion"": 1, ""general"": { ""theme"": ""Light"" }, ""diagnostics"": { ""detailedLoggingUntil"": ""$soon"" } }"
Start-Sleep -Seconds 25
Test-Check 'tray menu requested after detailed logging ended' (Send-TrayEvent $WM_CONTEXTMENU $menuX $menuY)
Start-Sleep -Seconds 2
Test-Check 'the tray menu closes after detailed logging ended' (Close-TrayMenu)

# The logs show detailed logging turning on, and the host's Debug entries ending when it turned off.
Stop-All
$appLog = (Get-ChildItem $logs -Filter 'app-*.log' -ErrorAction SilentlyContinue | ForEach-Object { Get-Content $_.FullName -Raw }) -join "`n"
$hostLines = @(Get-ChildItem $logs -Filter 'host-*.log' -ErrorAction SilentlyContinue | ForEach-Object { Get-Content $_.FullName })
$turnedOff = @(0..($hostLines.Count - 1) | Where-Object { $hostLines[$_] -match 'Detailed logging is off\.' })
$debugLines = @(0..($hostLines.Count - 1) | Where-Object { $hostLines[$_] -match '\[DBG\]' })
Test-Check 'settings app turned detailed logging on' ($appLog -match 'Detailed logging is on until')
Test-Check 'host turned detailed logging on' (@($hostLines | Where-Object { $_ -match 'Detailed logging is on until' }).Count -ge 1)
Test-Check 'host wrote Debug entries while detailed logging was on' (@($hostLines | Where-Object { $_ -match '\[DBG\] PCCompanion\.Host\.TrayController: Opened the tray menu' }).Count -ge 1)
Test-Check 'detailed logging turned itself off in the host' ($turnedOff.Count -ge 1)
Test-Check 'host wrote no Debug entries after detailed logging turned off' (($turnedOff.Count -ge 1) -and (@($debugLines | Where-Object { $_ -gt $turnedOff[-1] }).Count -eq 0))
