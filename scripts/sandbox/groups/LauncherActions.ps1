# Launcher Actions: turned on from Home, the settings app writes an entry in Start for each action
# available, in the PC Companion actions folder, with its arguments, description, icon and Windows'
# mark that keeps it out of Recently added; a feature switched off in Features in Start loses its
# entries, a profile renamed in the settings file is renamed once the settings app runs again, and
# in Arabic the folder and entries are renamed in Arabic. PCCompanion.exe --run uses the test
# network profile and exits with 0, refuses an unknown profile with 1, wrong arguments with 2, and
# exits with 3 while the feature is off; with no host running, it starts one and runs. A test client
# copy labeled low integrity can't open the pipe, and a medium one reaches the host, which runs as
# administrator in the Sandbox. The Windows key, an entry's name and Enter run its action from Start,
# and End App's choosing comes to the front from Start and from the Run box, where Esc ends it.
# Turned off, the folder is gone. The page is checked in English and Arabic, light and dark. Lock,
# Turn off displays and Start the screen saver are never run, since they would stop the checks.
[void] (Close-SettingsApp)

# The process of the window in front.
Add-Type -TypeDefinition 'using System; using System.Runtime.InteropServices; public static class LauncherWindows { [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow(); [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId); public static uint FrontProcess() { uint id; GetWindowThreadProcessId(GetForegroundWindow(), out id); return id; } }'

$detailedUntil = [DateTimeOffset]::Now.AddHours(1).ToString('o')
$office = '{ "name": "Office", "proxy": { "useServer": true, "server": "proxy.example.com:8080" } }'
$homeProfile = '{ "name": "Home", "proxy": { "autoDetect": true } }'
function Write-LauncherSettings([string] $Launcher, [string] $Profiles = "$office, $homeProfile", [string] $Language = 'null', [string] $Theme = 'Light') {
    Write-Settings "{ ""schemaVersion"": 1, ""general"": { ""theme"": ""$Theme"", ""language"": $Language }, ""diagnostics"": { ""detailedLoggingUntil"": ""$detailedUntil"" }, ""features"": { ""launcherActions"": $Launcher, ""networkProfiles"": { ""enabled"": true, ""profiles"": [ $Profiles ] }, ""endApp"": { ""enabled"": true } } }"
}
function Start-LauncherHost {
    if ((Get-Processes 'PCCompanion.Host').Count -eq 0) {
        Start-Process $hostExe
        [void] (Wait-Until { [Win32]::FindWindow('PCCompanion.Host', $null) -ne [IntPtr]::Zero } 30)
    }
    Start-Sleep -Seconds 2
}
function Stop-LauncherHost {
    [void] [Win32]::PostMessage([Win32]::FindWindow('PCCompanion.Host', $null), 0x0010, [UIntPtr]::Zero, [IntPtr]::Zero)
    [void] (Wait-Until { (Get-Processes 'PCCompanion.Host').Count -eq 0 } 15)
}
# Runs the command and returns its exit code and how long it took. It waits for the command only:
# Start-Process -Wait would also wait for a host the command starts. The handle is read at once, so
# Windows PowerShell keeps the exit code.
function Invoke-Launcher([string[]] $Arguments) {
    $watch = [Diagnostics.Stopwatch]::StartNew()
    $process = Start-Process $appExe -ArgumentList $Arguments -PassThru
    [void] $process.Handle
    $code = if ($process.WaitForExit(30000)) { $process.ExitCode } else { $process.Kill(); 'none within 30 s' }
    @{ Code = $code; Milliseconds = $watch.ElapsedMilliseconds }
}
$programs = [Environment]::GetFolderPath('Programs')
$folder = Join-Path $programs 'PC Companion actions'
function Get-Entries([string] $In = $folder) { @(Get-ChildItem $In -Filter '*.lnk' -ErrorAction SilentlyContinue | ForEach-Object { $_.BaseName } | Sort-Object) }
$shell = New-Object -ComObject WScript.Shell

# The settings app writes the entries as it starts and after each save.
Write-LauncherSettings '{ "enabled": false }'
Start-Process $appExe
[void] (Wait-Until { Get-AppWindow })
Start-Sleep -Seconds 3
Test-Check 'turn on Launcher Actions on Home' (Set-Toggle 'Launcher Actions' $true)
$expected = @('Choose an app to end - End App', 'Lock - PC Companion', 'Start the screen saver - PC Companion', 'Turn off displays - PC Companion', 'Use Home - Network Profiles', 'Use Office - Network Profiles')
Test-Check 'turning it on writes an entry for each action available' (Wait-Until { ((Get-Entries) -join '|') -eq ($expected -join '|') } 15) "entries: $((Get-Entries) -join ', ')"
$link = $shell.CreateShortcut((Join-Path $folder 'Use Office - Network Profiles.lnk'))
Test-Check 'an entry starts the command with its arguments and its feature as its description' (($link.TargetPath -eq $appExe) -and ($link.Arguments -eq '--run networkProfiles use Office') -and ($link.Description -eq 'Network Profiles')) "$($link.TargetPath) $($link.Arguments) / $($link.Description)"
Test-Check 'an entry has PC Companion''s icon' ($link.IconLocation -like "$appExe,0") $link.IconLocation
$store = (New-Object -ComObject Shell.Application).NameSpace($folder).ParseName('Use Office - Network Profiles.lnk')
$excluded = $store.ExtendedProperty('System.AppUserModel.ExcludeFromShowInNewInstall')
Test-Check 'an entry is kept out of Recently added' ($excluded -eq $true) "mark: $excluded"
$marker = Get-Item (Join-Path $folder 'PCCompanion.LauncherActions') -Force -ErrorAction SilentlyContinue
Test-Check 'the folder is marked as PC Companion''s, hidden' (($null -ne $marker) -and $marker.Attributes.HasFlag([IO.FileAttributes]::Hidden))

# The page: its switches, the actions with Copy command, and Features in Start.
Test-Check 'select Launcher Actions' (Select-NavigationItem 'Launcher Actions')
Start-Sleep -Seconds 2
$text = Get-WindowText
Test-Check 'the page lists the actions available' (($text -contains 'Use Office') -and ($text -contains 'Choose an app to end') -and ($text -contains 'Turn off displays')) ($text -join ' | ')
Test-Check 'the page doesn''t list actions on the window in front' (-not ($text -contains 'Hide app')) ($text -join ' | ')
Test-Check 'Copy command for Use Office is there' ($null -ne (Find-AppElement 'Copy command for Use Office'))
Invoke-AppElement 'Copy command for Use Office'
Start-Sleep -Milliseconds 500
$copied = [System.Windows.Forms.Clipboard]::GetText()
# The program's path is quoted only when it has a space, as programs read their arguments.
$program = if ($appExe.Contains(' ')) { "`"$appExe`"" } else { $appExe }
Test-Check 'Copy command copies the command that runs the action' ($copied -eq "$program --run networkProfiles use Office") $copied
Save-Screenshot '21a-launcher-actions'
$condition = New-Object System.Windows.Automation.AndCondition(
    (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, 'Features in Start')),
    (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::IsExpandCollapsePatternAvailableProperty, $true)))
$expander = (Get-AppWindow).FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
if ($expander) { $expander.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern).Expand() }
Test-Check 'open Features in Start' ($null -ne $expander)
Start-Sleep -Seconds 1
Save-Screenshot '21b-launcher-actions-features'
Test-Check 'switch End App off in Features in Start' (Set-Toggle 'End App' $false)
Test-Check 'its entries leave Start' (Wait-Until { -not ((Get-Entries) -contains 'Choose an app to end - End App') } 15) "entries: $((Get-Entries) -join ', ')"
$saved = (Get-Content (Join-Path $dataFolder 'settings.json') -Raw | ConvertFrom-Json).features.launcherActions.hiddenFeatures
Test-Check 'the feature is saved as left out of Start' (@($saved) -contains 'endApp') "saved: $($saved -join ', ')"
Test-Check 'switch End App on again' ((Set-Toggle 'End App' $true) -and (Wait-Until { (Get-Entries) -contains 'Choose an app to end - End App' } 15))

# The command, through the host.
Start-LauncherHost
$run = Invoke-Launcher @('--run', 'networkProfiles', 'use', 'Office')
Test-Check 'the command uses the test profile and exits with 0' ($run.Code -eq 0) "exit code $($run.Code)"
Write-Report "  the command took $($run.Milliseconds) ms with the host running"
Test-Check 'the host switched to the profile' (Wait-Until { (Get-HostLog) -match 'Switched to network profile 1\.' } 10)
$run = Invoke-Launcher @('--run', 'NETWORKPROFILES', 'USE', 'home')
Test-Check 'a command''s IDs ignore case' ($run.Code -eq 0) "exit code $($run.Code)"
$run = Invoke-Launcher @('--run', 'networkProfiles', 'use', 'Lab')
Test-Check 'an unknown profile is refused with 1' ($run.Code -eq 1) "exit code $($run.Code)"
Test-Check 'the host logs why, without the profile''s name' (((Get-HostLog) -match 'action "?use"? was asked for from a launcher or the command and refused: "?RefusedByFeature') -and -not ((Get-HostLog) -match '\bLab\b'))
$run = Invoke-Launcher @('--run', 'networkProfiles')
Test-Check 'wrong arguments exit with 2' ($run.Code -eq 2) "exit code $($run.Code)"
$run = Invoke-Launcher @('--run', 'network-profiles', 'use', 'Office')
Test-Check 'an ID that isn''t one exits with 2' ($run.Code -eq 2) "exit code $($run.Code)"

# Programs that open the pipe themselves: a copy labeled low integrity is refused, and one labeled
# medium reaches the host, which runs as administrator in the Sandbox.
$clientFolder = Join-Path $env:TEMP 'PCCompanionLauncherActions'
New-Item -ItemType Directory -Force -Path $clientFolder | Out-Null
$client = Join-Path $clientFolder 'pipe-client.exe'
Add-Type -OutputAssembly $client -OutputType ConsoleApplication -ReferencedAssemblies System.Core -TypeDefinition @'
using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
public static class PipeClient {
    public static int Main(string[] args) {
        try {
            using (var pipe = new NamedPipeClientStream(".", args[0], PipeDirection.InOut)) {
                pipe.Connect(5000);
                pipe.ReadMode = PipeTransmissionMode.Message;
                var request = Encoding.UTF8.GetBytes(args[1]);
                pipe.Write(request, 0, request.Length);
                var answer = new byte[256];
                var read = pipe.Read(answer, 0, answer.Length);
                File.WriteAllText(args[2], Encoding.UTF8.GetString(answer, 0, read));
                return 0;
            }
        }
        catch (UnauthorizedAccessException) { return 5; }
        catch (Exception) { return 1; }
    }
}
'@
Copy-Item $client (Join-Path $clientFolder 'pipe-client-low.exe')
Copy-Item $client (Join-Path $clientFolder 'pipe-client-medium.exe')
[void] (icacls (Join-Path $clientFolder 'pipe-client-low.exe') /setintegritylevel low)
[void] (icacls (Join-Path $clientFolder 'pipe-client-medium.exe') /setintegritylevel medium)
$pipe = "PCCompanion.Actions.$((Get-Process -Id $PID).SessionId)"
$request = '{\"feature\":\"hideApps\",\"action\":\"showAll\"}'
$low = Start-Process (Join-Path $clientFolder 'pipe-client-low.exe') -ArgumentList @($pipe, $request, (Join-Path $clientFolder 'low.txt')) -PassThru -Wait
Test-Check 'a low-integrity program can''t open the pipe' ($low.ExitCode -eq 5) "exit code $($low.ExitCode)"
$medium = Start-Process (Join-Path $clientFolder 'pipe-client-medium.exe') -ArgumentList @($pipe, $request, (Join-Path $clientFolder 'medium.txt')) -PassThru -Wait
$answer = Get-Content (Join-Path $clientFolder 'medium.txt') -Raw -ErrorAction SilentlyContinue
Test-Check 'a medium-integrity program reaches the host running as administrator' (($medium.ExitCode -eq 0) -and ($answer -match '"result":"(ran|refused)"')) "exit code $($medium.ExitCode), answer $answer"
$bad = Start-Process (Join-Path $clientFolder 'pipe-client-medium.exe') -ArgumentList @($pipe, 'notjson', (Join-Path $clientFolder 'bad.txt')) -PassThru -Wait
$answer = Get-Content (Join-Path $clientFolder 'bad.txt') -Raw -ErrorAction SilentlyContinue
Test-Check 'a request that isn''t one is answered invalid' ($answer -match '"result":"invalid"') "answer $answer"

# Start and the Run box: End App's choosing comes to the front, and Esc ends it.
function Test-EndAppInFront([string] $From) {
    $inFront = Wait-Until { (Get-Processes 'PCCompanion.Host' | Where-Object { $_.Id -eq [LauncherWindows]::FrontProcess() }).Count -eq 1 } 10
    Save-Screenshot "21c-end-app-from-$($From.ToLowerInvariant() -replace ' ', '-')"
    Test-Check "End App's choosing comes to the front from $From" $inFront "in front: $([Win32]::GetForegroundWindow())"
    Send-Key 0x1B
    Test-Check "Esc ends the choosing started from $From" (Wait-Until { (Get-Processes 'PCCompanion.Host' | Where-Object { $_.Id -eq [LauncherWindows]::FrontProcess() }).Count -eq 0 } 10)
}
[void] (Close-SettingsApp)
[void] [Win32]::Key(0x5B, $false)
[void] [Win32]::Key(0x5B, $true)
Start-Sleep -Seconds 2
[System.Windows.Forms.SendKeys]::SendWait('Choose an app to end')
Start-Sleep -Seconds 3
Save-Screenshot '21d-start-search'
[System.Windows.Forms.SendKeys]::SendWait('{ENTER}')
Test-EndAppInFront 'Start'
[void] [Win32]::Key(0x5B, $false)
Send-Key 0x52
[void] [Win32]::Key(0x5B, $true)
$runCondition = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, 'Run')
[void] (Wait-Until { $null -ne [System.Windows.Automation.AutomationElement]::RootElement.FindFirst([System.Windows.Automation.TreeScope]::Children, $runCondition) } 10)
Start-Sleep -Milliseconds 500
[System.Windows.Forms.SendKeys]::SendWait("`"$appExe`" --run endApp chooseApp")
[System.Windows.Forms.SendKeys]::SendWait('{ENTER}')
Test-EndAppInFront 'the Run box'

# A profile renamed by hand: the old entry is refused until the settings app runs again.
Write-LauncherSettings '{ "enabled": true }' '{ "name": "Office 2", "proxy": { "useServer": true, "server": "proxy.example.com:8080" } }'
Start-Sleep -Seconds 3
$link = $shell.CreateShortcut((Join-Path $folder 'Use Office - Network Profiles.lnk'))
$run = Invoke-Launcher (@($link.Arguments -split ' '))
Test-Check 'the old entry is refused with 1 after a hand edit' ($run.Code -eq 1) "exit code $($run.Code)"
Start-Process $appExe
[void] (Wait-Until { Get-AppWindow })
Test-Check 'once the settings app runs, the entry follows the renamed profile' (Wait-Until { ((Get-Entries) -contains 'Use Office 2 - Network Profiles') -and -not ((Get-Entries) -contains 'Use Office - Network Profiles') } 15) "entries: $((Get-Entries) -join ', ')"
$link = $shell.CreateShortcut((Join-Path $folder 'Use Office 2 - Network Profiles.lnk'))
Test-Check 'the renamed entry''s target is quoted' ($link.Arguments -eq '--run networkProfiles use "Office 2"') $link.Arguments
# Start-Process joins its arguments with spaces, so a target with a space is quoted here.
$run = Invoke-Launcher @('--run', 'networkProfiles', 'use', '"Office 2"')
Test-Check 'the renamed profile''s command runs' ($run.Code -eq 0) "exit code $($run.Code)"

# With no host running, the command starts one and runs.
[void] (Close-SettingsApp)
Stop-LauncherHost
$run = Invoke-Launcher @('--run', 'networkProfiles', 'use', '"Office 2"')
Test-Check 'with no host running, the command starts one and runs' (($run.Code -eq 0) -and ((Get-Processes 'PCCompanion.Host').Count -eq 1)) "exit code $($run.Code)"
Write-Report "  the command took $($run.Milliseconds) ms, starting the host"

# Off: the command can't reach it, and the folder is gone.
Write-LauncherSettings '{ "enabled": false }'
Start-Sleep -Seconds 3
$run = Invoke-Launcher @('--run', 'networkProfiles', 'use', 'Office')
Test-Check 'while the feature is off, the command exits with 3' ($run.Code -eq 3) "exit code $($run.Code), $($run.Milliseconds) ms"
Start-Process $appExe
[void] (Wait-Until { Get-AppWindow })
Test-Check 'while the feature is off, the folder is gone' (Wait-Until { -not (Test-Path $folder) } 15)
[void] (Close-SettingsApp)

# Arabic, with the dark theme: the folder and entries are named in Arabic.
Write-LauncherSettings '{ "enabled": true }' -Language '"ar"' -Theme 'Dark'
Start-Process $appExe
[void] (Wait-Until { Get-AppWindow })
Start-Sleep -Seconds 3
$arabicFolder = Join-Path $programs 'إجراءات PC Companion'
Test-Check 'in Arabic, the folder is named in Arabic' (Wait-Until { Test-Path $arabicFolder } 15) "folders: $((Get-ChildItem $programs -Directory -Name) -join ', ')"
Test-Check 'in Arabic, the English folder is gone' (-not (Test-Path $folder))
$arabicEntries = Get-Entries $arabicFolder
Test-Check 'in Arabic, the entries are named in Arabic' (($arabicEntries | Where-Object { $_ -match '[؀-ۿ]' }).Count -ge 3) "entries: $($arabicEntries -join ', ')"
[void] (Select-NavigationItem 'إجراءات المشغّل')
Start-Sleep -Seconds 2
Save-Screenshot '21e-launcher-actions-arabic-dark'
[void] (Close-SettingsApp)

# Turned off on its page, the folder goes.
Write-LauncherSettings '{ "enabled": true }'
Start-Process $appExe
[void] (Wait-Until { Get-AppWindow })
Start-Sleep -Seconds 3
Test-Check 'back in English, the folder is renamed' (Wait-Until { (Test-Path $folder) -and -not (Test-Path $arabicFolder) } 15)
[void] (Select-NavigationItem 'Launcher Actions')
Start-Sleep -Seconds 1
Test-Check 'turn off Launcher Actions on its page' (Set-Toggle 'Launcher Actions' $false)
Test-Check 'turned off, the folder is gone' (Wait-Until { -not (Test-Path $folder) } 15)
[void] (Close-SettingsApp)
Stop-LauncherHost

$logText = (Get-ChildItem $logs -File -ErrorAction SilentlyContinue | ForEach-Object { Get-Content $_.FullName -Raw }) -join "`n"
Test-Check 'the logs hold no profile names' (-not ($logText -match 'Office 2|"Home"|Use Office'))
Test-Check 'Launcher Actions logs no errors' (-not ($logText -match '\[(ERR|FTL)\]'))
