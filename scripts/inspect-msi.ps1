<#
.SYNOPSIS
Shows what an installer contains, without installing it.

.DESCRIPTION
Without -Table, shows a summary: the main properties, the number and size of the files, the
executables, the registry values and the shortcuts. With -Table, returns every row of that
Windows Installer table as objects.

.PARAMETER Path
The .msi file.

.PARAMETER Table
A table to show in full, such as File, Registry, Shortcut or Property.

.EXAMPLE
powershell -ExecutionPolicy Bypass -File scripts\inspect-msi.ps1 src\PCCompanion.Installer\bin\x64\Release\PCCompanion-x64.msi

.EXAMPLE
powershell -ExecutionPolicy Bypass -File scripts\inspect-msi.ps1 src\PCCompanion.Installer\bin\x64\Release\PCCompanion-x64.msi -Table Registry
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $Path,
    [string] $Table
)

$ErrorActionPreference = 'Stop'
$installer = New-Object -ComObject WindowsInstaller.Installer
$database = $installer.GetType().InvokeMember('OpenDatabase', 'InvokeMethod', $null, $installer, @((Resolve-Path $Path).Path, 0))

function Invoke-Member($Target, [string] $Name, [string] $Kind, [object[]] $Arguments = $null) {
    $Target.GetType().InvokeMember($Name, $Kind, $null, $Target, $Arguments)
}

# Writes each row of a query to the pipeline as one array of strings.
function Invoke-Query([string] $Sql) {
    $view = Invoke-Member $database 'OpenView' 'InvokeMethod' @($Sql)
    [void] (Invoke-Member $view 'Execute' 'InvokeMethod')
    while ($null -ne ($record = Invoke-Member $view 'Fetch' 'InvokeMethod')) {
        $count = [int] (Invoke-Member $record 'FieldCount' 'GetProperty')
        $values = @(for ($field = 1; $field -le $count; $field++) { [string] (Invoke-Member $record 'StringData' 'GetProperty' @($field)) })
        Write-Output -NoEnumerate $values
    }
    [void] (Invoke-Member $view 'Close' 'InvokeMethod')
}

if ($Table) {
    $columns = @(Invoke-Query "SELECT ``Name`` FROM ``_Columns`` WHERE ``Table`` = '$Table' ORDER BY ``Number``" | ForEach-Object { $_[0] })
    if ($columns.Count -eq 0) {
        Write-Host "The installer has no $Table table." -ForegroundColor Red
        exit 1
    }

    foreach ($row in Invoke-Query "SELECT * FROM ``$Table``") {
        $item = [ordered]@{}
        for ($i = 0; $i -lt $columns.Count; $i++) { $item[$columns[$i]] = $row[$i] }
        [pscustomobject] $item
    }

    return
}

'Properties'
foreach ($name in 'ProductName', 'ProductVersion', 'Manufacturer', 'UpgradeCode', 'ALLUSERS', 'ARPPRODUCTICON') {
    $rows = @(Invoke-Query "SELECT ``Value`` FROM ``Property`` WHERE ``Property`` = '$name'")
    '  {0,-16} {1}' -f $name, $(if ($rows.Count) { $rows[0][0] } else { '(not set)' })
}

# File names are stored as "short|long".
$files = @(Invoke-Query 'SELECT `FileName`, `FileSize` FROM `File`')
$size = ($files | ForEach-Object { [int64] $_[1] } | Measure-Object -Sum).Sum
''
'Files: {0}, {1:N0} MB uncompressed' -f $files.Count, ($size / 1MB)
$files | ForEach-Object { ($_[0] -split '\|')[-1] } | Where-Object { $_ -like '*.exe' } | Sort-Object | ForEach-Object { "  $_" }

''
'Registry values (root -1 is HKCU for a per-user install, HKLM for all users)'
Invoke-Query 'SELECT `Root`, `Key`, `Name`, `Value` FROM `Registry`' | ForEach-Object { '  {0} {1} : {2} = {3}' -f $_[0], $_[1], $_[2], $_[3] }

''
'Shortcuts'
Invoke-Query 'SELECT `Name`, `Target` FROM `Shortcut`' | ForEach-Object { '  {0} -> {1}' -f ($_[0] -split '\|')[-1], $_[1] }
