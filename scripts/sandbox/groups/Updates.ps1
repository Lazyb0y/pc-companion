# Updates: the update check against a stand-in for GitHub's releases API, with networking still off.
# The hosts file points api.github.com at an HTTPS server in the Sandbox, whose certificate's root
# is trusted only here. Turning the check on runs it at once; it finds version 9.9.9, shows the
# notification and the banner, and selecting the notification opens the release page. Then, with
# the check off, Check now asks each time it's selected.

$serverFolder = Join-Path $results 'fake-github'
New-Item -ItemType Directory -Force -Path $serverFolder | Out-Null
$requestLog = Join-Path $serverFolder 'requests.jsonl'
$answer = Join-Path $serverFolder 'answer.json'
$status = Join-Path $serverFolder 'status.txt'
Set-Content -Path $answer -Encoding utf8 -Value '{ "tag_name": "v9.9.9", "name": "PC Companion 9.9.9 Preview", "draft": false, "prerelease": false, "body": "Notes" }'
$hostsFile = Join-Path $env:SystemRoot 'System32\drivers\etc\hosts'
$hostsBefore = Get-Content $hostsFile -Raw -ErrorAction SilentlyContinue
$appId = '{6f1c3c58-0d7e-4c0e-9d3f-3a4c1e0b2a71}'

$certificate = New-SelfSignedCertificate -DnsName 'api.github.com' -CertStoreLocation 'Cert:\LocalMachine\My' -NotAfter (Get-Date).AddDays(1)
$rootStore = New-Object System.Security.Cryptography.X509Certificates.X509Store('Root', 'LocalMachine')
$rootStore.Open('ReadWrite')
$rootStore.Add([System.Security.Cryptography.X509Certificates.X509Certificate2]::new($certificate.RawData))
$rootStore.Close()
[void] (netsh.exe http add sslcert ipport=0.0.0.0:443 certhash=$($certificate.Thumbprint) appid=$appId)
Add-Content -Path $hostsFile -Value "`r`n127.0.0.1 api.github.com" -Encoding ascii
Clear-DnsClientCache -ErrorAction SilentlyContinue
$server = Start-Process powershell.exe -PassThru -WindowStyle Hidden -ArgumentList @(
    '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', (Join-Path $PSScriptRoot '..\fake-github.ps1'), '-RequestLog', $requestLog, '-Answer', $answer, '-Status', $status)
$serverUp = Wait-Until { Get-NetTCPConnection -LocalPort 443 -State Listen -ErrorAction SilentlyContinue } 15
Test-Check 'the stand-in for GitHub listens on port 443' ([bool] $serverUp)

function Get-Requests { @(Get-Content $requestLog -Encoding utf8 -ErrorAction SilentlyContinue | Where-Object { $_ } | ForEach-Object { $_ | ConvertFrom-Json }) }
# Counted apart: PowerShell returns one request as itself, without a Count.
function Get-RequestCount { @(Get-Content $requestLog -Encoding utf8 -ErrorAction SilentlyContinue | Where-Object { $_ }).Count }

# Exactly the request the host makes.
function Test-Request([string] $Name, $Request) {
    $headerNames = @($Request.Headers.PSObject.Properties.Name | Where-Object { $_ -ne 'Host' } | Sort-Object)
    Test-Check "$Name asks for the latest release" (($Request.Method -eq 'GET') -and ($Request.Path -eq '/repos/Lazyb0y/pc-companion/releases/latest')) "$($Request.Method) $($Request.Path)"
    Test-Check "$Name has only the Accept and User-Agent headers" (($headerNames -join ',') -eq 'Accept,User-Agent') ($headerNames -join ', ')
    Test-Check "$Name's headers say only what they must" (($Request.Headers.Accept -eq 'application/vnd.github+json') -and ($Request.Headers.'User-Agent' -eq 'PCCompanion')) "Accept: $($Request.Headers.Accept); User-Agent: $($Request.Headers.'User-Agent')"
}

function Get-AppLog {
    (Get-ChildItem $logs -Filter 'app-*.log' -ErrorAction SilentlyContinue | ForEach-Object { Get-Content $_.FullName -Raw }) -join "`n"
}

# Focused first, as from the keyboard. The page is scrolled to its end, where the card is, for the
# screenshots: focus from UI Automation doesn't scroll it.
function Invoke-CheckNow {
    $button = Find-AppElement 'Check now'
    if (-not $button) { return $false }
    $walker = [System.Windows.Automation.TreeWalker]::RawViewWalker
    $area = $walker.GetParent($button)
    while ($area -and -not [bool] $area.GetCurrentPropertyValue([System.Windows.Automation.AutomationElement]::IsScrollPatternAvailableProperty)) {
        $area = $walker.GetParent($area)
    }
    if ($area) {
        $scroll = $area.GetCurrentPattern([System.Windows.Automation.ScrollPattern]::Pattern)
        if ($scroll.Current.VerticallyScrollable) { $scroll.SetScrollPercent([System.Windows.Automation.ScrollPattern]::NoScroll, 100) }
    }
    $button.SetFocus()
    $button.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    return $true
}

function Set-Answer([string] $Tag) {
    Set-Content -Path $answer -Encoding utf8 -Value "{ `"tag_name`": `"$Tag`", `"name`": `"PC Companion Preview`", `"draft`": false, `"prerelease`": false, `"body`": `"Notes`" }"
}

# The check is off, so starting PC Companion asks nothing.
Write-Settings '{ "schemaVersion": 1, "updates": { "checkAutomatically": false } }'
Start-Process $appExe
[void] (Wait-Until { Get-AppWindow })
Start-Sleep -Seconds 3
[void] (Select-NavigationItem 'General')
Start-Sleep -Seconds 2
$toggle = Find-Toggle 'Check for updates'
Test-Check 'General shows Check for updates, off' (($null -ne $toggle) -and ($toggle.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern).Current.ToggleState -eq [System.Windows.Automation.ToggleState]::Off))
$text = Get-WindowText
Test-Check 'General describes the check' ($text -contains 'Once a day, ask GitHub whether a newer version of PC Companion is available.') ($text -join ' | ')
Test-Check 'nothing is asked while the check is off' ((Get-RequestCount) -eq 0)

# Turned on, the check runs at once.
Test-Check 'turn on Check for updates' (Set-Toggle 'Check for updates' $true)
Test-Check 'the host asks GitHub once' (Wait-Until { (Get-RequestCount) -ge 1 } 30) "requests: $(Get-RequestCount)"
$request = Get-Requests | Select-Object -First 1
if ($request) {
    Test-Request 'the request' $request
}

$resultFile = Join-Path $dataFolder 'update-check.json'
Test-Check 'the host saves the result' (Wait-Until { Test-Path $resultFile } 15)
$saved = Get-Content $resultFile -Raw -ErrorAction SilentlyContinue | ConvertFrom-Json
Test-Check 'the result names the new version, as shown' (($saved.latestVersion -eq '9.9.9') -and ($saved.notifiedVersion -eq '9.9.9')) "latest: $($saved.latestVersion); notified: $($saved.notifiedVersion)"
# Windows took the notification. Whether it shows it isn't checked here: in the Sandbox it didn't,
# likely because of quiet time, the first hour of a new user, which the Sandbox signs in each time.
Test-Check 'the host shows the notification' (Wait-Until { (Get-HostLog) -match 'Showed the update notification for PC Companion 9\.9\.9\.' } 15)

# The banner appears on the open General page once the host saves the result.
$bannerPattern = '^PC Companion 9\.9\.9 is available\. You have version \d+\.\d+\.\d+\.$'
Test-Check 'General shows the banner' (Wait-Until { @(Get-WindowText | Where-Object { $_ -match $bannerPattern }).Count -eq 1 } 10) ((Get-WindowText) -join ' | ')
Test-Check 'the banner has Open release page' ($null -ne (Find-AppElement 'Open release page'))
Save-Screenshot '09-update-banner'

# Selecting the notification opens the release page in the browser.
$NIN_BALLOONUSERCLICK = 0x0405
$browsersBefore = @(Get-CimInstance Win32_Process -Filter "Name = 'msedge.exe'" | ForEach-Object { $_.ProcessId })
Test-Check 'select the notification' (Send-TrayEvent $NIN_BALLOONUSERCLICK)
$opened = Wait-Until {
    @(Get-CimInstance Win32_Process -Filter "Name = 'msedge.exe'" | Where-Object {
        ($browsersBefore -notcontains $_.ProcessId) -and ($_.CommandLine -like '*https://github.com/Lazyb0y/pc-companion/releases/latest*') }).Count -ge 1
} 20
Test-Check 'the browser opens the release page' $opened
Test-Check 'the host logs that the notification was selected' ((Get-HostLog) -match 'The update notification was selected; opening the release page\.')
Get-Process msedge -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue

# Turned off, the banner goes away and the setting is saved.
Test-Check 'turn off Check for updates' (Set-Toggle 'Check for updates' $false)
Test-Check 'General hides the banner' (Wait-Until { @(Get-WindowText | Where-Object { $_ -match $bannerPattern }).Count -eq 0 } 10)
$settings = Get-Content (Join-Path $dataFolder 'settings.json') -Raw | ConvertFrom-Json
Test-Check 'the setting is saved off' ($settings.updates.checkAutomatically -eq $false)
Test-Check 'GitHub was asked only once' ((Get-RequestCount) -eq 1) "requests: $(Get-RequestCount)"

# Check now, with the check off. The status shows the saved result first.
$text = Get-WindowText
Test-Check 'the update status shows the saved newer version' ($text -contains 'PC Companion 9.9.9 is available') ($text -join ' | ')
Test-Check 'the update status says when the last check was' (@($text | Where-Object { $_ -like 'Last checked: *' }).Count -eq 1) ($text -join ' | ')

# An older version: this one is up to date.
Set-Answer 'v0.0.1'
Test-Check 'select Check now' (Invoke-CheckNow)
Test-Check 'Check now asks GitHub once' (Wait-Until { (Get-RequestCount) -ge 2 } 30) "requests: $(Get-RequestCount)"
$request = Get-Requests | Select-Object -Last 1
if ($request) {
    Test-Request 'Check now''s request' $request
}
Test-Check 'General says this version is up to date' (Wait-Until { (Get-WindowText) -contains 'You have the latest version' } 15) ((Get-WindowText) -join ' | ')
$saved = Get-Content $resultFile -Raw -ErrorAction SilentlyContinue | ConvertFrom-Json
Test-Check 'Check now saves the result' (($saved.latestVersion -eq '0.0.1') -and ($saved.notifiedVersion -eq '9.9.9')) "latest: $($saved.latestVersion); notified: $($saved.notifiedVersion)"
Test-Check 'the app logs Check now' ((Get-AppLog) -match 'Checking for updates, as Check now was selected\.')
$focused = [System.Windows.Automation.AutomationElement]::FocusedElement
Test-Check 'the keyboard focus stays on Check now' ($focused -and ($focused.Current.Name -eq 'Check now')) "focused: $(if ($focused) { $focused.Current.Name })"

# A newer version shows the banner, with the check off, and no notification.
Set-Answer 'v9.9.10'
Test-Check 'select Check now again' (Invoke-CheckNow)
Test-Check 'General shows the newer version' (Wait-Until { (Get-WindowText) -contains 'PC Companion 9.9.10 is available' } 30) ((Get-WindowText) -join ' | ')
$newerBannerPattern = '^PC Companion 9\.9\.10 is available\. You have version \d+\.\d+\.\d+\.$'
Test-Check 'General shows the banner after Check now, with the check off' (Wait-Until { @(Get-WindowText | Where-Object { $_ -match $newerBannerPattern }).Count -eq 1 } 10) ((Get-WindowText) -join ' | ')
$saved = Get-Content $resultFile -Raw -ErrorAction SilentlyContinue | ConvertFrom-Json
Test-Check 'Check now saves the newer version as shown' (($saved.latestVersion -eq '9.9.10') -and ($saved.notifiedVersion -eq '9.9.10')) "latest: $($saved.latestVersion); notified: $($saved.notifiedVersion)"
Test-Check 'Check now shows no notification' ((Get-HostLog) -notmatch '9\.9\.10')
Save-Screenshot '10-update-check-now'

# A rate limit.
Set-Content -Path $status -Value '429' -Encoding ascii
Test-Check 'select Check now during a rate limit' (Invoke-CheckNow)
Test-Check 'General says it can''t check right now' (Wait-Until { ((Get-WindowText) -contains 'Can''t check for updates right now') -and ((Get-WindowText) -contains 'Try again later.') } 30) ((Get-WindowText) -join ' | ')
Remove-Item -Path $status

# No answer, once the stand-in stops.
Stop-Process -Id $server.Id -Force -ErrorAction SilentlyContinue
[void] (Wait-Until { -not (Get-NetTCPConnection -LocalPort 443 -State Listen -ErrorAction SilentlyContinue) } 10)
Test-Check 'select Check now without an answer' (Invoke-CheckNow)
Test-Check 'General says GitHub can''t be reached' (Wait-Until { ((Get-WindowText) -contains 'Can''t reach GitHub') -and ((Get-WindowText) -contains 'Check your internet connection, then try again.') } 45) ((Get-WindowText) -join ' | ')
Test-Check 'the app logs that Check now got no answer' ((Get-AppLog) -match 'Check now got no answer from GitHub\.')
Save-Screenshot '11-update-check-now-no-answer'
Test-Check 'GitHub was asked once for each Check now that reached it' ((Get-RequestCount) -eq 4) "requests: $(Get-RequestCount)"

# Leaves the Sandbox as it found it for the next groups.
[void] (Close-SettingsApp)
Stop-Process -Id $server.Id -Force -ErrorAction SilentlyContinue
[void] (netsh.exe http delete sslcert ipport=0.0.0.0:443)
Set-Content -Path $hostsFile -Value $hostsBefore -NoNewline -Encoding ascii
Clear-DnsClientCache -ErrorAction SilentlyContinue
Remove-Item -Path "Cert:\LocalMachine\My\$($certificate.Thumbprint)" -ErrorAction SilentlyContinue
Remove-Item -Path "Cert:\LocalMachine\Root\$($certificate.Thumbprint)" -ErrorAction SilentlyContinue
