# Always run as administrator, in an all-users install. UAC is off in the Sandbox, so every
# program already runs as administrator: the hand-off to the task and the protected log
# folder in use are checked by hand on a PC with UAC on.
Stop-All
$code = (Start-Process msiexec.exe -ArgumentList @('/i', "`"$msi`"", '/qn', 'ALLUSERS=1', '/l*v', "`"$results\install-all-users.log`"") -Wait -PassThru).ExitCode
Test-Check 'install for all users' ($code -eq 0) "exit code $code"
Test-OtherScopeStops 'installing just for you over an all-users install' @('ALLUSERS=2', 'MSIINSTALLPERUSER=1') 'install-other-scope-user.log' 'already installed for all users'
$allUsersFolder = Join-Path $env:ProgramW6432 'PCCompanion'
$protectedLogs = Join-Path $env:ProgramData 'PCCompanion\Logs'
$acl = Get-Acl $protectedLogs -ErrorAction SilentlyContinue
$writers = @($acl.Access | Where-Object { $_.AccessControlType -eq 'Allow' -and ($_.FileSystemRights -band [System.Security.AccessControl.FileSystemRights]::WriteData) } | ForEach-Object { $_.IdentityReference.Translate([System.Security.Principal.SecurityIdentifier]).Value })
Test-Check 'the protected logs folder is created' ($null -ne $acl)
Test-Check 'only SYSTEM and Administrators can change it' (($writers.Count -gt 0) -and -not ($writers | Where-Object { $_ -notin 'S-1-5-18', 'S-1-5-32-544' })) "writers: $($writers -join ', ')"
$sid = [System.Security.Principal.WindowsIdentity]::GetCurrent().User.Value
Write-Settings '{ "schemaVersion": 1, "general": { "theme": "Light" } }'
Start-Process (Join-Path $allUsersFolder 'PCCompanion.exe')
[void] (Wait-Until { Get-AppWindow })
Start-Sleep -Seconds 3
[void] (Select-NavigationItem 'General')
Start-Sleep -Seconds 2
$adminToggle = Find-Toggle 'Always run as administrator'
Test-Check 'Always run as administrator is available for an all-users install' (($null -ne $adminToggle) -and $adminToggle.Current.IsEnabled)
Test-Check 'turn on Always run as administrator' (Set-Toggle 'Always run as administrator' $true)
Test-Check 'turning it on creates the user''s two tasks' (Wait-Until { (Get-StartupTasks).Count -eq 2 } 30) "tasks: $((Get-StartupTasks) -join ', ')"
Save-Screenshot '14-general-run-as-administrator'
$saved = (Get-Content (Join-Path $dataFolder 'settings.json') -Raw | ConvertFrom-Json).general.alwaysRunAsAdministrator
Test-Check 'the setting is saved' ($saved -eq $true)
# The helper logs to the user's protected logs folder, which the settings app can't get from
# it directly; a failure says why, with Task Scheduler's own message.
$helperLogs = Join-Path $env:ProgramData "PCCompanion\Logs\$sid"
$helperLog = (Get-ChildItem $helperLogs -Filter 'elevated-*.log' -ErrorAction SilentlyContinue | ForEach-Object { Get-Content $_.FullName -Raw }) -join "`n"
Test-Check 'the elevated helper logs that it created the tasks' ($helperLog -match 'Created the Always run as administrator tasks') "folder: $((Get-ChildItem $helperLogs -Name -ErrorAction SilentlyContinue) -join ', ')"
$missingSid = 'S-1-5-21-1111111111-2222222222-3333333333-1001'
$helper = Join-Path $allUsersFolder 'PCCompanion.Elevated.exe'
$code = (Start-Process $helper -ArgumentList @('create-startup-tasks', $missingSid) -Wait -PassThru).ExitCode
$failureLog = (Get-ChildItem (Join-Path $env:ProgramData "PCCompanion\Logs\$missingSid") -Filter 'elevated-*.log' -ErrorAction SilentlyContinue | ForEach-Object { Get-Content $_.FullName -Raw }) -join "`n"
Test-Check 'the elevated helper fails for a user that doesn''t exist' ($code -eq 1) "exit code $code"
Test-Check 'the elevated helper logs why it failed' (($failureLog -match '\[ERR\].*create-startup-tasks failed') -and ($failureLog -match 'schtasks')) ($failureLog -replace '\s+', ' ')
[void] (Start-Process $helper -ArgumentList @('delete-startup-tasks', $missingSid) -Wait -PassThru)
$signInTask = [xml] ((schtasks.exe /Query /TN "\PCCompanion\Host at sign-in $sid" /XML) -join "`n")
$exec = $signInTask.Task.Actions.Exec
$triggerUser = [string] $signInTask.Task.Triggers.LogonTrigger.UserId
$runLevel = [string] $signInTask.Task.Principals.Principal.RunLevel
# Task Scheduler may show the user as an account name rather than a SID.
$triggerSid = try { ([System.Security.Principal.NTAccount] $triggerUser).Translate([System.Security.Principal.SecurityIdentifier]).Value } catch { $triggerUser }
Test-Check 'the sign-in task starts the host from Program Files at sign-in with highest rights' (
    ($exec.Command -eq "`"$allUsersFolder\PCCompanion.Host.exe`"") -and ($exec.Arguments -eq '--startup') -and ($triggerSid -eq $sid) -and ($runLevel -eq 'HighestAvailable')) "$($exec.Command) $($exec.Arguments); user: $triggerUser; run level: $runLevel"
Test-Check 'the host restarts after the change' (Wait-Until { (Get-Processes 'PCCompanion.Host').Count -eq 1 } 20)
# With UAC off, the host keeps its logs in the user's logs folder, so there are no host logs to open.
[void] (Wait-Until { (Find-Toggle 'Always run as administrator').Current.IsEnabled } 20)
Test-Check 'with UAC off, the General page doesn''t offer to open host logs' (-not (Find-AppElement 'Open host logs')) ((Get-WindowText) -join ' | ')
[void] (Close-SettingsApp)
Get-Processes 'PCCompanion.Host' | ForEach-Object { Stop-Process -Id $_.Id -Force }
[void] (Wait-Until { (Get-Processes 'PCCompanion.Host').Count -eq 0 } 10)
schtasks.exe /Run /TN "\PCCompanion\Host $sid" | Out-Null
Test-Check 'the on-request task starts the host' (Wait-Until { @(Get-Process -Name 'PCCompanion.Host' -ErrorAction SilentlyContinue | Where-Object { $_.Path -like "$allUsersFolder\*" }).Count -eq 1 } 20)
Start-Process (Join-Path $allUsersFolder 'PCCompanion.exe')
[void] (Wait-Until { Get-AppWindow })
Start-Sleep -Seconds 3
[void] (Select-NavigationItem 'General')
Start-Sleep -Seconds 2
# The switch stays disabled until a change, the host's restart included, has finished.
[void] (Wait-Until { (Find-Toggle 'Always run as administrator').Current.IsEnabled } 20)
Test-Check 'turn off Always run as administrator' (Set-Toggle 'Always run as administrator' $false)
Test-Check 'turning it off deletes the tasks' (Wait-Until { (Get-StartupTasks).Count -eq 0 } 30) "tasks: $((Get-StartupTasks) -join ', ')"
[void] (Wait-Until { (Find-Toggle 'Always run as administrator').Current.IsEnabled } 20)
Test-Check 'turn it on again' ((Set-Toggle 'Always run as administrator' $true) -and (Wait-Until { (Get-StartupTasks).Count -eq 2 } 30))
Stop-All
$allUsersLogs = New-Item -ItemType Directory -Force -Path (Join-Path $results 'logs-all-users')
Copy-Item -Path (Join-Path $logs '*') -Destination $allUsersLogs -ErrorAction SilentlyContinue
$logText = (Get-ChildItem $logs -File -ErrorAction SilentlyContinue | ForEach-Object { Get-Content $_.FullName -Raw }) -join "`n"
Test-Check 'Always run as administrator logs no errors' (-not ($logText -match '\[(ERR|FTL)\]'))
# Uninstalling removes every user's tasks and the protected logs folder, with the logs in it. Setup
# checks what it removes from the user's own data folder.
$machineLog = Join-Path $env:ProgramData 'PCCompanion\Logs\host-test.log'
Set-Content -Path $machineLog -Value 'test'
$code = (Start-Process msiexec.exe -ArgumentList @('/x', "`"$msi`"", '/qn', '/l*v', "`"$results\uninstall-all-users.log`"") -Wait -PassThru).ExitCode
Test-Check 'uninstall for all users' ($code -eq 0) "exit code $code"
Test-Check 'uninstalling deletes the tasks' ((Get-StartupTasks).Count -eq 0) "tasks: $((Get-StartupTasks) -join ', ')"
Test-Check 'the all-users folder is removed' (-not (Test-Path (Join-Path $allUsersFolder 'PCCompanion.Host.exe')))
Test-Check 'uninstalling for all users removes the protected logs folder, with its logs' (-not (Test-Path (Join-Path $env:ProgramData 'PCCompanion')))
