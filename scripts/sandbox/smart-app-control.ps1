<#
.SYNOPSIS
Sets Smart App Control inside Windows Sandbox before the checks install anything. Started by the
checks; don't run it on your own PC: once Smart App Control is off, only reinstalling Windows turns
it on again.

.DESCRIPTION
Windows Sandbox starts Smart App Control in evaluation mode. With networking off, its check of every
package that Windows Installer opens for an install waits about two minutes before it lets the
package run. Off removes that wait. On blocks what Smart App Control doesn't trust, such as an
unsigned build of PC Companion. CiTool applies the change without a restart.

Returns a line for the report.
#>
param(
    [ValidateSet('Off', 'Evaluation', 'On')]
    [string] $Mode = 'Off'
)

$values = @{ Off = 0; On = 1; Evaluation = 2 }
$key = 'HKLM:\SYSTEM\CurrentControlSet\Control\CI\Policy'
$name = 'VerifiedAndReputablePolicyState'
$before = (Get-ItemProperty $key -Name $name -ErrorAction SilentlyContinue).$name
if ($before -ne $values[$Mode]) {
    Set-ItemProperty -Path $key -Name $name -Value $values[$Mode] -Type DWord
    $refresh = (& (Join-Path $env:WINDIR 'System32\CiTool.exe') --refresh --json 2>&1) -join ' '
}
$after = (Get-ItemProperty $key -Name $name -ErrorAction SilentlyContinue).$name
$detail = if ($refresh) { "; CiTool --refresh: $refresh" } else { '' }
return "Smart App Control: $Mode ($name $before -> $after$detail)"
