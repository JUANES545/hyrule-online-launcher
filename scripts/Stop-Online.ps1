$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot

# Only processes that run from this project are stopped; any other Cemu is left alone.
$owned = Get-Process -ErrorAction SilentlyContinue | Where-Object {
    $_.Name -in 'Cemu', 'Milk Bar Launcher', 'MBL.DedicatedServer', 'InputRouter' -and
    $_.Path -and $_.Path.StartsWith($root, [StringComparison]::OrdinalIgnoreCase)
}
foreach ($process in $owned) {
    Write-Output "Stopping $($process.Name) ($($process.Id))."
    Stop-Process -Id $process.Id
}
if (-not $owned) { Write-Output 'Nothing to stop.' }
