# Smoke: a per-user install, the host at sign-in, and the settings app starting the host. It runs
# first in every run, with PC Companion not installed.

# Install for the current user.
$code = (Start-Process msiexec.exe -ArgumentList @('/i', "`"$msi`"", '/qn', 'ALLUSERS=2', 'MSIINSTALLPERUSER=1', '/l*v', "`"$results\install.log`"") -Wait -PassThru).ExitCode
Test-Check 'install for the current user' ($code -eq 0) "exit code $code"
Test-Check 'settings app and host installed' ((Test-Path $appExe) -and (Test-Path $hostExe))
Test-Check 'third-party notices installed' (Test-Path (Join-Path $installFolder 'ThirdPartyNotices.txt'))
$run = (Get-ItemProperty $runKey -Name PCCompanion -ErrorAction SilentlyContinue).PCCompanion
Test-Check 'startup entry in HKCU Run' ($run -eq "`"$hostExe`" --startup") "value: $run"

# Starts $Path and checks that Windows started it: Smart App Control, for one, blocks an unsigned
# build. Returns the process, or $null.
function Start-Checked([string] $Name, [string] $Path, [string[]] $Arguments) {
    $parameters = @{ FilePath = $Path; PassThru = $true; ErrorAction = 'Stop' }
    if ($Arguments) { $parameters.ArgumentList = $Arguments }
    try {
        $process = Start-Process @parameters
        Test-Check "$Name starts" $true
        return $process
    } catch {
        Test-Check "$Name starts" $false $_.Exception.Message
        return $null
    }
}

# Started at sign-in while Start with Windows is off: the host exits and creates nothing.
$process = Start-Checked 'host --startup' $hostExe '--startup'
Test-Check 'host --startup exits when Start with Windows is off' (($null -ne $process) -and $process.WaitForExit(10000))
Test-Check 'host --startup creates no data folder' (-not (Test-Path $dataFolder))

# The settings app starts the host and shows Home; both write their logs.
$process = Start-Checked 'the settings app' $appExe
Test-Check 'settings app window opens' (($null -ne $process) -and (Wait-Until { Get-AppWindow }))
Test-Check 'settings app starts the host' (($null -ne $process) -and (Wait-Until { (Get-Processes 'PCCompanion.Host').Count -eq 1 } 15))
Start-Sleep -Seconds 3
Save-Screenshot '01-home-english'
$text = Get-WindowText
Test-Check 'Home shows the feature cards' (($text -contains 'Home') -and ($text -contains 'Input & Triggers') -and ($text -contains 'Hot Corners')) ($text -join ' | ')
Test-Check 'settings app and host logs written' (Wait-Until { (Test-Path (Join-Path $logs 'app-*.log')) -and (Test-Path (Join-Path $logs 'host-*.log')) } 10)
