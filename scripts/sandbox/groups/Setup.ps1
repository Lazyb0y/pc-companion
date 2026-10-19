# Setup: the host's exit argument, another build of the same version, an upgrade, uninstalling with
# the reduced pages and with KEEPSETTINGS=1, one install per user, and what an uninstall for all
# users removes from the user's data folder.

# The host's exit argument, which setup runs before Windows Installer looks for files in use,
# closes the settings app and the host and returns once they've ended.
function Get-InstalledVersions { @(Get-InstalledProducts | ForEach-Object { $_.DisplayVersion }) }
function Open-PCCompanion {
    Start-Process $appExe
    return (Wait-Until { ((Get-Processes 'PCCompanion').Count -eq 1) -and ((Get-Processes 'PCCompanion.Host').Count -eq 1) } 30)
}
# Files setup couldn't remove or replace while they were in use, left for the next restart.
function Get-PendingRenames {
    @((Get-ItemProperty 'HKLM:\SYSTEM\CurrentControlSet\Control\Session Manager' -Name PendingFileRenameOperations -ErrorAction SilentlyContinue).PendingFileRenameOperations | Where-Object { $_ -like '*PCCompanion*' })
}
function Get-RunningApp { @(Get-Processes 'PCCompanion') + @(Get-Processes 'PCCompanion.Host') }
# Selects OK in a window of Windows Installer, such as its prompt to close programs.
function Confirm-InstallerPrompt {
    $msiexec = @(Get-Processes 'msiexec' | ForEach-Object { $_.Id })
    $ok = [System.Windows.Automation.AndCondition]::new(
        [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Button),
        [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::NameProperty, 'OK'))
    foreach ($window in [System.Windows.Automation.AutomationElement]::RootElement.FindAll([System.Windows.Automation.TreeScope]::Children, [System.Windows.Automation.Condition]::TrueCondition)) {
        if ($msiexec -notcontains $window.Current.ProcessId) { continue }
        $button = $window.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $ok)
        $pattern = $null
        if ($button -and $button.TryGetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern, [ref] $pattern)) {
            $pattern.Invoke()
            return $true
        }
    }
    return $false
}
# Had Windows Installer found them in use, its log would show Restart Manager closing them; with
# setup pages, it would ask first.
function Test-ClosedBySetup([string] $Name, [int] $PendingBefore, [string] $Log) {
    $running = Get-RunningApp
    Test-Check "$Name closes the settings app and the host" ($running.Count -eq 0) "running: $(($running | ForEach-Object { $_.ProcessName }) -join ', ')"
    $restartManager = @(Select-String -Path $Log -Pattern 'RESTART MANAGER: (Will attempt to shut down|Successfully shut down)' -ErrorAction SilentlyContinue | ForEach-Object { $_.Line -replace '^.*RESTART MANAGER: ', '' })
    Test-Check "$Name closes them before Windows Installer looks for files in use" ($restartManager.Count -eq 0) "Restart Manager: $($restartManager -join ' | ')"
    $pending = @(Get-PendingRenames)
    Test-Check "$Name leaves no files for a restart" ($pending.Count -le $PendingBefore) "pending: $($pending -join ', ')"
}
# Settings that another build and the upgrade must keep.
Write-Settings '{ "schemaVersion": 1, "general": { "theme": "Dark" } }'
Test-Check 'the settings app and the host run before the exit argument' (Open-PCCompanion)
$exitTime = [Diagnostics.Stopwatch]::StartNew()
$code = (Start-Process $hostExe -ArgumentList '--exit' -Wait -PassThru).ExitCode
$running = Get-RunningApp
Test-Check 'the exit argument closes the settings app and the host before it returns' ($running.Count -eq 0) "running: $(($running | ForEach-Object { $_.ProcessName }) -join ', '); returned after $($exitTime.ElapsedMilliseconds) ms"
Test-Check 'the exit argument returns 0' ($code -eq 0) "exit code $code"
$exitTime.Restart()
[void] (Start-Process $hostExe -ArgumentList '--exit' -Wait -PassThru)
$running = Get-RunningApp
Test-Check 'with nothing running, the exit argument starts nothing and returns without waiting' (($running.Count -eq 0) -and ($exitTime.ElapsedMilliseconds -lt 4000)) "running: $(($running | ForEach-Object { $_.ProcessName }) -join ', '); returned after $($exitTime.ElapsedMilliseconds) ms"

# Another build of the same version replaces the installed one, as an upgrade does, and keeps
# the settings and logs, rather than installing next to it. Before this, the upgrade and the
# uninstall, the settings app and the host run: setup closes them first, so it needs no restart.
Test-Check 'the settings app and the host run before installing another build' (Open-PCCompanion)
$pendingBefore = @(Get-PendingRenames).Count
$settingsBefore = Get-Content (Join-Path $dataFolder 'settings.json') -Raw
$logsBefore = @(Get-ChildItem $logs -File).Count
$productsBefore = @(Get-InstalledProducts | ForEach-Object { $_.PSChildName })
$code = (Start-Process msiexec.exe -ArgumentList @('/i', "`"$sameVersionMsi`"", '/qn', 'ALLUSERS=2', 'MSIINSTALLPERUSER=1', '/l*v', "`"$results\same-version.log`"") -Wait -PassThru).ExitCode
Test-Check 'install another build of the same version' ($code -eq 0) "exit code $code"
Test-ClosedBySetup 'installing another build' $pendingBefore "$results\same-version.log"
$productsAfter = @(Get-InstalledProducts | ForEach-Object { $_.PSChildName })
Test-Check 'it replaces the installed build, leaving one entry in Settings > Apps' (($productsAfter.Count -eq 1) -and ($productsAfter[0] -ne $productsBefore[0])) "before: $($productsBefore -join ', '); after: $($productsAfter -join ', ')"
Test-Check 'it keeps the settings' ((Get-Content (Join-Path $dataFolder 'settings.json') -Raw -ErrorAction SilentlyContinue) -eq $settingsBefore)
Test-Check 'it keeps the logs' (@(Get-ChildItem $logs -File -ErrorAction SilentlyContinue).Count -ge $logsBefore) "before: $logsBefore; after: $(@(Get-ChildItem $logs -File -ErrorAction SilentlyContinue).Count)"

# An upgrade to the next version keeps the settings and logs.
Test-Check 'the settings app and the host run before the upgrade' (Open-PCCompanion)
$pendingBefore = @(Get-PendingRenames).Count
$settingsBefore = Get-Content (Join-Path $dataFolder 'settings.json') -Raw
$logsBefore = @(Get-ChildItem $logs -File).Count
$versionBefore = @(Get-InstalledVersions)
$code = (Start-Process msiexec.exe -ArgumentList @('/i', "`"$upgradeMsi`"", '/qn', 'ALLUSERS=2', 'MSIINSTALLPERUSER=1', '/l*v', "`"$results\upgrade.log`"") -Wait -PassThru).ExitCode
Test-Check 'upgrade to the next version' ($code -eq 0) "exit code $code"
Test-ClosedBySetup 'the upgrade' $pendingBefore "$results\upgrade.log"
$versionAfter = @(Get-InstalledVersions)
Test-Check 'the upgrade replaces the installed version' (($versionAfter.Count -eq 1) -and ($versionAfter[0] -ne $versionBefore[0])) "before: $($versionBefore -join ', '); after: $($versionAfter -join ', ')"
Test-Check 'the upgrade keeps the settings' ((Get-Content (Join-Path $dataFolder 'settings.json') -Raw -ErrorAction SilentlyContinue) -eq $settingsBefore)
Test-Check 'the upgrade keeps the logs' (@(Get-ChildItem $logs -File -ErrorAction SilentlyContinue).Count -ge $logsBefore) "before: $logsBefore; after: $(@(Get-ChildItem $logs -File -ErrorAction SilentlyContinue).Count)"

# Uninstall, with the reduced setup pages that Settings > Apps shows, where Windows Installer
# would ask to close PC Companion if it found it running. It removes the data folder.
Test-Check 'the settings app and the host run before uninstalling' (Open-PCCompanion)
$pendingBefore = @(Get-PendingRenames).Count
$savedSettings = Get-Content (Join-Path $dataFolder 'settings.json') -Raw
$uninstall = Start-Process msiexec.exe -ArgumentList @('/x', "`"$upgradeMsi`"", '/qb', '/l*v', "`"$results\uninstall.log`"") -PassThru
[void] $uninstall.Handle
$finished = $uninstall.WaitForExit(120000)
Test-Check 'uninstalling with reduced pages asks nothing' $finished
if (-not $finished) {
    # Most likely Windows Installer asks to close PC Companion: OK, its default, closes it.
    Save-Screenshot 'uninstall-asks'
    Test-Check 'OK on the prompt that keeps the uninstall waiting' (Confirm-InstallerPrompt)
    [void] $uninstall.WaitForExit(120000)
}
$code = $uninstall.ExitCode
Test-Check 'uninstall' ($code -eq 0) "exit code $code"
Test-ClosedBySetup 'uninstalling' $pendingBefore "$results\uninstall.log"
Test-Check 'startup entry removed' (-not (Get-ItemProperty $runKey -Name PCCompanion -ErrorAction SilentlyContinue))
Test-Check 'install folder removed' (-not (Test-Path $appExe))
Test-Check 'uninstalling removes the settings and logs' (-not (Test-Path $dataFolder)) "left: $((Get-ChildItem $dataFolder -Force -Recurse -Name -ErrorAction SilentlyContinue) -join ', ')"

# Uninstalling with KEEPSETTINGS=1, as Keep my settings does, keeps settings.json and removes
# the rest, the logs included.
$code = (Start-Process msiexec.exe -ArgumentList @('/i', "`"$msi`"", '/qn', 'ALLUSERS=2', 'MSIINSTALLPERUSER=1', '/l*v', "`"$results\install-again.log`"") -Wait -PassThru).ExitCode
Test-Check 'install for the current user again' ($code -eq 0) "exit code $code"
Write-Settings $savedSettings
Test-Check 'the settings app and the host run before uninstalling with KEEPSETTINGS=1' (Open-PCCompanion)
Test-Check 'the settings app and the host write their logs' (Wait-Until { @(Get-ChildItem $logs -File -ErrorAction SilentlyContinue).Count -ge 2 } 15)
$settingsBefore = Get-Content (Join-Path $dataFolder 'settings.json') -Raw
$code = (Start-Process msiexec.exe -ArgumentList @('/x', "`"$msi`"", '/qn', 'KEEPSETTINGS=1', '/l*v', "`"$results\uninstall-keep-settings.log`"") -Wait -PassThru).ExitCode
Test-Check 'uninstall with KEEPSETTINGS=1' ($code -eq 0) "exit code $code"
$left = @(Get-ChildItem $dataFolder -Force -Recurse -Name -ErrorAction SilentlyContinue)
Test-Check 'KEEPSETTINGS=1 keeps the settings and removes the logs' (($left.Count -eq 1) -and ($left[0] -eq 'settings.json') -and ((Get-Content (Join-Path $dataFolder 'settings.json') -Raw -ErrorAction SilentlyContinue) -eq $settingsBefore)) "left: $($left -join ', ')"
Remove-Item $dataFolder -Recurse -Force -ErrorAction SilentlyContinue

# One install per user: setup stops when PC Companion is installed for the other scope, and
# leaves that install alone. Here it's installed just for you; RunAsAdmin tries the other way round.
$code = (Start-Process msiexec.exe -ArgumentList @('/i', "`"$msi`"", '/qn', 'ALLUSERS=2', 'MSIINSTALLPERUSER=1', '/l*v', "`"$results\install-scope-user.log`"") -Wait -PassThru).ExitCode
Test-Check 'install just for you before trying all users' ($code -eq 0) "exit code $code"
Test-OtherScopeStops 'installing for all users over a just-for-you install' @('ALLUSERS=1') 'install-other-scope-machine.log' 'already installed just for you'
$code = (Start-Process msiexec.exe -ArgumentList @('/x', "`"$msi`"", '/qn', '/l*v', "`"$results\uninstall-scope-user.log`"") -Wait -PassThru).ExitCode
Test-Check 'uninstall the just-for-you install' ($code -eq 0) "exit code $code"
Remove-Item $dataFolder -Recurse -Force -ErrorAction SilentlyContinue

# An uninstall for all users runs as administrator, while any program running as the user can
# change the user's data folder: the host removes that folder as the user, and removes links
# planted in it without what they lead to.
$code = (Start-Process msiexec.exe -ArgumentList @('/i', "`"$msi`"", '/qn', 'ALLUSERS=1', '/l*v', "`"$results\install-all-users.log`"") -Wait -PassThru).ExitCode
Test-Check 'install for all users before planting links' ($code -eq 0) "exit code $code"
Write-Settings '{ "schemaVersion": 1, "general": { "theme": "Light" } }'
New-Item -ItemType Directory -Force -Path $logs | Out-Null
Set-Content -Path (Join-Path $logs 'app-test.log') -Value 'test'
Test-Check 'the user''s data folder is there before uninstalling for all users' (Test-Path (Join-Path $dataFolder 'settings.json'))
$outside = (New-Item -ItemType Directory -Force -Path (Join-Path $env:TEMP 'PCCompanion.Outside')).FullName
Set-Content -Path (Join-Path $outside 'behind-folder-link.txt') -Value 'keep'
Set-Content -Path (Join-Path $outside 'behind-file-link.txt') -Value 'keep'
New-Item -ItemType Junction -Path (Join-Path $dataFolder 'FolderLink') -Target $outside | Out-Null
New-Item -ItemType SymbolicLink -Path (Join-Path $dataFolder 'FileLink.txt') -Target (Join-Path $outside 'behind-file-link.txt') | Out-Null
Test-Check 'links to other files are planted in the data folder' ((Test-Path (Join-Path $dataFolder 'FolderLink\behind-folder-link.txt')) -and (Test-Path (Join-Path $dataFolder 'FileLink.txt')))
$code = (Start-Process msiexec.exe -ArgumentList @('/x', "`"$msi`"", '/qn', '/l*v', "`"$results\uninstall-all-users.log`"") -Wait -PassThru).ExitCode
Test-Check 'uninstall for all users' ($code -eq 0) "exit code $code"
$left = @(Get-ChildItem $dataFolder -Force -Name -ErrorAction SilentlyContinue)
Test-Check 'uninstalling for all users removes the user''s settings and logs' ($left.Count -eq 0) "left: $($left -join ', ')"
$kept = @(Get-ChildItem $outside -Force -Name -ErrorAction SilentlyContinue)
Test-Check 'uninstalling doesn''t remove files that links in the data folder lead to' (($kept -contains 'behind-folder-link.txt') -and ($kept -contains 'behind-file-link.txt')) "left outside: $($kept -join ', ')"
