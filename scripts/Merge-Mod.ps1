# Installs the multiplayer mod into the project's BCML, exports it to both Cemu folders and applies scripts/Patch-Mod.ps1.
# Output lines follow the SETUP| protocol of Install-Components.ps1 (PROGRESS, STEP, DONE); the setup wizard runs this once the game is located.
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$python = Join-Path $root '.tools\python38\python.exe'

function Say($kind, $id, $state, $text) { Write-Output "SETUP|$kind|$id|$state|$text" }

$previous = $env:LOCALAPPDATA
try {
    if (-not (Test-Path -LiteralPath $python)) { throw 'The project Python is missing. Download the missing components first.' }
    $running = Get-Process -Name Cemu -ErrorAction SilentlyContinue | Where-Object { $_.Path -and $_.Path.StartsWith($root, [StringComparison]::OrdinalIgnoreCase) }
    if ($running) { throw 'Close the project Cemu instances first (scripts/Stop-Online.ps1).' }

    Say 'STEP' 'mod' 'start' 'Merging the multiplayer mod'
    $env:LOCALAPPDATA = Join-Path $root '.tools\localappdata'
    $reason = $null
    & $python (Join-Path $PSScriptRoot 'merge_mod.py') --root $root | ForEach-Object {
        if ($_ -like 'SETUP|STEP|mod|failed|*') { $reason = $_.Split('|', 5)[4] } else { $_ }
    }
    if ($LASTEXITCODE -ne 0) { throw $(if ($reason) { $reason } else { "BCML could not merge the mod (exit code $LASTEXITCODE)." }) }
    $env:LOCALAPPDATA = $previous

    Say 'PROGRESS' 'all' 90 'the fixes BCML leaves out'
    & (Join-Path $PSScriptRoot 'Patch-Mod.ps1')
    if ($LASTEXITCODE -ne 0) { throw "Patch-Mod.ps1 failed (exit code $LASTEXITCODE)." }

    Say 'STEP' 'mod' 'done' 'Multiplayer mod merged'
    Say 'DONE' 'all' 'OK' 'The multiplayer mod is ready.'
}
catch {
    $env:LOCALAPPDATA = $previous
    Say 'STEP' 'mod' 'failed' $_.Exception.Message
    Say 'DONE' 'all' 'FAILED' $_.Exception.Message
    exit 1
}
